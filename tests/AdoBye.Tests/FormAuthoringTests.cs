using System.Text;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Core;
using PortableDocument.Document;
using Xunit;

namespace AdoBye.Tests;

public class FormAuthoringTests
{
    static PdfEditor Blank() => PdfEditor.Open(PdfDocument.Load(TestPdf.Build("", mediaBox: "[0 0 400 400]")));
    static PdfDocument Reload(PdfEditor e) => PdfDocument.Load(e.Save());
    static byte[] Render(PdfDocument d) => DocumentRenderer.RenderPage(d.Pages[0], 72).Rgba;
    static PdfRect Box(double x, double y, double w, double h) => new(x, y, x + w, y + h);
    static readonly double[] Black = [0], Gray = [0.9], Blue = [0, 0, 1];

    static PdfDictionary AcroForm(PdfDocument d) => (PdfDictionary)d.File.Resolve(d.File.Catalog.Get("AcroForm"))!;
    static string StreamText(PdfDocument d, PdfObject? o) => Encoding.Latin1.GetString(((PdfStream)d.File.Resolve(o)!).RawData);
    static PdfObject? R(PdfDocument d, PdfObject? o) => d.File.Resolve(o);

    static IEnumerable<PdfDictionary> Widgets(PdfDocument d) => d.Pages[0].GetAnnotations().Where(a => a.Subtype == "Widget").Select(a => a.Dict);
    static string Name(PdfDocument d, PdfDictionary w)
    {
        var parts = new List<string>();
        for (PdfDictionary? c = w; c is not null; c = R(d, c.Get("Parent")) as PdfDictionary)
            if (R(d, c.Get("T")) is PdfString s) parts.Insert(0, s.Text);
        return string.Join('.', parts);
    }
    static PdfDictionary Widget(PdfDocument d, string name) => Widgets(d).First(w => Name(d, w) == name);
    static PdfDictionary Ap(PdfDocument d, PdfDictionary w, string key) => (PdfDictionary)R(d, ((PdfDictionary)R(d, w.Get("AP"))!).Get(key))!;

    static PdfEditor AllKinds()
    {
        var e = Blank();
        e.AddFormField(new(PdfFormFieldKind.Text, "name", 0, Box(10, 350, 150, 24)) { Value = "Ada Lovelace", Tooltip = "Full name", Required = true, BorderColor = Black, BackgroundColor = Gray });
        e.AddFormField(new(PdfFormFieldKind.MultilineText, "notes", 0, Box(10, 250, 150, 80)) { Value = "one two three four five six seven eight nine ten eleven", BorderColor = Black });
        e.AddFormField(new(PdfFormFieldKind.CombText, "zip", 0, Box(200, 350, 120, 24)) { Value = "12345", MaxLength = 8, BorderColor = Black });
        e.AddFormField(new(PdfFormFieldKind.PasswordText, "pin", 0, Box(200, 310, 120, 24)) { Value = "secret", BorderColor = Black });
        e.AddFormField(new(PdfFormFieldKind.Checkbox, "agree", 0, Box(10, 200, 20, 20)) { Value = "Yes", BorderColor = Black });
        e.AddFormField(new(PdfFormFieldKind.Radio, "size", 0, Box(10, 160, 16, 16)) { ExportValue = "S", BorderColor = Black });
        e.AddFormField(new(PdfFormFieldKind.Radio, "size", 0, Box(40, 160, 16, 16)) { ExportValue = "L", Value = "L", BorderColor = Black });
        e.AddFormField(new(PdfFormFieldKind.ComboBox, "color", 0, Box(200, 250, 100, 22)) { Options = [new("r", "Red"), new("g", "Green")], Value = "g", BorderColor = Black });
        e.AddFormField(new(PdfFormFieldKind.ListBox, "tags", 0, Box(200, 150, 100, 60)) { Options = [new("a"), new("b"), new("c")], Value = "b", BorderColor = Black });
        e.AddFormField(new(PdfFormFieldKind.PushButton, "go", 0, Box(10, 100, 80, 26)) { Caption = "Submit", BackgroundColor = [0.75], BorderColor = Black, BorderStyle = "B" });
        e.AddFormField(new(PdfFormFieldKind.Signature, "sig", 0, Box(200, 60, 140, 40)) { BorderColor = Blue });
        return e;
    }

