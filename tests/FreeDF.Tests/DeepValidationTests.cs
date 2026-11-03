using System.Text;
using PortableDocument.Document;

namespace FreeDF.Tests;

/// <summary>Deep PDF/A and PDF/UA rules. Each rule has a conformant and a deliberately non-conformant fixture; rule semantics were compared by hand with the published veraPDF profiles (PDFA-1B/2B/3B, PDFUA-1); veraPDF itself was not run.</summary>
public class DeepValidationTests
{
    const string PdfAIdXmp = "<rdf:Description rdf:about='' xmlns:pdfaid='http://www.aiim.org/pdfa/ns/id/' pdfaid:part='{0}' pdfaid:conformance='B'/>";

    static string Xmp(int part = 2, string more = "") => string.Format(PdfAIdXmp, part) + more;

    static string Icc(int major, string cls, string space, int? declaredSize = null)
    {
        var b = new char[132];
        Array.Fill(b, '\0');
        var size = declaredSize ?? b.Length;
        b[0] = (char)(size >> 24 & 255); b[1] = (char)(size >> 16 & 255); b[2] = (char)(size >> 8 & 255); b[3] = (char)(size & 255);
        b[8] = (char)major; b[9] = (char)0x20;
        cls.CopyTo(0, b, 12, 4); space.CopyTo(0, b, 16, 4); "XYZ ".CopyTo(0, b, 20, 4); "acsp".CopyTo(0, b, 36, 4);
        return new string(b);
    }

    static string IccStream(int major = 2, string cls = "prtr", string space = "RGB ", int n = 3, int? size = null) =>
        ObjPdf.Stream(Icc(major, cls, space, size), $"/N {n}");

