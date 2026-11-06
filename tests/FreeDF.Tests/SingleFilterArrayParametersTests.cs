using PortableDocument.Core;

namespace FreeDF.Tests;

public class SingleFilterArrayParametersTests
{
    [Theory]
    [InlineData("direct", false)]
    [InlineData("direct", true)]
    [InlineData("array", false)]
    [InlineData("array", true)]
    [InlineData("name", false)]
    [InlineData("name", true)]
    [InlineData("parameters", false)]
    [InlineData("parameters", true)]
    public void Single_filter_arrays_apply_nondefault_dictionary_parameters(string indirectField, bool ordinary)
    {
        var expected = "2 0 << /Type /Target >>"u8.ToArray();
        var differences = expected.ToArray();
        for (var i = 1; i < expected.Length; i++)
            differences[i] = unchecked((byte)(expected[i] - expected[i - 1]));
        var encoded = PdfFilters.DeflateEncode(differences);
        var parameters = $"<< /Predictor 2 /Columns {expected.Length} >>";
        var filter = indirectField switch { "array" => "5 0 R", "name" => "[5 0 R]", _ => "[/FlateDecode]" };
        var indirect = indirectField switch
        {
            "array" => "[/FlateDecode]", "name" => "/FlateDecode", "parameters" => parameters, _ => "null"
        };
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamFilterNameTests.Build(filter, indirect, false, repair, ordinary,
                decodeParms: indirectField == "parameters" ? "5 0 R" : parameters, encodedBytesOverride: encoded));
            if (ordinary)
                Assert.Equal(expected, file.DecodeStream(Assert.IsType<PdfStream>(file.GetObject(3))));
            else
            {
                var member = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
                Assert.Equal("Target", member.Get("Type").AsName());
                Assert.Same(member, file.Resolve(file.Catalog.Get("Probe")));
            }
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData("FlateDecode", "FlateDecode")]
    [InlineData("Fl", "FlateDecode")]
    [InlineData("LZWDecode", "LZWDecode")]
    [InlineData("LZW", "LZWDecode")]
    public void Single_filter_array_keeps_exact_parameter_identity(string name, string canonical)
    {
        var parameters = new PdfDictionary(new Dictionary<string, PdfObject> { ["Predictor"] = new PdfInteger(2) });
        var stream = new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["Filter"] = new PdfArray([new PdfName(name)]), ["DecodeParms"] = parameters
        });
        foreach (var strict in new[] { false, true })
        {
            var filters = strict ? PdfFilters.GetFilters(stream, x => x, x => x) : PdfFilters.GetFilters(stream, x => x);
            var filter = Assert.Single(filters);
            Assert.Equal(canonical, filter.Name);
            Assert.Same(parameters, filter.Parms);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("<< >>")]
    [InlineData("<< /Predictor 1 >>")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("[<< >>]")]
    public void Single_filter_array_preserves_supported_default_shapes(string? parameters)
    {
        var expected = "2 0 << /Type /Target >>"u8.ToArray();
        foreach (var ordinary in new[] { false, true })
            foreach (var repair in new[] { false, true })
            {
                var file = PdfFile.Load(ObjectStreamFilterNameTests.Build("[/FlateDecode]", "null", false, repair,
                    ordinary, decodeParms: parameters, encodedBytesOverride: PdfFilters.DeflateEncode(expected)));
                if (ordinary)
                    Assert.Equal(expected, file.DecodeStream(Assert.IsType<PdfStream>(file.GetObject(3))));
                else
                    Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")))
                        .Get("Type").AsName());
                Assert.Equal(repair, file.Repaired);
            }
    }
}
