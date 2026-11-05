using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefStreamFilterDirectnessTests
{
    [Theory]
    [InlineData("4 0 R", null, "null")]
    [InlineData("4 0 R", null, "/FlateDecode")]
    [InlineData("4 0 R", null, "[/FlateDecode]")]
    [InlineData("9 0 R", null, null)]
    [InlineData("/FlateDecode", "4 0 R", "null")]
    [InlineData("/FlateDecode", "4 0 R", "<< /Predictor 1 >>")]
    [InlineData("/FlateDecode", "4 0 R", "[<< /Predictor 1 >>]")]
    [InlineData("/FlateDecode", "9 0 R", null)]
    public void Indirect_xref_filter_metadata_requires_recovery(string filter, string? parameters, string? indirect)
    {
        var file = PdfFile.Load(XrefStreamCryptFilterTests.Build(filter,
            flate: parameters is not null, decodeParms: parameters, indirectObject: indirect));
        Assert.True(file.Repaired);
        AssertTarget(file);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("<< >>")]
    [InlineData("<< /Predictor 1 >>")]
    [InlineData("[null]")]
    [InlineData("[<< /Predictor 1 >>]")]
    public void Direct_default_parameters_preserve_encoded_index_loading(string parameters)
    {
        var file = PdfFile.Load(XrefStreamCryptFilterTests.Build("/FlateDecode", true, decodeParms: parameters));
        Assert.False(file.Repaired);
        AssertTarget(file);
        Assert.False(file.Repaired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("[]")]
    public void Unencoded_streams_do_not_apply_encoded_parameter_directness(string? filter)
    {
        var file = PdfFile.Load(XrefStreamCryptFilterTests.Build(filter, false,
            decodeParms: "4 0 R", indirectObject: "<< >>"));
        Assert.False(file.Repaired);
        AssertTarget(file);
        Assert.False(file.Repaired);
    }

    [Fact]
    public void Ordinary_stream_metadata_still_resolves_indirect_values()
    {
        var expected = "ordinary indirect filter parameters"u8.ToArray();
        var parameters = new PdfDictionary { Entries = { ["Predictor"] = new PdfInteger(1) } };
        var dictionary = new PdfDictionary
        {
            Entries = { ["Filter"] = new PdfRef(4, 0), ["DecodeParms"] = new PdfRef(5, 0) }
        };
        PdfObject? Resolve(PdfObject? value) => value is PdfRef reference
            ? reference.Number == 4 ? new PdfName("FlateDecode") : parameters : value;
        var filters = PdfFilters.GetFilters(dictionary, Resolve);
        Assert.Same(parameters, Assert.Single(filters).Parms);
        Assert.Equal(expected, PdfFilters.Decode(PdfFilters.DeflateEncode(expected), filters, Resolve).Data);
    }

    static void AssertTarget(PdfFile file)
    {
        var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.Resolve(file.Catalog.Get("Probe")));
    }
}
