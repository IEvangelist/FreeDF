using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;

[MemoryDiagnoser]
public class OptionalContentEventBench
{
    [Params("View", "Print", "Export")]
    public string Event { get; set; } = "View";

    OptionalContentState _state = null!;
    PdfDictionary _group = null!;
    ContentInterpreter.OptionalContentEvent _displayEvent;

    [GlobalSetup]
    public void Setup()
    {
        _displayEvent = Enum.Parse<ContentInterpreter.OptionalContentEvent>(Event);
        static PdfObject Parse(string text) => new PdfLexer(Encoding.ASCII.GetBytes(text)).ReadObject()!;
        var objects = new Dictionary<int, PdfObject>
        {
            [1] = Parse("<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [5 0 R] " +
                $"/D << /BaseState /ON /OFF [5 0 R] /AS [<< /Event /{Event} /OCGs [5 0 R] /Category [/{Event}] >>] >> >> >>"),
            [2] = Parse("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = Parse("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R >>"),
            [4] = new PdfStream(new PdfDictionary(), []),
            [5] = Parse($"<< /Type /OCG /Name (Usage) /Usage << /{Event} << /{Event}State /ON >> >> >>")
        };
        var bytes = PdfWriter.Write(objects, 1, null, options: new PdfWriteOptions { FileId = new byte[16] });
        var document = PdfDocument.Load(bytes);
        _state = new OptionalContentState(document);
        _group = _state.Groups.Single();
        var otherEvent = _displayEvent == ContentInterpreter.OptionalContentEvent.View
            ? ContentInterpreter.OptionalContentEvent.Print : ContentInterpreter.OptionalContentEvent.View;
        if (document.File.Repaired || document.Pages.Count != 1 || !IsVisible()
            || _state.IsVisible(_group, otherEvent) || _state.Warnings.Count != 0)
            throw new InvalidOperationException("Optional-content event preflight failed.");
        Console.WriteLine($"PASS optional-content event: event={Event}, bytes={bytes.Length}, " +
            $"SHA256={Convert.ToHexString(SHA256.HashData(bytes))}, " +
            $"engine={typeof(OptionalContentState).Module.ModuleVersionId}.");
    }

    [Benchmark]
    public bool IsVisible() => _state.IsVisible(_group, _displayEvent);
}