    /// <summary>Object 6 is the first extra; /Info (when given) is the extra at <paramref name="infoIndex"/> (1-based among extras).</summary>
    static byte[] PdfA(string xmp, string catalogExtra = "", string resources = "", string pageExtra = "", int infoIndex = 0, params string[] extras)
    {
        var objs = new List<string>
        {
            $"<< /Type /Catalog /Pages 2 0 R /Metadata 5 0 R {catalogExtra} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Resources << {resources} >> {pageExtra} >>",
            ObjPdf.Stream(""),
            ObjPdf.Xmp(xmp),
        };
        objs.AddRange(extras);
        var bytes = ObjPdf.Build(objs.ToArray());
        if (infoIndex > 0) bytes = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(bytes).Replace("/Root 1 0 R", $"/Root 1 0 R /Info {5 + infoIndex} 0 R"));
        Dump(bytes);
        return bytes;
    }

    static int _dumpCounter;

    // Set PDF_VALIDATION_DUMP to a directory to export every fixture for cross-checking with qpdf/pikepdf.
    static void Dump(byte[] pdf)
    {
        if (Environment.GetEnvironmentVariable("PDF_VALIDATION_DUMP") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"fixture-{Interlocked.Increment(ref _dumpCounter):D3}.pdf"), pdf);
        }
    }

    static ValidationReport A(byte[] pdf, PdfValidationProfile p = PdfValidationProfile.PdfA2b) => PdfValidator.Validate(PdfDocument.Load(pdf), p);

    static string OutputIntent(string profileObj = "6 0 R") => $"/OutputIntents [<< /Type /OutputIntent /S /GTS_PDFA1 /DestOutputProfile {profileObj} >>]";

    // ---- PDF/A: ICC ----

    [Fact]
    public void IccConformantOutputIntentIsClean()
    {
        var r = A(PdfA(Xmp(), OutputIntent(), extras: IccStream()));
        Assert.DoesNotContain(r.Findings, f => f.RuleId.StartsWith("PDFA-ICC"));
        Assert.True(r.IsCompliant, string.Join("\n", r.Findings));
    }

    [Fact]
    public void IccVersionLimitDiffersBetweenPartOneAndLater()
    {
        var v4 = PdfA(Xmp(1), OutputIntent(), extras: IccStream(major: 4));
        Assert.True(A(v4, PdfValidationProfile.PdfA1b).Has("PDFA-ICC-VERSION"));
        Assert.False(A(PdfA(Xmp(2), OutputIntent(), extras: IccStream(major: 4))).Has("PDFA-ICC-VERSION"));
        Assert.True(A(PdfA(Xmp(2), OutputIntent(), extras: IccStream(major: 5))).Has("PDFA-ICC-VERSION"));
    }

    [Fact]
    public void IccClassColourSpaceAndComponentCountAreChecked()
    {
        Assert.True(A(PdfA(Xmp(), OutputIntent(), extras: IccStream(cls: "abst"))).Has("PDFA-ICC-CLASS"));
        Assert.True(A(PdfA(Xmp(), OutputIntent(), extras: IccStream(space: "Lab "))).Has("PDFA-ICC-COLORSPACE"));
        Assert.True(A(PdfA(Xmp(), OutputIntent(), extras: IccStream(space: "CMYK", n: 3))).Has("PDFA-ICC-N"));
        Assert.False(A(PdfA(Xmp(), OutputIntent(), extras: IccStream(space: "CMYK", n: 4))).Has("PDFA-ICC-N"));
    }

    [Fact]
    public void IccHeaderSizeMismatchIsAWarning()
    {
        var r = A(PdfA(Xmp(), OutputIntent(), extras: IccStream(size: 9999)));
        Assert.Contains(r.Findings, f => f.RuleId == "PDFA-ICC-SIZE" && f.Severity == ValidationSeverity.Warning);
        Assert.True(r.IsCompliant, string.Join("\n", r.Findings));
    }

    [Fact]
    public void IccBasedColourSpaceProfilesAreChecked()
    {
        var ok = A(PdfA(Xmp(), OutputIntent(), "/ColorSpace << /CS0 [/ICCBased 6 0 R] >>", extras: IccStream()));
        Assert.DoesNotContain(ok.Findings, f => f.RuleId.StartsWith("PDFA-ICC"));
        var bad = A(PdfA(Xmp(), OutputIntent(), "/ColorSpace << /CS0 [/ICCBased 6 0 R] >>", extras: IccStream(space: "GRAY", n: 3)));
        Assert.True(bad.Has("PDFA-ICC-N"));
    }

    // ---- PDF/A: Info / XMP ----

    const string DocXmp = "<rdf:Description rdf:about='' xmlns:dc='http://purl.org/dc/elements/1.1/' xmlns:pdf='http://ns.adobe.com/pdf/1.3/' xmlns:xmp='http://ns.adobe.com/xap/1.0/'>"
        + "<dc:title><rdf:Alt><rdf:li xml:lang='x-default'>T</rdf:li></rdf:Alt></dc:title><dc:creator><rdf:Seq><rdf:li>A</rdf:li></rdf:Seq></dc:creator>"
        + "<dc:description><rdf:Alt><rdf:li xml:lang='x-default'>S</rdf:li></rdf:Alt></dc:description>"
        + "<pdf:Producer>P</pdf:Producer><pdf:Keywords>k</pdf:Keywords><xmp:CreatorTool>C</xmp:CreatorTool>"
        + "<xmp:CreateDate>2024-01-02T03:04:05+00:00</xmp:CreateDate><xmp:ModifyDate>2024-01-02T04:04:05Z</xmp:ModifyDate></rdf:Description>";

    const string DocInfo = "<< /Title (T) /Author (A) /Subject (S) /Producer (P) /Keywords (k) /Creator (C) /CreationDate (D:20240102030405Z) /ModDate (D:20240102050405+01'00') >>";

    [Fact]
    public void InfoMatchingXmpIsClean()
    {
        var r = A(PdfA(Xmp(1, DocXmp), infoIndex: 1, extras: DocInfo), PdfValidationProfile.PdfA1b);
        Assert.False(r.Has("PDFA-XMP-INFO-MISMATCH"), string.Join("\n", r.Findings));
        Assert.True(r.IsCompliant, string.Join("\n", r.Findings));
    }

    [Fact]
    public void InfoXmpEquivalenceIsAPartOneRuleOnly()
    {
        // veraPDF PDFA-2B/3B contain no CosInfo rules; differing Info entries must not be reported.
        var differing = DocInfo.Replace("/Title (T)", "/Title (Other)").Replace("/Author (A)", "/Author (A; B)").Replace("D:20240102030405Z", "D:20250102030405Z");
        foreach (var (part, profile) in new[] { (2, PdfValidationProfile.PdfA2b), (3, PdfValidationProfile.PdfA3b) })
            Assert.False(A(PdfA(Xmp(part, DocXmp), infoIndex: 1, extras: differing), profile).Has("PDFA-XMP-INFO-MISMATCH"));
        Assert.True(A(PdfA(Xmp(1, DocXmp), infoIndex: 1, extras: differing), PdfValidationProfile.PdfA1b).Has("PDFA-XMP-INFO-MISMATCH"));
    }

    [Fact]
    public void InfoDatesAreComparedAtSecondPrecisionAcrossTimeZones()
    {
        var xmp = DocXmp.Replace("2024-01-02T03:04:05+00:00", "2024-01-02T04:04:05.789+01:00");
        var r = A(PdfA(Xmp(1, xmp), infoIndex: 1, extras: DocInfo), PdfValidationProfile.PdfA1b);
        Assert.False(r.Has("PDFA-XMP-INFO-MISMATCH"), string.Join("\n", r.Findings));
        Assert.True(r.IsCompliant, string.Join("\n", r.Findings));
    }

    [Theory]
    [InlineData("/Title (T) ", "/Title (Other) ")]
    [InlineData("/Author (A) ", "/Author (A; B) ")]
    [InlineData("/Subject (S) ", "/Subject (Other) ")]
    [InlineData("/Producer (P) ", "/Producer (Other) ")]
    [InlineData("/Keywords (k) ", "/Keywords (z) ")]
    [InlineData("/Creator (C) ", "/Creator (Other) ")]
    [InlineData("/CreationDate (D:20240102030405Z) ", "/CreationDate (D:20250102030405Z) ")]
    [InlineData("/ModDate (D:20240102050405+01'00')", "/ModDate (D:20240102050405Z)")]
    public void InfoMismatchesAreReportedPerField(string original, string replacement)
    {
        var info = DocInfo.Replace(original, replacement);
        Assert.NotEqual(DocInfo, info);
        var r = A(PdfA(Xmp(1, DocXmp), infoIndex: 1, extras: info), PdfValidationProfile.PdfA1b);
        Assert.Contains(r.Findings, f => f.RuleId == "PDFA-XMP-INFO-MISMATCH" && f.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void InfoEntryWithoutXmpCounterpartIsReported()
    {
        var r = A(PdfA(Xmp(1), infoIndex: 1, extras: "<< /Producer (P) >>"), PdfValidationProfile.PdfA1b);
        Assert.True(r.Has("PDFA-XMP-INFO-MISMATCH"));
    }

    // ---- PDF/A: XMP extension schemas and packet ----

    const string CustomProp = "<rdf:Description rdf:about='' xmlns:ex='http://example.com/ns/' ex:level='3'/>";

    const string CustomSchema = "<rdf:Description rdf:about='' xmlns:pdfaExtension='http://www.aiim.org/pdfa/ns/extension/' xmlns:pdfaSchema='http://www.aiim.org/pdfa/ns/schema#' xmlns:pdfaProperty='http://www.aiim.org/pdfa/ns/property#'>"
        + "<pdfaExtension:schemas><rdf:Bag><rdf:li rdf:parseType='Resource'><pdfaSchema:schema>Example</pdfaSchema:schema><pdfaSchema:namespaceURI>http://example.com/ns/</pdfaSchema:namespaceURI><pdfaSchema:prefix>ex</pdfaSchema:prefix>"
        + "<pdfaSchema:property><rdf:Seq><rdf:li rdf:parseType='Resource'><pdfaProperty:name>level</pdfaProperty:name><pdfaProperty:valueType>Integer</pdfaProperty:valueType><pdfaProperty:category>external</pdfaProperty:category><pdfaProperty:description>d</pdfaProperty:description></rdf:li></rdf:Seq></pdfaSchema:property>"
        + "</rdf:li></rdf:Bag></pdfaExtension:schemas></rdf:Description>";

    [Fact]
    public void XmpPropertyOutsidePredefinedSchemasNeedsExtensionDeclaration()
    {
        var undeclared = A(PdfA(Xmp(2, CustomProp)));
        Assert.True(undeclared.Has("PDFA-XMP-EXTENSION-UNDECLARED"));
        var declared = A(PdfA(Xmp(2, CustomProp + CustomSchema)));
        Assert.False(declared.Has("PDFA-XMP-EXTENSION-UNDECLARED"), string.Join("\n", declared.Findings));
        Assert.False(declared.Has("PDFA-XMP-EXTENSION-INVALID"));
    }

    [Fact]
    public void IncompleteExtensionSchemaIsReported()
    {
        var broken = CustomSchema.Replace("<pdfaSchema:prefix>ex</pdfaSchema:prefix>", "");
        var r = A(PdfA(Xmp(2, CustomProp + broken)));
        Assert.True(r.Has("PDFA-XMP-EXTENSION-INVALID"));
    }

    [Fact]
    public void XmpPacketHeaderMustNotUseBytesOrEncoding()
    {
        var ok = A(PdfA(Xmp()));
        Assert.False(ok.Has("PDFA-XMP-PACKET-HEADER"));
        var pdf = ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R /Metadata 5 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>",
            ObjPdf.Stream(""),
            ObjPdf.Stream("<?xpacket begin='' id='W5M0MpCehiHzreSzNTczkc9d' bytes='100'?><x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>" + Xmp() + "</rdf:RDF></x:xmpmeta><?xpacket end='w'?>", "/Type /Metadata /Subtype /XML"));
        Dump(pdf);
        Assert.True(A(pdf).Has("PDFA-XMP-PACKET-HEADER")); // veraPDF 1B 6.7.5-1/2 and 2B/3B 6.6.2.1-2/3 ban bytes/encoding in every part
        Assert.True(A(pdf, PdfValidationProfile.PdfA3b).Has("PDFA-XMP-PACKET-HEADER")); // veraPDF PDFA-1B 6.7.5-1/2 and PDFA-2B/3B 6.6.2.1-2/3 ban bytes/encoding in every part
        Assert.True(A(pdf, PdfValidationProfile.PdfA3b).Has("PDFA-XMP-PACKET-HEADER"));
    }

    // ---- PDF/A: fonts, actions, forms ----

    static string Type1Font(string extra) => $"<< /Type /Font /Subtype /Type1 /BaseFont /ABCDEF+Foo {extra} /FontDescriptor 7 0 R >>";
    const string Descriptor = "<< /Type /FontDescriptor /FontName /ABCDEF+Foo /Flags 32 /FontFile3 8 0 R >>";

    [Fact]
    public void SimpleFontWidthsMustBeConsistent()
    {
        var ok = A(PdfA(Xmp(), "", "/Font << /F1 6 0 R >>", extras: [Type1Font("/FirstChar 65 /LastChar 66 /Widths [500 500]"), Descriptor, ObjPdf.Stream("x", "/Subtype /Type1C")]));
        Assert.False(ok.Has("PDFA-FONT-WIDTHS"));
        var bad = A(PdfA(Xmp(), "", "/Font << /F1 6 0 R >>", extras: [Type1Font("/FirstChar 65 /LastChar 70 /Widths [500 500]"), Descriptor, ObjPdf.Stream("x", "/Subtype /Type1C")]));
        Assert.True(bad.Has("PDFA-FONT-WIDTHS"));
        var missing = A(PdfA(Xmp(), "", "/Font << /F1 6 0 R >>", extras: [Type1Font(""), Descriptor, ObjPdf.Stream("x", "/Subtype /Type1C")]));
        Assert.True(missing.Has("PDFA-FONT-WIDTHS"));
    }

    [Fact]
    public void PdfA1SubsetFontsNeedCharSet()
    {
        var pdf = PdfA(Xmp(1), "", "/Font << /F1 6 0 R >>", extras: [Type1Font("/FirstChar 65 /LastChar 65 /Widths [500]"), Descriptor, ObjPdf.Stream("x", "/Subtype /Type1C")]);
        Assert.True(A(pdf, PdfValidationProfile.PdfA1b).Has("PDFA1-FONT-CHARSET"));
        var withSet = PdfA(Xmp(1), "", "/Font << /F1 6 0 R >>", extras: [Type1Font("/FirstChar 65 /LastChar 65 /Widths [500]"), Descriptor.Replace("/Flags 32", "/Flags 32 /CharSet (/A)"), ObjPdf.Stream("x", "/Subtype /Type1C")]);
        Assert.False(A(withSet, PdfValidationProfile.PdfA1b).Has("PDFA1-FONT-CHARSET"));
        Assert.False(A(pdf).Has("PDFA1-FONT-CHARSET"));
    }

    [Fact]
    public void TrueTypeEncodingRulesFollowTheSymbolicFlag()
    {
        string Tt(string flags, string enc) => $"<< /Type /Font /Subtype /TrueType /BaseFont /Foo /FirstChar 65 /LastChar 65 /Widths [500] {enc} /FontDescriptor 7 0 R >>";
        string Desc(string flags) => $"<< /Type /FontDescriptor /FontName /Foo /Flags {flags} /FontFile2 8 0 R >>";
        var ff = ObjPdf.Stream("x");
        Assert.False(A(PdfA(Xmp(), "", "/Font << /F1 6 0 R >>", extras: [Tt("32", "/Encoding /WinAnsiEncoding"), Desc("32"), ff])).Has("PDFA-FONT-TRUETYPE-ENCODING"));
        Assert.True(A(PdfA(Xmp(), "", "/Font << /F1 6 0 R >>", extras: [Tt("32", ""), Desc("32"), ff])).Has("PDFA-FONT-TRUETYPE-ENCODING"));
        Assert.True(A(PdfA(Xmp(), "", "/Font << /F1 6 0 R >>", extras: [Tt("4", "/Encoding /WinAnsiEncoding"), Desc("4"), ff])).Has("PDFA-FONT-TRUETYPE-ENCODING"));
        Assert.False(A(PdfA(Xmp(), "", "/Font << /F1 6 0 R >>", extras: [Tt("4", ""), Desc("4"), ff])).Has("PDFA-FONT-TRUETYPE-ENCODING"));
    }

    [Fact]
    public void NamedActionsAreRestrictedAndNeedAppearancesMustBeFalse()
    {
        Assert.False(A(PdfA(Xmp(), extras: "<< /Type /Action /S /Named /N /NextPage >>")).Has("PDFA-ACTION-NAMED"));
        Assert.True(A(PdfA(Xmp(), extras: "<< /Type /Action /S /Named /N /Print >>")).Has("PDFA-ACTION-NAMED"));
        Assert.True(A(PdfA(Xmp(), "/AcroForm << /Fields [] /NeedAppearances true >>")).Has("PDFA-FORM-NEEDAPPEARANCES"));
        Assert.False(A(PdfA(Xmp(), "/AcroForm << /Fields [] /NeedAppearances false >>")).Has("PDFA-FORM-NEEDAPPEARANCES"));
    }

    // ---- PDF/UA ----

    const string UaXmp = "<rdf:Description rdf:about='' xmlns:pdfuaid='http://www.aiim.org/pdfua/ns/id/' xmlns:dc='http://purl.org/dc/elements/1.1/' pdfuaid:part='1'><dc:title><rdf:Alt><rdf:li xml:lang='x-default'>Doc</rdf:li></rdf:Alt></dc:title></rdf:Description>";

    /// <summary>Object 7 is the Document element whose kids are <paramref name="kids"/>; extras start at object 8; the parent tree maps MCID 0 to object 8.</summary>
    static byte[] Ua(string content, string kids, string pageExtra = "", string catalogExtra = "", params string[] extras)
    {
        var objs = new List<string>
        {
            $"<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 5 0 R /MarkInfo << /Marked true >> /Lang (en) /ViewerPreferences << /DisplayDocTitle true >> /Metadata 6 0 R {catalogExtra} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /StructParents 0 {pageExtra} >>",
            ObjPdf.Stream(content),
            "<< /Type /StructTreeRoot /K 7 0 R /ParentTree << /Nums [0 [8 0 R]] >> >>",
            ObjPdf.Xmp(UaXmp),
            $"<< /Type /StructElem /S /Document /K [{kids}] >>",
        };
        objs.AddRange(extras);
        var pdf = ObjPdf.Build(objs.ToArray());
        Dump(pdf);
        return pdf;
    }

    static ValidationReport U(byte[] pdf) => PdfValidator.Validate(PdfDocument.Load(pdf), PdfValidationProfile.PdfUA1);

    const string Para = "<< /Type /StructElem /S /P /P 7 0 R /Pg 3 0 R /K 0 >>";
    const string TaggedText = "/P <</MCID 0>> BDC BT 10 10 Td (x) Tj ET EMC";

    [Fact]
    public void ContentTaggingConformantPageIsClean()
    {
        var r = U(Ua(TaggedText + " /Artifact BMC 0 0 5 5 re f EMC /Span <</MCID 0 >> BDC EMC", "8 0 R", extras: Para));
        Assert.DoesNotContain(r.Findings, f => f.RuleId is "PDFUA-CONTENT-UNTAGGED" or "PDFUA-MCID-UNMAPPED" or "PDFUA-MCID-MISSING" or "PDFUA-ARTIFACT-TAGGED");
    }

    [Fact]
    public void UntaggedPaintingIsReportedButGraphicsStateIsNot()
    {
        var bad = U(Ua(TaggedText + " 0 0 5 5 re f", "8 0 R", extras: Para));
        Assert.True(bad.Has("PDFUA-CONTENT-UNTAGGED"));
        var ok = U(Ua("1 0 0 rg 1 w q Q " + TaggedText + " 0 0 5 5 re n", "8 0 R", extras: Para));
        Assert.False(ok.Has("PDFUA-CONTENT-UNTAGGED"));
    }

    [Fact]
    public void InlineImagesAreScannedWithoutDesync()
    {
        var content = "q 10 0 0 10 0 0 cm BI /W 1 /H 1 /CS /G /BPC 8 ID \x01 EI Q " + TaggedText;
        Assert.True(U(Ua(content, "8 0 R", extras: Para)).Has("PDFUA-CONTENT-UNTAGGED"));
        var tagged = "/P <</MCID 0>> BDC q 10 0 0 10 0 0 cm BI /W 1 /H 1 /CS /G /BPC 8 ID \x01 EI Q EMC";
        Assert.False(U(Ua(tagged, "8 0 R", extras: Para)).Has("PDFUA-CONTENT-UNTAGGED"));
    }

    [Fact]
    public void McidMappingMustBeCompleteInBothDirections()
    {
        var unmapped = U(Ua(TaggedText + " /P <</MCID 5>> BDC BT (y) Tj ET EMC", "8 0 R", extras: Para));
        Assert.True(unmapped.Has("PDFUA-MCID-UNMAPPED"));
        var missing = U(Ua("/Artifact BMC 0 0 5 5 re f EMC", "8 0 R", extras: Para));
        Assert.Contains(missing.Findings, f => f.RuleId == "PDFUA-MCID-MISSING" && f.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void ArtifactMustNotCarryAnMcid()
    {
        Assert.True(U(Ua("/Artifact <</MCID 0>> BDC 0 0 5 5 re f EMC", "8 0 R", extras: Para)).Has("PDFUA-ARTIFACT-TAGGED"));
    }

    [Fact]
    public void TableSectionRules()
    {
        string Tbl(string kids) => $"<< /S /Table /K [{kids}] >>";
        var tr = "<< /S /TR /K [] >>";
        // objects: 8 Table, 9 THead, 10 THead, 11 TFoot, 12 Caption, 13 TR
        var dup = U(Ua("", "8 0 R", extras: [Tbl("9 0 R 10 0 R 14 0 R"), "<< /S /THead /K [13 0 R] >>", "<< /S /THead /K [13 0 R] >>", "<< /S /TFoot /K [13 0 R] >>", "<< /S /Caption >>", tr, "<< /S /TBody /K [13 0 R] >>"]));
        Assert.True(dup.Has("PDFUA-TABLE-THEAD-MULTIPLE"));
        var footOnly = U(Ua("", "8 0 R", extras: [Tbl("11 0 R"), "<< /S /THead >>", "<< /S /THead >>", "<< /S /TFoot /K [13 0 R] >>", "<< /S /Caption >>", tr]));
        Assert.True(footOnly.Has("PDFUA-TABLE-TBODY"));
        var captionMiddle = U(Ua("", "8 0 R", extras: [Tbl("14 0 R 12 0 R 15 0 R"), "<< /S /THead >>", "<< /S /THead >>", "<< /S /TFoot >>", "<< /S /Caption >>", tr, "<< /S /TBody /K [13 0 R] >>", "<< /S /TBody /K [13 0 R] >>"]));
        Assert.True(captionMiddle.Has("PDFUA-TABLE-CAPTION"));
        var bad = U(Ua("", "8 0 R", extras: [Tbl("14 0 R"), "<< /S /THead >>", "<< /S /THead >>", "<< /S /TFoot >>", "<< /S /Caption >>", tr, "<< /S /TBody /K [12 0 R] >>"]));
        Assert.True(bad.Has("PDFUA-TABLE-SECTION-CHILD"));
        var good = U(Ua("", "8 0 R", extras: [Tbl("12 0 R 14 0 R"), "<< /S /THead >>", "<< /S /THead >>", "<< /S /TFoot >>", "<< /S /Caption >>", tr, "<< /S /TBody /K [13 0 R] >>"]));
        Assert.DoesNotContain(good.Findings, f => f.RuleId is "PDFUA-TABLE-CAPTION" or "PDFUA-TABLE-TBODY" or "PDFUA-TABLE-SECTION-CHILD" or "PDFUA-TABLE-THEAD-MULTIPLE");
    }

    [Fact]
    public void ListTocNoteAndHeadingStructure()
    {
        // 8 L with a late Caption, 9 LI, 10 Caption, 11 LBody under L (wrong), 12 TOC, 13 TOCI, 14 Note, 15 Note, 16 H, 17 H
        var r = U(Ua("", "8 0 R 12 0 R 14 0 R 15 0 R 16 0 R 17 0 R 11 0 R", extras:
        [
            "<< /S /L /K [9 0 R 10 0 R] >>", "<< /S /LI /K [] >>", "<< /S /Caption >>", "<< /S /LBody >>",
            "<< /S /TOC /K [13 0 R 18 0 R] >>", "<< /S /TOCI >>", "<< /S /Note /ID (n1) >>", "<< /S /Note /ID (n1) >>", "<< /S /H >>", "<< /S /H >>", "<< /S /TOCI >>",
        ]));
        Assert.True(r.Has("PDFUA-LIST-CAPTION"));
        Assert.True(r.Has("PDFUA-LBODY-PARENT"));
        Assert.True(r.Has("PDFUA-NOTE-ID-DUPLICATE"));
        Assert.True(r.Has("PDFUA-HEADING-MULTIPLE"));
        var toc = U(Ua("", "8 0 R", extras: ["<< /S /TOC /K [9 0 R] >>", "<< /S /P >>", ]));
        Assert.True(toc.Has("PDFUA-TOC-CHILD"));
        var tociOutside = U(Ua("", "8 0 R", extras: ["<< /S /TOCI >>"]));
        Assert.True(tociOutside.Has("PDFUA-TOCI-PARENT"));
        var noteNoId = U(Ua("", "8 0 R", extras: ["<< /S /Note >>"]));
        Assert.True(noteNoId.Has("PDFUA-NOTE-ID"));
        var good = U(Ua("", "8 0 R 10 0 R", extras: ["<< /S /L /K [9 0 R] >>", "<< /S /LI /K [11 0 R] >>", "<< /S /Note /ID (a) >>", "<< /S /LBody >>"]));
        Assert.DoesNotContain(good.Findings, f => f.RuleId is "PDFUA-LIST-CAPTION" or "PDFUA-NOTE-ID" or "PDFUA-NOTE-ID-DUPLICATE" or "PDFUA-HEADING-MULTIPLE" or "PDFUA-LBODY-PARENT");
    }

    [Fact]
    public void WidgetAndAnnotationDescriptions()
    {
        var annots = "/Annots [9 0 R 10 0 R 11 0 R 12 0 R] /Tabs /S";
        var r = U(Ua(TaggedText, "8 0 R", annots, extras:
        [
            Para,
            "<< /Type /Annot /Subtype /Widget /FT /Tx /Rect [0 0 10 10] /F 4 >>",
            "<< /Type /Annot /Subtype /Widget /FT /Tx /Rect [0 0 10 10] /F 4 /TU (Name) >>",
            "<< /Type /Annot /Subtype /Text /Rect [0 0 10 10] /F 4 >>",
            "<< /Type /Annot /Subtype /Text /Rect [0 0 10 10] /F 4 /Contents (Note) >>",
        ]));
        Assert.Equal(1, r.Findings.Count(f => f.RuleId == "PDFUA-FORM-TU"));
        Assert.Equal(1, r.Findings.Count(f => f.RuleId == "PDFUA-ANNOT-CONTENTS"));
    }

    [Fact]
    public void WidgetFieldTooltipIsInheritedFromParent()
    {
        var r = U(Ua(TaggedText, "8 0 R", "/Annots [9 0 R] /Tabs /S", extras:
        [
            Para,
            "<< /Type /Annot /Subtype /Widget /Parent 10 0 R /Rect [0 0 10 10] /F 4 >>",
            "<< /FT /Tx /T (f) /TU (Name) >>",
        ]));
        Assert.False(r.Has("PDFUA-FORM-TU"));
    }

    [Fact]
    public void DocumentFlagsSuspectsAndOptionalContent()
    {
        var r = U(Ua(TaggedText, "8 0 R", catalogExtra: "/OCProperties << /OCGs [] /D << /AS [] >> >>", extras: Para));
        Assert.True(r.Has("PDFUA-OC-NAME"));
        Assert.True(r.Has("PDFUA-OC-AS"));
        var suspects = Encoding.Latin1.GetString(Ua(TaggedText, "8 0 R", extras: Para)).Replace("/Marked true", "/Marked true /Suspects true");
        Assert.True(U(Encoding.Latin1.GetBytes(suspects)).Has("PDFUA-SUSPECTS"));
    }

    [Fact]
    public void EmbeddedFileSpecificationNeedsFAndUf()
    {
        Assert.True(U(Ua(TaggedText, "8 0 R", extras: [Para, "<< /Type /Filespec /F (a.txt) /EF << /F 10 0 R >> >>", ObjPdf.Stream("x", "/Type /EmbeddedFile")])).Has("PDFUA-FILESPEC-UF"));
        Assert.False(U(Ua(TaggedText, "8 0 R", extras: [Para, "<< /Type /Filespec /F (a.txt) /UF (a.txt) /EF << /F 10 0 R >> >>", ObjPdf.Stream("x", "/Type /EmbeddedFile")])).Has("PDFUA-FILESPEC-UF"));
    }

    [Fact]
    public void FontsNeedUnicodeMappingAndCidToGid()
    {
        const string cid = "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Foo /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor 11 0 R >>";
        const string desc = "<< /Type /FontDescriptor /FontName /Foo /Flags 4 /FontFile2 12 0 R >>";
        string Type0(string extra) => $"<< /Type /Font /Subtype /Type0 /BaseFont /Foo /Encoding /Identity-H /DescendantFonts [10 0 R] {extra} >>";
        var bad = U(Ua(TaggedText, "8 0 R", "/Resources << /Font << /F1 9 0 R >> >>", extras: [Para, Type0(""), cid, desc, ObjPdf.Stream("x")]));
        Assert.True(bad.Has("PDFUA-FONT-TOUNICODE"));
        Assert.True(bad.Has("PDFUA-FONT-CIDTOGID"));
        var good = U(Ua(TaggedText, "8 0 R", "/Resources << /Font << /F1 9 0 R >> >>", extras: [Para, Type0("/ToUnicode 13 0 R"), cid.Replace("/FontDescriptor", "/CIDToGIDMap /Identity /FontDescriptor"), desc, ObjPdf.Stream("x"), ObjPdf.Stream("/CIDInit")]));
        Assert.False(good.Has("PDFUA-FONT-TOUNICODE"));
        Assert.False(good.Has("PDFUA-FONT-CIDTOGID"));
    }

    [Fact]
    public void LongDocumentWithoutOutlinesGetsAdvisoryOnly()
    {
        var kids = string.Join(" ", Enumerable.Range(0, 21).Select(i => $"{3 + i} 0 R"));
        var objs = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked true >> /Lang (en) >>",
            $"<< /Type /Pages /Kids [{kids}] /Count 21 >>",
        };
        for (var i = 0; i < 21; i++) objs.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] >>");
        var r = U(ObjPdf.Build(objs.ToArray()));
        Assert.Contains(r.Findings, f => f.RuleId == "PDFUA-BOOKMARKS" && f.Severity == ValidationSeverity.Info);
    }
}
