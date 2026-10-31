using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using PortableDocument.Content;

return;

[SupportedOSPlatform("browser")]
public static partial class BrowserEditor
{
    // Browser locale must never reach PDF output (decimal commas, Turkish casing); ICU stays loaded for Unicode normalization.
    static readonly CultureInfo Culture = UseInvariantCulture();
    static readonly EditorSession Session = new(new FontCatalog(), new BrowserFontFallback());
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public sealed record Command(string Method, string Path, Dictionary<string, string> Query, string? Body, string? FileName);

    [JSExport]
    public static string Request(string command)
    {
        Command? c = null;
        try
        {
            c = JsonSerializer.Deserialize<Command>(command, Json) ?? throw new ArgumentException("Missing editor command.");
            return Execute(c, c.Body is null ? null : Convert.FromBase64String(c.Body));
        }
        catch (Exception e)
        {
            return Error(c, e);
        }
    }

    [JSExport]
    public static string RequestBinary(
        string method,
        string path,
        string query,
        JSObject body,
        string? fileName)
    {
        Command? c = null;
        using var requestBody = body;
        try
        {
            c = new(method, path, JsonSerializer.Deserialize<Dictionary<string, string>>(query, Json) ?? [], null, fileName);
            return Execute(c, requestBody.GetPropertyAsByteArray("bytes") ?? throw new ArgumentException("Missing binary request body."));
        }
        catch (Exception e)
        {
            return Error(c, e);
        }
    }

    static CultureInfo UseInvariantCulture()
    {
        var culture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        return culture;
    }

    static string Diagnostics() => JsonSerializer.Serialize(new
    {
        culture = CultureInfo.CurrentCulture.Name,
        decimalFormat = 1234.5.ToString("0.##"),
        normalizesDecomposed = "e\u0301".Normalize(NormalizationForm.FormC) == "\u00e9",
        decomposes = "\u00e9".Normalize(NormalizationForm.FormD) == "e\u0301",
        upper = "i".ToUpper(),
        invariantGlobalization = AppContext.TryGetSwitch("System.Globalization.Invariant", out var invariant) && invariant,
    }, Json);

    static string Execute(Command c, byte[]? body)
    {
        if (c.Path == "/api/_diagnostics") return JsonSerializer.Serialize(new { data = Convert.ToBase64String(Encoding.UTF8.GetBytes(Diagnostics())), contentType = "application/json" }, Json);
        try
        {
            var reply = Session.Handle(c.Method, c.Path, c.Query, body, c.FileName);
            return JsonSerializer.Serialize(new { data = Convert.ToBase64String(reply.Data), contentType = reply.ContentType, fileName = reply.FileName, performance = Session.LastRenderMetrics }, Json);
        }
        catch (Exception e)
        {
            return Error(c, e);
        }
    }

    static string Error(Command? c, Exception e)
    {
        if (e is System.Security.Cryptography.CryptographicException or PlatformNotSupportedException && c?.Method == "POST" && c.Path is "/api/open" or "/api/import" or "/api/inspect")
        {
            var message = "Encrypted PDFs require the server editor. This browser cannot open this protection; no decrypted recovery copy was stored. " + e.Message;
            Console.Error.WriteLine(message);
            return JsonSerializer.Serialize(new { error = message }, Json);
        }
        Console.Error.WriteLine(e is NotSupportedException ? e.Message : e);
        return JsonSerializer.Serialize(new { error = e.Message }, Json);
    }
}

/// <summary>Licensed in-repo substitutes; embedded PDF fonts still take precedence.</summary>
sealed class BrowserFontFallback : IFontFallback
{
    readonly TrueTypeFont regular = Load("Vera.ttf"), bold = Load("VeraBd.ttf");
    static TrueTypeFont Load(string name)
    {
        using var stream = typeof(BrowserFontFallback).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing bundled font: " + name);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return new(ms.ToArray());
    }
    public TrueTypeFont Find(string baseFont, bool isBold, bool italic, bool fixedPitch, bool serif) => isBold ? bold : regular;
}
