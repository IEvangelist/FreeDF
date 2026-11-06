using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using PortableDocument.Core;

[MemoryDiagnoser]
public class OrdinaryStreamBench
{
    [Params(64, 4096)]
    public int Streams { get; set; }

    [Params(false, true)]
    public bool IndirectLength { get; set; }

    byte[] _pdf = [];

    [GlobalSetup]
    public void Setup()
    {
        var text = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int> { text.Length };
        text.Append("1 0 obj\n<< /Type /Catalog >>\nendobj\n");
        for (var number = 2; number < Streams + 2; number++)
        {
            offsets.Add(text.Length);
            var value = $"value {number:D8}";
            var length = IndirectLength ? $"{Streams + 2} 0 R" : $"{value.Length}";
            text.Append($"{number} 0 obj\n<< /Length {length} >>\nstream\n{value}\nendstream\nendobj\n");
        }
        offsets.Add(text.Length);
        text.Append($"{Streams + 2} 0 obj\n14\nendobj\n");
        var xref = text.Length;
        text.Append($"xref\n0 {Streams + 3}\n0000000000 65535 f \n");
        foreach (var offset in offsets) text.Append($"{offset:D10} 00000 n \n");
        text.Append($"trailer\n<< /Size {Streams + 3} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        _pdf = Encoding.ASCII.GetBytes(text.ToString());
        var file = PdfFile.Load(_pdf);
        long expected = 0;
        for (var number = 2; number < Streams + 2; number++)
        {
            var bytes = Encoding.ASCII.GetBytes($"value {number:D8}");
            if (file.GetObject(number) is not PdfStream stream || !stream.RawData.AsSpan().SequenceEqual(bytes))
                throw new InvalidOperationException("Ordinary stream benchmark byte preflight failed.");
            foreach (var value in bytes) expected += value;
        }
        if (file.Repaired || file.Xref.Count != Streams + 3 || LoadStreams() != expected)
            throw new InvalidOperationException("Ordinary stream benchmark load preflight failed.");
        Console.WriteLine($"PASS ordinary streams: count={Streams}, indirect={IndirectLength}, bytes={_pdf.Length}, " +
            $"SHA256={Convert.ToHexString(SHA256.HashData(_pdf))}, engine={typeof(PdfFile).Module.ModuleVersionId}.");
    }

    [Benchmark]
    public long LoadStreams()
    {
        var file = PdfFile.Load(_pdf);
        long sum = 0;
        for (var number = 2; number < Streams + 2; number++)
            foreach (var value in ((PdfStream)file.GetObject(number)).RawData)
                sum += value;
        return sum;
    }
}
