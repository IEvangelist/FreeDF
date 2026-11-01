using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using PortableDocument.Content;
using PortableDocument.Document;
using PortableDocument.FallbackFonts;
using PortableDocument.Render;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;

/// <summary>
/// Read-only render replica surface for pool workers. A replica never edits: it opens the primary editor's saved snapshot bytes
/// (GET /api/snapshot) and renders whole pages or horizontal bands that are bit-identical to the primary's whole-page raster.
/// Lives in its own class so booting a replica never constructs the editor session.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class BrowserReplica
{
    static readonly System.Globalization.CultureInfo Culture = BrowserCulture.UseInvariant();
    static PdfDocument? document;
    // Same lazy fallback-font/CJK asset path as the primary engine (verified against the same manifest digests); assets are fetched by replica-worker.js on first use.
    static readonly ProvidedAssetSource FontAssets = new();
    static readonly PdfRenderOptions Options = new() { FontFallback = new BundledFontFallback(FontAssets, new BrowserFontFallback()), ImageCache = new ImageDecodeCache() };

    [JSExport]
    public static string TakeFontRequests() => JsonSerializer.Serialize(FontAssets.TakeRequests());

    [JSExport]
    public static void ProvideFontAsset(string path, JSObject body)
    {
        using var b = body;
        FontAssets.Provide(path, b.GetPropertyAsByteArray("bytes"));
    }

    [JSExport]
    public static void FontAssetUnavailable(string path) => FontAssets.Provide(path, null);

    [JSExport]
    public static void ProvideFontManifest(JSObject body)
    {
        using var b = body;
        FontAssets.Manifest = AssetManifest.TryParse(b.GetPropertyAsByteArray("bytes") ?? []);
    }

    [JSExport]
    public static int Load(byte[] pdf)
    {
        document = PdfDocument.Load(pdf);
        Options.ImageCache!.Clear();
        return document.Pages.Count;
    }

    /// <summary>"width,height" of the whole-page raster.</summary>
    [JSExport]
    public static string Size(int page, int dpi)
    {
        var (w, h) = DocumentRenderer.RasterSize(Page(page), dpi);
        return w + "," + h;
    }

    /// <summary>RGBA rows [y0, y1) of the page raster.</summary>
    [JSExport]
    public static byte[] Band(int page, int dpi, int y0, int y1) =>
        DocumentRenderer.RenderBand(Page(page), Options, dpi, y0, y1).Rgba;

    [JSExport]
    public static byte[] Png(int page, int dpi) => DocumentRenderer.RenderPage(Page(page), Options, dpi).ToPng();

    static PdfPage Page(int page) =>
        (document ?? throw new InvalidOperationException("No snapshot is loaded.")).Pages[page];
}
