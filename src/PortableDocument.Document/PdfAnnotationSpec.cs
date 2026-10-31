namespace PortableDocument.Document;

/// <summary>An RGB(A) bitmap embedded as a custom stamp image. <see cref="Rgb"/> is Width*Height*3 bytes; <see cref="Alpha"/> is Width*Height bytes.</summary>
public sealed record PdfAnnotationImage(int Width, int Height, byte[] Rgb, byte[]? Alpha = null);

public sealed record PdfAnnotationAttachment(string FileName, byte[] Data, string? MimeType = null);

/// <summary>
/// Annotation authoring input and read model. For <see cref="PdfEditor.UpdateAnnotation"/> every null member means "unchanged"; an empty
/// <see cref="Color"/>/<see cref="InteriorColor"/> array clears the colour. Coordinates are default user space of the page.
/// </summary>
public sealed record PdfAnnotationSpec
{
    public string? Subtype { get; init; }
    public double[]? Rect { get; init; }
    public double[]? Color { get; init; }
    public double[]? InteriorColor { get; init; }
    public double? Opacity { get; init; }
    public double? BorderWidth { get; init; }
    /// <summary>S, D, B, I or U (12.5.4).</summary>
    public string? BorderStyle { get; init; }
    public double[]? DashPattern { get; init; }
    public string? Contents { get; init; }
    /// <summary>Rich text (/RC) is stored inert; appearances use <see cref="Contents"/>.</summary>
    public string? RichContents { get; init; }
    public string? Author { get; init; }
    public string? Subject { get; init; }
    public string? Name { get; init; }
    /// <summary>Eight numbers per quad in TL, TR, BL, BR order (what Acrobat and pdf.js write).</summary>
    public double[]? QuadPoints { get; init; }
    public double[]? Vertices { get; init; }
    public double[]? Line { get; init; }
    public IReadOnlyList<double[]>? InkList { get; init; }
    public string? LineStart { get; init; }
    public string? LineEnd { get; init; }
    public bool? Cloud { get; init; }
    public string? Icon { get; init; }
    public string? StampName { get; init; }
    public double? FontSize { get; init; }
    /// <summary>Free text colour (written into /DA); <see cref="Color"/> is the border colour.</summary>
    public double[]? TextColor { get; init; }
    public int? Quadding { get; init; }
    public string? State { get; init; }
    public string? StateModel { get; init; }
    /// <summary>Object number of the annotation this one replies to (/IRT).</summary>
    public int? InReplyTo { get; init; }
    /// <summary>R (reply) or Group.</summary>
    public string? ReplyType { get; init; }
    public bool? Open { get; init; }
    public string? OverlayText { get; init; }
    public string? Symbol { get; init; }
    public PdfAnnotationImage? Image { get; init; }
    public PdfAnnotationAttachment? Attachment { get; init; }
    public DateTimeOffset? Created { get; init; }
    public DateTimeOffset? Modified { get; init; }
}

/// <summary>An annotation as stored: object number, page and its spec-shaped properties.</summary>
public sealed record PdfAnnotationInfo(int Number, int Page, int? ParentNumber, int? PopupNumber, PdfAnnotationSpec Spec);
