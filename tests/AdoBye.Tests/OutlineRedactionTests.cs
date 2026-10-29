using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;

namespace AdoBye.Tests;

public class OutlineRedactionTests
{
    [Theory]
    [InlineData("caf\u00e9", "Before cafe\u0301, after.", "Before [redacted], after.")]
    [InlineData("cafe\u0301", "Before CAF\u00c9, after.", "Before [redacted], after.")]
    [InlineData("cafe\u0301", "cafe\u0301 / CAF\u00c9", "[redacted] / [redacted]")]
    [InlineData("e\u0301\u0323", "Before e\u0323\u0301 after", "Before [redacted] after")]
    [InlineData("Top Secret", "\tTop \t\r\n\u00a0Secret\u00a0 notes\n", "\t[redacted]\u00a0 notes\n")]
    [InlineData("Top \t\r\n\u00a0Secret", "Top Secret / Top\u00a0Secret", "[redacted] / [redacted]")]
    [InlineData("caf\u00e9 Secret", "cafe\u0301\tSECRET / Caf\u00e9\r\nSecret", "[redacted] / [redacted]")]
    [InlineData("caf\u00e9 Secret", "cafe\u0301 news / Secret notes", "cafe\u0301 news / Secret notes")]
    [InlineData("caf\u00e9", "cafe\u0301s / MyCAF\u00c9 / cafe\u0301_1", "cafe\u0301s / MyCAF\u00c9 / cafe\u0301_1")]
    [InlineData("Top Secret", "Top\tSecrets / MyTop\nSecret", "Top\tSecrets / MyTop\nSecret")]
    [InlineData("caf\u00e9 Secret", "cafe\u0301-Secret / cafe\u0301, Secret", "cafe\u0301-Secret / cafe\u0301, Secret")]
    [InlineData("caf\u00e9", "cafe / cafe\u0301\u0301", "cafe / cafe\u0301\u0301")]
    [InlineData("Top Secret", "TopSecret", "TopSecret")]
    [InlineData("Top Secret", "Top\u200bSecret", "Top\u200bSecret")]
    [InlineData("fine", "\ufb01ne / FINE", "\ufb01ne / [redacted]")]
    [InlineData("caf\u00e9", "  re\u0301sume\u0301\tcafe\u0301\nnai\u0308ve  ", "  re\u0301sume\u0301\t[redacted]\nnai\u0308ve  ")]
    public void Matches_canonical_unicode_and_whitespace_runs_without_changing_surroundings(string text, string title, string expected)
    {
        var bytes = Build(Text(text), [title]);
        Assert.Equal(text, Assert.Single(DocumentRenderer.ExtractText(PdfDocument.Load(bytes).Pages[0])).Text);
        var editor = PdfEditor.Open(bytes);
        Redactor.Apply(editor, 0, [new(0, 0, 600, 200)]);

        Assert.Equal(expected, Assert.Single(PdfDocument.Load(editor.Save()).GetOutlines()).Title);
    }

    [Theory]
    [InlineData("Top Secret", "Top Secret", "[redacted]")]
    [InlineData("Top Secret", "Before TOP SECRET, after top secret.", "Before [redacted], after [redacted].")]
    [InlineData("Top Secret", "Top stories and Secret history", "Top stories and Secret history")]
    [InlineData("Top Secret", "Top Secrets and MyTop Secret", "Top Secrets and MyTop Secret")]
    [InlineData("Secret", "Secrets Secretive MySecret Secret_1", "Secrets Secretive MySecret Secret_1")]
    [InlineData("Secret", "\u00e9Secret Secret\u0301 Secret\u00e9", "\u00e9Secret Secret\u0301 Secret\u00e9")]
    [InlineData("Secret", "(Secret): Secret/Secret", "([redacted]): [redacted]/[redacted]")]
    [InlineData("AB", "AB and ABC", "[redacted] and ABC")]
    [InlineData("X", "X and XX", "[redacted] and XX")]
    [InlineData("42", "42 and 142", "[redacted] and 142")]
    [InlineData("A+B", "A+B and AA+B", "[redacted] and AA+B")]
    [InlineData("caf\u00e9", "CAF\u00c9 notes", "[redacted] notes")]
    [InlineData("  Top Secret  ", "Top Secret overview", "[redacted] overview")]
    [InlineData("   ", "Public title", "Public title")]
    public void Replaces_only_literal_phrases_at_word_boundaries(string text, string title, string expected)
    {
        var editor = PdfEditor.Open(Build(Text(text), [title]));
        Redactor.Apply(editor, 0, [new(0, 0, 600, 200)]);

        var saved = PdfDocument.Load(editor.Save());
        Assert.Equal(expected, Assert.Single(saved.GetOutlines()).Title);
        Assert.All(DocumentRenderer.ExtractText(saved.Pages[0]), s => Assert.True(string.IsNullOrWhiteSpace(s.Text)));
    }