    [Fact]
    public void Every_field_kind_is_created_with_valid_structure_and_appearances()
    {
        var d = Reload(AllKinds());
        var form = AcroForm(d);
        Assert.Equal(new PdfBoolean(false), R(d, form.Get("NeedAppearances")));
        Assert.StartsWith("/Helv", ((PdfString)R(d, form.Get("DA"))!).Text);
        var fonts = (PdfDictionary)R(d, ((PdfDictionary)R(d, form.Get("DR"))!).Get("Font"))!;
        Assert.NotNull(fonts.Get("Helv"));
        Assert.Equal(11, Widgets(d).Count());

        var fields = d.GetFormFields().ToDictionary(f => f.FullName);
        Assert.Equal("Ada Lovelace", fields["name"].Value);
        Assert.Equal("L", fields["size"].Value);
        Assert.Equal("g", fields["color"].Value);

        var name = Widget(d, "name");
        Assert.Equal(2, (int)(long)((PdfInteger)R(d, name.Get("Ff"))!).Value);
        Assert.Equal("Full name", ((PdfString)R(d, name.Get("TU"))!).Text);
        Assert.Contains("(Ada Lovelace) Tj", StreamText(d, ((PdfDictionary)R(d, name.Get("AP"))!).Get("N")));
        Assert.Contains("0.9 g", StreamText(d, ((PdfDictionary)R(d, name.Get("AP"))!).Get("N")));

        Assert.Contains("(******) Tj", StreamText(d, ((PdfDictionary)R(d, Widget(d, "pin").Get("AP"))!).Get("N")));
        var comb = StreamText(d, ((PdfDictionary)R(d, Widget(d, "zip").Get("AP"))!).Get("N"));
        Assert.Equal(5, comb.Split("Tj").Length - 1);
        var multi = StreamText(d, ((PdfDictionary)R(d, Widget(d, "notes").Get("AP"))!).Get("N"));
        Assert.True(multi.Split("Tj").Length - 1 >= 2);

        var agree = Widget(d, "agree");
        Assert.Equal("Yes", ((PdfName)R(d, agree.Get("AS"))!).Value);
        Assert.Equal(new[] { "Off", "Yes" }, Ap(d, agree, "N").Entries.Keys.Order().ToArray());
        Assert.Equal(new[] { "Off", "Yes" }, Ap(d, agree, "D").Entries.Keys.Order().ToArray());

        var radios = Widgets(d).Where(w => Name(d, w) == "size").ToList();
        Assert.Equal(2, radios.Count);
        Assert.Equal(["Off", "L"], radios.Select(w => ((PdfName)R(d, w.Get("AS"))!).Value));
        Assert.Same(R(d, radios[0].Get("Parent")), R(d, radios[1].Get("Parent")));

        Assert.Contains("(Green) Tj", StreamText(d, ((PdfDictionary)R(d, Widget(d, "color").Get("AP"))!).Get("N")));
        Assert.Contains("(Submit) Tj", StreamText(d, ((PdfDictionary)R(d, Widget(d, "go").Get("AP"))!).Get("N")));
        Assert.NotNull(((PdfDictionary)R(d, Widget(d, "go").Get("AP"))!).Get("D"));
        Assert.Equal("Sig", ((PdfName)R(d, Widget(d, "sig").Get("FT"))!).Value);
    }

    [Fact]
    public void Authored_fields_render()
    {
        var empty = Render(Reload(Blank()));
        var d = Reload(AllKinds());
        var px = Render(d);
        Assert.NotEqual(empty, px);
        int At(int x, int y) => (y * 400 + x) * 4;
        // name field background (0.9 gray) at page (20, 360) => raster y = 400 - 360
        Assert.InRange(px[At(150, 40)], 200, 240);
    }

