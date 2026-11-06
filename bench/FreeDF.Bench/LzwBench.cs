using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using PortableDocument.Core;

[MemoryDiagnoser]
public class LzwBench
{
    [Params("Literal", "Repeated")]
    public string Pattern { get; set; } = "Literal";

    [Params(false, true)]
    public bool Strict { get; set; }

    byte[] _encoded = [];
    readonly List<(string Name, PdfDictionary? Parms)> _filters = [("LZWDecode", null)];

    [GlobalSetup]
    public void Setup()
    {
        var source = new byte[65536];
        var codes = new List<int> { 256 };
        if (Pattern == "Literal")
        {
            new Random(42).NextBytes(source);
            for (var i = 0; i < source.Length; i++)
            {
                if (i != 0 && i % 3839 == 0) codes.Add(256);
                codes.Add(source[i]);
            }
        }
        else
        {
            Array.Fill(source, (byte)65);
            codes.Add(65);
            var remaining = source.Length - 1;
            var length = 2;
            var next = 258;
            while (remaining >= length)
            {
                codes.Add(next++);
                remaining -= length++;
            }
            codes.AddRange(Enumerable.Repeat(65, remaining));
        }
        codes.Add(257);
        var encoded = new List<byte>();
        var buffer = 0;
        var buffered = 0;
        var width = 9;
        var count = 258;
        var previous = false;
        foreach (var code in codes)
        {
            buffer = buffer << width | code;
            buffered += width;
            while (buffered >= 8)
            {
                buffered -= 8;
                encoded.Add((byte)(buffer >> buffered));
            }
            buffer &= (1 << buffered) - 1;
            if (code == 256) { width = 9; count = 258; previous = false; continue; }
            if (code == 257) break;
            if (previous) count++;
            previous = true;
            if (width < 12 && count + 1 >= 1 << width) width++;
        }
        if (buffered != 0) encoded.Add((byte)(buffer << (8 - buffered)));
        _encoded = encoded.ToArray();
        if (!Decode().AsSpan().SequenceEqual(source))
            throw new InvalidOperationException("LZW benchmark preflight failed.");
        Console.WriteLine($"PASS LZW: pattern={Pattern}, strict={Strict}, bytes={_encoded.Length}, " +
            $"SHA256={Convert.ToHexString(SHA256.HashData(_encoded))}, " +
            $"decodedSHA256={Convert.ToHexString(SHA256.HashData(source))}, " +
            $"engine={typeof(PdfFilters).Module.ModuleVersionId}.");
    }

    [Benchmark]
    public byte[] Decode() => PdfFilters.Decode(_encoded, _filters, value => value, Strict).Data;
}
