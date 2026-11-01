// Prints PDF/UA validator findings as JSON lines for tools/ua_crosscheck.py (independent veraPDF comparison).
using System.Text.Json;
using PortableDocument.Document;

// autotag <in.pdf> <out.pdf>: conservative auto-tagging (PdfEditor.AutoTagUntagged) for independent checking with veraPDF.
if (args is ["autotag", var input, var output])
{
    var editor = PdfEditor.Open(File.ReadAllBytes(input));
    Console.WriteLine(editor.AutoTagUntagged());
    File.WriteAllBytes(output, editor.Save());
    return;
}

foreach (var path in args)
{
    try
    {
        var doc = PdfDocument.Open(path);
        foreach (var part in new[] { 1, 2 })
        {
            var report = PdfValidator.ValidatePdfUA(doc, part);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                file = path,
                part,
                compliant = report.IsCompliant,
                errors = report.Errors.Select(f => f.RuleId).Distinct().Order().ToArray(),
                warnings = report.Warnings.Select(f => f.RuleId).Distinct().Order().ToArray(),
            }));
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { file = path, error = ex.GetType().Name + ": " + ex.Message }));
    }
}