    [Fact]
    public void Hierarchical_names_create_parents_and_duplicates_are_refused()
    {
        var e = Blank();
        e.AddFormField(new(PdfFormFieldKind.Text, "person.first", 0, Box(10, 350, 100, 20)));
        e.AddFormField(new(PdfFormFieldKind.Text, "person.last", 0, Box(10, 320, 100, 20)));
        Assert.Throws<ArgumentException>(() => e.AddFormField(new(PdfFormFieldKind.Text, "person.first", 0, Box(10, 290, 100, 20))));
        Assert.Throws<ArgumentException>(() => e.AddFormField(new(PdfFormFieldKind.Text, "person", 0, Box(10, 290, 100, 20))));
        e.AddFormField(new(PdfFormFieldKind.Text, "solo", 0, Box(10, 260, 100, 20)));
        Assert.Throws<ArgumentException>(() => e.AddFormField(new(PdfFormFieldKind.Text, "solo.child", 0, Box(10, 230, 100, 20))));
        Assert.Throws<ArgumentException>(() => e.AddFormField(new(PdfFormFieldKind.Text, "a..b", 0, Box(10, 230, 100, 20))));
        e.AddFormField(new(PdfFormFieldKind.Radio, "g", 0, Box(10, 200, 14, 14)) { ExportValue = "A" });
        Assert.Throws<ArgumentException>(() => e.AddFormField(new(PdfFormFieldKind.Radio, "g", 0, Box(40, 200, 14, 14)) { ExportValue = "A" }));
        Assert.Throws<ArgumentException>(() => e.AddFormField(new(PdfFormFieldKind.Checkbox, "g", 0, Box(40, 200, 14, 14))));

        var d = Reload(e);
        var fields = AcroForm(d).Get("Fields");
        var roots = ((PdfArray)R(d, fields)!).Items;
        Assert.Equal(3, roots.Count);
        var person = roots.Select(x => (PdfDictionary)R(d, x)!).First(x => R(d, x.Get("T")) is PdfString { Text: "person" });
        Assert.Equal(2, ((PdfArray)R(d, person.Get("Kids"))!).Count);
        Assert.Equal(["person.first", "person.last"], Widgets(d).Select(w => Name(d, w)).Where(n => n.StartsWith("person")).Order());
        Assert.Contains(d.GetFormFields(), f => f.FullName == "person.last");
    }

    [Fact]
    public void Update_rename_move_and_remove()
    {
        var e = AllKinds();
        var spec = e.GetFormFieldSpecs().First(s => s.Spec.Name == "name").Spec;
        Assert.Equal("Ada Lovelace", spec.Value);
        Assert.Equal("Full name", spec.Tooltip);
        Assert.True(spec.Required);
        e.UpdateFormField("name", spec with { Name = "group.fullname", Value = "Grace", Alignment = 1, FontSize = 14, FontName = "TiRo", TextColor = [1, 0, 0], Rect = Box(20, 340, 160, 30) });
        var d = Reload(e);
        var w = Widget(d, "group.fullname");
        Assert.Equal(1, (int)((PdfInteger)R(d, w.Get("Q"))!).Value);
        Assert.Equal("/TiRo 14 Tf 1 0 0 rg", ((PdfString)R(d, w.Get("DA"))!).Text);
        var ap = StreamText(d, ((PdfDictionary)R(d, w.Get("AP"))!).Get("N"));
        Assert.Contains("(Grace) Tj", ap);
        Assert.Contains("/TiRo 14 Tf", ap);
        Assert.DoesNotContain(Widgets(d), x => Name(d, x) == "name");
        Assert.NotNull(((PdfDictionary)R(d, ((PdfDictionary)R(d, AcroForm(d).Get("DR"))!).Get("Font"))!).Get("TiRo"));

        Assert.Throws<ArgumentException>(() => e.RenameFormField("group.fullname", "notes"));
        Assert.True(e.RemoveFormField("group.fullname"));
        Assert.False(e.RemoveFormField("group.fullname"));
        Assert.True(e.RemoveFormField("size", 0));
        d = Reload(e);
        Assert.DoesNotContain(Widgets(d), x => Name(d, x) == "group.fullname");
        Assert.Single(Widgets(d), x => Name(d, x) == "size");
        Assert.True(e.RemoveFormField("size"));
        Assert.DoesNotContain(Reload(e).GetFormFields(), f => f.FullName is "size" or "group.fullname" or "group");
    }

    [Fact]
    public void Duplicate_via_spec_gets_independent_widget_and_undo_restores()
    {
        var e = AllKinds();
        var before = e.Capture();
        var spec = e.GetFormFieldSpecs().First(s => s.Spec.Name == "agree").Spec;
        e.AddFormField(spec with { Name = "agree2", Rect = Box(60, 200, 20, 20) });
        Assert.Equal(12, Widgets(Reload(e)).Count());
        e.Restore(before);
        Assert.Equal(11, Widgets(Reload(e)).Count());
    }

    [Fact]
    public void Tab_order_reorders_widgets_and_sets_tabs()
    {
        var e = Blank();
        foreach (var (n, y) in new[] { ("a", 350), ("b", 320), ("c", 290) }) e.AddFormField(new(PdfFormFieldKind.Text, n, 0, Box(10, y, 100, 20)));
        e.ReorderFormWidgets(0, ["c", "a", "b"]);
        e.SetPageTabOrder(0, PdfFormTabOrder.Row);
        var d = Reload(e);
        Assert.Equal(["c", "a", "b"], d.Pages[0].GetAnnotations().Select(a => Name(d, a.Dict)));
        Assert.Equal("R", ((PdfName)R(d, d.Pages[0].Dict.Get("Tabs"))!).Value);
    }

