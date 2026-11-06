using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using PortableDocument.Core;

[MemoryDiagnoser]
public class ObjectStreamBench
{
    [Params(1, 1000)]
    public int MemberCount { get; set; }

    [Params(false, true)]
    public bool Encrypted { get; set; }

    byte[] _pdf = [];
    int[] _members = [];
    PdfFile _cached = null!;

    [GlobalSetup]
    public void Setup()
    {
        var objects = new Dictionary<int, PdfObject>();
        var references = new List<PdfObject>();
        for (var index = 0; index < MemberCount; index++)
        {
            var number = index + 2;
            references.Add(new PdfRef(number, 0));
            objects[number] = new PdfDictionary(new Dictionary<string, PdfObject>
            {
                ["Type"] = new PdfName("BenchmarkMember"), ["Value"] = new PdfInteger(index)
            });
        }
        objects[1] = new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["Type"] = new PdfName("Catalog"), ["Members"] = new PdfArray(references)
        });
        var map = new Dictionary<int, int>();
        _pdf = PdfWriter.Write(objects, 1, null, options: new PdfWriteOptions
        {
            ObjectStreams = true,
            ObjectMapOut = map,
            FileId = new byte[16],
            Encryption = Encrypted ? new PdfEncryption
            {
                Method = PdfEncryptionMethod.Aes128, UserPassword = "benchmark", OwnerPassword = "owner"
            } : null
        });
        _members = Enumerable.Range(2, MemberCount).Select(number => map[number]).ToArray();
        _cached = Open();
        for (var index = 0; index < _members.Length; index++)
        {
            if (!_cached.Xref[_members[index]].Compressed
                || _cached.GetObject(_members[index]) is not PdfDictionary member
                || member.Get("Type") is not PdfName { Value: "BenchmarkMember" }
                || member.Get("Value") is not PdfInteger value || value.Value != index)
                throw new InvalidOperationException($"Object-stream preflight failed at member {index}.");
        }
        if (_cached.Repaired || FreshAllMembers() != (long)MemberCount * (MemberCount - 1) / 2)
            throw new InvalidOperationException("Object-stream preflight failed: repair or unequal member values.");
        Console.WriteLine($"PASS object stream: members={MemberCount}, encrypted={Encrypted}, " +
            $"bytes={_pdf.Length}, SHA256={Convert.ToHexString(SHA256.HashData(_pdf))}.");
    }

    PdfFile Open() => PdfFile.Load(_pdf, Encrypted ? "benchmark" : "");

    [Benchmark]
    public PdfFile LoadOnly() => Open();

    [Benchmark]
    public PdfObject FreshFirstMember() => Open().GetObject(_members[0]);

    [Benchmark]
    public long FreshAllMembers()
    {
        var file = Open();
        long sum = 0;
        foreach (var number in _members)
            sum += ((PdfInteger)((PdfDictionary)file.GetObject(number)).Get("Value")!).Value;
        return sum;
    }

    [Benchmark]
    public PdfObject CachedMember() => _cached.GetObject(_members[0]);
}
