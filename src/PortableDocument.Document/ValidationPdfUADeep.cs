using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>
/// Deeper PDF/UA checks (ISO 14289-1 clause 7 / Matterhorn Protocol; ISO 14289-2 where the requirement carries over): content tagging and MCID mapping,
/// structure-type nesting, notes, form-field and annotation descriptions, font mapping and document-level flags.
/// Rule semantics follow the published veraPDF PDF/UA-1 profile; rules without a profile counterpart say so in their messages.
/// </summary>
internal static class PdfUADeepChecks
{
    static string Clause(ValidationContext c, string a, string b) => c.Part == 1 ? $"ISO 14289-1 {a}" : $"ISO 14289-2 {b}";

    static readonly HashSet<string> PaintOps = new(StringComparer.Ordinal)
        { "Tj", "TJ", "'", "\"", "f", "F", "f*", "B", "B*", "b", "b*", "S", "s", "sh", "Do", "BI" };

    public static void DocumentFlags(ValidationContext c)
    {
        var f = c.F;
        if (f.Resolve(f.Catalog.Get("MarkInfo")) is PdfDictionary mi && f.Resolve(mi.Get("Suspects")) is PdfBoolean { Value: true })
            c.Add("PDFUA-SUSPECTS", ValidationSeverity.Error, "MarkInfo /Suspects is true (tagged content is declared unreliable)", Clause(c, "7.1", "8.2"));
        if (c.Part == 1 && !System.Text.RegularExpressions.Regex.IsMatch(f.Version ?? "", @"^1\.[0-7]$"))
            c.Add("PDFUA-VERSION", ValidationSeverity.Error, $"file header version '{f.Version}' is not PDF 1.0 to 1.7", "ISO 14289-1 6.1");
        if (f.Resolve(f.Catalog.Get("NeedsRendering")) is PdfBoolean { Value: true } && f.Resolve(f.Catalog.Get("AcroForm")) is PdfDictionary af && af.Get("XFA") is not null)
            c.Add("PDFUA-XFA-DYNAMIC", ValidationSeverity.Error, "dynamic XFA rendering is required (/NeedsRendering true with an XFA form)", Clause(c, "7.15", "8.9"));
        if (f.Resolve(f.Catalog.Get("OCProperties")) is PdfDictionary oc)
        {
            var configs = new List<PdfDictionary>();
            if (f.Resolve(oc.Get("D")) is PdfDictionary d) configs.Add(d);
            if (f.Resolve(oc.Get("Configs")) is PdfArray arr) configs.AddRange(arr.Items.Take(1000).Select(f.Resolve).OfType<PdfDictionary>());
            foreach (var cfg in configs)
            {
                if (f.Resolve(cfg.Get("Name")) is not PdfString { Bytes.Length: > 0 })
                    c.Add("PDFUA-OC-NAME", ValidationSeverity.Error, "optional content configuration has no /Name", Clause(c, "7.10", "8.9"));
                if (cfg.Get("AS") is not null)
                    c.Add("PDFUA-OC-AS", ValidationSeverity.Error, "optional content configuration must not contain /AS", Clause(c, "7.10", "8.9"));
            }
        }
        var reported = false;
        foreach (var o in c.AllObjects())
        {
            if (reported) break;
            if (o is PdfDictionary d && d.Get("EF") is not null && f.Resolve(d.Get("Type")).AsName() is "Filespec" or null
                && !(f.Resolve(d.Get("F")) is PdfString { Bytes.Length: > 0 } && f.Resolve(d.Get("UF")) is PdfString { Bytes.Length: > 0 }))
            { reported = true; c.Add("PDFUA-FILESPEC-UF", ValidationSeverity.Error, "embedded-file specification must have both /F and /UF", Clause(c, "7.11", "8.9")); }
        }
        if (c.Doc.Pages.Count > 20 && f.Resolve(f.Catalog.Get("Outlines")) is not PdfDictionary)
            c.Add("PDFUA-BOOKMARKS", ValidationSeverity.Info, $"document has {c.Doc.Pages.Count} pages and no /Outlines (navigation aid recommended; advisory only, no veraPDF counterpart)");
    }

