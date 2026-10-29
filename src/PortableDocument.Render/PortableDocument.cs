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
    /// <summary>Name of a catalog /OCProperties /Configs entry; omitted selects the default /D configuration.</summary>
    public string? OptionalContentConfigurationName { get; init; }
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
    public static List<ContentInterpreter.TextSpan> ExtractText(PdfPage page)
    {
        var box = page.CropBox;
        using var raster = new SoftwareRasterizer(1, 1);
        var interp = new ContentInterpreter(page.Document.File, raster)
        {
            TextSpans = [], DrawAnnotations = false, DrawGraphics = false
        };
        interp.Render(page, new Matrix(1, 0, 0, -1, -box.Left, box.Top));
        return interp.TextSpans;
    }

    /// <summary>Text spans plus painted graphics objects, in the same CropBox space as <see cref="ExtractText"/>.</summary>
    public static (List<ContentInterpreter.TextSpan> Spans, List<ContentInterpreter.PageObject> Objects) Analyze(PdfPage page)
    {
        var box = page.CropBox;
        using var raster = new SoftwareRasterizer(1, 1);
        var interp = new ContentInterpreter(page.Document.File, raster) { TextSpans = [], Objects = [], DrawAnnotations = false };
        interp.Render(page, new Matrix(1, 0, 0, -1, -box.Left, box.Top));
        return (interp.TextSpans, interp.Objects);
    }

    public static RenderedPage RenderPage(
        PdfPage page,
        double dpi = 96,
        IRenderBackendFactory? factory = null,
        PdfRenderOptions? options = null)
    {
        var scale = dpi / 72.0 * page.UserUnit;
        var m = PageMatrix(page, scale, out var w, out var h);
        using var raster = new SoftwareRasterizer(w, h);
        var interp = new ContentInterpreter(page.Document.File, raster);
        var (defaultConfiguration, selectedConfiguration) =
            ResolveOptionalContentConfiguration(page, options?.OptionalContentConfigurationName);
        interp.DefaultOptionalContentConfiguration = defaultConfiguration;
        interp.OptionalContentConfiguration = selectedConfiguration;
        interp.Render(page, m);
        return new RenderedPage(w, h, raster.ToRgba(), interp.Warnings);
    }

    static (PdfDictionary? Default, PdfDictionary? Selected) ResolveOptionalContentConfiguration(PdfPage page, string? name)
    {
        var file = page.Document.File;
        if (file.Resolve(file.Catalog.Get("OCProperties")) is not PdfDictionary properties)
        {
            if (name is null) return (null, null);
            throw new ArgumentException($"No optional-content configuration named '{name}' exists.", nameof(name));
        }

        var defaultConfiguration = file.Resolve(properties.Get("D")) as PdfDictionary;
        if (name is null || defaultConfiguration is not null
            && file.Resolve(defaultConfiguration.Get("Name")) is PdfString defaultName
            && defaultName.Text == name)
            return (defaultConfiguration, defaultConfiguration);

        if (file.Resolve(properties.Get("Configs")) is PdfArray configurations)
            foreach (var value in configurations.Items)
                if (file.Resolve(value) is PdfDictionary configuration
                    && file.Resolve(configuration.Get("Name")) is PdfString configName
                    && configName.Text == name)
                    return (defaultConfiguration, configuration);

        throw new ArgumentException($"No optional-content configuration named '{name}' exists.", nameof(name));
    }
}

public interface IRenderBackendFactory { IRenderBackend Create(int width, int height); }
