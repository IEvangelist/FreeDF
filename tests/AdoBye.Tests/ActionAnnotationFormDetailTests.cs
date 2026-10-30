using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;

namespace AdoBye.Tests;

public class ActionAnnotationFormDetailTests
{
    // Object 1 = catalog (entries supplied), 2 = pages, 3 = page (annots supplied), 4.. = extra objects.
    static PdfDocument Load(string catalogExtra = "", string pageExtra = "", string objects = "")
    {
        var pdf = "%PDF-2.0\n" +
            $"1 0 obj\n<< /Type /Catalog /Pages 2 0 R {catalogExtra} >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
            $"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] {pageExtra} >>\nendobj\n" +
            objects +
            "trailer\n<< /Root 1 0 R /Size 100 >>\n%%EOF\n";
        return PdfDocument.Load(Encoding.ASCII.GetBytes(pdf));
    }

    static PdfDocument LoadOpenAction(string action, string objects = "") =>
        Load($"/OpenAction 10 0 R", "", $"10 0 obj\n<< {action} >>\nendobj\n" + objects);

    [Fact]
    public void Launch_action_is_typed_and_inert()
    {
        var doc = LoadOpenAction("/S /Launch /F (a.exe) /NewWindow true /Win << /F (calc.exe) /D (c:\\\\) /O (open) /P (x) >> /Mac (m) /Unix [1]");
        var d = Assert.IsType<PdfLaunchAction>(Assert.Single(doc.GetActions()).LaunchDetails);
        Assert.Equal("a.exe", Assert.IsType<PdfString>(d.FileSpecification).Text);
        Assert.True(d.NewWindow);
        Assert.Equal("calc.exe", d.Windows!.FileName!.Text);
        Assert.Equal("open", d.Windows.Operation!.Text);
        Assert.NotNull(d.Mac);
        Assert.IsType<PdfArray>(d.Unix);
    }

    [Fact]
    public void Thread_sound_movie_hide_defaults_and_values()
    {
        var thread = Assert.IsType<PdfThreadAction>(Assert.Single(LoadOpenAction("/S /Thread /D 2 /B 3").GetActions()).ThreadDetails);
        Assert.Equal(2, Assert.IsType<PdfInteger>(thread.Thread).Value);
        Assert.Equal(3, Assert.IsType<PdfInteger>(thread.Bead).Value);

        var sound = Assert.IsType<PdfSoundAction>(Assert.Single(LoadOpenAction("/S /Sound /Sound 11 0 R", "11 0 obj\n<< /R 8000 /Length 0 >>\nstream\n\nendstream\nendobj\n").GetActions()).SoundDetails);
        Assert.NotNull(sound.Sound);
        Assert.Null(sound.Volume);
        Assert.Equal(1.0, sound.EffectiveVolume);
        Assert.False(sound.EffectiveRepeat);

        var movie = Assert.IsType<PdfMovieAction>(Assert.Single(LoadOpenAction("/S /Movie /T (clip)").GetActions()).MovieDetails);
        Assert.Equal("Play", movie.EffectiveOperation);
        Assert.Equal("clip", movie.Title!.Text);

        var hide = Assert.IsType<PdfHideAction>(Assert.Single(LoadOpenAction("/S /Hide /T (field1)").GetActions()).HideDetails);
        Assert.True(hide.EffectiveHide);
        Assert.Null(hide.Hide);
        var show = Assert.IsType<PdfHideAction>(Assert.Single(LoadOpenAction("/S /Hide /T [(a) (b)] /H false").GetActions()).HideDetails);
        Assert.False(show.EffectiveHide);
        Assert.IsType<PdfArray>(show.Target);
    }

    [Fact]
    public void SetOcgState_groups_changes_and_preserves_defaults()
    {
        var doc = LoadOpenAction("/S /SetOCGState /State [/ON 11 0 R 12 0 R /Toggle 11 0 R /Weird]", "11 0 obj\n<< /Type /OCG /Name (A) >>\nendobj\n12 0 obj\n<< /Type /OCG /Name (B) >>\nendobj\n");
        var d = Assert.IsType<PdfSetOcgStateAction>(Assert.Single(doc.GetActions()).SetOcgStateDetails);
        Assert.True(d.EffectivePreserveRadioButtons);
        Assert.Equal(3, d.Changes.Count);
        Assert.Equal("ON", d.Changes[0].Operation);
        Assert.Equal(2, d.Changes[0].Groups.Count);
        Assert.Empty(d.Changes[2].Groups);
    }

