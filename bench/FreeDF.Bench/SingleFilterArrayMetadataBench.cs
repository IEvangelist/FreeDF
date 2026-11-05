using BenchmarkDotNet.Attributes;
using PortableDocument.Core;

[MemoryDiagnoser]
public class SingleFilterArrayMetadataBench
{
    [Params(false, true)]
    public bool IndirectParameters { get; set; }

    [Params("FlateDecode", "LZWDecode")]
    public string CompressionFilter { get; set; } = "FlateDecode";

    readonly ObjectStreamMetadataBench _workload = new() { FilterCount = 1, SingleFilterArray = true };

    [GlobalSetup]
    public void Setup()
    {
        _workload.IndirectParameters = IndirectParameters;
        _workload.CompressionFilter = CompressionFilter;
        _workload.Setup();
    }

    [Benchmark]
    public PdfObject FreshMember() => _workload.FreshMember();
}