    public static void Fonts(ValidationContext c)
    {
        var f = c.F;
        c.EnsureWalked();
        foreach (var u in c.Fonts)
        {
            var font = u.Font;
            var subtype = f.Resolve(font.Get("Subtype")).AsName();
            if (subtype == "Type0" && f.Resolve(font.Get("DescendantFonts")) is PdfArray { Count: > 0 } df && f.Resolve(df[0]) is PdfDictionary cid
                && f.Resolve(cid.Get("Subtype")).AsName() == "CIDFontType2" && cid.Get("CIDToGIDMap") is null && FontChecks.IsEmbedded(f, font))
                c.Add("PDFUA-FONT-CIDTOGID", ValidationSeverity.Error, $"CIDFontType2 {FontChecks.Name(f, cid)} has no /CIDToGIDMap", Clause(c, "7.21.3.2", "8.8"), u.Page);
            if (f.Resolve(font.Get("ToUnicode")) is PdfStream) continue;
            var name = FontChecks.Name(f, font);
            if (subtype == "Type0")
                c.Add("PDFUA-FONT-TOUNICODE", ValidationSeverity.Error, $"composite font {name} has no /ToUnicode CMap (heuristic: predefined Unicode CMaps would be exempt)", Clause(c, "7.21.7", "8.8"), u.Page);
            else if (subtype is "Type1" or "TrueType" or "MMType1" or "Type3")
            {
                var enc = f.Resolve(font.Get("Encoding"));
                var standardEnc = enc.AsName() is "WinAnsiEncoding" or "MacRomanEncoding" or "StandardEncoding" or "MacExpertEncoding"
                    || (enc is PdfDictionary ed && ed.Get("Differences") is null);
                var symbolic = f.Resolve(font.Get("FontDescriptor")) is PdfDictionary fd && f.Resolve(fd.Get("Flags")).AsNumber() is { } fl && ((int)fl & 4) != 0;
                if (subtype == "Type3" || !standardEnc || (symbolic && FontChecks.IsEmbedded(f, font)))
                    c.Add("PDFUA-FONT-TOUNICODE", ValidationSeverity.Warning, $"font {name} has no /ToUnicode CMap and no standard text encoding (heuristic: glyph-level mapping is not evaluated)", Clause(c, "7.21.7", "8.8"), u.Page);
            }
        }
    }

    /// <summary>Verifies that page content is marked content with an MCID that the structure tree knows, or an Artifact (ISO 14289-1 7.1).</summary>
    public static void ContentTagging(ValidationContext c, StructureTree tree)
    {
        var f = c.F;
        for (var i = 0; i < c.Doc.Pages.Count; i++)
        {
            var page = c.Doc.Pages[i];
            var content = page.GetContents();
            if (content.Length == 0) continue;
            var props = f.Resolve(page.Resources?.Get("Properties")) as PdfDictionary;
            var used = new HashSet<int>();
            Scan(c, content, props, i, tree, used);
            foreach (var mc in tree.Elements().SelectMany(e => e.MarkedContent).Where(m => m.Stream is null && m.PageIndex == i && !used.Contains(m.Mcid)).Take(100))
                c.Add("PDFUA-MCID-MISSING", ValidationSeverity.Warning, $"structure tree references MCID {mc.Mcid} but the page content has no such marked-content sequence", "ISO 32000-2 14.7.4.2", i);
        }
    }

    static void Scan(ValidationContext c, byte[] content, PdfDictionary? props, int page, StructureTree tree, HashSet<int> used)
    {
        var f = c.F;
        var lex = new PdfLexer(content);
        var operands = new List<PdfObject>();
        var stack = new List<(bool Artifact, int? Mcid)>();
        bool reportedUntagged = false, reportedArtifact = false;
        var steps = 0;
        while (steps++ < 5_000_000)
        {
            lex.SkipWhite();
            if (lex.Pos >= lex.Length) break;
            var before = lex.Pos;
            PdfObject? o;
            try { o = lex.ReadObject(false); }
            catch (PdfParseException) { lex.Pos = before + 1; operands.Clear(); continue; }
            if (o is null) break;
            if (lex.Pos <= before) lex.Pos = before + 1;
            if (o is not PdfKeyword kw) { if (operands.Count < 64) operands.Add(o); continue; }
            var op = kw.Value;
            switch (op)
            {
                case "BMC":
                    stack.Add((operands.Count > 0 && operands[0].AsName() == "Artifact", null));
                    break;
                case "BDC":
                    {
                        var tag = operands.Count > 0 ? operands[0].AsName() : null;
                        PdfDictionary? d = operands.Count > 1 ? operands[1] as PdfDictionary ?? (operands[1].AsName() is { } n ? f.Resolve(props?.Get(n)) as PdfDictionary : null) : null;
                        int? mcid = d is not null && f.Resolve(d.Get("MCID")).AsNumber() is { } m ? (int)m : null;
                        var artifact = tag == "Artifact";
                        if (mcid is { } id)
                        {
                            used.Add(id);
                            if (artifact && !reportedArtifact) { reportedArtifact = true; c.Add("PDFUA-ARTIFACT-TAGGED", ValidationSeverity.Error, "Artifact marked content carries an MCID (content is both artifact and tagged)", Clause(c, "7.1", "8.2"), page); }
                            else if (!artifact && tree.FindMarkedContent(page, id) is null)
                                c.Add("PDFUA-MCID-UNMAPPED", ValidationSeverity.Error, $"marked content MCID {id} is not referenced by the structure tree", Clause(c, "7.1", "8.2"), page);
                        }
                        stack.Add((artifact, mcid));
                        break;
                    }
                case "EMC":
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                    break;
                case "ID":
                    SkipInlineImage(lex);
                    goto default;
                default:
                    if (PaintOps.Contains(op) && !reportedUntagged && !stack.Any(s => s.Artifact || s.Mcid is not null))
                    {
                        reportedUntagged = true;
                        c.Add("PDFUA-CONTENT-UNTAGGED", ValidationSeverity.Error, "page content is neither tagged (marked content with an MCID) nor marked as an Artifact", Clause(c, "7.1", "8.2"), page);
                    }
                    break;
            }
            operands.Clear();
        }
    }