    [Fact]
    public void Set_field_value_on_authored_fields_keeps_generated_appearances_without_NeedAppearances()
    {
        var e = AllKinds();
        Assert.True(e.SetFieldValue("name", "Grace Hopper"));
        Assert.True(e.SetFieldValue("agree", "Off"));
        var d = Reload(e);
        Assert.Equal(new PdfBoolean(false), R(d, AcroForm(d).Get("NeedAppearances")));
        Assert.Contains("(Grace Hopper) Tj", StreamText(d, ((PdfDictionary)R(d, Widget(d, "name").Get("AP"))!).Get("N")));
        Assert.Equal("Off", ((PdfName)R(d, Widget(d, "agree").Get("AS"))!).Value);
    }

    [Fact]
    public void Flatten_bakes_appearances_and_removes_widgets_and_acroform()
    {
        var e = AllKinds();
        var before = Render(Reload(e));
        var result = e.FlattenForm();
        Assert.Equal(10, result.Flattened);
        var d = Reload(e);
        Assert.Empty(Widgets(d));
        Assert.Null(d.File.Catalog.Get("AcroForm"));
        var after = Render(d);
        var diff = 0;
        for (var i = 0; i < before.Length; i += 4) if (Math.Abs(before[i] - after[i]) + Math.Abs(before[i + 1] - after[i + 1]) + Math.Abs(before[i + 2] - after[i + 2]) > 30) diff++;
        Assert.True(diff < before.Length / 4 / 200, $"{diff} pixels differ");
        Assert.NotEqual(Render(Reload(Blank())), after);
    }

    [Fact]
    public void Flatten_scopes_selected_and_read_only()
    {
        var e = Blank();
        e.AddFormField(new(PdfFormFieldKind.Text, "a", 0, Box(10, 350, 100, 20)) { Value = "A", ReadOnly = true });
        e.AddFormField(new(PdfFormFieldKind.Text, "b", 0, Box(10, 320, 100, 20)) { Value = "B" });
        e.AddFormField(new(PdfFormFieldKind.Text, "c", 0, Box(10, 290, 100, 20)) { Value = "C" });
        var ro = e.Capture();
        Assert.Equal(new PdfFlattenResult(1, 2), e.FlattenForm(new(PdfFlattenScope.ReadOnly)));
        Assert.Equal(["b", "c"], Widgets(Reload(e)).Select(w => ((PdfString)w.Get("T")!).Text));
        Assert.Equal(new PdfFlattenResult(1, 1), e.FlattenForm(new(PdfFlattenScope.Selected, ["c"])));
        var d = Reload(e);
        Assert.Equal(["b"], Widgets(d).Select(w => ((PdfString)w.Get("T")!).Text));
        Assert.NotNull(d.File.Catalog.Get("AcroForm"));
        e.Restore(ro);
        Assert.Equal(3, Widgets(Reload(e)).Count());
    }

    [Fact]
    public void Xfdf_and_fdf_roundtrip_and_import_validates()
    {
        var e = AllKinds();
        e.AddFormField(new(PdfFormFieldKind.Text, "person.first", 0, Box(10, 20, 100, 20)) { Value = "Ada <&> \"Q\"" });
        var xfdf = e.ExportFormData(PdfFormDataFormat.Xfdf);
        Assert.Contains("<field name=\"person\">", xfdf);
        Assert.Contains("Ada &lt;&amp;&gt;", xfdf);
        var fdf = e.ExportFormData(PdfFormDataFormat.Fdf);
        Assert.StartsWith("%FDF-1.2", fdf);

        foreach (var data in new[] { xfdf, fdf })
        {
            var target = Blank();
            target.AddFormField(new(PdfFormFieldKind.Text, "name", 0, Box(10, 350, 150, 24)));
            target.AddFormField(new(PdfFormFieldKind.Checkbox, "agree", 0, Box(10, 200, 20, 20)));
            target.AddFormField(new(PdfFormFieldKind.Text, "person.first", 0, Box(10, 20, 100, 20)));
            var result = target.ImportFormData(Encoding.UTF8.GetBytes(data));
            Assert.Equal(3, result.Applied);
            Assert.Contains(result.Skipped, s => s.StartsWith("pin:"));
            var d = Reload(target);
            var fields = d.GetFormFields().ToDictionary(f => f.FullName);
            Assert.Equal("Ada Lovelace", fields["name"].Value);
            Assert.Equal("Ada <&> \"Q\"", fields["person.first"].Value);
            Assert.Equal("Yes", fields["agree"].Value);
            Assert.Contains("(Ada Lovelace) Tj", StreamText(d, ((PdfDictionary)R(d, Widget(d, "name").Get("AP"))!).Get("N")));
        }
    }

