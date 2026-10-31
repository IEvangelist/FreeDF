using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;

namespace PortableDocument.Render;

public sealed record RenderedPage(int Width, int Height, byte[] Rgba, IReadOnlyList<string> Warnings)
{
    public byte[] ToPng() => PngWriter.Encode(Width, Height, Rgba);
}

public sealed record PdfRenderOptions
{
    /// <summary>Host-provided substitutes for non-embedded fonts; null uses installed system fonts.</summary>
    public IFontFallback? FontFallback { get; init; }
    /// <summary>Name of a catalog /OCProperties /Configs entry; omitted selects the default /D configuration.</summary>
    public string? OptionalContentConfigurationName { get; init; }
    /// <summary>Reusable interactive layer state; cannot be combined with a configuration name.</summary>
    public OptionalContentState? OptionalContentState { get; init; }
    /// <summary>Explicit language and user identities for usage rules; omitted fields leave those rules unapplied.</summary>
    public OptionalContentContext? OptionalContentContext { get; init; }
    /// <summary>Usage event to apply when rendering optional content.</summary>
    public ContentInterpreter.OptionalContentEvent OptionalContentDisplayEvent { get; init; } =
        ContentInterpreter.OptionalContentEvent.View;
    /// <summary>
    /// Simulates ExtGState overprint (/OP, /op, /OPM) with an RGB approximation (see <see cref="ContentInterpreter.OverprintSimulation"/>); off by default.
    /// </summary>
    public bool OverprintSimulation { get; init; }
    /// <summary>Reuses decoded image XObjects across renders of the same document (for example the bands of one page).</summary>
    public ImageDecodeCache? ImageCache { get; init; }
}

public static class PortableDocument
{
    /// <summary>Page user space -> device pixels (y down), honoring CropBox, /Rotate and /UserUnit.</summary>
    public static Matrix PageMatrix(PdfPage page, double scale, out int width, out int height)
    {
        var box = page.CropBox;
        double w = box.Width, h = box.Height;
        var m = Matrix.Translate(-box.Left, -box.Bottom);
        double nw = w, nh = h;
        switch (page.Rotate)
        {
            case 90: m = m.Then(new Matrix(0, -1, 1, 0, 0, w)); nw = h; nh = w; break;
            case 180: m = m.Then(new Matrix(-1, 0, 0, -1, w, h)); break;
            case 270: m = m.Then(new Matrix(0, 1, -1, 0, h, 0)); nw = h; nh = w; break;
        }
        m = m.Then(new Matrix(1, 0, 0, -1, 0, nh)).Then(Matrix.Scale(scale, scale));
        var pw = Math.Ceiling(nw * scale - 1e-6);
        var ph = Math.Ceiling(nh * scale - 1e-6);
        if (!(pw < SoftwareRasterizer.MaxDimension) || !(ph < SoftwareRasterizer.MaxDimension) || double.IsNaN(pw) || double.IsNaN(ph))
            throw new global::PortableDocument.Core.PdfParseException($"Page size {nw:0.##}x{nh:0.##} at scale {scale:0.##} is not renderable");
        width = Math.Max(1, (int)pw);
        height = Math.Max(1, (int)ph);
        if ((long)width * height > SoftwareRasterizer.MaxPixels) throw new global::PortableDocument.Core.PdfParseException($"Page raster {width}x{height} exceeds the pixel limit");
        return m;
    }

    /// <summary>Text-showing operators of the page's own content (not forms), positioned in CropBox space: points, origin top-left, y down, unrotated.</summary>
    public static List<ContentInterpreter.TextSpan> ExtractText(PdfPage page) => ExtractText(page, includeAnnotationAppearances: false);

    internal static List<ContentInterpreter.TextSpan> ExtractText(PdfPage page, bool includeAnnotationAppearances) =>
        ExtractText(page, includeAnnotationAppearances, false);

