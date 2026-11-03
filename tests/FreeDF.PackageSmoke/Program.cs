using PortableDocument.Document;
using PortableDocument.FallbackFonts;
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
Console.WriteLine($"PASS: {document.Pages.Count} pages, {text.Length} characters, bundled Latin font and {chunks.Length} CJK assets.");
