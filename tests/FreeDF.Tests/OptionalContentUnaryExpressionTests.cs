using System.Text;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using DocumentRenderer = PortableDocument.Render.PortableDocument;

namespace FreeDF.Tests;

public class OptionalContentUnaryExpressionTests
{
    [Theory]
    [InlineData("And", false, false)]
    [InlineData("And", false, true)]
    [InlineData("And", true, false)]
    [InlineData("And", true, true)]
    [InlineData("Or", false, false)]
    [InlineData("Or", false, true)]
    [InlineData("Or", true, false)]
    [InlineData("Or", true, true)]
    public void And_and_Or_accept_one_operand(string op, bool on, bool nested)
    {
        var expression = $"[/{op} 5 0 R]";
        if (nested) expression = $"[/And [/Or {expression}]]";
        AssertRendering(expression, on, on);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Not_keeps_its_exactly_one_operand_semantics(bool on) =>
        AssertRendering("[/Not 5 0 R]", on, !on);

    [Theory]
    [InlineData("And")]
    [InlineData("Or")]
    public void Empty_boolean_expressions_remain_invalid_and_hidden(string op) =>
        AssertRendering($"[/{op}]", true, false, invalid: true);

    [Theory]
    [InlineData(64)]
    [InlineData(65)]
    public void Single_operand_nesting_respects_the_existing_depth_budget(int levels)
    {
        var expression = "5 0 R";
        for (var i = 0; i < levels; i++) expression = $"[/And {expression}]";
        AssertRendering(expression, true, levels == 64, invalid: levels > 64);
    }

    [Theory]
    [InlineData(9999)]
    [InlineData(10000)]
    public void Flat_expressions_respect_the_existing_node_budget(int operands) =>
        AssertRendering("[/And " + string.Concat(Enumerable.Repeat("5 0 R ", operands)) + "]",
            true, operands == 9999, invalid: operands > 9999);

    static void AssertRendering(string expression, bool on, bool visible, bool invalid = false)
    {
        static PdfObject Parse(string text) => new PdfLexer(Encoding.ASCII.GetBytes(text)).ReadObject()!;
        var objects = new Dictionary<int, PdfObject>
        {
            [1] = Parse("<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [5 0 R] " +
                $"/D << /BaseState /ON {(on ? "" : "/OFF [5 0 R]")} >> >> >>"),
            [2] = Parse("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = Parse("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R " +
                "/Resources << /Properties << /Layer 6 0 R >> >> >>"),
            [4] = new PdfStream(new PdfDictionary(),
                Encoding.ASCII.GetBytes("/OC /Layer BDC 1 0 0 rg 0 0 200 200 re f EMC")),
            [5] = Parse("<< /Type /OCG /Name (Operand) >>"),
            [6] = Parse($"<< /Type /OCMD /VE {expression} >>")
        };
        var document = PdfDocument.Load(PdfWriter.Write(objects, 1, null,
            options: new PdfWriteOptions { FileId = new byte[16] }));
        Assert.False(document.File.Repaired);
        foreach (var state in new OptionalContentState?[] { null, new(document) })
        {
            var rendered = DocumentRenderer.RenderPage(document.Pages[0],
                new PdfRenderOptions { OptionalContentState = state }, 72);
            var pixel = (50 * rendered.Width + 50) * 4;
            Assert.Equal(visible ? new byte[] { 255, 0, 0, 255 } : [255, 255, 255, 255],
                rendered.Rgba[pixel..(pixel + 4)]);
            if (invalid)
                Assert.Contains("invalid optional-content visibility expression", rendered.Warnings);
            else
                Assert.Empty(rendered.Warnings);
        }
    }
}
