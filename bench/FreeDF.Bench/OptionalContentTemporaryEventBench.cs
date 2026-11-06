using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;

[MemoryDiagnoser]
public class OptionalContentTemporaryEventBench
{
    [Params("Print", "Export")]
    public string Event { get; set; } = "Print";
    [Params(false, true)]
    public bool Manual { get; set; }
    [Params(false, true)]
    public bool Recommendation { get; set; }

    OptionalContentState _state = null!;
    PdfDictionary _group = null!;
    ContentInterpreter.OptionalContentEvent _displayEvent;

    [GlobalSetup]
    public void Setup()
    {
        _displayEvent = Enum.Parse<ContentInterpreter.OptionalContentEvent>(Event);
        static PdfObject Parse(string text) => new PdfLexer(Encoding.ASCII.GetBytes(text)).ReadObject()!;
        var usage = Recommendation ? $"/{Event} << /{Event}State /OFF >>" : "";
        var objects = new Dictionary<int, PdfObject>
        {
            [1] = Parse("<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [5 0 R] " +
                "/D << /BaseState /ON /OFF [5 0 R] /AS [" +
                "<< /Event /View /OCGs [5 0 R] /Category [/View] >> " +
                $"<< /Event /{Event} /OCGs [5 0 R] /Category [/{Event}] >>] >> >> >>"),
            [2] = Parse("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = Parse("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R >>"),
            [4] = new PdfStream(new PdfDictionary(), []),
            [5] = Parse($"<< /Type /OCG /Name (Usage) /Usage << /View << /ViewState /ON >> {usage} >> >>")
        };
        var bytes = PdfWriter.Write(objects, 1, null, options: new PdfWriteOptions { FileId = new byte[16] });
        var document = PdfDocument.Load(bytes);
        _state = new OptionalContentState(document);
        _group = _state.Groups.Single();
        if (Manual) _state.SetVisibility(_group, true);
        if (document.File.Repaired || document.Pages.Count != 1 || IsVisible() != !Recommendation
            || !_state.IsVisible(_group) || _state.Warnings.Count != 0)
            throw new InvalidOperationException("Temporary optional-content event preflight failed.");
        Console.WriteLine($"PASS temporary optional-content event: event={Event}, manual={Manual}, " +
            $"recommendation={Recommendation}, visible={!Recommendation}, bytes={bytes.Length}, " +
            $"SHA256={Convert.ToHexString(SHA256.HashData(bytes))}, " +
            $"engine={typeof(OptionalContentState).Module.ModuleVersionId}.");
    }

    [Benchmark]
    public bool IsVisible() => _state.IsVisible(_group, _displayEvent);
}
