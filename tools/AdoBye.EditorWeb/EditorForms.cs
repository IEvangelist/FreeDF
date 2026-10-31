using System.Globalization;
using System.Text;
using System.Text.Json;
using PortableDocument.Core;
using PortableDocument.Document;

namespace AdoBye.EditorWeb;

public sealed record FormOptionDto(string Export, string? Display = null);

/// <summary>Editor-side description of one widget. Coordinates are points relative to the page CropBox, y down (the editor layer's space).</summary>
public sealed record FormFieldDto(
    string Kind = "text", string Name = "", int Page = 0, double X = 0, double Y = 0, double W = 0, double H = 0,
    string? Tooltip = null, bool Required = false, bool ReadOnly = false, string? Value = null, string? DefaultValue = null, int MaxLength = 0,
    string FontName = "Helv", double FontSize = 0, string? TextColor = null, string? BorderColor = null, string? BackgroundColor = null,
    double BorderWidth = 1, string BorderStyle = "S", int Alignment = 0, List<FormOptionDto>? Options = null, string ExportValue = "Yes",
    string? Caption = null, bool Editable = false, bool MultiSelect = false, int Widget = 0, int Tab = 0);

public sealed record FormMoveDto(string Name, int Widget, double X, double Y, double W, double H);
public sealed record FormNamesDto(List<string>? Names = null);
public sealed record FormPasteDto(int Page, double Dx, double Dy, List<FormFieldDto> Items);