    [Fact]
    public void Import_rejects_unsafe_or_invalid_input_and_ignores_scripts()
    {
        var e = Blank();
        e.AddFormField(new(PdfFormFieldKind.Text, "short", 0, Box(10, 350, 100, 20)) { MaxLength = 3 });
        e.AddFormField(new(PdfFormFieldKind.ComboBox, "pick", 0, Box(10, 320, 100, 20)) { Options = [new("x"), new("y")] });
        e.AddFormField(new(PdfFormFieldKind.PushButton, "btn", 0, Box(10, 290, 60, 20)) { Caption = "Go" });
        const string dtd = "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///c:/windows/win.ini\">]><xfdf xmlns=\"http://ns.adobe.com/xfdf/\"><fields><field name=\"short\"><value>&e;</value></field></fields></xfdf>";
        Assert.Throws<ArgumentException>(() => e.ImportFormData(Encoding.UTF8.GetBytes(dtd)));
        Assert.Throws<ArgumentException>(() => e.ImportFormData("not xml"u8.ToArray()));
        const string xml = "<xfdf xmlns=\"http://ns.adobe.com/xfdf/\"><fields><field name=\"short\"><value>toolong</value></field><field name=\"pick\"><value>z</value></field>" +
            "<field name=\"btn\"><value>v</value></field><field name=\"nope\"><value>v</value></field></fields><f href=\"x\"/><ids/><javascript>app.alert(1)</javascript></xfdf>";
        var result = e.ImportFormData(Encoding.UTF8.GetBytes(xml));
        Assert.Equal(0, result.Applied);
        Assert.Equal(4, result.Skipped.Count);
        var fdf = "%FDF-1.2\n1 0 obj\n<< /FDF << /Fields [<< /T (short) /V (ok) /AA << /K << /S /JavaScript /JS (app.alert(1)) >> >> >>] /JavaScript << /Before (x) >> >> >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF";
        Assert.Equal(1, e.ImportFormData(Encoding.Latin1.GetBytes(fdf)).Applied);
        var d = Reload(e);
        Assert.Null(Widget(d, "short").Get("AA"));
        Assert.Equal("ok", d.GetFormFields().First(f => f.FullName == "short").Value);
    }

    [Fact]
    public void Authored_fields_survive_import_into_another_document_and_incremental_save()
    {
        var src = Reload(AllKinds());
        var e = PdfEditor.Open(src);
        Assert.Equal(11, e.GetFormFieldSpecs().Count);
        e.AddFormField(new(PdfFormFieldKind.Text, "late", 0, Box(10, 10, 100, 20)) { Value = "x" });
        Assert.True(e.CanSaveIncremental);
        var inc = PdfDocument.Load(e.SaveIncremental());
        Assert.Contains(inc.GetFormFields(), f => f.FullName == "late");
        Assert.Contains(inc.GetFormFields(), f => f.FullName == "name");
    }

    [Fact]
    public void Batch_flatten_prunes_emptied_ancestors_and_keeps_siblings()
    {
        var e = Blank();
        e.AddFormField(new(PdfFormFieldKind.Text, "a.b.c", 0, Box(10, 350, 100, 20)) { Value = "1" });
        e.AddFormField(new(PdfFormFieldKind.Text, "a.b.d", 0, Box(10, 320, 100, 20)) { Value = "2" });
        e.AddFormField(new(PdfFormFieldKind.Text, "a.keep", 0, Box(10, 290, 100, 20)) { Value = "3" });
        e.AddFormField(new(PdfFormFieldKind.Text, "z", 0, Box(10, 260, 100, 20)) { Value = "4" });
        Assert.Equal(new PdfFlattenResult(2, 2), e.FlattenForm(new(PdfFlattenScope.Selected, ["a.b.c", "a.b.d"])));
        var d = Reload(e);
        Assert.Equal(["a.keep", "z"], d.GetFormFields().Select(f => f.FullName).Order());
        var a = ((PdfArray)R(d, AcroForm(d).Get("Fields"))!).Items.Select(x => (PdfDictionary)R(d, x)!).First(x => R(d, x.Get("T")) is PdfString { Text: "a" });
        Assert.Single(((PdfArray)R(d, a.Get("Kids"))!).Items);
        Assert.Equal(2, Widgets(d).Count());
    }

