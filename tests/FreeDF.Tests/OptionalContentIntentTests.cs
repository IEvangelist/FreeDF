using System.Text;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using DocumentRenderer = PortableDocument.Render.PortableDocument;

namespace FreeDF.Tests;

public class OptionalContentIntentTests
{
    [Theory]
    [InlineData("", "/Intent /Design", true)]
    [InlineData("/Intent null", "/Intent /Design", true)]
    [InlineData("/Intent 99 0 R", "/Intent /Design", true)]
    [InlineData("/Intent /View", "/Intent /Design", true)]
    [InlineData("/Intent /View", "/Intent [/Design /View]", false)]
    [InlineData("/Intent /View", "/Intent []", true)]
    [InlineData("/Intent /Design", "", true)]
    [InlineData("/Intent /Design", "/Intent null", true)]
    [InlineData("/Intent /Design", "/Intent 99 0 R", true)]
    [InlineData("/Intent /Design", "/Intent /Design", false)]
    [InlineData("/Intent [/View /Design]", "/Intent /Design", false)]
    [InlineData("/Intent /All", "/Intent /Design", false)]
    [InlineData("/Intent [/All]", "/Intent /Design", false)]
    [InlineData("/Intent []", "", true)]
    [InlineData("/Intent []", "/Intent /Design", true)]
    [InlineData("/Intent 9 0 R", "/Intent 10 0 R", false)]
    public void Direct_groups_use_only_matching_configuration_intents(
        string configuration, string group, bool visible) =>
        AssertRendering(configuration, group, "6 0 R", "", visible);

    [Theory]
    [InlineData("AnyOn")]
    [InlineData("AllOn")]
    [InlineData("AnyOff")]
    [InlineData("AllOff")]
    public void Excluded_members_have_no_effect_on_any_policy(string policy)
    {
        AssertRendering("/Intent /View", "/Intent /Design", "8 0 R",
            $"/OCGs [6 0 R] /P /{policy}", true);
        AssertRendering("/Intent []", "", "8 0 R",
            $"/OCGs [5 0 R 6 0 R 7 0 R] /P /{policy}", true);
        AssertRendering("/Intent /View", "/Intent /Design", "8 0 R",
            $"/OCGs [5 0 R 6 0 R] /P /{policy}", policy is "AnyOn" or "AllOn");
        AssertRendering("/Intent /View", "/Intent /Design", "8 0 R",
            $"/OCGs [6 0 R 7 0 R] /P /{policy}", policy is "AnyOff" or "AllOff");
        AssertRendering("/Intent /View", "/Intent /Design", "8 0 R",
            $"/OCGs 6 0 R /P /{policy}", true);
    }

    [Theory]
    [InlineData("[/Not 6 0 R]", true)]
    [InlineData("[/And 6 0 R]", true)]
    [InlineData("[/Or 6 0 R]", true)]
    [InlineData("[/And 5 0 R 6 0 R]", true)]
    [InlineData("[/Or 6 0 R 7 0 R]", false)]
    [InlineData("[/And [/Not 6 0 R] 5 0 R]", true)]
    [InlineData("[/Or [/Not 6 0 R] 7 0 R]", false)]
    [InlineData("[/Not [/Or 6 0 R 7 0 R]]", true)]
    [InlineData("[/Not [/And 6 0 R 5 0 R]]", false)]
    [InlineData("[/Not [/Not 6 0 R]]", true)]
    public void Excluded_expression_operands_are_neutral_not_boolean_ON(string expression, bool visible) =>
        AssertRendering("/Intent /View", "/Intent /Design", "8 0 R", $"/VE {expression}", visible);

    [Theory]
    [InlineData("[/Not 5 0 R]")]
    [InlineData("[/And 5 0 R 6 0 R]")]
    [InlineData("[/Or [/Not 5 0 R] 7 0 R]")]
    public void Empty_configuration_intent_makes_valid_expressions_visible(string expression) =>
        AssertRendering("/Intent []", "/Intent /Design", "8 0 R", $"/VE {expression}", true);

    [Fact]
    public void Manual_state_and_radio_locks_remain_separate_from_render_participation()
    {
        var document = Load("/Intent /View /RBGroups [[5 0 R 6 0 R]] /Locked [5 0 R]",
            "/Intent /Design", "6 0 R", "");
        var state = new OptionalContentState(document, "Selected");
        var group = state.Groups.Single(g => (g.Get("Name") as PdfString)?.Text == "Subject");
        Assert.False(state.IsVisible(group));
        Assert.Throws<InvalidOperationException>(() => state.SetVisibility(group, true));
        Assert.False(state.IsVisible(group));
        AssertPixels(DocumentRenderer.RenderPage(document.Pages[0],
            new PdfRenderOptions { OptionalContentState = state }, 72), true);

        document = Load("/Intent /View", "/Intent /Design", "6 0 R", "");
        state = new OptionalContentState(document, "Selected");
        group = state.Groups.Single(g => (g.Get("Name") as PdfString)?.Text == "Subject");
        foreach (var on in new[] { true, false })
        {
            state.SetVisibility(group, on);
            Assert.Equal(on, state.IsVisible(group));
            AssertPixels(DocumentRenderer.RenderPage(document.Pages[0],
                new PdfRenderOptions { OptionalContentState = state }, 72), true);
        }
    }