    static void SkipInlineImage(PdfLexer lex)
    {
        var d = lex.Data;
        for (var p = lex.Pos; p + 1 < d.Length; p++)
            if (d[p] == 'E' && d[p + 1] == 'I' && p > 0 && PdfLexer.IsWhite(d[p - 1]) && (p + 2 >= d.Length || PdfLexer.IsWhite(d[p + 2]) || PdfLexer.IsDelim(d[p + 2])))
            { lex.Pos = p + 2; return; }
        lex.Pos = d.Length;
    }

    public static void StructureRules(ValidationContext c, StructureTree tree)
    {
        var sec = Clause(c, "7.2", "8.2.5");
        foreach (var e in tree.Elements())
        {
            var kids = e.ChildElements.ToList();
            if (kids.Count(k => k.StandardType == "H") > 1)
                c.Add("PDFUA-HEADING-MULTIPLE", ValidationSeverity.Error, $"/{e.Type} contains more than one H heading", Clause(c, "7.4.4", "8.2.5.22"), e.PageIndex);
            switch (e.StandardType)
            {
                case "Table":
                    if (kids.Count(k => k.StandardType == "THead") > 1) c.Add("PDFUA-TABLE-THEAD-MULTIPLE", ValidationSeverity.Error, "Table has more than one THead", sec, e.PageIndex);
                    if (kids.Count(k => k.StandardType == "TFoot") > 1) c.Add("PDFUA-TABLE-TFOOT-MULTIPLE", ValidationSeverity.Error, "Table has more than one TFoot", sec, e.PageIndex);
                    if (!kids.Any(k => k.StandardType == "TBody") && kids.Any(k => k.StandardType is "THead" or "TFoot"))
                        c.Add("PDFUA-TABLE-TBODY", ValidationSeverity.Error, "Table has THead or TFoot but no TBody", sec, e.PageIndex);
                    CaptionRules(c, e, kids, "PDFUA-TABLE-CAPTION", sec, firstOrLast: true);
                    break;
                case "THead" or "TBody" or "TFoot":
                    if (e.Parent?.StandardType != "Table") c.Add("PDFUA-TABLE-SECTION-PARENT", ValidationSeverity.Error, $"{e.StandardType} is not a child of Table", sec, e.PageIndex);
                    if (kids.Any(k => k.StandardType != "TR")) c.Add("PDFUA-TABLE-SECTION-CHILD", ValidationSeverity.Error, $"{e.StandardType} contains a child other than TR", sec, e.PageIndex);
                    break;
                case "L":
                    CaptionRules(c, e, kids, "PDFUA-LIST-CAPTION", sec, firstOrLast: false);
                    break;
                case "LBody":
                    if (e.Parent?.StandardType != "LI") c.Add("PDFUA-LBODY-PARENT", ValidationSeverity.Error, "LBody is not inside an LI", sec, e.PageIndex);
                    break;
                case "TOC":
                    if (kids.Any(k => k.StandardType is not ("TOC" or "TOCI" or "Caption"))) c.Add("PDFUA-TOC-CHILD", ValidationSeverity.Error, "TOC contains a child other than TOC, TOCI or Caption", sec, e.PageIndex);
                    CaptionRules(c, e, kids, "PDFUA-TOC-CAPTION", sec, firstOrLast: false);
                    break;
                case "TOCI":
                    if (e.Parent?.StandardType != "TOC") c.Add("PDFUA-TOCI-PARENT", ValidationSeverity.Error, "TOCI is not inside a TOC", sec, e.PageIndex);
                    break;
            }
        }
        var noteIds = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var n in tree.Elements().Where(e => e.StandardType == "Note"))
        {
            if (string.IsNullOrEmpty(n.Id)) c.Add("PDFUA-NOTE-ID", ValidationSeverity.Error, "Note has no /ID", Clause(c, "7.9", "8.2.5.30"), n.PageIndex);
            else if (noteIds.TryGetValue(n.Id, out var count) && count == 1)
            { noteIds[n.Id] = 2; c.Add("PDFUA-NOTE-ID-DUPLICATE", ValidationSeverity.Error, $"Note /ID '{n.Id}' is not unique", Clause(c, "7.9", "8.2.5.30"), n.PageIndex); }
            else noteIds.TryAdd(n.Id, 1);
        }
    }

    static void CaptionRules(ValidationContext c, StructElement e, List<StructElement> kids, string id, string clause, bool firstOrLast)
    {
        var caps = kids.Select((k, i) => (k, i)).Where(t => t.k.StandardType == "Caption").Select(t => t.i).ToList();
        if (caps.Count > 1 && firstOrLast) c.Add(id, ValidationSeverity.Error, $"{e.StandardType} has more than one Caption", clause, e.PageIndex);
        else if (caps.Any(i => firstOrLast ? i != 0 && i != kids.Count - 1 : i != 0))
            c.Add(id, ValidationSeverity.Error, firstOrLast ? $"{e.StandardType} Caption must be the first or last child" : $"{e.StandardType} Caption must be the first child", clause, e.PageIndex);
    }
}