    [Fact]
    public void Trans_goto3dview_richmediaexecute_and_javascript()
    {
        var trans = Assert.Single(LoadOpenAction("/S /Trans /Trans << /S /Fade /D 2.5 >>").GetActions()).TransDetails!;
        Assert.Equal("Fade", trans.Transition!.EffectiveStyle);
        Assert.Equal(2.5, trans.Transition.EffectiveDuration);
        Assert.Equal("H", trans.Transition.EffectiveDimension);
        Assert.Equal("I", trans.Transition.EffectiveMotion);

        var view = Assert.Single(LoadOpenAction("/S /GoTo3DView /TA << /Subtype /3D >> /V /F").GetActions()).GoTo3DViewDetails!;
        Assert.NotNull(view.Target);
        Assert.Equal("F", Assert.IsType<PdfName>(view.View).Value);

        var rm = Assert.Single(LoadOpenAction("/S /RichMediaExecute /TA << /Subtype /RichMedia >> /TI << /X 1 >> /CMD << /C (play) /A [1 2] >>").GetActions()).RichMediaExecuteDetails!;
        Assert.Equal("play", rm.CommandName!.Text);
        Assert.Equal(2, rm.CommandArguments!.Count);

        var js = Assert.Single(LoadOpenAction("/S /JavaScript /JS (app.alert(1))").GetActions()).JavaScriptDetails!;
        Assert.Equal("app.alert(1)", Assert.IsType<PdfString>(js.Script).Text);
    }

    [Fact]
    public void Next_chain_depth_and_diamond_dag_are_bounded()
    {
        var doc = LoadOpenAction("/S /Named /N /NextPage /Next [11 0 R 12 0 R]",
            "11 0 obj\n<< /S /Named /N /A /Next 13 0 R >>\nendobj\n12 0 obj\n<< /S /Named /N /B /Next 13 0 R >>\nendobj\n13 0 obj\n<< /S /Named /N /C >>\nendobj\n");
        var report = doc.GetActionReport();
        Assert.False(report.Truncated);
        Assert.Equal(5, report.Actions.Count);
        Assert.Equal(new[] { 0, 1, 1, 2, 2 }, report.Actions.Select(a => a.ChainDepth).Order().ToArray());
    }

    [Fact]
    public void Exponential_fan_out_array_is_cut_by_work_budget_or_cycle_detection()
    {
        // Array 10 contains itself 40 times plus an action: cycle detection must stop this immediately.
        var self = string.Join(' ', Enumerable.Repeat("10 0 R", 40));
        var doc = Load("/OpenAction 10 0 R", "", $"10 0 obj\n[ {self} << /S /Named /N /X >> ]\nendobj\n");
        var report = doc.GetActionReport();
        Assert.True(report.Actions.Count <= 1);

        // Layered diamonds: 30 levels of two actions each pointing at both actions of the next level.
        var sb = new StringBuilder();
        for (var i = 0; i < 30; i++)
        {
            var a = 20 + i * 2; var b = a + 1; var next = $"[{a + 2} 0 R {a + 3} 0 R]";
            sb.Append($"{a} 0 obj\n<< /S /Named /N /A /Next {next} >>\nendobj\n{b} 0 obj\n<< /S /Named /N /B /Next {next} >>\nendobj\n");
        }
        var dag = Load("/OpenAction 20 0 R", "", sb.ToString());
        var dagReport = dag.GetActionReport();
        Assert.True(dagReport.Truncated);
        Assert.True(dagReport.Actions.Count <= 10_000);
    }