    [Fact]
    public void Large_forms_create_flatten_and_import_in_linear_walks()
    {
        const int count = 5000;
        var e = Blank();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
            e.AddFormField(new(i % 10 == 0 ? PdfFormFieldKind.Checkbox : PdfFormFieldKind.Text, $"g{i / 50}.f{i}", 0, Box(5 + i % 20 * 15, 5 + i / 20 % 380, 12, 8)) { Value = i % 10 == 0 ? "Off" : "v" + i });
        Assert.True(e.FormTreeWalkCount <= 2, $"creating {count} fields walked the tree {e.FormTreeWalkCount} times");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(120), $"create took {sw.Elapsed}");

        var xfdf = new StringBuilder("<?xml version=\"1.0\"?><xfdf xmlns=\"http://ns.adobe.com/xfdf/\"><fields>");
        for (var i = 1; i < count; i += 10) xfdf.Append($"<field name=\"g{i / 50}\"><field name=\"f{i}\"><value>w{i}</value></field></field>");
        xfdf.Append("<field name=\"missing\"><value>x</value></field></fields></xfdf>");
        var walks = e.FormTreeWalkCount; sw.Restart();
        var imported = e.ImportFormData(Encoding.UTF8.GetBytes(xfdf.ToString()));
        Assert.Equal(count / 10, imported.Applied);
        Assert.Single(imported.Skipped);
        Assert.True(e.FormTreeWalkCount - walks <= 1, "import walked the tree per entry");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(60), $"import took {sw.Elapsed}");
        Assert.Contains("w1", e.ExportFormData(PdfFormDataFormat.Xfdf));

        walks = e.FormTreeWalkCount; sw.Restart();
        var result = e.FlattenForm();
        Assert.Equal(count, result.Flattened);
        Assert.True(e.FormTreeWalkCount - walks <= 3, $"flatten walked the tree {e.FormTreeWalkCount - walks} times");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(60), $"flatten took {sw.Elapsed}");
        var d = Reload(e);
        Assert.Empty(d.GetFormFields());
        Assert.Empty(Widgets(d));
    }

    [Fact]
    public void Import_is_capped_and_reports_truncation()
    {
        var e = Blank();
        e.AddFormField(new(PdfFormFieldKind.Text, "one", 0, Box(10, 350, 100, 20)));
        var xfdf = new StringBuilder("<xfdf xmlns=\"http://ns.adobe.com/xfdf/\"><fields>");
        for (var i = 0; i < 50_010; i++) xfdf.Append("<field name=\"nope").Append(i).Append("\"><value>x</value></field>");
        xfdf.Append("</fields></xfdf>");
        var result = e.ImportFormData(Encoding.UTF8.GetBytes(xfdf.ToString()));
        Assert.True(result.Truncated);
        Assert.Equal(0, result.Applied);
        Assert.True(result.Skipped.Count <= 1002);
    }

    [Fact]
    public void Writes_external_validation_sample_when_requested()
    {
        if (Environment.GetEnvironmentVariable("FORM_AUTHORING_OUT") is not { Length: > 0 } dir) return;
        var e = AllKinds();
        e.AddFormField(new(PdfFormFieldKind.Text, "person.first", 0, Box(10, 20, 100, 20)) { Value = "Ada" });
        File.WriteAllBytes(Path.Combine(dir, "authored.pdf"), e.Save());
        var reopened = PdfEditor.Open(PdfDocument.Load(e.Save()));
        reopened.AddFormField(new(PdfFormFieldKind.Text, "more", 0, Box(10, 60, 100, 20)));
        File.WriteAllBytes(Path.Combine(dir, "authored-incremental.pdf"), reopened.SaveIncremental());
        e.FlattenForm();
        File.WriteAllBytes(Path.Combine(dir, "flattened.pdf"), e.Save());
    }

    [Theory]
    [InlineData("name\u0000x")]
    [InlineData("")]
    public void Invalid_names_are_rejected(string name) =>
        Assert.Throws<ArgumentException>(() => Blank().AddFormField(new(PdfFormFieldKind.Text, name, 0, Box(0, 0, 10, 10))));
}
