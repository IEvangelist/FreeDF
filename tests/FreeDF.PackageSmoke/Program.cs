using PortableDocument.Document;
using PortableDocument.FallbackFonts;
using PortableDocument.Render;
using Pdf = PortableDocument.Render.PortableDocument;

var document = PdfDocument.Open(args[0]);
var text = string.Concat(document.Pages.SelectMany(Pdf.ExtractText).Select(span => span.Text));
if (string.IsNullOrWhiteSpace(text))
    throw new InvalidOperationException("The package consumer did not extract text.");
var fallback = new BundledFontFallback(new DirectoryAssetSource());
if (fallback.Find("Helvetica", false, false, false, false) is null)
    throw new InvalidOperationException("The fallback-font assets were not copied to the consumer.");
var chunks = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fallback-fonts", "cjk"), "*.bin", SearchOption.AllDirectories);
if (chunks.Length == 0)
    throw new InvalidOperationException("The CJK glyph assets were not copied to the consumer.");
var options = new PdfRenderOptions { FontFallback = fallback };
foreach (var image in new[] { Pdf.RenderPage(document.Pages[0], dpi: 144), Pdf.RenderPage(document.Pages[0], options, dpi: 144) })
    if (!image.ToPng().AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        throw new InvalidOperationException("The documented rendering examples did not produce PNG images.");
Console.WriteLine($"PASS: {document.Pages.Count} pages, {text.Length} characters, bundled Latin font and {chunks.Length} CJK assets.");
