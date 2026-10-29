using AdoBye.Content;
using AdoBye.Document;

namespace AdoBye.Render;

public sealed record RenderedPage(int Width, int Height, byte[] Rgba, IReadOnlyList<string> Warnings)
{
    public byte[] ToPng() => PngWriter.Encode(Width, Height, Rgba);
}

public static class PdfRenderer
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
            throw new AdoBye.Core.PdfParseException($"Page size {nw:0.##}x{nh:0.##} at scale {scale:0.##} is not renderable");
        width = Math.Max(1, (int)pw);
        height = Math.Max(1, (int)ph);
        if ((long)width * height > SoftwareRasterizer.MaxPixels) throw new AdoBye.Core.PdfParseException($"Page raster {width}x{height} exceeds the pixel limit");
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

    public static RenderedPage RenderPage(PdfPage page, double dpi = 96, IRenderBackendFactory? factory = null)
    {
        var scale = dpi / 72.0 * page.UserUnit;
        var m = PageMatrix(page, scale, out var w, out var h);
        using var raster = new SoftwareRasterizer(w, h);
        var interp = new ContentInterpreter(page.Document.File, raster);
        interp.Render(page, m);
        return new RenderedPage(w, h, raster.ToRgba(), interp.Warnings);
    }
}

public interface IRenderBackendFactory { IRenderBackend Create(int width, int height); }
