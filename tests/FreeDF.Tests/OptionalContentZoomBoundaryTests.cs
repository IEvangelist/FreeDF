using PortableDocument.Content;
using PortableDocument.Document;
using PortableDocument.Render;
using DocumentRenderer = PortableDocument.Render.PortableDocument;

namespace FreeDF.Tests;

[Collection("Optional-content allocation")]
public class OptionalContentZoomBoundaryTests
{
    [Theory]
    [InlineData(0.5, false)]
    [InlineData(1, true)]
    [InlineData(1.5, true)]
    [InlineData(2, false)]
    [InlineData(2.5, false)]
    public void View_zoom_range_includes_minimum_but_excludes_maximum(double zoom, bool visible)
    {
        var doc = Load("/min 1 /max 2");
        var state = new OptionalContentState(doc);
        Assert.Equal(visible, state.IsVisible(state.Groups[0], zoom: zoom));
        Assert.Empty(state.Warnings);
        var rendered = DocumentRenderer.RenderPage(doc.Pages[0],
            new PdfRenderOptions { OptionalContentState = state }, 72 * zoom);
        var pixel = (50 * rendered.Width + 50) * 4;
        Assert.Equal(visible ? new byte[] { 255, 0, 0, 255 } : [255, 255, 255, 255],
            rendered.Rgba[pixel..(pixel + 4)]);
    }

    [Fact]
    public void Adjacent_floating_point_values_preserve_half_open_boundaries()
    {
        var doc = Load("/min 1 /max 2");
        var state = new OptionalContentState(doc);
        var group = Assert.Single(state.Groups);
        Assert.False(state.IsVisible(group, zoom: double.BitDecrement(1)));
        Assert.True(state.IsVisible(group, zoom: 1));
        Assert.True(state.IsVisible(group, zoom: double.BitIncrement(1)));
        Assert.True(state.IsVisible(group, zoom: double.BitDecrement(2)));
        Assert.False(state.IsVisible(group, zoom: 2));
        Assert.False(state.IsVisible(group, zoom: double.BitIncrement(2)));
    }

    [Theory]
    [InlineData("/max 1", 0.5, true)]
    [InlineData("/max 1", 1, false)]
    [InlineData("/min 1", 1, true)]
    [InlineData("/min 1", 100, true)]
    [InlineData("/min 2 /max 2", 2, false)]
    public void Single_bounds_and_empty_ranges_preserve_the_upper_exclusion(string bounds, double zoom, bool visible)
    {
        var state = new OptionalContentState(Load(bounds));
        Assert.Equal(visible, state.IsVisible(Assert.Single(state.Groups), zoom: zoom));
    }

    [Theory]
    [InlineData(ContentInterpreter.OptionalContentEvent.Print)]
    [InlineData(ContentInterpreter.OptionalContentEvent.Export)]
    public void Missing_temporary_recommendations_preserve_the_current_view_zoom(
        ContentInterpreter.OptionalContentEvent displayEvent)
    {
        var state = new OptionalContentState(Load("/min 1 /max 2"));
        var group = Assert.Single(state.Groups);
        Assert.True(state.IsVisible(group, displayEvent, zoom: 1));
        Assert.False(state.IsVisible(group, displayEvent, zoom: 2));
        state.SetVisibility(group, true);
        Assert.True(state.IsVisible(group, displayEvent, zoom: 2));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Repeated_simple_zoom_queries_do_not_allocate(double zoom)
    {
        var state = new OptionalContentState(Load("/min 1 /max 2"));
        var group = Assert.Single(state.Groups);
        for (var i = 0; i < 8192; i++) state.IsVisible(group, zoom: zoom);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var visible = 0;
        for (var i = 0; i < 4096; i++)
            if (state.IsVisible(group, zoom: zoom)) visible++;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(zoom == 1 ? 4096 : 0, visible);
    }

    static PdfDocument Load(string bounds) => PdfDocument.Load(TestPdf.Build(
        "/OC /Layer BDC 1 0 0 rg 0 0 200 200 re f EMC",
        "/Properties << /Layer 5 0 R >>",
        $"5 0 obj\n<< /Type /OCG /Name (Zoom) /Usage << /Zoom << {bounds} >> >> >>\nendobj\n",
        brokenXref: true,
        extraCatalog: "/OCProperties << /OCGs [5 0 R] /D << /BaseState /ON " +
            "/AS [<< /Event /View /OCGs [5 0 R] /Category [/Zoom] >>] >> >>"));
}

[CollectionDefinition("Optional-content allocation", DisableParallelization = true)]
public class OptionalContentAllocationCollection
{
}
