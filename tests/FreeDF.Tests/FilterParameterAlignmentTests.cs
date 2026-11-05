using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class FilterParameterAlignmentTests
{
    public static IEnumerable<object[]> NonIntegerAlignmentParameters()
    {
        foreach (var (filter, key) in new[] { ("FlateDecode", "Predictor"), ("LZWDecode", "EarlyChange") })
            foreach (var value in new[] { "1.0", "2.0", "(1)", "/Invalid", "true", "[]", "<< >>" })
                foreach (var shape in new[] { "dictionary", "short", "long" })
                    yield return [filter, key, value, shape];
    }

    [Theory]
    [MemberData(nameof(NonIntegerAlignmentParameters))]
    public void Noninteger_values_cannot_bypass_nondefault_alignment(
        string filter, string key, string value, string shape)
    {
        var parameters = $"<< /{key} {value} >>";
        var entry = shape switch
        {
            "dictionary" => parameters,
            "short" => $"[{parameters}]",
            _ => $"[{parameters} null null]"
        };
        var dictionary = Parse($"<< /Filter [/{filter} /ASCIIHexDecode] /DecodeParms {entry} >>");
        Assert.Equal(2, PdfFilters.GetFilters(dictionary, item => item).Count);
        Assert.Throws<PdfParseException>(() => PdfFilters.GetFilters(dictionary, item => item, item => item));
    }

    [Theory]
    [InlineData("/Invalid")]
    [InlineData("2.0")]
    public void Discarded_malformed_parameter_dictionaries_cannot_publish_members(string predictor)
    {
        var decoded = "2 0 << /Type /Target >>"u8.ToArray();
        var encoded = PdfFilters.DeflateEncode(Encoding.ASCII.GetBytes(Convert.ToHexString(decoded) + ">"));
        var parameters = $"<< /Predictor {predictor} >>";
        foreach (var repair in new[] { false, true })
        {
            var ordinary = PdfFile.Load(ObjectStreamFilterNameTests.Build("[/FlateDecode /ASCIIHexDecode]",
                "null", false, repair, ordinary: true, decodeParms: parameters, encodedBytesOverride: encoded));
            Assert.Equal(decoded, ordinary.DecodeStream(Assert.IsType<PdfStream>(ordinary.GetObject(3))));
            var file = PdfFile.Load(ObjectStreamFilterNameTests.Build("[/FlateDecode /ASCIIHexDecode]",
                "null", false, repair, decodeParms: parameters, encodedBytesOverride: encoded));
            Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
        }
    }

    [Theory]
    [InlineData("FlateDecode", "Predictor", "null")]
    [InlineData("FlateDecode", "Predictor", "5 0 R")]
    [InlineData("LZWDecode", "EarlyChange", "null")]
    [InlineData("LZWDecode", "EarlyChange", "5 0 R")]
    public void Resolved_null_parameters_retain_default_alignment_compatibility(string filter, string key, string value)
    {
        var dictionary = Parse($"<< /Filter [/{filter} /ASCIIHexDecode] /DecodeParms [<< /{key} {value} >>] >>");
        PdfObject? Resolve(PdfObject? item) => item is PdfRef ? PdfNull.Instance : item;
        Assert.Equal(2, PdfFilters.GetFilters(dictionary, Resolve, Resolve).Count);
    }

    [Theory]
    [InlineData("ASCIIHexDecode", "Predictor", "1.0")]
    [InlineData("ASCIIHexDecode", "Predictor", "/Invalid")]
    [InlineData("FlateDecode", "EarlyChange", "1.0")]
    [InlineData("FlateDecode", "EarlyChange", "/Invalid")]
    public void Noninteger_detection_remains_filter_specific(string filter, string key, string value)
    {
        var dictionary = Parse($"<< /Filter [/{filter} /FlateDecode] /DecodeParms [<< /{key} {value} >>] >>");
        Assert.Equal(2, PdfFilters.GetFilters(dictionary, item => item, item => item).Count);
    }

    [Theory]
    [InlineData("FlateDecode", "Predictor")]
    [InlineData("LZWDecode", "EarlyChange")]
    public void Noninteger_values_use_the_strict_metadata_resolver(string filter, string key)
    {
        var dictionary = Parse($"<< /Filter [/{filter} /ASCIIHexDecode] /DecodeParms [<< /{key} 5 0 R >>] >>");
        PdfObject? Resolve(PdfObject? item) => item is PdfRef ? new PdfName("Invalid") : item;
        Assert.Throws<PdfParseException>(() => PdfFilters.GetFilters(dictionary, item => item, Resolve));
    }

    [Theory]
    [InlineData("dictionary")]
    [InlineData("short")]
    [InlineData("long")]
    [InlineData("indirect")]
    public void Nondefault_parameters_require_one_entry_per_filter(string shape)
    {
        var parameters = "<< /Predictor 2 >>";
        var value = shape switch
        {
            "dictionary" => parameters,
            "short" => $"[{parameters}]",
            "long" => $"[{parameters} null null]",
            _ => "5 0 R"
        };
        var dictionary = Parse($"<< /Filter [/FlateDecode /ASCIIHexDecode] /DecodeParms {value} >>");
        PdfObject? Resolve(PdfObject? item) => item is PdfRef ? Parse(parameters) : item;
        Assert.Equal(2, PdfFilters.GetFilters(dictionary, Resolve).Count);
        Assert.Throws<PdfParseException>(() => PdfFilters.GetFilters(dictionary, Resolve, Resolve));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("long")]
    public void Misaligned_nondefault_arrays_cannot_publish_members(string shape)
    {
        var decoded = "2 0 << /Type /Target >>"u8.ToArray();
        var hex = Encoding.ASCII.GetBytes(Convert.ToHexString(decoded) + ">");
        var differences = hex.ToArray();
        for (var i = 1; i < hex.Length; i++)
            differences[i] = unchecked((byte)(hex[i] - hex[i - 1]));
        var parameters = $"<< /Predictor 2 /Columns {hex.Length} >>";
        var value = shape == "short" ? $"[{parameters}]" : $"[{parameters} null null]";
        foreach (var repair in new[] { false, true })
        {
            var bytes = PdfFilters.DeflateEncode(differences);
            var ordinary = PdfFile.Load(ObjectStreamFilterNameTests.Build("[/FlateDecode /ASCIIHexDecode]",
                "null", false, repair, ordinary: true, decodeParms: value, encodedBytesOverride: bytes));
            Assert.Equal(decoded, ordinary.DecodeStream(Assert.IsType<PdfStream>(ordinary.GetObject(3))));
            var file = PdfFile.Load(ObjectStreamFilterNameTests.Build("[/FlateDecode /ASCIIHexDecode]",
                "null", false, repair, decodeParms: value, encodedBytesOverride: bytes));
            Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
        }
    }

    [Theory]
    [InlineData("<< >>")]
    [InlineData("<< /Predictor 1 /Colors 1 /BitsPerComponent 8 /Columns 1 >>")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("[<< /Predictor 1 >> null null]")]
    [InlineData("[<< /Predictor null >>]")]
    public void Default_only_compatibility_shapes_are_not_rejected(string value)
    {
        var dictionary = Parse($"<< /Filter [/FlateDecode /ASCIIHexDecode] /DecodeParms {value} >>");
        Assert.Equal(2, PdfFilters.GetFilters(dictionary, item => item, item => item).Count);
    }

    [Theory]
    [InlineData("[<< /Predictor 2 >> null]")]
    [InlineData("[null << /Predictor 2 >>]")]
    public void Aligned_nondefault_parameters_keep_their_stage_and_identity(string value)
    {
        var dictionary = Parse($"<< /Filter [/FlateDecode /FlateDecode] /DecodeParms {value} >>");
        var array = Assert.IsType<PdfArray>(dictionary.Get("DecodeParms"));
        var filters = PdfFilters.GetFilters(dictionary, item => item, item => item);
        for (var i = 0; i < 2; i++)
            Assert.Same(array[i] as PdfDictionary, filters[i].Parms);
    }

    [Theory]
    [InlineData("LZWDecode", "<< /EarlyChange 0 >>", true)]
    [InlineData("LZW", "<< /EarlyChange 0 >>", true)]
    [InlineData("LZWDecode", "<< /EarlyChange 1 >>", false)]
    [InlineData("FlateDecode", "<< /EarlyChange 0 >>", false)]
    public void EarlyChange_alignment_applies_only_to_LZW(string filter, string parameters, bool invalid)
    {
        var dictionary = Parse($"<< /Filter [/{filter} /ASCIIHexDecode] /DecodeParms [{parameters}] >>");
        if (invalid)
            Assert.Throws<PdfParseException>(() => PdfFilters.GetFilters(dictionary, item => item, item => item));
        else
            Assert.Equal(2, PdfFilters.GetFilters(dictionary, item => item, item => item).Count);
    }

    [Theory]
    [InlineData("[/ASCIIHexDecode /FlateDecode]", "[<< /Predictor 2 >>]", false)]
    [InlineData("[/FlateDecode /LZWDecode]", "[<< /EarlyChange 0 >>]", false)]
    [InlineData("[/ASCIIHexDecode /LZWDecode]", "[null << /EarlyChange 0 >> null]", true)]
    [InlineData("[/ASCIIHexDecode /FlateDecode]", "[null << /Predictor 2 >> null]", true)]
    public void Nondefault_detection_respects_each_associated_filter(string filters, string parameters, bool invalid)
    {
        var dictionary = Parse($"<< /Filter {filters} /DecodeParms {parameters} >>");
        if (invalid)
            Assert.Throws<PdfParseException>(() => PdfFilters.GetFilters(dictionary, item => item, item => item));
        else
            Assert.Equal(2, PdfFilters.GetFilters(dictionary, item => item, item => item).Count);
    }

    [Fact]
    public void Indirect_nondefault_values_use_the_strict_metadata_resolver()
    {
        var dictionary = Parse("<< /Filter [/FlateDecode /ASCIIHexDecode] /DecodeParms [<< /Predictor 5 0 R >>] >>");
        PdfObject? Resolve(PdfObject? item) => item is PdfRef ? new PdfInteger(2) : item;
        Assert.Throws<PdfParseException>(() => PdfFilters.GetFilters(dictionary, item => item, Resolve));
    }

    [Fact]
    public void Aligned_arrays_do_not_eagerly_resolve_parameter_values()
    {
        var dictionary = Parse("<< /Filter [/FlateDecode /ASCIIHexDecode] /DecodeParms [<< /Predictor 5 0 R >> null] >>");
        PdfObject? Resolve(PdfObject? item)
        {
            Assert.IsNotType<PdfRef>(item);
            return item;
        }
        Assert.Equal(2, PdfFilters.GetFilters(dictionary, Resolve, Resolve).Count);
    }

    static PdfDictionary Parse(string text) =>
        (PdfDictionary)new PdfLexer(Encoding.ASCII.GetBytes(text)).ReadObject()!;
}
