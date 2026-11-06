using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using PortableDocument.Core;

[MemoryDiagnoser]
public class XrefHybridBench
{
    [Params(64, 4096)]
    public int Rows { get; set; }

    [Params(1, 16)]
    public int Updates { get; set; }

    byte[] _pdf = [];

    [GlobalSetup]
    public void Setup()
    {
        using var data = new MemoryStream();
        void Write(string text) => data.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.7\n");
        var root = data.Position;
        Write("1 0 obj\n<< /Type /Catalog /Probe 2 0 R >>\nendobj\n");
        var target = data.Position;
        Write("2 0 obj\n<< /Type /Target >>\nendobj\n");
        var stream = data.Position;
        using var rows = new MemoryStream();
        for (var number = 0; number < Rows; number++)
        {
            var offset = number switch
            {
                0 => 4, 1 => root, 2 => target, 3 => stream,
                _ => number + 1 < Rows ? number + 1 : 0
            };
            rows.WriteByte((byte)(number is >= 1 and <= 3 ? 1 : 0));
            for (var shift = 24; shift >= 0; shift -= 8) rows.WriteByte((byte)(offset >> shift));
            rows.WriteByte((byte)(number == 0 ? 255 : 0));
            rows.WriteByte((byte)(number == 0 ? 255 : number >= 4 ? 1 : 0));
        }
        Write($"3 0 obj\n<< /Type /XRef /Size {Rows} /Root 1 0 R /W [1 4 2] /Length {rows.Length} >>\nstream\n");
        data.Write(rows.ToArray());
        Write("\nendstream\nendobj\n");
        var previous = data.Position;
        Write($"xref\n0 {Rows}\n");
        for (var number = 0; number < Rows; number++)
        {
            var offset = number switch
            {
                0 => 2, 1 => root, 2 => 4, 3 => stream,
                _ => number + 1 < Rows ? number + 1 : 0
            };
            Write($"{offset:D10} {(number is 0 or 2 ? 65535 : number >= 4 ? 1 : 0):D5} {(number is 1 or 3 ? "n" : "f")} \n");
        }
        Write($"trailer\n<< /Size {Rows} /Root 1 0 R >>\nstartxref\n{previous}\n%%EOF\n");
        for (var update = 0; update < Updates; update++)
        {
            var current = data.Position;
            Write($"xref\n1 1\n{root:D10} 00000 n \ntrailer\n<< /Size {Rows} /Root 1 0 R /Prev {previous} /XRefStm {stream} >>\nstartxref\n{current}\n%%EOF\n");
            previous = current;
        }
        _pdf = data.ToArray();
        var file = Load();
        var member = file.Resolve(file.Catalog.Get("Probe"));
        if (file.Repaired || file.Xref.Count != Rows || member is not PdfDictionary dictionary
            || dictionary.Get("Type").AsName() != "Target" || !ReferenceEquals(member, file.GetObject(2)))
            throw new InvalidOperationException("Hybrid xref benchmark preflight failed.");
        Console.WriteLine($"PASS hybrid xref: rows={Rows}, updates={Updates}, bytes={_pdf.Length}, " +
            $"SHA256={Convert.ToHexString(SHA256.HashData(_pdf))}, engine={typeof(PdfFile).Module.ModuleVersionId}.");
    }

    [Benchmark]
    public PdfFile Load() => PdfFile.Load(_pdf);
}
