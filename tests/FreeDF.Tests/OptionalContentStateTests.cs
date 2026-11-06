using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using DocumentRenderer = PortableDocument.Render.PortableDocument;

namespace FreeDF.Tests;

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

    [Theory]
    [InlineData("", true)]
    [InlineData("/RBGroups null", true)]
    [InlineData("/RBGroups 7 0 R", true)]
    [InlineData("/RBGroups 99 0 R", true)]
    [InlineData("/RBGroups []", false)]
    [InlineData("/RBGroups 9 0 R", false)]
    [InlineData("/RBGroups 10 0 R", true)]
    public void Named_radio_groups_resolve_null_before_inheritance(string entry, bool radio)
    {
        var objects = new Dictionary<int, string>
        {
            [1] = "<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [5 0 R 6 0 R] " +
                "/D << /BaseState /ON /OFF [6 0 R] /RBGroups [[5 0 R 6 0 R]] >> /Configs [8 0 R] >> >>",
            [2] = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            [3] = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R " +
                "/Resources << /Properties << /A 5 0 R /B 6 0 R >> >> >>",
            [4] = $"<< /Length {Content.Length} >>\nstream\n{Content}\nendstream",
            [5] = "<< /Type /OCG /Name (A) >>",
            [6] = "<< /Type /OCG /Name (B) >>",
            [7] = "null",
            [8] = $"<< /Name (Named) /BaseState /ON /OFF [6 0 R] {entry} >>",
            [9] = "[]",
            [10] = "[[5 0 R 6 0 R]]"
        };
        // Encode independently: PdfWriter normalizes nonexistent references to direct null.
        var bytes = new System.Text.StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        foreach (var (number, body) in objects.OrderBy(pair => pair.Key))
        {
            offsets.Add(bytes.Length);
            bytes.Append($"{number} 0 obj\n{body}\nendobj\n");
        }
        var xref = bytes.Length;
        bytes.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) bytes.Append($"{offset:D10} 00000 n \n");
        bytes.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        var document = PdfDocument.Load(System.Text.Encoding.ASCII.GetBytes(bytes.ToString()));
        Assert.False(document.File.Repaired);
        var state = new OptionalContentState(document, "Named");
        Assert.Equal(radio ? 1 : 0, state.RadioButtonGroups.Count);
        var a = state.Groups.Single(g => (g.Get("Name") as PdfString)?.Text == "A");
        var b = state.Groups.Single(g => (g.Get("Name") as PdfString)?.Text == "B");
        Assert.True(state.IsVisible(a));
        Assert.False(state.IsVisible(b));
        state.SetVisibility(b, true);
        Assert.Equal(!radio, state.IsVisible(a));
        Assert.True(state.IsVisible(b));
        var rendered = Render(document, state);
        Assert.Equal(radio ? (255, 255, 255) : (255, 0, 0), Pixel(rendered, 50));
        Assert.Equal((0, 0, 255), Pixel(rendered, 150));
        Assert.Empty(state.Warnings);
        Assert.Empty(rendered.Warnings);
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

    static PdfDocument UsageDocument(string usageA, string usageB = "", string categories = "/Language",
        string? configuration = null, string configs = "", string extraObjects = "") =>
        Load(configuration ?? $"/BaseState /ON /AS [<< /Event /View /Category [{categories}] /OCGs [5 0 R 6 0 R] >>]",
            configs: configs, extraObjects: extraObjects, objects:
                $"5 0 obj\n<< /Type /OCG /Name (A) /Usage << {usageA} >> >>\nendobj\n" +
                $"6 0 obj\n<< /Type /OCG /Name (B) /Usage << {usageB} >> >>\nendobj\n" +
                "7 0 obj\n<< /Type /OCG /Name (C) >>\nendobj\n");

    [Theory]
    [InlineData("en-US", "en-US", "", true)]
    [InlineData("EN-us", "en-US", "/Preferred /OFF", true)]
    [InlineData("en-GB", "en-US", "/Preferred /ON", true)]
    [InlineData("en", "en-US", "/Preferred /ON", true)]
    [InlineData("en-US", "en", "/Preferred /ON", true)]
    [InlineData("en-GB", "en-US", "", false)]
    [InlineData("en-GB", "en-US", "/Preferred /OFF", false)]
    [InlineData("eng-US", "en-US", "/Preferred /ON", false)]
    [InlineData("fr-FR", "en-US", "/Preferred /ON", false)]
    [InlineData("zh-Hant-TW", "zh-Hans-CN", "/Preferred /ON", true)]
    [InlineData("EN-us-u-CA-gregory", "en-US-u-ca-gregory", "", true)]
    [InlineData("x-custom", "X-CUSTOM", "", true)]
    [InlineData("", "en-US", "/Preferred /ON", false)]
    [InlineData("en-US", "", "/Preferred /ON", false)]
    [InlineData("", "", "", true)]
    public void Language_uses_exact_or_preferred_primary_language_matches(string language, string requested,
        string preferred, bool visible)
    {
        var doc = UsageDocument($"/Language << /Lang ({language}) {preferred} >>");
        var state = new OptionalContentState(doc);
        Assert.Equal(visible, state.IsVisible(Group(doc, 5), context: new() { Language = requested }));
        Assert.True(state.IsVisible(Group(doc, 6), context: new() { Language = requested }));
        Assert.Empty(state.Warnings);
    }

    [Fact]
    public void Language_exact_match_suppresses_preferred_partials_only_within_the_same_application()
    {
        var doc = UsageDocument("/Language << /Lang (en-GB) /Preferred /ON >>", "/Language << /Lang (en-US) >>");
        var context = new OptionalContentContext { Language = "en-US" };
        var state = new OptionalContentState(doc);
        Assert.False(state.IsVisible(Group(doc, 5), context: context));
        Assert.True(state.IsVisible(Group(doc, 6), context: context));

        var separate = UsageDocument("/Language << /Lang (en-GB) /Preferred /ON >>", "/Language << /Lang (en-US) >>",
            configuration: "/BaseState /OFF /AS [" +
                "<< /Event /View /Category [/Language] /OCGs [5 0 R] >> " +
                "<< /Event /View /Category [/Language] /OCGs [6 0 R] >>]");
        var separateState = new OptionalContentState(separate);
        Assert.True(separateState.IsVisible(Group(separate, 5), context: context));
        Assert.True(separateState.IsVisible(Group(separate, 6), context: context));
    }

    [Theory]
    [InlineData("Ind", "Ada", null, null, true)]
    [InlineData("Ind", "ada", null, null, false)]
    [InlineData("Ind", "Ada ", null, null, false)]
    [InlineData("Ttl", "unrelated", "Ada", null, true)]
    [InlineData("Ttl", "Ada", "Engineer", null, false)]
    [InlineData("Org", null, null, "Ada", true)]
    [InlineData("Org", "Ada", "Ada", "Company", false)]
    public void User_matches_only_the_supplied_identity_of_the_declared_type(string type, string? name,
        string? title, string? organization, bool visible)
    {
        var doc = UsageDocument($"/User << /Type /{type} /Name [(Other) (Ada)] >>", categories: "/User");
        var state = new OptionalContentState(doc);
        Assert.Equal(visible, state.IsVisible(Group(doc, 5), context: new()
        {
            UserName = name, UserTitle = title, UserOrganization = organization,
        }));
        Assert.Empty(state.Warnings);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Omitted_context_preserves_configuration_and_reports_unapplied_rules(bool initiallyVisible)
    {
        var doc = UsageDocument("/Language << /Lang (fr) >> /User << /Type /Ind /Name (Ada) >>",
            categories: "/Language /User",
            configuration: $"/BaseState /{(initiallyVisible ? "ON" : "OFF")} /AS [" +
                "<< /Event /View /Category [/Language /User] /OCGs [5 0 R] >>]");
        var state = new OptionalContentState(doc);
        Assert.Equal(initiallyVisible, state.IsVisible(Group(doc, 5)));
        Assert.Equal(initiallyVisible, state.IsVisible(Group(doc, 5), context: new()));
        Assert.Equal(2, state.Warnings.Count);
        var rendered = Render(doc, state);
        Assert.Contains(rendered.Warnings, w => w.Contains("caller language context required"));
        Assert.Contains(rendered.Warnings, w => w.Contains("caller identity context required"));
    }

    [Theory]
    [InlineData("/Language /bad", "/Language")]
    [InlineData("/Language << >>", "/Language")]
    [InlineData("/Language << /Lang /en >>", "/Language")]
    [InlineData("/Language << /Lang (en_US) >>", "/Language")]
    [InlineData("/Language << /Lang (en-) >>", "/Language")]
    [InlineData("/Language << /Lang (en--US) >>", "/Language")]
    [InlineData("/Language << /Lang (123) >>", "/Language")]
    [InlineData("/Language << /Lang (abcdefghi) >>", "/Language")]
    [InlineData("/Language << /Lang (en) /Preferred true >>", "/Language")]
    [InlineData("/Language << /Lang (en) /Preferred /bad >>", "/Language")]
    [InlineData("/User /bad", "/User")]
    [InlineData("/User << /Type /bad /Name (Ada) >>", "/User")]
    [InlineData("/User << /Name (Ada) >>", "/User")]
    [InlineData("/User << /Type /Ind >>", "/User")]
    [InlineData("/User << /Type /Ind /Name /Ada >>", "/User")]
    [InlineData("/User << /Type /Ind /Name [] >>", "/User")]
    [InlineData("/User << /Type /Ind /Name [(Ada) 42] >>", "/User")]
    [InlineData("/User << /Type /Ind /Name [99 0 R] >>", "/User")]
    [InlineData("/Language 99 0 R", "/Language")]
    [InlineData("/User 99 0 R", "/User")]
    public void Malformed_context_rules_warn_without_changing_configuration(string usage, string category)
    {
        var doc = UsageDocument(usage, categories: category);
        var state = new OptionalContentState(doc);
        Assert.True(state.IsVisible(Group(doc, 5), context: new() { Language = "en", UserName = "Ada" }));
        Assert.Contains(state.Warnings, w => w.StartsWith("invalid optional-content"));
        Assert.True(state.IsVisible(Group(doc, 6)));
    }

    [Theory]
    [InlineData("en_US")]
    [InlineData("en-")]
    [InlineData(" en")]
    public void Invalid_caller_language_does_not_change_visibility(string language)
    {
        var doc = UsageDocument("/Language << /Lang (fr) >>");
        var state = new OptionalContentState(doc);
        Assert.True(state.IsVisible(Group(doc, 5), context: new() { Language = language }));
        Assert.Contains("invalid optional-content Language context", state.Warnings);
    }

    [Fact]
    public void Usage_rules_require_application_selection_and_matching_event()
    {
        var doc = UsageDocument("/Language << /Lang (fr) >> /User << /Type /Ind /Name (Other) >>",
            configuration: "/BaseState /ON /AS [<< /Event /Print /Category [/Language /User] /OCGs [5 0 R] >>]");
        var context = new OptionalContentContext { Language = "en", UserName = "Ada" };
        var state = new OptionalContentState(doc);
        Assert.True(state.IsVisible(Group(doc, 5), context: context));
        Assert.False(state.IsVisible(Group(doc, 5), ContentInterpreter.OptionalContentEvent.Print, context: context));
        Assert.True(state.IsVisible(Group(doc, 6), ContentInterpreter.OptionalContentEvent.Print, context: context));
        Assert.True(state.IsVisible(Group(doc, 5), ContentInterpreter.OptionalContentEvent.Export, context: context));
        var noApplications = UsageDocument("/Language << /Lang (fr) >>", configuration: "/BaseState /ON");
        Assert.True(new OptionalContentState(noApplications).IsVisible(Group(noApplications, 5), context: context));
    }

    [Theory]
    [InlineData("/OCGs /bad")]
    [InlineData("/OCGs 5 0 R")]
    public void Context_categories_require_a_valid_application_group_array(string groups)
    {
        var doc = UsageDocument("/Language << /Lang (fr) >> /User << /Type /Ind /Name (Other) >>",
            configuration: $"/BaseState /ON /AS [<< /Event /View /Category [/Language /User] {groups} >>]");
        var state = new OptionalContentState(doc);
        Assert.True(state.IsVisible(Group(doc, 5), context: new() { Language = "en", UserName = "Ada" }));
        Assert.Contains(state.Warnings, w => w.Contains("application OCGs array"));
    }

    [Theory]
    [InlineData("/Language /User")]
    [InlineData("/User /Language")]
    public void Context_recommendations_are_conjoined_and_can_enable_initially_off_layers(string categories)
    {
        var doc = UsageDocument("/Language << /Lang (en) >> /User << /Type /Ind /Name (Ada) >>",
            configuration: $"/BaseState /OFF /AS [<< /Event /Export /Category [{categories}] /OCGs [5 0 R] >>]");
        var state = new OptionalContentState(doc);
        Assert.True(state.IsVisible(Group(doc, 5), ContentInterpreter.OptionalContentEvent.Export,
            context: new() { Language = "en", UserName = "Ada" }));
        Assert.False(state.IsVisible(Group(doc, 5), ContentInterpreter.OptionalContentEvent.Export,
            context: new() { Language = "en", UserName = "Other" }));
        Assert.False(state.IsVisible(Group(doc, 5), ContentInterpreter.OptionalContentEvent.Export,
            context: new() { Language = "fr", UserName = "Ada" }));
    }

    [Fact]
    public void Context_recommendations_cannot_undo_off_recommendations_in_other_applications()
    {
        var doc = UsageDocument("/Language << /Lang (en-GB) /Preferred /ON >> /User << /Type /Ind /Name (Ada) >>",
            "/Language << /Lang (en-US) >>",
            configuration: "/BaseState /ON /AS [" +
                "<< /Event /View /Category [/Language] /OCGs [5 0 R 6 0 R] >> " +
                "<< /Event /View /Category [/User] /OCGs [5 0 R] >> " +
                "<< /Event /View /Category [/Language] /OCGs [5 0 R] >>]");
        var state = new OptionalContentState(doc);
        Assert.False(state.IsVisible(Group(doc, 5), context: new() { Language = "en-US", UserName = "Ada" }));
    }

    [Theory]
    [InlineData("/View /Language /Zoom", "OFF", 1, false)]
    [InlineData("/Language /View /Zoom", "OFF", 1, false)]
    [InlineData("/Language /View /Zoom", "ON", 1, true)]
    [InlineData("/Language /View /Zoom", "ON", 2, false)]
    public void New_categories_respect_existing_event_and_zoom_recommendations(string categories,
        string viewState, double zoom, bool visible)
    {
        var doc = UsageDocument($"/Language << /Lang (en) >> /View << /ViewState /{viewState} >> /Zoom << /max 1.5 >>",
            configuration: $"/BaseState /OFF /AS [<< /Event /View /Category [{categories}] /OCGs [5 0 R] >>]");
        Assert.Equal(visible, new OptionalContentState(doc).IsVisible(Group(doc, 5), zoom: zoom,
            context: new() { Language = "en" }));
    }

    [Fact]
    public void Named_configuration_locked_peers_and_manual_overrides_use_the_supplied_context()
    {
        var doc = UsageDocument("/Language << /Lang (en) >>", configuration: "/BaseState /OFF",
            configs: "/Configs [<< /Name (Named) /BaseState /Unchanged /Locked [5 0 R] /RBGroups [[5 0 R 6 0 R]] " +
                "/AS [<< /Event /View /Category [/Language] /OCGs [5 0 R 6 0 R] >>] >>]");
        var state = new OptionalContentState(doc, "Named");
        var context = new OptionalContentContext { Language = "en" };
        Assert.False(new OptionalContentState(doc).IsVisible(Group(doc, 5), context: context));
        Assert.True(state.IsVisible(Group(doc, 5), context: context));
        Assert.Throws<InvalidOperationException>(() => state.SetVisibility(Group(doc, 6), true, context: context));
        Assert.False(state.IsVisible(Group(doc, 6), context: context));
        Assert.Throws<InvalidOperationException>(() => state.SetVisibility(Group(doc, 5), false, context: context));
        state.SetVisibility(Group(doc, 6), true, context: new() { Language = "fr" });
        Assert.True(state.IsVisible(Group(doc, 6), context: context));

        var manual = new OptionalContentState(doc, "Named");
        manual.SetVisibility(Group(doc, 6), true);
        Assert.True(manual.IsVisible(Group(doc, 6), context: new() { Language = "fr" }));
    }

    [Fact]
    public void Rendering_and_direct_interpreter_share_context_and_refresh_cached_visibility_between_renders()
    {
        var doc = UsageDocument("/Language << /Lang (en) >>", "/Language << /Lang (fr) >>");
        var context = new OptionalContentContext { Language = "en" };
        var rendered = DocumentRenderer.RenderPage(doc.Pages[0], new PdfRenderOptions { OptionalContentContext = context }, 72);
        Assert.Equal((255, 0, 0), Pixel(rendered, 50));
        Assert.Equal((255, 255, 255), Pixel(rendered, 150));
        Assert.Empty(rendered.Warnings);

        using var raster = new SoftwareRasterizer(200, 200);
        var interpreter = new ContentInterpreter(doc.File, raster) { OptionalContentContext = context };
        interpreter.Render(doc.Pages[0], Matrix.Identity);
        interpreter.OptionalContentContext = new() { Language = "fr" };
        interpreter.Render(doc.Pages[0], Matrix.Identity);
        var repeated = new RenderedPage(200, 200, raster.ToRgba(), interpreter.Warnings);
        Assert.Equal((0, 0, 255), Pixel(repeated, 150));
        Assert.Empty(interpreter.Warnings);

        var state = new OptionalContentState(doc);
        var warnings = DocumentRenderer.RenderPage(doc.Pages[0], new PdfRenderOptions { OptionalContentState = state }, 72);
        Assert.Contains(warnings.Warnings, w => w.Contains("caller language context required"));
        Assert.Contains(state.Warnings, w => w.Contains("caller language context required"));
    }

    [Fact]
    public void Indirect_usage_dictionaries_names_and_languages_are_resolved()
    {
        var doc = UsageDocument("/Language 8 0 R /User 9 0 R", categories: "/Language /User",
            extraObjects: "8 0 obj\n<< /Lang 10 0 R /Preferred /ON >>\nendobj\n" +
                "9 0 obj\n<< /Type /Ind /Name [11 0 R] >>\nendobj\n" +
                "10 0 obj\n(en)\nendobj\n11 0 obj\n(Ada)\nendobj\n");
        var state = new OptionalContentState(doc);
        Assert.True(state.IsVisible(Group(doc, 5), context: new() { Language = "en-US", UserName = "Ada" }));
        Assert.Empty(state.Warnings);
    }

    [Fact]
    public void Descriptive_and_unknown_categories_warn_without_executing_active_content()
    {
        var doc = UsageDocument("/CreatorInfo << /Creator (App) /Subtype /Artwork >> /PageElement << /Subtype /HF >> " +
            "/User << /Type /Ind /Name (Ada) >> /A << /S /JavaScript /JS (throw new Error) >>",
            categories: "/CreatorInfo /PageElement /Unknown 42");
        var state = new OptionalContentState(doc);
        Assert.True(state.IsVisible(Group(doc, 5), context: new() { UserName = "Other" }));
        Assert.Equal(4, state.Warnings.Count);
        Assert.All(state.Warnings, w => Assert.StartsWith("unsupported optional-content usage category", w));
        Assert.Equal((255, 0, 0), Pixel(Render(doc, state), 50));
    }

    [Fact]
    public void All_exact_matches_and_all_preferred_partials_are_enabled()
    {
        var doc = UsageDocument("/Language << /Lang (en-US) >>", "/Language << /Lang (EN-us) >>");
        var state = new OptionalContentState(doc);
        Assert.All(state.Groups.Take(2), g => Assert.True(state.IsVisible(g, context: new() { Language = "en-US" })));
        var partials = UsageDocument("/Language << /Lang (en-US) /Preferred /ON >>",
            "/Language << /Lang (en-GB) /Preferred /ON >>");
        var partialState = new OptionalContentState(partials);
        Assert.All(partialState.Groups.Take(2),
            g => Assert.True(partialState.IsVisible(g, context: new() { Language = "en-AU" })));
    }

    [Fact]
    public void Manual_visibility_overrides_context_driven_layer_state_and_does_not_change_language_metadata()
    {
        var doc = UsageDocument("/Language << /Lang (en) >> /User << /Type /Ind /Name (Ada) >>",
            categories: "/Language /User");
        var state = new OptionalContentState(doc);
        var a = Group(doc, 5);
        var matching = new OptionalContentContext { Language = "en", UserName = "Ada" };
        var other = new OptionalContentContext { Language = "fr", UserName = "Other" };
        Assert.False(state.IsVisible(a, context: other));
        state.SetVisibility(a, true, context: other);
        Assert.True(state.IsVisible(a, context: other));
        state.SetVisibility(a, false, context: matching);
        Assert.False(state.IsVisible(a, context: matching));
        var usage = Assert.IsType<PdfDictionary>(doc.File.Resolve(a.Get("Usage")));
        var language = Assert.IsType<PdfDictionary>(doc.File.Resolve(usage.Get("Language")));
        Assert.Equal("en", Assert.IsType<PdfString>(doc.File.Resolve(language.Get("Lang"))).Text);
        Assert.Null(doc.File.Catalog.Get("Lang"));
        Assert.Null(doc.Pages[0].Dict.Get("Lang"));
    }

    [Theory]
    [InlineData("5 1 R", true)]
    [InlineData("99 0 R", true)]
    [InlineData("5 0 R 6 1 R", true)]
    [InlineData("5 0 R 99 0 R", true)]
    [InlineData("5 0 R << /Type /OCG /Usage << /Language << /Lang (en-US) >> >> >>", true)]
    [InlineData("5 0 R 6 0 R", false)]
    public void Invalid_application_references_cannot_select_layers_or_suppress_preferred_language(string groups, bool visible)
    {
        var doc = UsageDocument("/Language << /Lang (en-GB) /Preferred /ON >>", "/Language << /Lang (en-US) >>",
            configuration: $"/BaseState /ON /AS [<< /Event /View /Category [/Language] /OCGs [{groups}] >>]");
        var state = new OptionalContentState(doc);
        Assert.Equal(visible, state.IsVisible(Group(doc, 5), context: new() { Language = "en-US" }));
        if (visible) Assert.Contains(state.Warnings, w => w.Contains("application OCGs reference"));
        else Assert.Empty(state.Warnings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/OCGs []")]
    public void Omitted_or_empty_application_group_list_selects_no_context_driven_layers(string groups)
    {
        var doc = UsageDocument("/Language << /Lang (fr) >> /User << /Type /Ind /Name (Other) >>",
            configuration: $"/BaseState /ON /AS [<< /Event /View /Category [/Language /User] {groups} >>]");
        var state = new OptionalContentState(doc);
        Assert.True(state.IsVisible(Group(doc, 5), context: new() { Language = "en", UserName = "Ada" }));
        Assert.Empty(state.Warnings);
    }
}