    [Fact]
    public void Additional_actions_are_exposed_for_catalog_page_annotation_field_and_link_pa()
    {
        var doc = Load(
            "/AA << /WC 10 0 R /WS 10 0 R /DS 10 0 R /WP 10 0 R /DP 10 0 R >> /AcroForm << /Fields [20 0 R] >>",
            "/AA << /O 10 0 R /C 10 0 R >> /Annots [21 0 R 22 0 R]",
            "10 0 obj\n<< /S /Named /N /NextPage >>\nendobj\n" +
            "20 0 obj\n<< /FT /Tx /T (t) /AA << /K 10 0 R /F 10 0 R /V 10 0 R /C 10 0 R /Zz 10 0 R >> >>\nendobj\n" +
            "21 0 obj\n<< /Subtype /Widget /Rect [0 0 1 1] /AA << /E 10 0 R /X 10 0 R /D 10 0 R /U 10 0 R /Fo 10 0 R /Bl 10 0 R /PO 10 0 R /PC 10 0 R /PV 10 0 R /PI 10 0 R >> >>\nendobj\n" +
            "22 0 obj\n<< /Subtype /Link /Rect [0 0 1 1] /PA << /S /URI /URI (http://example.invalid/) >> >>\nendobj\n");
        var actions = doc.GetActions();
        string[] Keys(PdfActionScope s) => actions.Where(a => a.Scope == s && a.Event is not null).Select(a => a.Event!.Key).ToArray();
        Assert.Equal(new[] { "WC", "WS", "DS", "WP", "DP" }, Keys(PdfActionScope.Catalog));
        Assert.Equal(new[] { "O", "C" }, Keys(PdfActionScope.Page));
        Assert.Contains("K", Keys(PdfActionScope.FormField));
        Assert.Contains("C", Keys(PdfActionScope.FormField));
        Assert.Equal(4, Keys(PdfActionScope.FormField).Length);
        Assert.Equal(11, Keys(PdfActionScope.Annotation).Length);
        Assert.Contains(actions, a => a.Trigger == "PA" && a.UriDetails is not null && a.Event!.Group == PdfTriggerEventGroup.LinkUri);
        Assert.Contains(actions, a => a.Trigger == "AA/Zz" && a.Event is null);
    }

    [Fact]
    public void Annotation_subtypes_expose_typed_keys_and_defaults()
    {
        var doc = Load("", "/Annots [10 0 R 11 0 R 12 0 R 13 0 R 14 0 R 15 0 R 16 0 R 17 0 R 18 0 R 19 0 R]",
            "10 0 obj\n<< /Subtype /Text /Rect [10 10 0 0] /Contents (hi) /F 4 /CA 0.5 /Name /Help /Open true >>\nendobj\n" +
            "11 0 obj\n<< /Subtype /Link /Rect [0 0 1 1] /Dest [3 0 R /Fit] /QuadPoints [0 0 1 0 1 1 0 1] >>\nendobj\n" +
            "12 0 obj\n<< /Subtype /Line /Rect [0 0 1 1] /L [1 2 3 4] /LL 5 /Cap true /CP /Top >>\nendobj\n" +
            "13 0 obj\n<< /Subtype /Ink /Rect [0 0 1 1] /InkList [[1 2 3 4] [5 6]] >>\nendobj\n" +
            "14 0 obj\n<< /Subtype /Redact /Rect [0 0 1 1] /QuadPoints [1 2 3 4 5 6 7 8] /OverlayText (X) /Repeat true >>\nendobj\n" +
            "15 0 obj\n<< /Subtype /Watermark /Rect [0 0 1 1] /FixedPrint << /Type /FixedPrint /Matrix [1 0 0 1 0 0] /H 0.5 >> >>\nendobj\n" +
            "16 0 obj\n<< /Subtype /Widget /Rect [0 0 1 1] /H /P >>\nendobj\n" +
            "17 0 obj\n<< /Subtype /TrapNet /Rect [0 0 1 1] /LastModified (D:20200101) >>\nendobj\n" +
            "18 0 obj\n<< /Subtype /Stamp /Rect [0 0 1 1] >>\nendobj\n" +
            "19 0 obj\n<< /Subtype /Popup /Rect [0 0 1 1] /Parent 10 0 R >>\nendobj\n");
        var set = doc.Pages[0].GetAnnotationDetails();
        Assert.False(set.Truncated);
        var a = set.Annotations;
        Assert.Equal(10, a.Count);
        Assert.Equal(0, a[0].Common.Rect!.Value.Left);
        Assert.True(a[0].Common.FlagSet.HasFlag(PdfAnnotationFlags.Print));
        Assert.Equal(0.5, a[0].Markup!.EffectiveOpacity);
        Assert.Equal("R", a[0].Markup!.EffectiveReplyType);
        Assert.Equal("Help", a[0].Text!.EffectiveIconName);
        Assert.True(a[0].Text!.EffectiveOpen);
        Assert.Equal("I", a[1].Link!.EffectiveHighlight);
        Assert.Equal(8, a[1].Link!.QuadPoints!.Values.Count);
        Assert.Equal(5, a[2].Line!.EffectiveLeaderLength);
        Assert.Equal("Top", a[2].Line!.EffectiveCaptionPosition);
        Assert.Equal(2, a[3].Ink!.InkList.Count);
        Assert.True(a[4].Redact!.EffectiveRepeat);
        Assert.Equal(0.5, a[5].Watermark!.EffectiveHorizontalTranslation);
        Assert.Equal(0, a[5].Watermark!.EffectiveVerticalTranslation);
        Assert.Equal("P", a[6].Widget!.Highlight!.Value);
        Assert.NotNull(a[7].TrapNet!.LastModified);
        Assert.Equal("Draft", a[8].Stamp!.EffectiveIconName);
        Assert.False(a[9].Popup!.EffectiveOpen);
        Assert.Null(a[9].Markup);
        Assert.Same(a[2].Dictionary, doc.Pages[0].GetAnnotations()[2].Dict);
    }

