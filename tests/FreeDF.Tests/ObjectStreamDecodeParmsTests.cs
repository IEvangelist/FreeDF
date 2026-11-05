using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamDecodeParmsTests
{
    [Theory]
    [InlineData("true", "null")]
    [InlineData("42", "null")]
    [InlineData("(parms)", "null")]
    [InlineData("/Parms", "null")]
    [InlineData("[42]", "null")]
    [InlineData("[true]", "null")]
    [InlineData("[(parms)]", "null")]
    [InlineData("[/Parms]", "null")]
    [InlineData("[[<< >>]]", "null")]
    [InlineData("[null 42]", "null")]
    [InlineData("[<< >> 42]", "null")]
    [InlineData("[5 0 R]", "42")]
    [InlineData("5 0 R", "true")]
    [InlineData("5 0 R", "5 0 R")]
    [InlineData("5 0 R", "[false]")]
    public void Malformed_parameter_types_cannot_publish_object_stream_members(string parms, string indirect)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(parms, indirect, repair));
            Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
            Assert.IsType<PdfNull>(file.GetObject(2));
        }
    }

    [Theory]
    [InlineData(null, "null")]
    [InlineData("null", "null")]
    [InlineData("7 0 R", "null")]
    [InlineData("5 0 R", "null")]
    [InlineData("<< >>", "null")]
    [InlineData("<< /Predictor 1 >>", "null")]
    [InlineData("[]", "null")]
    [InlineData("[null]", "null")]
    [InlineData("[<< >>]", "null")]
    [InlineData("[7 0 R]", "null")]
    [InlineData("5 0 R", "<< >>")]
    [InlineData("5 0 R", "[null]")]
    [InlineData("[5 0 R]", "<< >>")]
    public void Supported_parameter_shapes_preserve_exact_member_and_cache(string? parms, string indirect)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(parms, indirect, repair));
            var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.Equal("Target", target.Get("Type").AsName());
            Assert.Same(target, file.Resolve(file.Catalog.Get("Probe")));
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData("42")]
    [InlineData("[false]")]
    [InlineData("[<< >> 42]")]
    public void Ordinary_stream_parameter_recovery_is_unchanged(string parms)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(parms, "null", repair, ordinary: true));
            var stream = Assert.IsType<PdfStream>(file.GetObject(3));
            Assert.Equal("2 0 << /Type /Target >>"u8.ToArray(), file.DecodeStream(stream));
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(32)]
    public void Parameter_aliases_preserve_the_existing_32_reference_limit(int aliases)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build("5 0 R", "<< >>", repair, aliases: aliases));
            var target = file.Resolve(file.Catalog.Get("Probe"));
            if (aliases < 32)
            {
                Assert.Equal("Target", Assert.IsType<PdfDictionary>(target).Get("Type").AsName());
                Assert.Equal(repair, file.Repaired);
            }
            else
            {
                Assert.IsType<PdfNull>(target);
                Assert.True(file.Repaired);
                Assert.False(file.Xref.ContainsKey(2));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Filter_pipeline_applies_indirect_nondefault_parameters_at_the_correct_stage(bool indirectArray)
    {
        var decoded = "2 0 << /Type /Target >>"u8.ToArray();
        var differences = decoded.ToArray();
        for (var i = 1; i < decoded.Length; i++)
            differences[i] = unchecked((byte)(decoded[i] - decoded[i - 1]));
        var encoded = Convert.ToHexString(PdfFilters.DeflateEncode(differences)) + ">";
        var dictionary = $"<< /Predictor 2 /Columns {decoded.Length} >>";
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamFilterNameTests.Build("[/ASCIIHexDecode /FlateDecode]",
                indirectArray ? $"[null {dictionary}]" : dictionary, true, repair,
                decodeParms: indirectArray ? "5 0 R" : "[null 5 0 R]", encodedOverride: encoded));
            var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.Equal("Target", target.Get("Type").AsName());
            Assert.Same(target, file.Resolve(file.Catalog.Get("Probe")));
            Assert.Equal(repair, file.Repaired);
        }
    }

    static byte[] Build(string? parms, string indirect, bool repair, bool ordinary = false, int aliases = 0)
        => ObjectStreamFilterNameTests.Build("/ASCIIHexDecode", indirect, true, repair,
            ordinary, aliases, parms);
}
