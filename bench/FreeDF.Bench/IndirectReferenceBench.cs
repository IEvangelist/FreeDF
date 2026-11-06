using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using PortableDocument.Core;

[MemoryDiagnoser]
[ShortRunJob]
public class IndirectReferenceBench
{
    [Params(64, 4096)]
    public int Objects { get; set; }

    byte[] _pdf = [];

    [GlobalSetup]
    public void Setup()
    {
        var text = new StringBuilder("%PDF-2.0\n");
        var offsets = new List<int> { text.Length };
        text.Append("1 0 obj\n<< /Type /Catalog >>\nendobj\n");
        long expected = 0;
        for (var number = 2; number < Objects + 2; number++)
        {
            offsets.Add(text.Length);
            var target = number == Objects + 1 ? 2 : number + 1;
            expected += target;
            text.Append($"{number} 0 obj\n<< /Next {target} 0 R >>\nendobj\n");
        }
        var xref = text.Length;
        text.Append($"xref\n0 {Objects + 2}\n0000000000 65535 f \n");
        foreach (var offset in offsets) text.Append($"{offset:D10} 00000 n \n");
        text.Append($"trailer\n<< /Size {Objects + 2} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        _pdf = Encoding.ASCII.GetBytes(text.ToString());
        var file = PdfFile.Load(_pdf);
        if (file.Repaired || file.Xref.Count != Objects + 2 || LoadReferences() != expected)
            throw new InvalidOperationException("Indirect reference benchmark preflight failed.");
        Console.WriteLine($"PASS indirect references: count={Objects}, bytes={_pdf.Length}, " +
            $"SHA256={Convert.ToHexString(SHA256.HashData(_pdf))}, engine={typeof(PdfFile).Module.ModuleVersionId}.");
    }

    [Benchmark]
    public long LoadReferences()
    {
        var file = PdfFile.Load(_pdf);
        long sum = 0;
        for (var number = 2; number < Objects + 2; number++)
            sum += ((PdfRef)((PdfDictionary)file.GetObject(number)).Get("Next")!).Number;
        return sum;
    }
}
