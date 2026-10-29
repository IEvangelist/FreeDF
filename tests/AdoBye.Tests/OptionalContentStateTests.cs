using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using DocumentRenderer = PortableDocument.Render.PortableDocument;

namespace AdoBye.Tests;

public class OptionalContentStateTests
{
    const string Content =
        "/OC /A BDC 1 0 0 rg 0 0 100 200 re f EMC " +
        "/OC /B BDC 0 0 1 rg 100 0 100 200 re f EMC";
    const string Resources = "/Properties << /A 5 0 R /B 6 0 R >>";
    const string Objects =
        "5 0 obj\n<< /Type /OCG /Name (A) >>\nendobj\n" +
        "6 0 obj\n<< /Type /OCG /Name (B) >>\nendobj\n" +
        "7 0 obj\n<< /Type /OCG /Name (C) >>\nendobj\n";

    static PdfDocument Load(string configuration, string configs = "", string extraObjects = "",
        string content = Content, string resources = Resources, string objects = Objects) =>
        PdfDocument.Load(TestPdf.Build(content, resources, objects + extraObjects, brokenXref: true,
            extraCatalog: $"/OCProperties << /OCGs [5 0 R 6 0 R 7 0 R] /D << /Name (Default) {configuration} >> {configs} >>"));

    static PdfDictionary Group(PdfDocument doc, int number) => Assert.IsType<PdfDictionary>(doc.File.Resolve(new PdfRef(number, 0)));

    static (int, int, int) Pixel(RenderedPage page, int x)
    {
        var i = (100 * page.Width + x) * 4;
        return (page.Rgba[i], page.Rgba[i + 1], page.Rgba[i + 2]);
    }

    static RenderedPage Render(PdfDocument doc, OptionalContentState state) =>
        DocumentRenderer.RenderPage(doc.Pages[0], new PdfRenderOptions { OptionalContentState = state }, 72);

    [Fact]
    public void Selecting_radio_member_hides_peers_and_turning_off_all_is_allowed()
    {
        var doc = Load("/BaseState /OFF /ON [5 0 R] /RBGroups [[5 0 R 6 0 R]]");
        var state = new OptionalContentState(doc);
        var a = Group(doc, 5); var b = Group(doc, 6);
        Assert.Equal(3, state.Groups.Count);
        Assert.Equal(2, Assert.Single(state.RadioButtonGroups).Count);
        Assert.True(state.IsVisible(a));
        Assert.False(state.IsVisible(b));

        state.SetVisibility(b, true);
        Assert.False(state.IsVisible(a));
        Assert.True(state.IsVisible(b));
        var rendered = Render(doc, state);
        Assert.Equal((255, 255, 255), Pixel(rendered, 50));
        Assert.Equal((0, 0, 255), Pixel(rendered, 150));

        state.SetVisibility(b, false);
        Assert.False(state.IsVisible(a));
        Assert.False(state.IsVisible(b));
        Assert.Equal((255, 255, 255), Pixel(Render(doc, state), 150));
        Assert.True(new OptionalContentState(doc).IsVisible(a));
        Assert.Equal((255, 0, 0), Pixel(DocumentRenderer.RenderPage(doc.Pages[0], 72), 50));
        Assert.Empty(state.Warnings);
    }

    [Fact]
    public void Radio_metadata_does_not_normalize_initial_configuration_visibility()
    {
        var doc = Load("/BaseState /ON /RBGroups [[5 0 R 6 0 R]] /Locked [5 0 R]");
        var state = new OptionalContentState(doc);
        Assert.True(state.IsVisible(Group(doc, 5)));
        Assert.True(state.IsVisible(Group(doc, 6)));
        var rendered = DocumentRenderer.RenderPage(doc.Pages[0], 72);
        Assert.Equal((255, 0, 0), Pixel(rendered, 50));
        Assert.Equal((0, 0, 255), Pixel(rendered, 150));
    }

