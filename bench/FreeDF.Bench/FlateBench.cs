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

    byte[] _encoded = [];

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
        Console.WriteLine($"PASS Flate: decoded={DecodedBytes}, random={RandomInput}, " +
            $"encoded={_encoded.Length}, SHA256={Convert.ToHexString(SHA256.HashData(_encoded))}.");
    }

    [Benchmark]
    public byte[] Decode() => PdfFilters.Flate(_encoded);
}
