using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using PortableDocument.Core;

[MemoryDiagnoser]
public class ObjectStreamMetadataBench
{
    [Params(1, 2)]
    public int FilterCount { get; set; }

    [Params(false, true)]
    public bool IndirectParameters { get; set; }

    byte[] _pdf = [];

    [GlobalSetup]
    public void Setup()
    {
        var decoded = "2 0 << /Type /BenchmarkMember >>"u8.ToArray();
        var differences = decoded.ToArray();
        for (var i = 1; i < decoded.Length; i++)
            differences[i] = unchecked((byte)(decoded[i] - decoded[i - 1]));
        var compressed = PdfFilters.DeflateEncode(differences);
        var encoded = FilterCount == 1 ? Encoding.Latin1.GetString(compressed)
            : Convert.ToHexString(compressed) + ">";
        var filters = FilterCount == 1 ? "/FlateDecode" : "[/ASCIIHexDecode /FlateDecode]";
        var predictor = $"<< /Predictor 2 /Columns {decoded.Length} >>";
        var parms = FilterCount == 1 ? predictor : $"[null {predictor}]";
        var data = new StringBuilder("%PDF-1.7\n");
        var root = data.Length;
        data.Append("1 0 obj\n<< /Type /Catalog /Probe 2 0 R >>\nendobj\n");
        var container = data.Length;
        data.Append($"3 0 obj\n<< /Type /ObjStm /N 1 /First 4 /Filter {filters} " +
            $"/DecodeParms {(IndirectParameters ? "5 0 R" : parms)} /Length {encoded.Length} >>\nstream\n");
        data.Append(encoded).Append("\nendstream\nendobj\n");
        var indirect = data.Length;
        data.Append($"5 0 obj\n{(IndirectParameters ? parms : "null")}\nendobj\n");
        var xref = data.Length;
        var rows = $"01{root:X8}0000" + "02000000030000" + $"01{container:X8}0000" +
            $"01{xref:X8}0000" + $"01{indirect:X8}0000>";
        data.Append($"4 0 obj\n<< /Type /XRef /Root 1 0 R /Size 6 /Index [1 5] /W [1 4 2] " +
            $"/Filter /ASCIIHexDecode /Length {rows.Length} >>\nstream\n{rows}\nendstream\nendobj\n");
        data.Append($"startxref\n{xref}\n%%EOF\n");
        _pdf = Encoding.Latin1.GetBytes(data.ToString());
        for (var i = 0; i < 2; i++)
        {
            var file = PdfFile.Load(_pdf);
            if (file.GetObject(2) is not PdfDictionary member
                || member.Get("Type") is not PdfName { Value: "BenchmarkMember" }
                || !ReferenceEquals(member, file.GetObject(2)) || file.Repaired || !file.Xref[2].Compressed)
                throw new InvalidOperationException("Object-stream filter-metadata preflight failed.");
        }
        Console.WriteLine($"PASS object-stream metadata: filters={FilterCount}, indirect={IndirectParameters}, " +
            $"bytes={_pdf.Length}, SHA256={Convert.ToHexString(SHA256.HashData(_pdf))}.");
    }

    [Benchmark]
    public PdfObject FreshMember() => PdfFile.Load(_pdf).GetObject(2);
}
