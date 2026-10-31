using PortableDocument.Core;

namespace PortableDocument.Document;

// Form-field authoring model (ISO 32000-2:2020 12.7). Nothing is executed: no scripts, calculations, validation or submission.

public enum PdfFormFieldKind { Text, MultilineText, CombText, PasswordText, Checkbox, Radio, ComboBox, ListBox, PushButton, Signature }

/// <summary>One /Opt entry of a choice field. <see cref="Display"/> falls back to the export value.</summary>
public sealed record PdfFormOption(string Export, string? Display = null);

/// <summary>Page tab-order mode (/Tabs): row, column or structure order.</summary>
public enum PdfFormTabOrder { Unspecified, Row, Column, Structure }

public enum PdfFlattenScope { All, Selected, ReadOnly }

public sealed record PdfFlattenOptions(PdfFlattenScope Scope = PdfFlattenScope.All, IReadOnlyCollection<string>? Names = null);

public sealed record PdfFlattenResult(int Flattened, int Kept);

public enum PdfFormDataFormat { Xfdf, Fdf }

public sealed record PdfFormImportResult(int Applied, IReadOnlyList<string> Skipped, bool Truncated = false);

public sealed record PdfFormFieldHandle(string FullName, PdfRef Field, PdfRef Widget, int WidgetIndex = 0);

/// <summary>Everything the editor needs to describe, create or update one widget. Colours are 1 (gray), 3 (RGB) or 4 (CMYK) components in 0..1.</summary>
public sealed record PdfFormFieldSpec(PdfFormFieldKind Kind, string Name, int Page, PdfRect Rect)
{
    public string? Tooltip { get; init; }
    public bool Required { get; init; }
    public bool ReadOnly { get; init; }
    /// <summary>Text/choice value, or the checked state name for check boxes and radio groups. Multi-select list boxes take one value per line.</summary>
    public string? Value { get; init; }
    public string? DefaultValue { get; init; }
    public int MaxLength { get; init; }
    /// <summary>A standard-14 resource name (Helv, HeBo, TiRo, Cour, ...) or the name under which <see cref="FontObject"/> is registered in /DR.</summary>
    public string FontName { get; init; } = "Helv";
    public PdfRef? FontObject { get; init; }
    /// <summary>0 selects automatic sizing.</summary>
    public double FontSize { get; init; }
    public IReadOnlyList<double>? TextColor { get; init; }
    public IReadOnlyList<double>? BorderColor { get; init; }
    public IReadOnlyList<double>? BackgroundColor { get; init; }
    public double BorderWidth { get; init; } = 1;
    /// <summary>S solid, D dashed, B beveled, I inset, U underline.</summary>
    public string BorderStyle { get; init; } = "S";
    /// <summary>0 left, 1 centre, 2 right.</summary>
    public int Alignment { get; init; }
    public IReadOnlyList<PdfFormOption>? Options { get; init; }
    /// <summary>The "on" appearance state of a check box or radio kid.</summary>
    public string ExportValue { get; init; } = "Yes";
    public string? Caption { get; init; }
    public bool Editable { get; init; }
    public bool MultiSelect { get; init; }
}
