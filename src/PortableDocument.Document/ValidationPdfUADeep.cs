using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>
/// Deeper PDF/UA checks (ISO 14289-1 clause 7 / Matterhorn Protocol; ISO 14289-2 where the requirement carries over): content tagging and MCID mapping,
/// structure-type nesting, notes, form-field and annotation descriptions, font mapping and document-level flags.
/// Rules were compared by hand with the published veraPDF PDFUA-1 profile (e.g. 7.1-1/2/3, 7.2-11..16/36..40, 7.4.4-1, 7.9-1/2, 7.18.1-2/3); rules without a profile counterpart say so in their messages. veraPDF itself was not run.
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
        if (c.Part == 1)
            foreach (var o in c.AllObjects())
                if (o is PdfStream rs && rs.Dict.Get("Ref") is not null && f.Resolve(rs.Dict.Get("Subtype")).AsName() == "Form")
                { c.Add("PDFUA-XOBJECT-REF", ValidationSeverity.Error, "reference XObjects (Form XObject with /Ref) are not permitted", "ISO 14289-1 7.20"); break; }
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

    static readonly Dictionary<string, string> PredefinedCMapOrdering = BuildPredefinedCMaps();

    static Dictionary<string, string> BuildPredefinedCMaps()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string ordering, params string[] bases) { foreach (var b in bases) { d[b + "-H"] = ordering; d[b + "-V"] = ordering; } }
        Add("GB1", "GB-EUC", "GBpc-EUC", "GBK-EUC", "GBKp-EUC", "GBK2K", "UniGB-UCS2", "UniGB-UTF16");
        Add("CNS1", "B5pc", "HKscs-B5", "ETen-B5", "ETenms-B5", "CNS-EUC", "UniCNS-UCS2", "UniCNS-UTF16");
        Add("Japan1", "90ms-RKSJ", "90msp-RKSJ", "Add-RKSJ", "EUC", "Ext-RKSJ", "UniJIS-UCS2", "UniJIS-UCS2-HW", "UniJIS-UTF16");
        d["83pv-RKSJ-H"] = d["90pv-RKSJ-H"] = d["H"] = d["V"] = "Japan1";
        Add("Korea1", "KSC-EUC", "KSCms-UHC", "KSCms-UHC-HW", "UniKS-UCS2", "UniKS-UTF16");
        d["KSCpc-EUC-H"] = "Korea1";
        return d;
    }

    static (string? Registry, string? Ordering, long Supplement) SystemInfoOf(PdfFile f, PdfDictionary d) =>
        f.Resolve(d.Get("CIDSystemInfo")) is PdfDictionary si
            ? ((f.Resolve(si.Get("Registry")) as PdfString)?.Text, (f.Resolve(si.Get("Ordering")) as PdfString)?.Text, (long)(f.Resolve(si.Get("Supplement")).AsNumber() ?? 0))
            : (null, null, 0);

    static void SymbolicTrueTypeCmap(ValidationContext c, ValidationContext.FontUse use, PdfDictionary descriptor)
    {
        if (c.F.Resolve(descriptor.Get("FontFile2")) is not PdfStream program) return;
        var name = FontChecks.Name(c.F, use.Font);
        (int Count, bool Symbol, bool Roman)? records;
        try { records = FontChecks.TrueTypeCmapRecords(c.F.DecodeStream(program)); }
        catch (Exception e) when (e is PdfParseException or InvalidDataException or NotSupportedException)
        {
            c.Add("PDFUA-FONT-CMAP-INCOMPLETE", ValidationSeverity.Error, $"cannot inspect embedded TrueType cmap records of {name}: {e.Message}", Clause(c, "7.21.6", "8.4.5.7"), use.Page);
            return;
        }
        if (records is not { } found)
            c.Add("PDFUA-FONT-CMAP-INCOMPLETE", ValidationSeverity.Error, $"embedded TrueType cmap headers of {name} are truncated, malformed or unsupported; symbolic encoding validation is incomplete", Clause(c, "7.21.6", "8.4.5.7"), use.Page);
        else if (c.Part == 1 ? found.Count != 1 && !found.Symbol : !found.Symbol && !found.Roman)
            c.Add("PDFUA-FONT-SYMBOLIC-CMAP", ValidationSeverity.Error,
                c.Part == 1 ? $"symbolic TrueType font {name} needs exactly one cmap encoding or Microsoft Symbol (3,0)"
                    : $"symbolic TrueType font {name} needs Microsoft Symbol (3,0) or Macintosh Roman (1,0) cmap encoding",
                Clause(c, "7.21.6", "8.4.5.7"), use.Page);
    }

    static void TrueTypeDifferences(ValidationContext c, ValidationContext.FontUse use, PdfDictionary encoding)
    {
        var result = FontChecks.InspectDifferences(c.F, encoding);
        var name = FontChecks.Name(c.F, use.Font);
        if (result.InvalidName is { } glyph)
            c.Add("PDFUA-FONT-TRUETYPE-DIFFERENCES", ValidationSeverity.Error, $"non-symbolic TrueType font {name} has a /Differences name /{glyph} not listed in the Adobe Glyph List", Clause(c, "7.21.6", "8.4.5.7"), use.Page);
        else if (result.IncompleteReason is { } reason)
            c.Add("PDFUA-FONT-ENCODING-INCOMPLETE", ValidationSeverity.Error, $"cannot completely inspect /Differences of {name}: {reason}", Clause(c, "7.21.6", "8.4.5.7"), use.Page);
    }

    static int? LiteralCmapWMode(byte[] data, bool hasParent)
    {
        if (data.Length > 16 * 1024 * 1024) return null;
        var lexer = new PdfLexer(data);
        var mode = 0;
        var defined = false;
        var procedureDepth = 0;
        var steps = 0;
        while (true)
        {
            if (++steps > 1_000_000) return null;
            lexer.SkipWhite();
            if (lexer.Pos >= lexer.Length) return procedureDepth == 0 && (defined || !hasParent) ? mode : null;
            var start = lexer.Pos;
            var token = lexer.ReadObject(false);
            if (lexer.Pos <= start || lexer.Pos > lexer.Length) return null;
            if (token is PdfKeyword { Value: "{" })
            {
                if (++procedureDepth > 64) return null;
            }
            else if (token is PdfKeyword { Value: "}" })
            {
                if (--procedureDepth < 0) return null;
            }
            else if (token is PdfKeyword { Value: "usecmap" }) hasParent = true;
            // CMap programs use PostScript names, not PDF's #xx name escapes.
            else if (token is PdfName { Value: "WMode" } && data.AsSpan(start, lexer.Pos - start).SequenceEqual("/WMode"u8))
            {
                if (procedureDepth != 0) return null;
                lexer.SkipWhite();
                var valueStart = lexer.Pos;
                var value = lexer.ReadObject(false);
                var valueEnd = lexer.Pos;
                if (value is not PdfInteger { Value: 0 or 1 } integer || valueEnd > data.Length
                    || valueEnd <= valueStart) return null;
                var literal = System.Text.Encoding.ASCII.GetString(data, valueStart, valueEnd - valueStart);
                if (literal is not ("0" or "1" or "+0" or "+1")) return null;
                if (lexer.ReadObject(false) is not PdfKeyword { Value: "def" }) return null;
                mode = (int)integer.Value;
                defined = true;
            }
        }
    }

    static void CmapWritingMode(ValidationContext c, ValidationContext.FontUse use, PdfStream cmap)
    {
        var f = c.F;
        var name = FontChecks.Name(f, use.Font);
        var dictionary = f.Resolve(cmap.Dict.Get("WMode"));
        var dictionaryMode = dictionary is null or PdfNull ? 0
            : dictionary is PdfInteger { Value: 0 or 1 } integer ? (int?)integer.Value : null;
        int? programMode;
        try { programMode = LiteralCmapWMode(f.DecodeStream(cmap), f.Resolve(cmap.Dict.Get("UseCMap")) is not (null or PdfNull)); }
        catch (Exception e) when (e is PdfParseException or InvalidDataException or NotSupportedException)
        {
            c.Add("PDFUA-FONT-CMAP-WMODE-INCOMPLETE", ValidationSeverity.Error, $"cannot inspect WMode of embedded CMap of {name}: {e.Message}", Clause(c, "7.21.3.3", "8.4.5.4"), use.Page);
            return;
        }
        if (dictionaryMode is null || programMode is null)
            c.Add("PDFUA-FONT-CMAP-WMODE-INCOMPLETE", ValidationSeverity.Error, $"WMode validation of embedded CMap of {name} is incomplete: invalid dictionary value, non-literal/procedural or inherited definition, malformed program or scan limit", Clause(c, "7.21.3.3", "8.4.5.4"), use.Page);
        else if (dictionaryMode != programMode)
            c.Add("PDFUA-FONT-CMAP-WMODE", ValidationSeverity.Error, $"WMode of embedded CMap of {name} ({programMode}) differs from its stream dictionary ({dictionaryMode})", Clause(c, "7.21.3.3", "8.4.5.4"), use.Page);
    }

    /// <summary>ISO 14289-1 7.21.3.1/7.21.3.3 (CMaps and CIDSystemInfo) and 7.21.6 (TrueType encodings); see veraPDF PDFUA-1 7.21.3.1-1, 7.21.3.3-1/2, 7.21.6-2/3.</summary>
    static void CompositeAndTrueTypeEncoding(ValidationContext c, ValidationContext.FontUse use, PdfDictionary font, string? subtype)
    {
        var f = c.F;
        var name = FontChecks.Name(f, font);
        if (subtype == "TrueType" && f.Resolve(font.Get("FontDescriptor")) is PdfDictionary desc && f.Resolve(desc.Get("Flags")).AsNumber() is { } flags)
        {
            var symbolic = ((int)flags & 4) != 0;
            if (symbolic) SymbolicTrueTypeCmap(c, use, desc);
            var enc = f.Resolve(font.Get("Encoding"));
            if (symbolic && enc is not null and not PdfNull)
                c.Add("PDFUA-FONT-TRUETYPE-ENCODING", ValidationSeverity.Error, $"symbolic TrueType font {name} must not have an /Encoding entry", Clause(c, "7.21.6", "8.4.5.7"), use.Page);
            else if (!symbolic && !(enc.AsName() is "MacRomanEncoding" or "WinAnsiEncoding"
                || enc is PdfDictionary ed && f.Resolve(ed.Get("BaseEncoding")).AsName() is "MacRomanEncoding" or "WinAnsiEncoding"))
                c.Add("PDFUA-FONT-TRUETYPE-ENCODING", ValidationSeverity.Error, $"non-symbolic TrueType font {name} needs MacRomanEncoding or WinAnsiEncoding", Clause(c, "7.21.6", "8.4.5.7"), use.Page);
            if (!symbolic && enc is PdfDictionary differencesEncoding) TrueTypeDifferences(c, use, differencesEncoding);
        }
        if (subtype != "Type0" || f.Resolve(font.Get("DescendantFonts")) is not PdfArray { Count: > 0 } df || f.Resolve(df[0]) is not PdfDictionary cid) return;
        var cidInfo = SystemInfoOf(f, cid);
        switch (f.Resolve(font.Get("Encoding")))
        {
            case PdfName { Value: "Identity-H" or "Identity-V" }:
                break;
            case PdfName cmapName:
                if (!PredefinedCMapOrdering.TryGetValue(cmapName.Value, out var ordering))
                    c.Add("PDFUA-FONT-CMAP-EMBEDDED", ValidationSeverity.Error, $"CMap /{cmapName.Value} of {name} is not predefined (ISO 32000-1 Table 118) and is not embedded", Clause(c, "7.21.3.3", "8.8"), use.Page);
                else if (cidInfo.Registry != "Adobe" || cidInfo.Ordering != ordering)
                    c.Add("PDFUA-FONT-CMAP-SYSTEMINFO", ValidationSeverity.Error, $"CIDSystemInfo of {name} ({cidInfo.Registry}-{cidInfo.Ordering}) does not match predefined CMap /{cmapName.Value} (Adobe-{ordering})", Clause(c, "7.21.3.1", "8.8"), use.Page);
                break;
            case PdfStream cmap:
                if (f.Resolve(cmap.Dict.Get("UseCMap")) is PdfName parentCMap
                    && parentCMap.Value is not ("Identity-H" or "Identity-V") && !PredefinedCMapOrdering.ContainsKey(parentCMap.Value))
                    c.Add("PDFUA-FONT-CMAP-REFERENCE", ValidationSeverity.Error, $"CMap of {name} references non-predefined /{parentCMap.Value} through /UseCMap", Clause(c, "7.21.3.3", "8.4.5.4"), use.Page);
                var cmapInfo = SystemInfoOf(f, cmap.Dict);
                if (cmapInfo.Registry != cidInfo.Registry || cmapInfo.Ordering != cidInfo.Ordering || cidInfo.Supplement > cmapInfo.Supplement)
                    c.Add("PDFUA-FONT-CMAP-SYSTEMINFO", ValidationSeverity.Error, $"CIDSystemInfo of {name} and its embedded CMap differ (Registry/Ordering must match; CIDFont Supplement must not exceed the CMap's)", Clause(c, "7.21.3.1", "8.8"), use.Page);
                CmapWritingMode(c, use, cmap);
                break;
        }
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
                && f.Resolve(cid.Get("Subtype")).AsName() == "CIDFontType2" && FontChecks.IsEmbedded(f, font)
                && f.Resolve(cid.Get("CIDToGIDMap")) is not (PdfStream or PdfName { Value: "Identity" }))
                c.Add("PDFUA-FONT-CIDTOGID", ValidationSeverity.Error, $"embedded CIDFontType2 {FontChecks.Name(f, cid)} requires /CIDToGIDMap to be a stream or /Identity", Clause(c, "7.21.3.2", "8.4.5.3.2"), u.Page);
            CompositeAndTrueTypeEncoding(c, u, font, subtype);
            if (f.Resolve(font.Get("ToUnicode")) is PdfStream) continue;
            var name = FontChecks.Name(f, font);
            if (subtype == "Type0")
            {
                // ISO 14289-1 7.21.7: Adobe CJK character collections map to Unicode through their UCS2 CMaps, so no /ToUnicode is needed.
                var ordering = f.Resolve(font.Get("DescendantFonts")) is PdfArray { Count: > 0 } dfs && f.Resolve(dfs[0]) is PdfDictionary dcid
                    && f.Resolve(dcid.Get("CIDSystemInfo")) is PdfDictionary csi && f.Resolve(csi.Get("Registry")) is PdfString { Text: "Adobe" }
                    && f.Resolve(csi.Get("Ordering")) is PdfString o ? o.Text : null;
                if (ordering is not ("GB1" or "CNS1" or "Japan1" or "Korea1"))
                    c.Add("PDFUA-FONT-TOUNICODE", ValidationSeverity.Error, $"composite font {name} has no /ToUnicode CMap (heuristic: predefined Unicode CMaps would be exempt)", Clause(c, "7.21.7", "8.8"), u.Page);
            }
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
        var forms = new Dictionary<PdfStream, (int Count, bool HasMcid)>(ReferenceEqualityComparer.Instance);
        var byPage = tree.Elements().SelectMany(e => e.MarkedContent).Where(m => m.Stream is null && m.PageIndex is not null)
            .GroupBy(m => m.PageIndex!.Value).ToDictionary(g => g.Key, g => g.ToList());
        for (var i = 0; i < c.Doc.Pages.Count; i++)
        {
            var page = c.Doc.Pages[i];
            var content = page.GetContents();
            if (content.Length == 0) continue;
            var props = f.Resolve(page.Resources?.Get("Properties")) as PdfDictionary;
            var used = new HashSet<int>();
            var budget = new ScanBudget { Forms = forms };
            Scan(c, content, page.Resources, props, i, tree, used, false, false, null, 0, budget);
            if (byPage.TryGetValue(i, out var refs))
                foreach (var mc in refs.Where(m => !used.Contains(m.Mcid)).Take(100))
                c.Add("PDFUA-MCID-MISSING", ValidationSeverity.Warning, $"structure tree references MCID {mc.Mcid} but the page content has no such marked-content sequence", "ISO 32000-2 14.7.4.2", i);
        }
        if (c.Part == 1 && forms.Values.Any(u => u.Count > 1 && u.HasMcid))
            c.Add("PDFUA-FORM-REUSED-TAGGED", ValidationSeverity.Error, "a Form XObject containing marked content with MCIDs is invoked more than once, so its content has no unique semantic parent (veraPDF PDFUA-1 7.20-2; heuristic, per-invocation structure is not evaluated)", "ISO 14289-1 7.20");
    }

    sealed class ScanBudget { public int Steps; public bool UntaggedReported, ArtifactReported, NestingReported; public Dictionary<PdfStream, (int Count, bool HasMcid)> Forms = new(ReferenceEqualityComparer.Instance); }

    const int MaxScanSteps = 5_000_000;
    const int MaxFormDepth = 8;

    static void Scan(ValidationContext c, byte[] content, PdfDictionary? resources, PdfDictionary? props, int page, StructureTree tree, HashSet<int> used,
        bool inheritedArtifact, bool inheritedMcid, PdfStream? formStream, int depth, ScanBudget budget)
    {
        var f = c.F;
        var lex = new PdfLexer(content);
        var operands = new List<PdfObject>();
        var stack = new List<(bool Artifact, int? Mcid, bool Lang)>();
        while (budget.Steps++ < MaxScanSteps)
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
                    {
                        var bmcArtifact = operands.Count > 0 && operands[0].AsName() == "Artifact";
                        if (bmcArtifact && !budget.NestingReported && (inheritedMcid || stack.Any(s => s.Mcid is not null)))
                        {
                            budget.NestingReported = true;
                            c.Add("PDFUA-ARTIFACT-IN-TAGGED", ValidationSeverity.Error, "Artifact content is nested inside tagged content (veraPDF PDFUA-1 7.1-1)", Clause(c, "7.1", "8.2"), page);
                        }
                        stack.Add((bmcArtifact, null, false));
                        break;
                    }
                case "BDC":
                    {
                        var tag = operands.Count > 0 ? operands[0].AsName() : null;
                        PdfDictionary? d = operands.Count > 1 ? operands[1] as PdfDictionary ?? (operands[1].AsName() is { } n ? f.Resolve(props?.Get(n)) as PdfDictionary : null) : null;
                        int? mcid = d is not null && f.Resolve(d.Get("MCID")).AsNumber() is { } m ? (int)m : null;
                        var artifact = tag == "Artifact";
                        if (mcid is { } id)
                        {
                            if (formStream is null) used.Add(id);
                            if (artifact && !budget.ArtifactReported) { budget.ArtifactReported = true; c.Add("PDFUA-ARTIFACT-TAGGED", ValidationSeverity.Error, "Artifact marked content carries an MCID (content is both artifact and tagged)", Clause(c, "7.1", "8.2"), page); }
                            else if (!artifact && (formStream is null ? tree.FindMarkedContent(page, id) : tree.FindMarkedContent(formStream, id) ?? tree.FindMarkedContent(page, id)) is null)
                                c.Add("PDFUA-MCID-UNMAPPED", ValidationSeverity.Error, $"marked content MCID {id} is not referenced by the structure tree", Clause(c, "7.1", "8.2"), page);
                        }
                        if (formStream is not null && mcid is not null && budget.Forms.TryGetValue(formStream, out var fu)) budget.Forms[formStream] = (fu.Count, true);
                        if (!budget.NestingReported && (artifact && (inheritedMcid || stack.Any(s => s.Mcid is not null)) || mcid is not null && !artifact && (inheritedArtifact || stack.Any(s => s.Artifact))))
                        {
                            budget.NestingReported = true;
                            c.Add(artifact ? "PDFUA-ARTIFACT-IN-TAGGED" : "PDFUA-TAGGED-IN-ARTIFACT", ValidationSeverity.Error, artifact ? "Artifact content is nested inside tagged content (veraPDF PDFUA-1 7.1-1)" : "tagged content is nested inside Artifact content (veraPDF PDFUA-1 7.1-2)", Clause(c, "7.1", "8.2"), page);
                        }
                        stack.Add((artifact, mcid, d?.Get("Lang") is not null));
                        break;
                    }
                case "EMC":
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                    break;
                case "ID":
                    SkipInlineImage(lex);
                    goto default;
                case "Do" when operands.Count > 0 && operands[0].AsName() is { } xname
                    && f.Resolve((f.Resolve(resources?.Get("XObject")) as PdfDictionary)?.Get(xname)) is PdfStream xo && f.Resolve(xo.Dict.Get("Subtype")).AsName() == "Form":
                    // A Form XObject is not content itself: its own operators are judged in the tagging context of the invocation.
                    if (depth < MaxFormDepth)
                    {
                        var art = inheritedArtifact || stack.Any(s => s.Artifact);
                        var mc = inheritedMcid || stack.Any(s => s.Mcid is not null);
                        budget.Forms[xo] = (budget.Forms.TryGetValue(xo, out var prior) ? prior.Count + 1 : 1, prior.HasMcid);
                        var formResources = f.Resolve(xo.Dict.Get("Resources")) as PdfDictionary ?? resources;
                        var formProps = f.Resolve(formResources?.Get("Properties")) as PdfDictionary ?? props;
                        Scan(c, f.DecodeStream(xo), formResources, formProps, page, tree, used, art, mc, xo, depth + 1, budget);
                    }
                    break;
                default:
                    if (formStream is null && op is "Tj" or "TJ" or "'" or "\"" && !stack.Any(s => s.Lang) && stack.LastOrDefault(s => s.Mcid is not null).Mcid is { } textMcid)
                        c.TextMcids.Add((page, textMcid));
                    if (PaintOps.Contains(op) && !budget.UntaggedReported && !inheritedArtifact && !inheritedMcid && !stack.Any(s => s.Artifact || s.Mcid is not null))
                    {
                        budget.UntaggedReported = true;
                        c.Add("PDFUA-CONTENT-UNTAGGED", ValidationSeverity.Error, "page content is neither tagged (marked content with an MCID) nor marked as an Artifact", Clause(c, "7.1", "8.2"), page);
                    }
                    break;
            }
            operands.Clear();
        }
    }

    internal static void SkipInlineImage(PdfLexer lex)
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
                        c.Add("PDFUA-TABLE-TBODY", ValidationSeverity.Error, "Table has THead or TFoot but no TBody (veraPDF PDFUA-1 7.2-13/14)", sec, e.PageIndex);
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
