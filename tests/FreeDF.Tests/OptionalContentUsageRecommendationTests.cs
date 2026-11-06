using PortableDocument.Content;
using PortableDocument.Document;
using PortableDocument.Render;
using DocumentRenderer = PortableDocument.Render.PortableDocument;

namespace FreeDF.Tests;

[Collection("Optional-content allocation")]
public class OptionalContentUsageRecommendationTests
{
    public static IEnumerable<object[]> EventCategories()
    {
        foreach (var displayEvent in new[] { ContentInterpreter.OptionalContentEvent.View,
            ContentInterpreter.OptionalContentEvent.Print, ContentInterpreter.OptionalContentEvent.Export })
            foreach (var category in new[] { "View", "Print", "Export" })
                foreach (var on in new[] { false, true })
                    yield return [displayEvent, category, on];
    }

    [Theory]
    [MemberData(nameof(EventCategories))]
    public void Event_selects_the_application_not_the_usage_category(
        ContentInterpreter.OptionalContentEvent displayEvent, string category, bool on)
    {
        var application = Application(displayEvent.ToString(), $"/{category}");
        var state = Load($"/{category} << /{category}State /{(on ? "ON" : "OFF")} >>",
            application, !on);
        Assert.Equal(on, state.IsVisible(state.Groups[0], displayEvent));
        Assert.Empty(state.Warnings);
        if (displayEvent == ContentInterpreter.OptionalContentEvent.View)
            AssertPixel(state, on);
    }

    public static IEnumerable<object[]> ZoomEvents()
    {
        foreach (var displayEvent in new[] { ContentInterpreter.OptionalContentEvent.View,
            ContentInterpreter.OptionalContentEvent.Print, ContentInterpreter.OptionalContentEvent.Export })
        {
            yield return [displayEvent, "/min 1 /max 2", 1.0, false, true];
            yield return [displayEvent, "/min 1 /max 2", 2.0, true, false];
            yield return [displayEvent, "", 0.0, false, true];
            yield return [displayEvent, "", -0.5, true, false];
        }
    }

    [Theory]
    [MemberData(nameof(ZoomEvents))]
    public void Zoom_recommends_both_states_for_each_selected_event(
        ContentInterpreter.OptionalContentEvent displayEvent, string bounds, double zoom, bool initial, bool visible)
    {
        var state = Load($"/Zoom << {bounds} >>", Application(displayEvent.ToString(), "/Zoom"), initial);
        Assert.Equal(visible, state.IsVisible(state.Groups[0], displayEvent, zoom));
        Assert.Empty(state.Warnings);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void All_present_recommendations_are_conjoined_across_categories_and_applications(
        bool separateApplications, bool reverse)
    {
        var first = reverse ? "/View" : "/Print";
        var second = reverse ? "/Print" : "/View";
        var applications = separateApplications
            ? Application("View", first) + Application("View", second)
            : Application("View", $"{first} {second}");
        var state = Load("/View << /ViewState /ON >> /Print << /PrintState /OFF >>", applications, true);
        Assert.False(state.IsVisible(state.Groups[0]));
        AssertPixel(state, false);
        Assert.Empty(state.Warnings);
        state.SetVisibility(state.Groups[0], true);
        Assert.True(state.IsVisible(state.Groups[0]));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData("/Print << >>", false)]
    [InlineData("/Print << >>", true)]
    public void Missing_usage_or_print_state_preserves_the_configured_state(string usage, bool initial)
    {
        var state = Load(usage, Application("Print", "/Print"), initial);
        Assert.Equal(initial, state.IsVisible(state.Groups[0], ContentInterpreter.OptionalContentEvent.Print));
        Assert.Empty(state.Warnings);
    }

    [Theory]
    [InlineData(ContentInterpreter.OptionalContentEvent.View)]
    [InlineData(ContentInterpreter.OptionalContentEvent.Print)]
    [InlineData(ContentInterpreter.OptionalContentEvent.Export)]
    public void Repeated_event_recommendations_do_not_allocate(
        ContentInterpreter.OptionalContentEvent displayEvent)
    {
        var category = displayEvent.ToString();
        var state = Load($"/{category} << /{category}State /ON >>", Application(category, $"/{category}"), false);
        var group = state.Groups[0];
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

    static string Application(string displayEvent, string categories) =>
        $"<< /Event /{displayEvent} /Category [{categories}] /OCGs [5 0 R] >>";

    static OptionalContentState Load(string usage, string applications, bool initial) =>
        new(PdfDocument.Load(TestPdf.Build(
            "/OC /Layer BDC 1 0 0 rg 0 0 200 200 re f EMC",
            extraResources: "/Properties << /Layer 5 0 R >>",
            extraObjects: $"5 0 obj\n<< /Type /OCG /Name (Usage) /Usage << {usage} >> >>\nendobj\n",
            brokenXref: true,
            extraCatalog: "/OCProperties << /OCGs [5 0 R] /D << " +
                $"/BaseState /ON {(initial ? "" : "/OFF [5 0 R]")} /AS [{applications}] >> >>")));

    static void AssertPixel(OptionalContentState state, bool visible)
    {
        foreach (var activeState in new OptionalContentState?[] { null, state })
        {
            var rendered = DocumentRenderer.RenderPage(state.Document.Pages[0],
                new PdfRenderOptions { OptionalContentState = activeState }, 72);
            var pixel = (50 * rendered.Width + 50) * 4;
            Assert.Equal(visible ? new byte[] { 255, 0, 0, 255 } : [255, 255, 255, 255],
                rendered.Rgba[pixel..(pixel + 4)]);
        }
    }
}
