using PortableDocument.Content;
using PortableDocument.Document;
using PortableDocument.Render;
using DocumentRenderer = PortableDocument.Render.PortableDocument;

namespace FreeDF.Tests;

public class OptionalContentApplicationScopeTests
{
    [Theory]
    [InlineData("", "null", false, false)]
    [InlineData("/OCGs null", "null", false, false)]
    [InlineData("/OCGs []", "null", false, false)]
    [InlineData("/OCGs 7 0 R", "null", false, false)]
    [InlineData("/OCGs 7 0 R", "[]", false, false)]
    [InlineData("/OCGs /bad", "null", false, true)]
    [InlineData("/OCGs 5 0 R", "null", false, true)]
    [InlineData("/OCGs [5 0 R]", "null", true, false)]
    [InlineData("/OCGs 7 0 R", "[5 0 R]", true, false)]
    [InlineData("/OCGs [6 0 R]", "null", false, false)]
    public void Event_usage_only_changes_explicitly_selected_groups(
        string groups, string indirect, bool selected, bool invalid)
    {
        foreach (var displayEvent in new[] { ContentInterpreter.OptionalContentEvent.View,
            ContentInterpreter.OptionalContentEvent.Print, ContentInterpreter.OptionalContentEvent.Export })
        {
            var state = Load(groups, indirect, displayEvent.ToString());
            Assert.Equal(!selected, state.IsVisible(state.Groups[0], displayEvent));
            Assert.True(state.IsVisible(state.Groups[1], displayEvent));
            if (invalid)
                Assert.Contains(state.Warnings, warning => warning.Contains("application OCGs array"));
            else
                Assert.Empty(state.Warnings);
            if (displayEvent == ContentInterpreter.OptionalContentEvent.View)
                foreach (var activeState in new OptionalContentState?[] { null, state })
                {
                    var rendered = DocumentRenderer.RenderPage(state.Document.Pages[0],
                        new PdfRenderOptions { OptionalContentState = activeState }, 72);
                    var pixel = (50 * rendered.Width + 50) * 4;
                    Assert.Equal(selected ? new byte[] { 255, 255, 255, 255 } : [255, 0, 0, 255],
                        rendered.Rgba[pixel..(pixel + 4)]);
                }
        }
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("/OCGs null", true)]
    [InlineData("/OCGs []", true)]
    [InlineData("/OCGs [5 0 R]", false)]
    public void Zoom_recommendations_use_the_same_application_selection(string groups, bool visible)
    {
        var state = Load(groups, "null", "View", "Zoom");
        Assert.Equal(visible, state.IsVisible(state.Groups[0], zoom: 2));
        Assert.Empty(state.Warnings);
    }

    static OptionalContentState Load(string groups, string indirect, string displayEvent, string? category = null)
    {
        var document = PdfDocument.Load(TestPdf.Build(
            "/OC /Layer BDC 1 0 0 rg 0 0 200 200 re f EMC",
            extraResources: "/Properties << /Layer 5 0 R >>", extraObjects:
            "5 0 obj\n<< /Type /OCG /Name (Selected) /Usage << " +
            "/View << /ViewState /OFF >> /Print << /PrintState /OFF >> " +
            "/Export << /ExportState /OFF >> /Zoom << /max 1 >> >> >>\nendobj\n" +
            "6 0 obj\n<< /Type /OCG /Name (Peer) >>\nendobj\n" +
            $"7 0 obj\n{indirect}\nendobj\n", brokenXref: true,
            extraCatalog: "/OCProperties << /OCGs [5 0 R 6 0 R] /D << /BaseState /ON " +
                $"/AS [<< /Event /{displayEvent} /Category [/{category ?? displayEvent}] {groups} >>] >> >>"));
        return new OptionalContentState(document);
    }
}