    [Fact]
    public void Partial_span_uses_only_removed_glyph_text()
    {
        var bytes = Build(Text("Public Secret Public"), ["Public", "Secret overview", "Public Secret Public", "Secretive"]);
        var span = Assert.Single(DocumentRenderer.Analyze(PdfDocument.Load(bytes).Pages[0]).Spans);
        var glyphs = span.Pieces!.SelectMany(p => p.Glyphs ?? []).Skip(7).Take(6).ToArray();
        var editor = PdfEditor.Open(bytes);
        Redactor.Apply(editor, 0, [new(glyphs[0].X0, glyphs[0].Y0, glyphs[^1].X1 - glyphs[0].X0, glyphs[0].Y1 - glyphs[0].Y0)]);

        var saved = PdfDocument.Load(editor.Save());
        Assert.Equal(["Public", "[redacted] overview", "Public [redacted] Public", "Secretive"], saved.GetOutlines().Select(o => o.Title));
        Assert.Equal(["Public", "Public"], string.Concat(DocumentRenderer.ExtractText(saved.Pages[0]).Select(s => s.Text))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Separate_removed_runs_do_not_include_surviving_text()
    {
        var bytes = Build(Text("Alpha KEEP Beta"), ["Alpha KEEP Beta", "KEEP", "Alphabet Betamax"]);
        var span = Assert.Single(DocumentRenderer.Analyze(PdfDocument.Load(bytes).Pages[0]).Spans);
        var glyphs = span.Pieces!.SelectMany(p => p.Glyphs ?? []).ToArray();
        Redactor.BoxRect Box(int start, int end) => new(glyphs[start].X0, glyphs[start].Y0,
            glyphs[end].X1 - glyphs[start].X0, glyphs[start].Y1 - glyphs[start].Y0);
        var editor = PdfEditor.Open(bytes);
        Redactor.Apply(editor, 0, [Box(0, 4), Box(11, 14)]);

        Assert.Equal(["[redacted] KEEP [redacted]", "KEEP", "Alphabet Betamax"],
            PdfDocument.Load(editor.Save()).GetOutlines().Select(o => o.Title));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rewrites_nested_and_indirect_titles_without_changing_tree_or_destinations(bool form)
    {
        var editor = PdfEditor.Open(Build(Text("Secret"), ["Secret overview", "Introduction"], child: "Nested SECRET notes", form: form));
        Redactor.Apply(editor, 0, [new(0, 0, 600, 200)]);

        var saved = editor.Save();
        var raw = Encoding.ASCII.GetString(saved);
        Assert.DoesNotContain(Convert.ToHexString(Encoding.ASCII.GetBytes("Secret")), raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes("Secret")), raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes("SECRET")), raw, StringComparison.OrdinalIgnoreCase);
        var outlines = PdfDocument.Load(saved).GetOutlines();
        Assert.Equal(["[redacted] overview", "Introduction"], outlines.Select(o => o.Title));
        Assert.All(outlines, o => Assert.Equal(0, o.PageIndex));
        var child = Assert.Single(outlines[0].Children);
        Assert.Equal("Nested [redacted] notes", child.Title);
        Assert.Equal(0, child.PageIndex);
    }

    [Fact]
    public void Uses_longest_phrase_and_does_not_reprocess_replacement_text()
    {
        var content = Text("Secret") + " " + Text("Top Secret", 60) + " " + Text("redacted", 30);
        var editor = PdfEditor.Open(Build(content, ["Top Secret / Secret / redacted"]));
        Redactor.Apply(editor, 0, [new(0, 0, 600, 200)]);

        Assert.Equal("[redacted] / [redacted] / [redacted]", Assert.Single(PdfDocument.Load(editor.Save()).GetOutlines()).Title);
    }

    [Fact]
    public void Normalized_matches_choose_longest_phrase_and_preserve_original_ranges()
    {
        var editor = PdfEditor.Open(Build(Text("caf\u00e9") + " " + Text("cafe\u0301 Secret", 60),
            ["\tcafe\u0301 \r\nSecret / CAF\u00c9\u00a0  Secret / cafe\u0301  \n"]));
        Redactor.Apply(editor, 0, [new(0, 0, 600, 200)]);

        Assert.Equal("\t[redacted] / [redacted] / [redacted]  \n",
            Assert.Single(PdfDocument.Load(editor.Save()).GetOutlines()).Title);
    }

    [Fact]
    public void Keeps_phrase_across_TJ_strings_and_word_spacing()
    {
        var editor = PdfEditor.Open(Build("BT /F1 12 Tf 10 100 Td [(Top) -250 (Sec) 20 (ret)] TJ ET",
            ["Top Secret notes", "Top notes", "Secret notes"]));
        Redactor.Apply(editor, 0, [new(0, 0, 600, 200)]);

        Assert.Equal(["[redacted] notes", "Top notes", "Secret notes"], PdfDocument.Load(editor.Save()).GetOutlines().Select(o => o.Title));
    }

    [Fact]
    public void Existing_predicate_API_still_replaces_whole_titles()
    {
        var editor = PdfEditor.Open(Build("", ["Secret overview", "Introduction"], child: "Secret child"));
        Assert.Equal(2, editor.RedactOutlineTitles(t => t.StartsWith("Secret")));
        Assert.Equal(2, editor.RedactOutlineTitles(t => t == "[redacted]"));
        Assert.Equal(0, editor.TransformOutlineTitles(t => t));

        var outlines = PdfDocument.Load(editor.Save()).GetOutlines();
        Assert.Equal(["[redacted]", "Introduction"], outlines.Select(o => o.Title));
        Assert.Equal("[redacted]", Assert.Single(outlines[0].Children).Title);
    }

    static string Text(string text, int y = 100) =>
        $"BT /F1 12 Tf 10 {y} Td <{Convert.ToHexString(Encoding.Latin1.GetBytes(text.Replace('\u0301', '\u0001').Replace('\u0323', '\u0002')))}> Tj ET";

    static byte[] Build(string content, string[] titles, string? child = null, bool form = false)
    {
        var sb = new StringBuilder("%PDF-1.7\n");
        void Obj(int n, string body) => sb.Append($"{n} 0 obj\n{body}\nendobj\n");
        static string Title(string title) => $"<FEFF{Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(title))}>";
        const string font = "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding /ToUnicode 8 0 R >> >>";
        Obj(1, "<< /Type /Catalog /Pages 2 0 R /Outlines 5 0 R >>");
        Obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var xobjects = form ? "/XObject << /Fm 6 0 R >>" : "";
        Obj(3, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 200] /Contents 4 0 R /Resources << {font} {xobjects} >> >>");
        var pageContent = form ? "/Fm Do" : content;
        Obj(4, $"<< /Length {pageContent.Length} >>\nstream\n{pageContent}\nendstream");
        Obj(5, $"<< /Type /Outlines /First 10 0 R /Last {9 + titles.Length} 0 R /Count {titles.Length + (child is null ? 0 : 1)} >>");
        Obj(6, $"<< /Type /XObject /Subtype /Form /BBox [0 0 600 200] /Resources << {font} >> /Length {content.Length} >>\nstream\n{content}\nendstream");
        Obj(7, child is null ? "null" : $"<< /Title {Title(child)} /Parent 10 0 R /Dest [3 0 R /Fit] >>");
        const string cmap = "begincmap\n1 begincodespacerange\n<00> <FF>\nendcodespacerange\n6 beginbfchar\n<01> <0301>\n<02> <0323>\n<09> <0009>\n<0A> <000A>\n<0D> <000D>\n<A0> <00A0>\nendbfchar\nendcmap";
        Obj(8, $"<< /Length {cmap.Length} >>\nstream\n{cmap}\nendstream");
        for (var i = 0; i < titles.Length; i++)
        {
            Obj(100 + i, Title(titles[i]));
            var next = i + 1 < titles.Length ? $"/Next {11 + i} 0 R" : "";
            var prev = i > 0 ? $"/Prev {9 + i} 0 R" : "";
            var kids = i == 0 && child is not null ? "/First 7 0 R /Last 7 0 R /Count 1" : "";
            Obj(10 + i, $"<< /Title {100 + i} 0 R /Parent 5 0 R {next} {prev} {kids} /Dest [3 0 R /Fit] >>");
        }
        sb.Append($"trailer\n<< /Root 1 0 R /Size {100 + titles.Length} >>\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