    [Fact]
    public void Annotation_numeric_arrays_are_bounded_and_malformed_values_are_null()
    {
        var big = string.Join(' ', Enumerable.Repeat("1", AnnotationDetailFeatures.MaxNumbersPerArray + 10));
        var doc = Load("", "/Annots [11 0 R 10 0 R]",
            "10 0 obj\n<< /Subtype /Square /Rect [0 0 1 1] /RD [1 (x) 2] /BS 5 >>\nendobj\n" +
            $"11 0 obj\n<< /Subtype /Highlight /Rect [0 0 1] /QuadPoints [{big}] >>\nendobj\n");
        var set = doc.Pages[0].GetAnnotationDetails();
        Assert.True(set.Truncated);
        Assert.Equal(2, set.Annotations.Count);
        Assert.Null(set.Annotations[0].Common.Rect);
        Assert.True(set.Annotations[0].TextMarkup!.QuadPoints!.Truncated);
        Assert.Equal(AnnotationDetailFeatures.MaxNumbersPerArray, set.Annotations[0].TextMarkup!.QuadPoints!.Values.Count);
        Assert.True(set.Annotations[1].Shape!.Differences!.HasNonNumeric);
        Assert.Null(set.Annotations[1].Shape!.BorderStyle);
    }

    [Fact]
    public void Form_field_inheritance_flags_options_and_signature_metadata()
    {
        var doc = Load("/AcroForm << /Fields [10 0 R 14 0 R] /DA (/Helv 0 Tf) /Q 1 /XFA [(x) 30 0 R] /NeedAppearances true /SigFlags 3 >>", "",
            "10 0 obj\n<< /T (parent) /FT /Ch /Ff 393216 /V (b) /DV (a) /Opt [(a) [(b) (Bee)]] /MaxLen 7 /Kids [11 0 R 12 0 R] >>\nendobj\n" +
            "11 0 obj\n<< /Parent 10 0 R /T (c1) /TI 2 /I [0 1] >>\nendobj\n" +
            "12 0 obj\n<< /Parent 10 0 R /T (c2) /FT /Tx /Ff 4096 /DA (/Cour 9 Tf) /Q 2 >>\nendobj\n" +
            "14 0 obj\n<< /T (sig) /FT /Sig /Lock << /Action /Include /Fields [(a)] /P 2 >> /SV << /Ff 3 /SubFilter [/adbe.pkcs7.detached] /V 1 /Reasons [(r)] /Cert << /X 1 >> >> >>\nendobj\n" +
            "30 0 obj\n<< /Length 0 >>\nstream\n\nendstream\nendobj\n");
        var set = doc.GetFormFieldDetails();
        Assert.True(set.HasXfa);
        Assert.True(set.NeedAppearances);
        Assert.Equal(3, set.SignatureFlags);
        var c1 = set.Fields.Single(x => x.FullName == "parent.c1");
        Assert.Equal("Ch", c1.FieldType!.Value);
        Assert.True(c1.FlagSet.Combo);
        Assert.True(c1.FlagSet.Edit);
        Assert.False(c1.FlagSet.Multiline);
        Assert.Equal("b", ((PdfString)c1.Value!).Text);
        Assert.Equal(7, c1.MaxLength);
        Assert.Equal(2, c1.Options.Count);
        Assert.Equal("Bee", c1.Options[1].Display!.Text);
        Assert.Equal("b", c1.Options[1].Export!.Text);
        Assert.Equal(2, c1.TopIndex);
        Assert.Equal(new long[] { 0, 1 }, c1.SelectedIndices);
        Assert.True(c1.DefaultAppearanceFromAcroForm);
        Assert.Equal(1, c1.EffectiveQuadding);
        var c2 = set.Fields.Single(x => x.FullName == "parent.c2");
        Assert.Equal("Tx", c2.FieldType!.Value);
        Assert.True(c2.FlagSet.Multiline);
        Assert.False(c2.DefaultAppearanceFromAcroForm);
        Assert.Equal(2, c2.EffectiveQuadding);
        var sig = set.Fields.Single(x => x.FullName == "sig");
        Assert.Equal("Include", sig.Lock!.Action!.Value);
        Assert.Equal(2, sig.Lock.Permissions);
        Assert.Equal(3, sig.SeedValue!.Flags);
        Assert.NotNull(sig.SeedValue.Certificate);
    }

