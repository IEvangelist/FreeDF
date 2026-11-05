using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using PortableDocument.Core;

[MemoryDiagnoser]
public class XrefClassicBench
{
    [Params(64, 4096)]
    public int Rows { get; set; }

    [Params(false, true)]
    public bool FixedEntry { get; set; }

    byte[] _pdf = [];

    [GlobalSetup]
    public void Setup()
    {
        var text = new StringBuilder("%PDF-1.7\n");
        var root = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Probe 2 0 R >>\nendobj\n");
        var target = text.Length;
        text.Append("2 0 obj\n<< /Type /Target >>\nendobj\n");
        var xref = text.Length;
        text.Append($"xref\n0 {Rows}\n");
        for (var number = 0; number < Rows; number++)
        {
            var offset = number switch
            {
                0 => 3, 1 => root, 2 => target,
                _ => number + 1 < Rows ? number + 1 : 0
            };
            var generation = number switch { 0 => 65535, 1 or 2 => 0, _ => 1 };
            var status = number is 1 or 2 ? "n" : "f";
            text.Append(FixedEntry
                ? $"{offset:D10} {generation:D5} {status} \n"
                : $"{offset} {generation} {status}\n");
        }
        text.Append($"trailer\n<< /Size {Rows} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        _pdf = Encoding.ASCII.GetBytes(text.ToString());
        var file = Load();
        var member = file.Resolve(file.Catalog.Get("Probe"));
        if (file.Repaired || file.Xref.Count != Rows || member is not PdfDictionary dictionary
            || dictionary.Get("Type").AsName() != "Target" || !ReferenceEquals(member, file.GetObject(2)))
            throw new InvalidOperationException("Classic xref benchmark preflight failed.");
        Console.WriteLine($"PASS classic xref: rows={Rows}, fixed={FixedEntry}, bytes={_pdf.Length}, " +
            $"SHA256={Convert.ToHexString(SHA256.HashData(_pdf))}, engine={typeof(PdfFile).Module.ModuleVersionId}.");
    }

    [Benchmark]
    public PdfFile Load() => PdfFile.Load(_pdf);
}