    [Theory]
    [InlineData("/Intent 123", "", false)]
    [InlineData("", "/Intent 123", false)]
    [InlineData("/Intent [123]", "", true)]
    [InlineData("", "/Intent [123]", true)]
    public void Invalid_intent_values_are_diagnosed(string configuration, string group, bool visible)
    {
        var document = Load(configuration, group, "6 0 R", "");
        var rendered = DocumentRenderer.RenderPage(document.Pages[0],
            new PdfRenderOptions { OptionalContentConfigurationName = "Selected" }, 72);
        Assert.Contains(rendered.Warnings, warning => warning.StartsWith("invalid optional-content Intent"));
        var offset = (50 * rendered.Width + 50) * 4;
        Assert.Equal(visible ? new byte[] { 255, 0, 0, 255 } : [255, 255, 255, 255],
            rendered.Rgba[offset..(offset + 4)]);
    }

    static void AssertRendering(string configuration, string group, string layer, string membership, bool visible)
    {
        foreach (var surface in new[] { "Marked", "Form", "Image", "Annotation" })
            AssertRendering(configuration, group, layer, membership, visible, surface);
    }

    static void AssertRendering(string configuration, string group, string layer, string membership, bool visible,
        string surface)
    {
        var document = Load(configuration, group, layer, membership, surface);
        foreach (var state in new OptionalContentState?[] { null, new(document, "Selected") })
        {
            AssertPixels(DocumentRenderer.RenderPage(document.Pages[0], new PdfRenderOptions
            {
                OptionalContentConfigurationName = state is null ? "Selected" : null,
                OptionalContentState = state
            }, 72), visible);
        }
        using var raster = new SoftwareRasterizer(200, 200);
        var properties = Assert.IsType<PdfDictionary>(document.File.Resolve(document.File.Catalog.Get("OCProperties")));
        var configs = Assert.IsType<PdfArray>(document.File.Resolve(properties.Get("Configs")));
        var interpreter = new ContentInterpreter(document.File, raster)
        {
            OptionalContentConfiguration = Assert.IsType<PdfDictionary>(document.File.Resolve(configs[0]))
        };
        interpreter.Render(document.Pages[0], Matrix.Identity);
        var pixels = raster.ToRgba();
        var offset = (50 * 200 + 50) * 4;
        Assert.Equal(visible ? new byte[] { 255, 0, 0, 255 } : [255, 255, 255, 255],
            pixels[offset..(offset + 4)]);
        Assert.Empty(interpreter.Warnings);
    }

    static void AssertPixels(RenderedPage rendered, bool visible)
    {
        var offset = (50 * rendered.Width + 50) * 4;
        Assert.Equal(visible ? new byte[] { 255, 0, 0, 255 } : [255, 255, 255, 255],
            rendered.Rgba[offset..(offset + 4)]);
        Assert.Empty(rendered.Warnings);
    }

    static PdfDocument Load(string configuration, string group, string layer, string membership,
        string surface = "Marked")
    {
        static PdfObject Parse(string text) => new PdfLexer(Encoding.ASCII.GetBytes(text)).ReadObject()!;
        var objects = new Dictionary<int, PdfObject>
        {
            [1] = Parse("<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [5 0 R 6 0 R 7 0 R] " +
                "/D << /BaseState /ON /OFF [6 0 R 7 0 R] >> /Configs [11 0 R] >> >>"),
            [2] = Parse("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = Parse("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R " +
                (surface == "Annotation" ? "/Annots [14 0 R] " : "") +
                $"/Resources << /Properties << /Layer {layer} >> /XObject << /Form 12 0 R /Image 13 0 R >> >> >>"),
            [4] = new PdfStream(new PdfDictionary(),
                Encoding.ASCII.GetBytes(surface switch
                {
                    "Form" => "/Form Do",
                    "Image" => "200 0 0 200 0 0 cm /Image Do",
                    "Annotation" => "",
                    _ => "/OC /Layer BDC 1 0 0 rg 0 0 200 200 re f EMC"
                })),
            [5] = Parse("<< /Type /OCG /Name (On) >>"),
            [6] = Parse($"<< /Type /OCG /Name (Subject) {group} >>"),
            [7] = Parse("<< /Type /OCG /Name (Off) >>"),
            [8] = Parse($"<< /Type /OCMD {membership} >>"),
            [9] = Parse("[/Design /View]"),
            [10] = Parse("/Design"),
            [11] = Parse("<< /Name (Selected) /BaseState /ON /OFF [6 0 R 7 0 R] " + configuration + " >>"),
            [12] = new PdfStream((PdfDictionary)Parse("<< /Type /XObject /Subtype /Form /BBox [0 0 200 200] " +
                (surface == "Form" ? $"/OC {layer} " : "") + "/Resources << >> >>"),
                Encoding.ASCII.GetBytes("1 0 0 rg 0 0 200 200 re f")),
            [13] = new PdfStream((PdfDictionary)Parse("<< /Type /XObject /Subtype /Image /Width 1 /Height 1 " +
                $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /OC {layer} >>"), [255, 0, 0]),
            [14] = Parse("<< /Type /Annot /Subtype /Stamp /Rect [0 0 200 200] " +
                $"/OC {layer} /AP << /N 12 0 R >> >>")
        };
        var document = PdfDocument.Load(PdfWriter.Write(objects, 1, null,
            options: new PdfWriteOptions { FileId = new byte[16] }));
        Assert.False(document.File.Repaired);
        return document;
    }
}
