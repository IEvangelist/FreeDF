using System.IO.Compression;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using PortableDocument.Core;

[MemoryDiagnoser]
public class FlateBench
{
    [Params(256, 1048576)]
    public int DecodedBytes { get; set; }

    [Params(false, true)]
    public bool RandomInput { get; set; }

    [Params(false, true)]
    public bool Strict { get; set; }

    byte[] _encoded = [];
    static readonly List<(string Name, PdfDictionary? Parms)> Filters = [("FlateDecode", null)];

    [GlobalSetup]
    public void Setup()
    {
        var decoded = new byte[DecodedBytes];
        if (RandomInput) new Random(42).NextBytes(decoded);
        else
            for (var index = 0; index < decoded.Length; index++) decoded[index] = (byte)(index % 17);
        _encoded = PdfFilters.DeflateEncode(decoded);
        if (!decoded.AsSpan().SequenceEqual(Decode()))
            throw new InvalidOperationException("Flate benchmark preflight produced unequal decoded bytes.");
        Console.WriteLine($"PASS Flate: decoded={DecodedBytes}, random={RandomInput}, strict={Strict}, " +
            $"encoded={_encoded.Length}, SHA256={Convert.ToHexString(SHA256.HashData(_encoded))}.");
    }

    [Benchmark]
    public byte[] Decode() => Strict
        ? PdfFilters.Decode(_encoded, Filters, x => x, strictContentFilters: true).Data
        : PdfFilters.Flate(_encoded);
}

[MemoryDiagnoser]
public class FlateEncodeBench
{
    [Params(0, 256, 1048576)]
    public int DecodedBytes { get; set; }

    byte[] _source = [];

    [GlobalSetup]
    public void Setup()
    {
        _source = new byte[DecodedBytes];
        new Random(42).NextBytes(_source);
        var encoded = Encode();
        using var zlib = new ZLibStream(new MemoryStream(encoded), CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        if (encoded.Length < 8 || !_source.AsSpan().SequenceEqual(output.ToArray())
            || DecodedBytes == 0 && !encoded.AsSpan().SequenceEqual(new byte[] { 0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01 }))
            throw new InvalidOperationException("Flate encoding preflight produced unequal decoded bytes.");
        Console.WriteLine($"PASS Flate encode: decoded={DecodedBytes}, encoded={encoded.Length}, " +
            $"SHA256={Convert.ToHexString(SHA256.HashData(encoded))}.");
    }

    [Benchmark]
    public byte[] Encode() => PdfFilters.DeflateEncode(_source);
}