    [Fact]
    public void Form_field_cycles_terminate_and_report_truncation_at_depth()
    {
        var doc = Load("/AcroForm << /Fields [10 0 R] >>", "",
            "10 0 obj\n<< /T (a) /Kids [10 0 R] >>\nendobj\n");
        var set = doc.GetFormFieldDetails();
        Assert.False(set.Truncated);
        Assert.Empty(set.Fields);
    }

    [Fact]
    public void Opening_listing_saving_and_rendering_never_executes_any_action()
    {
        var marker = Path.Combine(Path.GetTempPath(), "adobye-action-marker-" + Guid.NewGuid().ToString("N"));
        var escaped = marker.Replace("\\", "\\\\");
        var doc = Load(
            $"/OpenAction 10 0 R /AA << /WC 10 0 R /WS 10 0 R /WP 10 0 R >> /Names << /JavaScript << /Names [(j) 11 0 R] >> >>",
            "/AA << /O 10 0 R >> /Annots [12 0 R]",
            $"10 0 obj\n<< /S /Launch /F (cmd.exe) /Win << /F (cmd.exe) /P (/c echo x > {escaped}) >> /Next 11 0 R >>\nendobj\n" +
            $"11 0 obj\n<< /S /JavaScript /JS (app.launchURL('file:///{escaped}')) /Next 13 0 R >>\nendobj\n" +
            "12 0 obj\n<< /Subtype /Link /Rect [0 0 10 10] /A << /S /URI /URI (http://127.0.0.1:1/unreachable) >> /AA << /E 10 0 R >> >>\nendobj\n" +
            "13 0 obj\n<< /S /SubmitForm /F (http://127.0.0.1:1/submit) /Next << /S /ImportData /F (nonexistent.fdf) >> >>\nendobj\n");
        try
        {
            var actions = doc.GetActions();
            Assert.Contains(actions, a => a.LaunchDetails is not null);
            Assert.Contains(actions, a => a.JavaScriptDetails is not null);
            _ = doc.GetFormFieldDetails();
            _ = doc.Pages[0].GetAnnotationDetails();
            var saved = PdfEditor.Open(doc).Save();
            var reopened = PdfDocument.Load(saved);
            Assert.Equal(actions.Count, reopened.GetActions().Count);
            _ = DocumentRenderer.RenderPage(reopened.Pages[0], 24);
            Assert.False(File.Exists(marker));
        }
        finally { if (File.Exists(marker)) File.Delete(marker); }
    }

    [Fact]
    public void Document_assembly_has_no_process_or_network_dependency()
    {
        var names = typeof(PdfDocument).Assembly.GetReferencedAssemblies().Select(n => n.Name).ToArray();
        Assert.DoesNotContain("System.Diagnostics.Process", names);
        Assert.DoesNotContain("System.Net.Http", names);
        Assert.DoesNotContain("System.Net.Sockets", names);
    }
}