    [Fact]
    public void Overlapping_radio_groups_hide_only_direct_peers_and_deduplicate_members()
    {
        var doc = Load("/BaseState /ON /RBGroups [[5 0 R 6 0 R 5 0 R] [6 0 R 7 0 R]]");
        var state = new OptionalContentState(doc);
        Assert.Equal(2, state.RadioButtonGroups[0].Count);
        state.SetVisibility(Group(doc, 5), true);
        Assert.False(state.IsVisible(Group(doc, 6)));
        Assert.True(state.IsVisible(Group(doc, 7)));
        state.SetVisibility(Group(doc, 6), true);
        Assert.False(state.IsVisible(Group(doc, 5)));
        Assert.False(state.IsVisible(Group(doc, 7)));
        Assert.True(state.IsVisible(Group(doc, 6)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Locked_layer_rejects_direct_UI_changes_regardless_of_visibility(bool visible)
    {
        var doc = Load($"/BaseState /{(visible ? "ON" : "OFF")} /RBGroups [[5 0 R 6 0 R]] /Locked [5 0 R 6 0 R]");
        var state = new OptionalContentState(doc);
        var a = Group(doc, 5);
        Assert.True(state.IsLocked(a));
        Assert.Throws<InvalidOperationException>(() => state.SetVisibility(a, !visible));
        Assert.Equal(visible, state.IsVisible(a));
        Assert.Throws<InvalidOperationException>(() => state.SetVisibility(Group(doc, 6), !visible));
        Assert.Equal(visible, state.IsVisible(Group(doc, 6)));
    }

    [Fact]
    public void Locked_peer_rejection_is_atomic_across_overlapping_radio_groups()
    {
        var doc = Load("/BaseState /OFF /ON [5 0 R 7 0 R] /RBGroups [[5 0 R 6 0 R] [6 0 R 7 0 R]] /Locked [7 0 R]");
        var state = new OptionalContentState(doc);
        Assert.Throws<InvalidOperationException>(() => state.SetVisibility(Group(doc, 6), true));
        Assert.True(state.IsVisible(Group(doc, 5)));
        Assert.False(state.IsVisible(Group(doc, 6)));
        Assert.True(state.IsVisible(Group(doc, 7)));
        Assert.Equal((255, 0, 0), Pixel(Render(doc, state), 50));
    }

    [Fact]
    public void Locked_off_peer_does_not_prevent_selecting_an_unlocked_layer()
    {
        var doc = Load("/BaseState /OFF /RBGroups [[5 0 R 6 0 R]] /Locked [5 0 R]");
        var state = new OptionalContentState(doc);
        state.SetVisibility(Group(doc, 6), true);
        Assert.False(state.IsVisible(Group(doc, 5)));
        Assert.True(state.IsVisible(Group(doc, 6)));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("/RBGroups []", false)]
    [InlineData("/RBGroups [] /Locked []", false)]
    [InlineData("/RBGroups [[6 0 R 7 0 R]]", false)]
    public void Named_configuration_inherits_only_omitted_radio_groups(string rules, bool hidesA)
    {
        var doc = Load("/BaseState /OFF /ON [5 0 R] /RBGroups [[5 0 R 6 0 R]] /Locked [5 0 R]",
            $"/Configs [<< /Name (Named) /BaseState /Unchanged {rules} >>]");
        var state = new OptionalContentState(doc, "Named");
        Assert.False(state.IsLocked(Group(doc, 5)));
        Assert.True(state.IsVisible(Group(doc, 5)));
        state.SetVisibility(Group(doc, 6), true);
        Assert.Equal(!hidesA, state.IsVisible(Group(doc, 5)));
        Assert.True(state.IsVisible(Group(doc, 6)));
        Assert.True(new OptionalContentState(doc, "Default").IsLocked(Group(doc, 5)));
        Assert.Throws<ArgumentException>(() => new OptionalContentState(doc, "missing"));
    }

    [Fact]
    public void Named_configuration_locks_do_not_prevent_configuration_or_usage_initialization()
    {
        const string objects =
            "5 0 obj\n<< /Type /OCG /Name (A) /Usage << /View << /ViewState /ON >> /Print << /PrintState /OFF >> >> >>\nendobj\n" +
            "6 0 obj\n<< /Type /OCG /Name (B) >>\nendobj\n" +
            "7 0 obj\n<< /Type /OCG /Name (C) >>\nendobj\n";
        var doc = Load("/BaseState /ON /Locked [6 0 R]",
            "/Configs [<< /Name (Named) /BaseState /OFF /Locked [5 0 R] /RBGroups [[5 0 R 6 0 R]] /AS [" +
            "<< /Event /View /Category [/View] /OCGs [5 0 R] >> " +
            "<< /Event /Print /Category [/Print] /OCGs [5 0 R] >>] >>]", objects: objects);
        var state = new OptionalContentState(doc, "Named");
        var a = Group(doc, 5); var b = Group(doc, 6);
        Assert.True(state.IsLocked(a));
        Assert.False(state.IsLocked(b));
        Assert.True(state.IsVisible(a));
        Assert.False(state.IsVisible(a, ContentInterpreter.OptionalContentEvent.Print));
        Assert.Throws<InvalidOperationException>(() => state.SetVisibility(b, true));

        var print = DocumentRenderer.RenderPage(doc.Pages[0], new PdfRenderOptions
        {
            OptionalContentState = state,
            OptionalContentDisplayEvent = ContentInterpreter.OptionalContentEvent.Print,
        }, 72);
        Assert.Equal((255, 255, 255), Pixel(print, 50));
        state.SetVisibility(b, true, ContentInterpreter.OptionalContentEvent.Print);
        Assert.True(state.IsVisible(b));
    }

    [Theory]
    [InlineData("/RBGroups /bad /Locked /bad")]
    [InlineData("/RBGroups 99 0 R /Locked 98 0 R")]
    [InlineData("/RBGroups [null 99 0 R 42] /Locked [null 99 0 R 42]")]
    [InlineData("/RBGroups [8 0 R] /Locked 8 0 R")]
    [InlineData("/RBGroups [[5 0 R 6 0 R 99 0 R 8 0 R 9 0 R 5 1 R << /Type /OCG >>]] /Locked [99 0 R 8 0 R 9 0 R 5 1 R << /Type /OCG >>]")]
    public void Malformed_rule_references_warn_without_locking_or_changing_unrelated_layers(string rules)
    {
        var doc = Load("/BaseState /OFF " + rules, extraObjects:
            "8 0 obj\n<< /Type /OCMD >>\nendobj\n9 0 obj\n<< /Type /OCG /Name (Unregistered) >>\nendobj\n");
        var state = new OptionalContentState(doc);
        Assert.NotEmpty(state.Warnings);
        Assert.False(state.IsLocked(Group(doc, 5)));
        state.SetVisibility(Group(doc, 5), true);
        state.SetVisibility(Group(doc, 6), true);
        Assert.True(state.IsVisible(Group(doc, 6)));
        Assert.False(state.IsVisible(Group(doc, 7)));
        Assert.NotEmpty(Render(doc, state).Warnings);
        Assert.Throws<ArgumentException>(() => state.SetVisibility(Group(doc, 9), true));
    }

    [Fact]
    public void Invalid_registered_references_are_not_exposed_as_interactive_groups()
    {
        var doc = PdfDocument.Load(TestPdf.Build("", extraObjects: Objects +
            "8 0 obj\n<< /Type /OCMD >>\nendobj\n", brokenXref: true,
            extraCatalog: "/OCProperties << /OCGs [5 0 R 5 0 R 5 1 R 99 0 R 8 0 R << /Type /OCG >>] " +
                "/D << /RBGroups [[5 0 R 6 0 R]] /Locked [6 0 R] >> >>"));
        var state = new OptionalContentState(doc);
        Assert.Same(Group(doc, 5), Assert.Single(state.Groups));
        Assert.Same(Group(doc, 5), Assert.Single(Assert.Single(state.RadioButtonGroups)));
        Assert.NotEmpty(state.Warnings);
        Assert.Throws<ArgumentException>(() => state.IsLocked(Group(doc, 6)));
    }

    [Fact]
    public void Indirect_rule_arrays_and_member_arrays_are_resolved()
    {
        var doc = Load("/BaseState /OFF /ON [5 0 R] /RBGroups 8 0 R /Locked 10 0 R",
            extraObjects: "8 0 obj\n[9 0 R]\nendobj\n9 0 obj\n[5 0 R 6 0 R]\nendobj\n10 0 obj\n[7 0 R]\nendobj\n");
        var state = new OptionalContentState(doc);
        state.SetVisibility(Group(doc, 6), true);
        Assert.False(state.IsVisible(Group(doc, 5)));
        Assert.True(state.IsLocked(Group(doc, 7)));
        Assert.Empty(state.Warnings);
    }

    [Fact]
    public void Radio_state_drives_membership_policies_and_visibility_expressions()
    {
        var doc = Load("/BaseState /OFF /ON [5 0 R] /RBGroups [[5 0 R 6 0 R]]",
            content: "/OC /A BDC 1 0 0 rg 0 0 100 200 re f EMC /OC /B BDC 0 0 1 rg 100 0 100 200 re f EMC",
            resources: "/Properties << /A 8 0 R /B 9 0 R >>",
            extraObjects: "8 0 obj\n<< /Type /OCMD /OCGs [5 0 R] /P /AllOn >>\nendobj\n" +
                "9 0 obj\n<< /Type /OCMD /VE [/And 6 0 R [/Not 5 0 R]] >>\nendobj\n");
        var state = new OptionalContentState(doc);
        Assert.Equal((255, 0, 0), Pixel(Render(doc, state), 50));
        state.SetVisibility(Group(doc, 6), true);
        var rendered = Render(doc, state);
        Assert.Equal((255, 255, 255), Pixel(rendered, 50));
        Assert.Equal((0, 0, 255), Pixel(rendered, 150));
    }

    [Fact]
    public void Usage_zoom_controls_locked_peers_and_manual_state_overrides_usage()
    {
        const string objects =
            "5 0 obj\n<< /Type /OCG /Name (A) /Usage << /Zoom << /min 0.75 /max 1.5 >> >> >>\nendobj\n" +
            "6 0 obj\n<< /Type /OCG /Name (B) /Usage << /View << /ViewState /OFF >> >> >>\nendobj\n" +
            "7 0 obj\n<< /Type /OCG /Name (C) >>\nendobj\n";
        var doc = Load("/BaseState /ON /Locked [5 0 R] /RBGroups [[5 0 R 6 0 R]] /AS [" +
            "<< /Event /View /Category [/Zoom] /OCGs [5 0 R] >> " +
            "<< /Event /View /Category [/View] /OCGs [6 0 R] >>]", objects: objects);
        var state = new OptionalContentState(doc);
        Assert.Throws<InvalidOperationException>(() => state.SetVisibility(Group(doc, 6), true));
        state.SetVisibility(Group(doc, 6), true, zoom: 2);
        Assert.False(state.IsVisible(Group(doc, 5), zoom: 2));
        Assert.True(state.IsVisible(Group(doc, 6), zoom: 2));
        var rendered = DocumentRenderer.RenderPage(doc.Pages[0], new PdfRenderOptions { OptionalContentState = state }, 144);
        Assert.Equal((255, 255, 255), Pixel(rendered, 50));
        Assert.Equal((0, 0, 255), Pixel(rendered, 300));
    }

    [Fact]
    public void State_cannot_be_applied_to_another_document_or_combined_with_configuration_name()
    {
        var doc = Load("/BaseState /ON");
        var other = Load("/BaseState /ON");
        var state = new OptionalContentState(doc);
        Assert.Throws<ArgumentException>(() => Render(other, state));
        Assert.Throws<ArgumentException>(() => state.IsVisible(Group(other, 5)));
        Assert.Throws<ArgumentException>(() => DocumentRenderer.RenderPage(doc.Pages[0], new PdfRenderOptions
        {
            OptionalContentState = state, OptionalContentConfigurationName = "Default",
        }));
        using var raster = new SoftwareRasterizer(1, 1);
        var interpreter = new ContentInterpreter(other.File, raster) { OptionalContentState = state };
        Assert.Throws<ArgumentException>(() => interpreter.Render(other.Pages[0], Matrix.Identity));
    }

    [Fact]
    public void Editor_round_trip_preserves_rules_without_persisting_interactive_state()
    {
        var doc = Load("/BaseState /OFF /ON [5 0 R] /RBGroups [[5 0 R 6 0 R]] /Locked [7 0 R]");
        var state = new OptionalContentState(doc);
        state.SetVisibility(Group(doc, 6), true);
        var saved = PdfDocument.Load(PdfEditor.Open(doc).Save());
        var restored = new OptionalContentState(saved);
        Assert.Single(restored.RadioButtonGroups);
        PdfDictionary Named(string name) => Assert.Single(restored.Groups,
            g => saved.File.Resolve(g.Get("Name")) is PdfString s && s.Text == name);
        Assert.True(restored.IsVisible(Named("A")));
        Assert.False(restored.IsVisible(Named("B")));
        Assert.True(restored.IsLocked(Named("C")));
        Assert.Empty(restored.Warnings);
    }
}
