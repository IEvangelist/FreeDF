using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefStreamFilterSchemaTests
{
    [Theory]
    [InlineData("true", false)]
    [InlineData("42", false)]
    [InlineData("(FlateDecode)", false)]
    [InlineData("<< >>", false)]
    [InlineData("[null]", false)]
    [InlineData("[42]", false)]
    [InlineData("[[]]", false)]
    [InlineData("[<< >>]", false)]
    [InlineData("[/FlateDecode null]", true)]
    [InlineData("[false /FlateDecode]", true)]
    [InlineData("[/FlateDecode 42]", true)]
    public void Malformed_filter_shapes_require_explicit_recovery(string filter, bool flate)
    {
        var file = PdfFile.Load(XrefStreamCryptFilterTests.Build(filter, flate));
        Assert.True(file.Repaired);
        AssertTarget(file);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("/Invalid")]
    [InlineData("(parameters)")]
    [InlineData("[42]")]
    [InlineData("[true]")]
    [InlineData("[[]]")]
    [InlineData("[/Invalid]")]
    [InlineData("[null 42]")]
    public void Malformed_parameter_shapes_require_explicit_recovery(string parameters)
    {
        var file = PdfFile.Load(XrefStreamCryptFilterTests.Build("/FlateDecode", true, decodeParms: parameters));
        Assert.True(file.Repaired);
        AssertTarget(file);
    }

    [Theory]
    [InlineData(null, false, null)]
    [InlineData("null", false, null)]
    [InlineData("[]", false, null)]
    [InlineData("/FlateDecode", true, "null")]
    [InlineData("[/FlateDecode]", true, "<< /Predictor 1 >>")]
    [InlineData("[/FlateDecode]", true, "[null]")]
    [InlineData("/FlateDecode", true, "[<< /Predictor 1 >>]")]
    public void Valid_filter_forms_keep_clean_indexed_loading(string? filter, bool flate, string? parameters)
    {
        var file = PdfFile.Load(XrefStreamCryptFilterTests.Build(filter, flate, decodeParms: parameters));
        Assert.False(file.Repaired);
        AssertTarget(file);
        Assert.False(file.Repaired);
    }

    [Theory]
    [InlineData("true", "42", false)]
    [InlineData("[/FlateDecode null]", "[null true]", true)]
    public void Ordinary_filter_schema_recovery_is_unchanged(string filter, string parameters, bool flate)
    {
        var expected = "ordinary filter schema recovery"u8.ToArray();
        var dictionary = (PdfDictionary)new PdfLexer(
            Encoding.ASCII.GetBytes($"<< /Filter {filter} /DecodeParms {parameters} >>")).ReadObject()!;
        Assert.Equal(expected, PdfFilters.Decode(flate ? PdfFilters.DeflateEncode(expected) : expected,
            PdfFilters.GetFilters(dictionary, item => item), item => item).Data);
    }

    [Theory]
    [InlineData("[<< /Predictor 2 >> null]", false)]
    [InlineData("[<< /Predictor 2 >>]", true)]
    [InlineData("[<< /Predictor 2 >> null null]", true)]
    public void Nondefault_alignment_uses_the_existing_strict_metadata_rules(string parameters, bool repaired)
    {
        static byte[] Encode(byte[] rows)
        {
            var hex = Encoding.ASCII.GetBytes(Convert.ToHexString(rows) + ">");
            var differences = hex.ToArray();
            for (var i = 1; i < hex.Length; i++)
                differences[i] = unchecked((byte)(hex[i] - hex[i - 1]));
            return PdfFilters.DeflateEncode(differences);
        }
        var file = PdfFile.Load(XrefStreamCryptFilterTests.Build("[/FlateDecode /ASCIIHexDecode]", false,
            decodeParms: parameters.Replace("/Predictor 2", "/Predictor 2 /Columns 43", StringComparison.Ordinal),
            encode: Encode));
        Assert.Equal(repaired, file.Repaired);
        AssertTarget(file);
        Assert.Equal(repaired, file.Repaired);
    }

    static void AssertTarget(PdfFile file)
    {
        var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.Resolve(file.Catalog.Get("Probe")));
    }
}