/// <summary>HTTP-style form-authoring commands over <see cref="PdfEditor"/>. Nothing here executes scripts or touches the network.</summary>
public static class EditorForms
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    static readonly Dictionary<string, PdfFormFieldKind> Kinds = new()
    {
        ["text"] = PdfFormFieldKind.Text, ["multiline"] = PdfFormFieldKind.MultilineText, ["comb"] = PdfFormFieldKind.CombText,
        ["password"] = PdfFormFieldKind.PasswordText, ["checkbox"] = PdfFormFieldKind.Checkbox, ["radio"] = PdfFormFieldKind.Radio,
        ["combo"] = PdfFormFieldKind.ComboBox, ["list"] = PdfFormFieldKind.ListBox, ["button"] = PdfFormFieldKind.PushButton, ["signature"] = PdfFormFieldKind.Signature,
    };
    static readonly HashSet<string> Mutations = ["add", "update", "remove", "move", "paste", "tab", "order", "flatten", "import"];

    public static bool IsMutation(string verb) => Mutations.Contains(verb);

    static string KindName(PdfFormFieldKind k) => Kinds.First(kv => kv.Value == k).Key;

    public static double[]? ParseHex(string? hex) =>
        hex is { Length: 7 } && hex[0] == '#' && int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            ? [(v >> 16 & 255) / 255.0, (v >> 8 & 255) / 255.0, (v & 255) / 255.0] : null;

    public static string? ToHex(IReadOnlyList<double>? c)
    {
        if (c is null || c.Count == 0) return null;
        double r, g, b;
        switch (c.Count)
        {
            case 1: r = g = b = c[0]; break;
            case 3: (r, g, b) = (c[0], c[1], c[2]); break;
            default: (r, g, b) = ((1 - c[0]) * (1 - c[3]), (1 - c[1]) * (1 - c[3]), (1 - c[2]) * (1 - c[3])); break;
        }
        static int B(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return $"#{B(r):x2}{B(g):x2}{B(b):x2}";
    }

    static PdfFormFieldSpec ToSpec(FormFieldDto d, PdfRect crop)
    {
        if (!Kinds.TryGetValue(d.Kind, out var kind)) throw new ArgumentException($"Unknown field type '{d.Kind}'.");
        var left = crop.Left + d.X; var top = crop.Top - d.Y;
        return new PdfFormFieldSpec(kind, d.Name, d.Page, new PdfRect(left, top - d.H, left + d.W, top))
        {
            Tooltip = d.Tooltip, Required = d.Required, ReadOnly = d.ReadOnly, Value = d.Value, DefaultValue = d.DefaultValue, MaxLength = d.MaxLength,
            FontName = d.FontName, FontSize = d.FontSize, TextColor = ParseHex(d.TextColor), BorderColor = ParseHex(d.BorderColor),
            BackgroundColor = ParseHex(d.BackgroundColor), BorderWidth = d.BorderWidth, BorderStyle = d.BorderStyle, Alignment = d.Alignment,
            Options = d.Options?.Select(o => new PdfFormOption(o.Export, o.Display)).ToList(), ExportValue = d.ExportValue, Caption = d.Caption,
            Editable = d.Editable, MultiSelect = d.MultiSelect,
        };
    }

    static FormFieldDto ToDto(PdfFormFieldEntry e, PdfRect crop)
    {
        var s = e.Spec;
        return new FormFieldDto(KindName(s.Kind), s.Name, s.Page, s.Rect.Left - crop.Left, crop.Top - s.Rect.Top, s.Rect.Width, s.Rect.Height,
            s.Tooltip, s.Required, s.ReadOnly, s.Value, s.DefaultValue, s.MaxLength, s.FontName, s.FontSize, ToHex(s.TextColor), ToHex(s.BorderColor),
            ToHex(s.BackgroundColor), s.BorderWidth, s.BorderStyle, s.Alignment, s.Options?.Select(o => new FormOptionDto(o.Export, o.Display)).ToList(),
            s.ExportValue, s.Caption, s.Editable, s.MultiSelect, e.WidgetIndex, e.AnnotOrder);
    }

    public static IReadOnlyList<FormFieldDto> List(PdfEditor editor, Func<int, PdfRect> cropOf, int page) =>
        editor.GetFormFieldSpecs().Where(e => page < 0 || e.Spec.Page == page).Select(e => ToDto(e, cropOf(e.Spec.Page))).ToList();

    static string Prefix(PdfFormFieldKind k) => k switch
    {
        PdfFormFieldKind.MultilineText => "Multiline", PdfFormFieldKind.CombText => "Comb", PdfFormFieldKind.PasswordText => "Password", PdfFormFieldKind.Checkbox => "Check",
        PdfFormFieldKind.Radio => "Radio", PdfFormFieldKind.ComboBox => "Combo", PdfFormFieldKind.ListBox => "List", PdfFormFieldKind.PushButton => "Button",
        PdfFormFieldKind.Signature => "Signature", _ => "Text",
    };

    static bool Free(HashSet<string> taken, string name)
    {
        if (taken.Contains(name) || taken.Any(n => n.StartsWith(name + ".", StringComparison.Ordinal))) return false;
        var parts = name.Split('.');
        for (var i = 1; i < parts.Length; i++) if (taken.Contains(string.Join('.', parts.Take(i)))) return false;
        return true;
    }

    static string UniqueName(HashSet<string> taken, string wanted)
    {
        if (Free(taken, wanted)) return wanted;
        var stem = wanted.TrimEnd("0123456789".ToCharArray());
        for (var i = 2; ; i++) if (Free(taken, stem + i)) return stem + i;
    }

    static T Body<T>(byte[]? body) where T : class => JsonSerializer.Deserialize<T>(body ?? [], Json) ?? throw new ArgumentException("Missing form data.");

    /// <summary>Executes one mutating form command and returns the JSON reply.</summary>
    public static object Handle(PdfEditor editor, string verb, IReadOnlyDictionary<string, string> query, byte[]? body, Func<int, PdfRect> cropOf)
    {
        string? Q(string key) => query.GetValueOrDefault(key);
        int Int(string key, int fallback) => int.TryParse(Q(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;
        var taken = editor.GetFormFieldSpecs().Select(e => e.Spec.Name).ToHashSet(StringComparer.Ordinal);
        switch (verb)
        {
            case "add":
                {
                    var dto = Body<FormFieldDto>(body);
                    var spec = ToSpec(dto, cropOf(dto.Page));
                    if (string.IsNullOrWhiteSpace(spec.Name)) spec = spec with { Name = UniqueName(taken, Prefix(spec.Kind) + "1") };
                    var handle = editor.AddFormField(spec);
                    var index = editor.GetFormFieldSpecs().First(e => e.Handle.Widget == handle.Widget).WidgetIndex;
                    return new { name = handle.FullName, widget = index };
                }
            case "update":
                {
                    var dto = Body<FormFieldDto>(body);
                    var spec = ToSpec(dto, cropOf(dto.Page));
                    var old = Q("n") ?? throw new ArgumentException("Missing field name.");
                    editor.UpdateFormField(old, spec, Int("i", 0));
                    return new { name = spec.Name, widget = Int("i", 0) };
                }
            case "remove":
                {
                    var names = Body<FormNamesDto>(body).Names ?? [];
                    var removed = 0;
                    foreach (var n in names) if (editor.RemoveFormField(n)) removed++;
                    if (removed == 0) throw new ArgumentException("Form field not found.");
                    return new { removed };
                }
            case "move":
                {
                    var moves = Body<List<FormMoveDto>>(body);
                    var specs = editor.GetFormFieldSpecs();
                    foreach (var m in moves)
                    {
                        var e = specs.FirstOrDefault(s => s.Spec.Name == m.Name && s.WidgetIndex == m.Widget) ?? throw new ArgumentException($"No form field named '{m.Name}'.");
                        var crop = cropOf(e.Spec.Page);
                        var left = crop.Left + m.X; var top = crop.Top - m.Y;
                        editor.UpdateFormField(m.Name, e.Spec with { Rect = new PdfRect(left, top - m.H, left + m.W, top) }, m.Widget);
                    }
                    return new { moved = moves.Count };
                }
            case "paste":
                {
                    var paste = Body<FormPasteDto>(body);
                    var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
                    var created = new List<object>();
                    foreach (var item in paste.Items)
                    {
                        var dto = item with { Page = paste.Page, X = item.X + paste.Dx, Y = item.Y + paste.Dy };
                        if (!renamed.TryGetValue(item.Name, out var name))
                        {
                            name = UniqueName(taken, item.Name.Length == 0 ? Prefix(ToSpec(item, cropOf(paste.Page)).Kind) + "1" : item.Name);
                            renamed[item.Name] = name; taken.Add(name);
                        }
                        var spec = ToSpec(dto with { Name = name }, cropOf(paste.Page));
                        var handle = editor.AddFormField(spec);
                        created.Add(new { name = handle.FullName, widget = editor.GetFormFieldSpecs().First(e => e.Handle.Widget == handle.Widget).WidgetIndex });
                    }
                    return new { created };
                }
            case "tab":
                editor.SetPageTabOrder(Int("page", 0), Q("mode") switch { "row" => PdfFormTabOrder.Row, "column" => PdfFormTabOrder.Column, "structure" => PdfFormTabOrder.Structure, _ => PdfFormTabOrder.Unspecified });
                return new { };
            case "order":
                editor.ReorderFormWidgets(Int("page", 0), Body<FormNamesDto>(body).Names ?? []);
                return new { };
            case "flatten":
                {
                    var scope = Q("scope") switch { "selected" => PdfFlattenScope.Selected, "readonly" => PdfFlattenScope.ReadOnly, _ => PdfFlattenScope.All };
                    var result = editor.FlattenForm(new PdfFlattenOptions(scope, scope == PdfFlattenScope.Selected ? Body<FormNamesDto>(body).Names ?? [] : null));
                    return new { flattened = result.Flattened, kept = result.Kept };
                }
            case "import":
                {
                    var result = editor.ImportFormData(body ?? throw new ArgumentException("Choose an FDF or XFDF file."));
                    return new { applied = result.Applied, skipped = result.Skipped };
                }
            default: throw new ArgumentException("Unknown form command.");
        }
    }

    public static (byte[] Data, string ContentType, string FileName) Export(PdfEditor editor, IReadOnlyDictionary<string, string> query)
    {
        var fdf = query.GetValueOrDefault("format") == "fdf";
        var text = editor.ExportFormData(fdf ? PdfFormDataFormat.Fdf : PdfFormDataFormat.Xfdf);
        return (Encoding.UTF8.GetBytes(text), fdf ? "application/vnd.fdf" : "application/vnd.adobe.xfdf", fdf ? "form-data.fdf" : "form-data.xfdf");
    }
}
