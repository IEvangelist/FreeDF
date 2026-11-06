using System.Text;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using DocumentRenderer = PortableDocument.Render.PortableDocument;

namespace FreeDF.Tests;

[Collection("Optional-content allocation")]
public class OptionalContentTemporaryEventTests
{
    [Theory]
    [InlineData("Print", false, false)]
    [InlineData("Print", false, true)]
    [InlineData("Print", true, false)]
    [InlineData("Print", true, true)]
    [InlineData("Export", false, false)]
    [InlineData("Export", false, true)]
    [InlineData("Export", true, false)]
    [InlineData("Export", true, true)]
    public void Temporary_recommendations_override_manual_state_without_persisting(
        string eventName, bool manual, bool recommended)
    {
        var state = Load(eventName, $"/{eventName} << /{eventName}State /{(recommended ? "ON" : "OFF")} >>");
        var group = state.Groups[0];
        state.SetVisibility(group, manual);
        Assert.Equal(manual, state.IsVisible(group));
        var displayEvent = Enum.Parse<ContentInterpreter.OptionalContentEvent>(eventName);
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(recommended, state.IsVisible(group, displayEvent));
            AssertPixel(state, displayEvent, recommended);
            Assert.Equal(manual, state.IsVisible(group));
            AssertPixel(state, ContentInterpreter.OptionalContentEvent.View, manual);
        }
        Assert.Empty(state.Warnings);
    }

    [Theory]
    [InlineData("Print", "")]
    [InlineData("Export", "")]
    [InlineData("Print", "/Print << >>")]
    [InlineData("Export", "/Export << >>")]
    public void Missing_temporary_recommendations_preserve_current_view_state(string eventName, string usage)
    {
        var state = Load(eventName, usage);
        var group = state.Groups[0];
        var displayEvent = Enum.Parse<ContentInterpreter.OptionalContentEvent>(eventName);
        Assert.True(state.IsVisible(group));
        Assert.True(state.IsVisible(group, displayEvent));
        AssertPixel(state, displayEvent, true);
        AssertPixel(state, displayEvent, true, useState: false);
        AssertInterpreter(state, displayEvent, true);
        Assert.True(state.IsVisible(group));
        state.SetVisibility(group, false);
        Assert.False(state.IsVisible(group, displayEvent));
        AssertPixel(state, displayEvent, false);
        Assert.False(state.IsVisible(group));
        Assert.Empty(state.Warnings);
    }

    [Theory]
    [InlineData("Print")]
    [InlineData("Export")]
    public void Temporary_events_do_not_persist_into_each_other(string eventName)
    {
        var state = Load(eventName, $"/{eventName} << /{eventName}State /OFF >>");
        var selected = Enum.Parse<ContentInterpreter.OptionalContentEvent>(eventName);
        var other = selected == ContentInterpreter.OptionalContentEvent.Print
            ? ContentInterpreter.OptionalContentEvent.Export : ContentInterpreter.OptionalContentEvent.Print;
        state.SetVisibility(state.Groups[0], true);
        Assert.False(state.IsVisible(state.Groups[0], selected));
        Assert.True(state.IsVisible(state.Groups[0], other));
        Assert.True(state.IsVisible(state.Groups[0]));
    }

    [Theory]
    [InlineData("Print", false)]
    [InlineData("Export", false)]
    [InlineData("Print", true)]
    [InlineData("Export", true)]
    public void Empty_temporary_selection_preserves_the_current_state(string eventName, bool manual)
    {
        var state = Load(eventName, $"/{eventName} << /{eventName}State /OFF >>",
            selectedGroups: "");
        var displayEvent = Enum.Parse<ContentInterpreter.OptionalContentEvent>(eventName);
        Assert.True(state.IsVisible(state.Groups[0], displayEvent));
        AssertInterpreter(state, displayEvent, true);
        state.SetVisibility(state.Groups[0], manual);
        Assert.Equal(manual, state.IsVisible(state.Groups[0], displayEvent));
        AssertPixel(state, displayEvent, manual);
        Assert.Equal(manual, state.IsVisible(state.Groups[0]));
    }

    [Theory]
    [InlineData("Print", false)]
    [InlineData("Export", false)]
    [InlineData("Print", true)]
    [InlineData("Export", true)]
    public void Temporary_recommendations_are_conjoined_over_manual_state(string eventName, bool separate)
    {
        var applications = separate
            ? $"<< /Event /{eventName} /Category [/View] /OCGs [5 0 R] >> " +
              $"<< /Event /{eventName} /Category [/{eventName}] /OCGs [5 0 R] >>"
            : $"<< /Event /{eventName} /Category [/View /{eventName}] /OCGs [5 0 R] >>";
        var state = Load(eventName, $"/{eventName} << /{eventName}State /OFF >>", applications: applications);
        var displayEvent = Enum.Parse<ContentInterpreter.OptionalContentEvent>(eventName);
        state.SetVisibility(state.Groups[0], true);
        Assert.False(state.IsVisible(state.Groups[0], displayEvent));
        AssertPixel(state, displayEvent, false);
        Assert.True(state.IsVisible(state.Groups[0]));
    }

    [Theory]
    [InlineData("Print", false)]
    [InlineData("Export", false)]
    [InlineData("Print", true)]
    [InlineData("Export", true)]
    public void Repeated_temporary_fallback_queries_do_not_allocate(string eventName, bool manual)
    {
        var state = Load(eventName, "");
        var group = state.Groups[0];
        var displayEvent = Enum.Parse<ContentInterpreter.OptionalContentEvent>(eventName);
        if (manual) state.SetVisibility(group, true);
        for (var i = 0; i < 8192; i++) state.IsVisible(group, displayEvent);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var visible = 0;
        for (var i = 0; i < 4096; i++)
            if (state.IsVisible(group, displayEvent)) visible++;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(4096, visible);
        Assert.Empty(state.Warnings);
    }

    static OptionalContentState Load(string eventName, string usage, string selectedGroups = "5 0 R",
        string? applications = null)
    {
        static PdfObject Parse(string text) => new PdfLexer(Encoding.ASCII.GetBytes(text)).ReadObject()!;
        var objects = new Dictionary<int, PdfObject>
        {
            [1] = Parse("<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [5 0 R] " +
                "/D << /BaseState /ON /OFF [5 0 R] /AS [" +
                "<< /Event /View /Category [/View] /OCGs [5 0 R] >> " +
                (applications ?? $"<< /Event /{eventName} /Category [/{eventName}] /OCGs [{selectedGroups}] >>") +
                "] >> >> >>"),
            [2] = Parse("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = Parse("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R " +
                "/Resources << /Properties << /Layer 5 0 R >> >> >>"),
            [4] = new PdfStream(new PdfDictionary(),
                Encoding.ASCII.GetBytes("/OC /Layer BDC 1 0 0 rg 0 0 200 200 re f EMC")),
            [5] = Parse($"<< /Type /OCG /Name (Layer) /Usage << /View << /ViewState /ON >> {usage} >> >>")
        };
        var document = PdfDocument.Load(PdfWriter.Write(objects, 1, null,
            options: new PdfWriteOptions { FileId = new byte[16] }));
        Assert.False(document.File.Repaired);
        return new OptionalContentState(document);
    }

    static void AssertInterpreter(OptionalContentState state, ContentInterpreter.OptionalContentEvent displayEvent,
        bool visible)
    {
        var page = state.Document.Pages[0];
        using var raster = new SoftwareRasterizer(200, 200);
        var interpreter = new ContentInterpreter(state.Document.File, raster)
        {
            OptionalContentDisplayEvent = displayEvent
        };
        interpreter.Render(page, Matrix.Identity);
        AssertPixels(raster.ToRgba(), visible);
        Assert.Empty(interpreter.Warnings);
    }

    static void AssertPixel(OptionalContentState state, ContentInterpreter.OptionalContentEvent displayEvent, bool visible,
        bool useState = true)
    {
        var rendered = DocumentRenderer.RenderPage(state.Document.Pages[0],
            new PdfRenderOptions
            {
                OptionalContentState = useState ? state : null,
                OptionalContentDisplayEvent = displayEvent
            }, 72);
        Assert.Equal(200, rendered.Width);
        Assert.Equal(200, rendered.Height);
        AssertPixels(rendered.Rgba, visible);
        Assert.Empty(rendered.Warnings);
    }

    static void AssertPixels(byte[] pixels, bool visible)
    {
        var expected = new byte[200 * 200 * 4];
        Array.Fill(expected, (byte)255);
        if (visible)
            for (var i = 0; i < expected.Length; i += 4)
                expected[i + 1] = expected[i + 2] = 0;
        Assert.Equal(expected, pixels);
    }
}
