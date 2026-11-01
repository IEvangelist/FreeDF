using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Document;
using PortableDocument.Render;

// Usage: FreeDF.Demo <outDir> <input.pdf>...
// Merges the inputs, moves the last page to the front, rotates the second page, saves, reloads and renders every page to PNG.
var outDir = args[0];
Directory.CreateDirectory(outDir);
var ed = PdfEditor.Create();
foreach (var path in args.Skip(1))
{
    var src = PdfDocument.Load(File.ReadAllBytes(path));
    ed.ImportPages(src);
    Console.WriteLine($"imported {Path.GetFileName(path)}: {src.Pages.Count} page(s)");
}
ed.MovePage(ed.PageCount - 1, 0);
ed.SetRotation(1, 90);
ed.SetInfo("Title", "Edited by FreeDF.Demo");
var bytes = ed.Save();
var saved = Path.Combine(outDir, "edited.pdf");
File.WriteAllBytes(saved, bytes);
Console.WriteLine($"saved {saved} ({bytes.Length} bytes, {ed.PageCount} pages)");

var doc = PdfDocument.Load(bytes);
for (var i = 0; i < doc.Pages.Count; i++)
{
    var p = DocumentRenderer.RenderPage(doc.Pages[i], 72);
    var png = Path.Combine(outDir, $"page{i + 1}.png");
    File.WriteAllBytes(png, p.ToPng());
    Console.WriteLine($"rendered {png} ({p.Width}x{p.Height})");
}
