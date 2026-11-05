using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using PortableDocument.Core;

[MemoryDiagnoser]
public class XrefFilterBench
{
    [Params("FlateDecode", "LZWDecode")]
    public string Filter { get; set; } = "FlateDecode";

    [Params(64, 4096)]
    public int Rows { get; set; }

    byte[] _pdf = [];

    [GlobalSetup]
    public void Setup()
    {
        using var pdf = new MemoryStream();
        void Write(string text) => pdf.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.7\n");
        var root = pdf.Position;
        Write("1 0 obj\n<< /Type /Catalog /Probe 3 0 R >>\nendobj\n");
        var target = pdf.Position;
        Write("3 0 obj\n<< /Type /Target >>\nendobj\n");
        var xref = pdf.Position;
        var rows = new byte[Rows * 7];
        for (var number = 0; number < Rows; number++)
        {
            var offset = number switch
            {
                0 => 4, 1 => root, 2 => xref, 3 => target,
                _ => number + 1 < Rows ? number + 1 : 0
            };
            if (number is >= 1 and <= 3) rows[number * 7] = 1;
            else rows[number * 7 + 6] = 1;
            for (var index = 0; index < 4; index++)
                rows[number * 7 + 1 + index] = (byte)(offset >> (24 - index * 8));
        }
        rows[5] = rows[6] = 255;
        var encoded = Filter == "FlateDecode" ? PdfFilters.DeflateEncode(rows) : LiteralLzw(rows);
        Write($"2 0 obj\n<< /Type /XRef /Root 1 0 R /Size {Rows} /W [1 4 2] " +
            $"/Filter /{Filter} /Length {encoded.Length} >>\nstream\n");
        pdf.Write(encoded);
        Write($"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n");
        _pdf = pdf.ToArray();
        var file = Load();
        var member = file.Resolve(file.Catalog.Get("Probe"));
        if (file.Repaired || file.Xref.Count != Rows || member is not PdfDictionary dictionary
            || dictionary.Get("Type").AsName() != "Target" || !ReferenceEquals(member, file.GetObject(3)))
            throw new InvalidOperationException("Cross-reference filter benchmark preflight failed.");
        Console.WriteLine($"PASS xref filter: filter={Filter}, rows={Rows}, bytes={_pdf.Length}, " +
            $"SHA256={Convert.ToHexString(SHA256.HashData(_pdf))}, " +
            $"decodedSHA256={Convert.ToHexString(SHA256.HashData(rows))}, " +
            $"engine={typeof(PdfFile).Module.ModuleVersionId}.");
    }

    static byte[] LiteralLzw(byte[] bytes)
    {
        using var output = new MemoryStream();
        var bits = 0;
        var buffer = 0;
        void Code(int code)
        {
            buffer = buffer << 9 | code;
            bits += 9;
            while (bits >= 8)
            {
                bits -= 8;
                output.WriteByte((byte)(buffer >> bits));
            }
        }
        for (var i = 0; i < bytes.Length; i++)
        {
            if (i % 200 == 0) Code(256);
            Code(bytes[i]);
        }
        Code(257);
        if (bits != 0) output.WriteByte((byte)(buffer << (8 - bits)));
        return output.ToArray();
    }

    [Benchmark]
    public PdfFile Load() => PdfFile.Load(_pdf);
}