    /// <summary>As <see cref="ExtractText(PdfPage)"/>, but text inside marked content carrying /ActualText is reported as that replacement text (shaped ligatures, right-to-left runs, reordered Indic clusters).</summary>
    public static List<ContentInterpreter.TextSpan> ExtractText(PdfPage page, bool includeAnnotationAppearances, bool honorActualText)
    {
        var box = page.CropBox;
        using var raster = new SoftwareRasterizer(1, 1);
        var interp = new ContentInterpreter(page.Document.File, raster)
        {
            TextSpans = [], DrawAnnotations = includeAnnotationAppearances, DrawGraphics = false,
            SynthesizeAnnotationAppearances = false, CaptureAnnotationAppearanceText = includeAnnotationAppearances,
            HonorActualText = honorActualText
        };
        interp.Render(page, new Matrix(1, 0, 0, -1, -box.Left, box.Top));
        return interp.TextSpans;
    }

    /// <summary>Text spans plus painted graphics objects, in the same CropBox space as <see cref="ExtractText"/>.</summary>
    public static (List<ContentInterpreter.TextSpan> Spans, List<ContentInterpreter.PageObject> Objects) Analyze(PdfPage page, bool collectHidden = false)
    {
        var box = page.CropBox;
        using var raster = new SoftwareRasterizer(1, 1);
        var interp = new ContentInterpreter(page.Document.File, raster) { TextSpans = [], Objects = [], DrawAnnotations = false, DrawGraphics = false, CollectHidden = collectHidden, CaptureInlineImages = collectHidden };
        interp.Render(page, new Matrix(1, 0, 0, -1, -box.Left, box.Top));
        return (interp.TextSpans, interp.Objects);
    }

    public static RenderedPage RenderPage(PdfPage page, double dpi = 96, IRenderBackendFactory? factory = null) =>
        RenderPage(page, new PdfRenderOptions(), dpi, factory);

    public static RenderedPage RenderPage(
        PdfPage page,
        PdfRenderOptions options,
        double dpi = 96,
        IRenderBackendFactory? factory = null) => RenderBand(page, options, dpi, 0, int.MaxValue);

    /// <summary>Pixel size of the whole-page raster at <paramref name="dpi"/>.</summary>
    public static (int Width, int Height) RasterSize(PdfPage page, double dpi)
    {
        PageMatrix(page, dpi / 72.0 * page.UserUnit, out var w, out var h);
        return (w, h);
    }

    /// <summary>
    /// Renders device rows [<paramref name="y0"/>, <paramref name="y1"/>) of the page raster (clamped to the page). The rows are bit-identical
    /// to the same rows of <see cref="RenderPage(PdfPage, PdfRenderOptions, double, IRenderBackendFactory?)"/>; only band memory is allocated.
    /// </summary>
    public static RenderedPage RenderBand(PdfPage page, PdfRenderOptions options, double dpi, int y0, int y1)
    {
        var scale = dpi / 72.0 * page.UserUnit;
        var m = PageMatrix(page, scale, out var w, out var fullHeight);
        y0 = Math.Clamp(y0, 0, fullHeight - 1);
        y1 = Math.Clamp(y1, y0 + 1, fullHeight);
        var h = y1 - y0;
        using var raster = new SoftwareRasterizer(w, h, y0, fullHeight);
        var interp = new ContentInterpreter(page.Document.File, raster, options.FontFallback);
        if (options.OptionalContentState is not null && options.OptionalContentConfigurationName is not null)
            throw new ArgumentException("Specify optional-content state or a configuration name, not both.", nameof(options));
        var state = options.OptionalContentState ?? new OptionalContentState(page.Document, options.OptionalContentConfigurationName);
        if (!ReferenceEquals(state.Document, page.Document))
            throw new ArgumentException("Optional-content state belongs to another document.", nameof(options));
        interp.OptionalContentState = state;
        interp.OptionalContentDisplayEvent = options.OptionalContentDisplayEvent;
        interp.OptionalContentZoom = scale;
        interp.OptionalContentContext = options.OptionalContentContext;
        interp.OverprintSimulation = options.OverprintSimulation;
        interp.ImageCache = options.ImageCache;
        interp.Render(page, m);
        return new RenderedPage(w, h, raster.ToRgba(), interp.Warnings);
    }
}

public interface IRenderBackendFactory { IRenderBackend Create(int width, int height); }
