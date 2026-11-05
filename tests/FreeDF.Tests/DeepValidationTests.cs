using System.Text;
using PortableDocument.Core;
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
        Assert.False(A(PdfA(Xmp(), "/OpenAction 6 0 R", extras: "<< /Type /Action /S /Named /N /NextPage >>")).Has("PDFA-ACTION-NAMED"));
        Assert.True(A(PdfA(Xmp(), "/OpenAction 6 0 R", extras: "<< /Type /Action /S /Named /N /Print >>")).Has("PDFA-ACTION-NAMED"));
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

    public static IEnumerable<object[]> PropertyLanguageCases()
    {
        (string Value, bool Invalid)[] values =
        [
            ("(en)", false), ("(EN-us)", false), ("(i-klingon)", false),
            ("(zh-Hant-TW)", false), ("(abcdefgh-12345678)", false),
            ("null", false), ("()", true), ("<FEFF>", true), ("( )", true),
            ("(-pt)", true), ("(portugues-pt)", true), ("(1-pt)", true),
            ("(en_Us)", true), ("(en--US)", true), ("(en-123456789)", true),
            ("<656E0A>", true),
        ];
        foreach (var part in new[] { 1, 2 })
        foreach (var source in new[] { "direct", "named", "form" })
        foreach (var indirect in new[] { false, true })
        foreach (var (value, invalid) in values)
            if (!indirect || source == "named")
                yield return [part, source, indirect, value, invalid];
    }

    [Theory]
    [MemberData(nameof(PropertyLanguageCases))]
    public void SpanPropertyLanguageTagsAreValidated(
        int part, string source, bool indirect, string value, bool invalid)
    {
        var language = indirect ? "9 0 R" : value;
        var properties = $"<< /Lang {language} >>";
        var text = $"/Span {properties} BDC BT (x) Tj ET EMC";
        var pageExtra = "";
        var content = $"/P << /MCID 0 >> BDC {text} EMC";
        if (source == "named")
        {
            content = "/P << /MCID 0 >> BDC /Span /Language BDC BT (x) Tj ET EMC EMC";
            pageExtra = $"/Resources << /Properties << /Language {properties} >> >>";
        }
        else if (source == "form")
        {
            content = "/P << /MCID 0 >> BDC /Form Do EMC";
            pageExtra = "/Resources << /XObject << /Form 10 0 R >> >>";
        }
        var pdf = Ua(content, "8 0 R", pageExtra,
            extras: [Para, value, ObjPdf.Stream(text, "/Type /XObject /Subtype /Form /BBox [0 0 100 100]")]);
        var report = PdfValidator.Validate(PdfDocument.Load(pdf),
            part == 1 ? PdfValidationProfile.PdfUA1 : PdfValidationProfile.PdfUA2);
        Assert.Equal(invalid, report.Has("PDFUA-LANG-INVALID"));
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "VALIDATION-INTERNAL");
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void TrailingLineFeedIsNotPartOfAValidLanguageTag(int part, bool structure)
    {
        var pdf = Ua(TaggedText, "8 0 R",
            extras: structure ? Para.Replace("/K 0", "/K 0 /Lang <656E0A>") : Para);
        if (!structure)
            pdf = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(pdf).Replace("/Lang (en)", "/Lang <656E0A>"));
        var report = PdfValidator.Validate(PdfDocument.Load(pdf),
            part == 1 ? PdfValidationProfile.PdfUA1 : PdfValidationProfile.PdfUA2);
        Assert.True(report.Has("PDFUA-LANG-INVALID"));
    }

    [Theory]
    [InlineData(1, false, "()")]
    [InlineData(1, false, "<FEFF>")]
    [InlineData(1, true, "()")]
    [InlineData(1, true, "<FEFF>")]
    [InlineData(2, false, "()")]
    [InlineData(2, false, "<FEFF>")]
    [InlineData(2, true, "()")]
    [InlineData(2, true, "<FEFF>")]
    public void PresentEmptyLanguageIsNotAConformingUaLanguageTag(int part, bool structure, string value)
    {
        var pdf = Ua(TaggedText, "8 0 R",
            extras: structure ? Para.Replace("/K 0", $"/K 0 /Lang {value}") : Para);
        if (!structure)
            pdf = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(pdf).Replace("/Lang (en)", $"/Lang {value}"));
        var report = PdfValidator.Validate(PdfDocument.Load(pdf),
            part == 1 ? PdfValidationProfile.PdfUA1 : PdfValidationProfile.PdfUA2);
        Assert.True(report.Has("PDFUA-LANG-INVALID"));
    }

    public static IEnumerable<object[]> PrivateUseTextCases()
    {
        int[] codepoints = [0xD7FF, 0xE000, 0xF00C, 0xF8FF, 0xF900,
            0xEFFFF, 0xF0000, 0xFFFFD, 0xFFFFE, 0x100000, 0x10FFFD, 0x10FFFE];
        foreach (var part in new[] { 1, 2 })
        foreach (var target in new[] { "catalog", "structure", "property" })
        foreach (var codepoint in codepoints)
        foreach (var utf8 in new[] { false, true })
            yield return [part, target, codepoint, utf8,
                part == 2 && (codepoint is >= 0xE000 and <= 0xF8FF or >= 0xF0000 and <= 0xFFFFD or >= 0x100000 and <= 0x10FFFD)];
    }

    [Theory]
    [MemberData(nameof(PrivateUseTextCases))]
    public void Ua2LanguageStringsExcludePrivateUseCodepoints(
        int part, string target, int codepoint, bool utf8, bool privateUse)
    {
        var text = "a" + char.ConvertFromUtf32(codepoint) + "b";
        var bytes = utf8 ? Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray()
            : Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes(text)).ToArray();
        var value = $"<{Convert.ToHexString(bytes)}>";
        var pdf = Ua(target == "property" ? $"/P << /MCID 0 >> BDC /Span << /Lang {value} >> BDC BT (x) Tj ET EMC EMC" : TaggedText,
            "8 0 R", extras: target == "structure" ? Para.Replace("/K 0", $"/K 0 /Lang {value}") : Para);
        if (target == "catalog")
            pdf = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(pdf).Replace("/Lang (en)", $"/Lang {value}"));
        var report = PdfValidator.Validate(PdfDocument.Load(pdf),
            part == 1 ? PdfValidationProfile.PdfUA1 : PdfValidationProfile.PdfUA2);
        Assert.Equal(privateUse, report.Has("PDFUA2-TEXT-STRING-PUA"));
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "VALIDATION-INTERNAL");
    }

    [Fact]
    public void UnclassifiedFieldStringsDoNotBecomePrivateUseViolations()
    {
        var pdf = Ua(TaggedText, "8 0 R", catalogExtra: "/AcroForm << /Fields [9 0 R] >>",
            extras: [Para, "<< /FT /Tx /T <FEFFE000> /TU <FEFFE000> /TM <FEFFE000> >>"]);
        var report = PdfValidator.Validate(PdfDocument.Load(pdf), PdfValidationProfile.PdfUA2);
        Assert.False(report.Has("PDFUA2-TEXT-STRING-PUA"));
    }

    [Fact]
    public void EarlierInvalidTagDoesNotHideLaterPrivateUseAndRepeatedPrivateUseIsBounded()
    {
        var pdf = Ua("/P << /MCID 0 >> BDC /Span << /Lang (-en) >> BDC EMC " +
            "/Span << /Lang <FEFFE000> >> BDC EMC /Span << /Lang <FEFFF8FF> >> BDC EMC EMC",
            "8 0 R", extras: Para);
        var report = PdfValidator.Validate(PdfDocument.Load(pdf), PdfValidationProfile.PdfUA2);
        Assert.Single(report.Findings, f => f.RuleId == "PDFUA-LANG-INVALID");
        Assert.Single(report.Findings, f => f.RuleId == "PDFUA2-TEXT-STRING-PUA");
    }

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

    [Theory]
    [InlineData("")]
    [InlineData("/T null")]
    [InlineData("/T 11 0 R")]
    [InlineData("/T 999 0 R")]
    public void NullWidgetPartialNameKeepsUa1TerminalFieldTooltip(string name)
    {
        var pdf = Ua("", "8 0 R", "/Annots [9 0 R]",
            "/AcroForm << /Fields [10 0 R] >>", extras:
        [
            "<< /Type /StructElem /S /Form /P 7 0 R /K << /Type /OBJR /Obj 9 0 R /Pg 3 0 R >> >>",
            $"<< /Type /Annot /Subtype /Widget /Parent 10 0 R /Rect [0 0 10 10] /F 4 {name} >>",
            "<< /FT /Tx /T (name) /TU (Field description) /Kids [9 0 R] >>",
            "null",
        ]);
        var report = U(pdf);
        Assert.False(report.Has("PDFUA-FORM-TU"));
        Assert.False(report.Has("PDFUA-FORM-INCOMPLETE"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    public static IEnumerable<object[]> WidgetStructureCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var kind in new[] { "form", "artifact", "mapped-form", "mapped-artifact",
            "ancestor-artifact", "mapped-ancestor-artifact", "paragraph", "pdf17-artifact", "foreign-unmapped" })
            yield return [part, kind];
    }

    [Theory]
    [MemberData(nameof(WidgetStructureCases))]
    public void Ua2WidgetStructureResolvesFormAndArtifactNamespaces(int part, string kind)
    {
        var pdf = Ua("", "8 0 R", "/Annots [9 0 R]", extras:
        [
            "<< /Type /StructElem /S /Form /P 7 0 R /K << /Type /OBJR /Obj 9 0 R /Pg 3 0 R >> >>",
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /TU (name) /Contents (description) /StructParent 1 /Rect [0 0 10 10] >>",
        ]);
        var doc = PdfDocument.Load(pdf);
        var root = Assert.IsType<PdfDictionary>(doc.File.Resolve(doc.File.Catalog.Get("StructTreeRoot")));
        root.Entries["ParentTree"] = new PdfDictionary
        {
            Entries = { ["Nums"] = new PdfArray([new PdfInteger(1), new PdfRef(8, 0)]) },
        };
        var parent = Assert.IsType<PdfDictionary>(doc.File.GetObject(8));
        var document = Assert.IsType<PdfDictionary>(doc.File.GetObject(7));
        var pdf2 = new PdfDictionary { Entries = { ["NS"] = new PdfString(Encoding.ASCII.GetBytes("http://iso.org/pdf2/ssn")) } };
        if (kind is "artifact" or "pdf17-artifact")
        {
            parent.Entries["S"] = new PdfName("Artifact");
            if (kind == "artifact") parent.Entries["NS"] = pdf2;
        }
        if (kind is "mapped-form" or "mapped-artifact" or "mapped-ancestor-artifact" or "foreign-unmapped")
        {
            var element = kind == "mapped-ancestor-artifact" ? document : parent;
            var ns = new PdfDictionary { Entries = { ["NS"] = new PdfString(Encoding.ASCII.GetBytes("urn:widget-structure")) } };
            if (kind != "foreign-unmapped")
                ns.Entries["RoleMapNS"] = new PdfDictionary
                {
                    Entries = { ["Custom"] = new PdfArray([new PdfName(kind == "mapped-form" ? "Form" : "Artifact"), pdf2]) },
                };
            element.Entries["S"] = new PdfName("Custom");
            element.Entries["NS"] = ns;
        }
        if (kind is "ancestor-artifact" or "mapped-ancestor-artifact" or "paragraph")
        {
            parent.Entries["S"] = new PdfName("P");
            if (kind == "ancestor-artifact")
            {
                document.Entries["S"] = new PdfName("Artifact");
                document.Entries["NS"] = pdf2;
            }
        }
        var expected = part == 1 ? kind != "form" : kind is "paragraph" or "pdf17-artifact" or "foreign-unmapped";
        var report = PdfValidator.ValidatePdfUA(doc, part);
        Assert.Equal(expected, report.Has("PDFUA-FORM-STRUCT"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    public static IEnumerable<object[]> FormWidgetCountCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var kind in new[] { "default-form", "pdf2-form", "mapped-form", "mapped-chain-form",
            "mapped-artifact", "mapped-paragraph", "foreign-unmapped", "artifact" })
        foreach (var count in new[] { 0, 1, 2 })
            yield return [part, kind, count, "Widget"];
        foreach (var kind in new[] { "default-form", "mapped-form", "mapped-chain-form" })
            yield return [2, kind, 2, "Link"];
    }

    [Theory]
    [MemberData(nameof(FormWidgetCountCases))]
    public void Ua2FormWidgetCountUsesResolvedNamespaceType(int part, string kind, int count, string subtype)
    {
        var references = string.Join(" ", Enumerable.Range(9, count).Select(id => $"{id} 0 R"));
        var objects = string.Join(" ", Enumerable.Range(9, count).Select(id =>
            $"<< /Type /OBJR /Obj {id} 0 R /Pg 3 0 R >>"));
        var pdf = Ua("", "8 0 R", $"/Annots [{references}]",
            catalogExtra: $"/AcroForm << /Fields [{references}] >>", extras:
            [
                $"<< /Type /StructElem /S /Form /P 7 0 R /K [{objects}] >>",
                $"<< /Type /Annot /Subtype /{subtype} /FT /Tx /T (first) /TU (First) /Contents (First) /StructParent 1 /Rect [0 0 10 10] >>",
                $"<< /Type /Annot /Subtype /{subtype} /FT /Tx /T (second) /TU (Second) /Contents (Second) /StructParent 2 /Rect [20 0 30 10] >>",
            ]);
        var doc = PdfDocument.Load(pdf);
        var root = Assert.IsType<PdfDictionary>(doc.File.Resolve(doc.File.Catalog.Get("StructTreeRoot")));
        root.Entries["ParentTree"] = new PdfDictionary
        {
            Entries = { ["Nums"] = new PdfArray(Enumerable.Range(1, count)
                .SelectMany(id => new PdfObject[] { new PdfInteger(id), new PdfRef(8, 0) }).ToList()) },
        };
        var form = Assert.IsType<PdfDictionary>(doc.File.GetObject(8));
        var pdf2 = new PdfDictionary { Entries = { ["NS"] = new PdfString(Encoding.ASCII.GetBytes("http://iso.org/pdf2/ssn")) } };
        if (kind is "pdf2-form" or "artifact")
        {
            form.Entries["NS"] = pdf2;
            if (kind == "artifact") form.Entries["S"] = new PdfName("Artifact");
        }
        if (kind.StartsWith("mapped-", StringComparison.Ordinal) || kind == "foreign-unmapped")
        {
            var role = kind is "mapped-form" or "mapped-chain-form" ? "Custom" : "Form";
            var ns = new PdfDictionary { Entries = { ["NS"] = new PdfString(Encoding.ASCII.GetBytes("urn:form-count")) } };
            if (kind != "foreign-unmapped")
            {
                var target = kind == "mapped-artifact" ? "Artifact" : kind == "mapped-paragraph" ? "P" : "Form";
                var targetNamespace = pdf2;
                if (kind == "mapped-chain-form")
                    targetNamespace = new PdfDictionary
                    {
                        Entries =
                        {
                            ["NS"] = new PdfString(Encoding.ASCII.GetBytes("urn:form-count-intermediate")),
                            ["RoleMapNS"] = new PdfDictionary { Entries = { ["Form"] = new PdfArray([new PdfName("Form"), pdf2]) } },
                        },
                    };
                ns.Entries["RoleMapNS"] = new PdfDictionary
                {
                    Entries = { [role] = new PdfArray([new PdfName(target), targetNamespace]) },
                };
            }
            form.Entries["S"] = new PdfName(role);
            form.Entries["NS"] = ns;
        }
        var report = PdfValidator.ValidatePdfUA(doc, part);
        var expected = part == 2 && subtype == "Widget" && count > 1
            && kind is "default-form" or "pdf2-form" or "mapped-form" or "mapped-chain-form";
        Assert.Equal(expected, report.Has("PDFUA2-FORM-WIDGETS"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    public static IEnumerable<object[]> WidgetFieldTypeCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var fieldType in new[] { "", "/FT null", "/FT 11 0 R", "/FT 999 0 R", "/FT /Tx", "/FT 12 0 R" })
        foreach (var parentType in new[] { "", "/FT null", "/FT /Tx" })
        foreach (var tooltip in new[] { "", "/TU (Name)" })
            yield return [part, fieldType, parentType, tooltip];
    }

    [Theory]
    [MemberData(nameof(WidgetFieldTypeCases))]
    public void UaNullFieldTypeDoesNotEstablishFieldDescriptionRequirement(int part, string fieldType, string parentType, string tooltip)
    {
        var pdf = Ua("", "8 0 R", "/Annots [9 0 R]", extras:
        [
            "<< /Type /StructElem /S /Form /P 7 0 R /K << /Type /OBJR /Obj 9 0 R /Pg 3 0 R >> >>",
            $"<< /Type /Annot /Subtype /Widget /Parent 10 0 R /Rect [0 0 10 10] /F 4 {fieldType} {tooltip} >>",
            $"<< {parentType} >>",
            "null",
            "/Tx",
        ]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), part);
        var hasType = fieldType is "/FT /Tx" or "/FT 12 0 R" || parentType == "/FT /Tx";
        Assert.Equal(hasType && tooltip == "", report.Has("PDFUA-FORM-TU"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    public static IEnumerable<object[]> WidgetActionContextCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var contents in new[] { "", "/Contents null", "/Contents ()", "/Contents (description)" })
        foreach (var context in new[] { "normal", "hidden", "outside", "untagged", "artifact", "nonfield" })
        foreach (var action in new[] { "", "/AA null", "/AA << >>" })
            yield return [part, contents, context, action];
    }

    [Theory]
    [MemberData(nameof(WidgetActionContextCases))]
    public void Ua2WidgetAdditionalActionRequiresOwnContents(int part, string contents, string context, string action)
    {
        var type = context == "artifact" ? "/Artifact /NS << /NS (http://iso.org/pdf2/ssn) >>" : "/Form";
        var flags = context == "hidden" ? 2 : 4;
        var rect = context == "outside" ? "[200 200 210 210]" : "[0 0 10 10]";
        var structParent = context == "untagged" ? "" : "/StructParent 1";
        var field = context == "nonfield" ? "" : "/FT /Tx /T (name) /TU (tooltip)";
        var pdf = Ua("", "8 0 R", "/Annots [9 0 R]",
            extras: [$"<< /Type /StructElem /S {type} /P 7 0 R /Alt (alternative) /K << /Type /OBJR /Obj 9 0 R /Pg 3 0 R >> >>",
                $"<< /Type /Annot /Subtype /Widget {field} {structParent} /Rect {rect} /F {flags} {contents} {action} >>"]);
        var doc = PdfDocument.Load(pdf);
        var root = Assert.IsType<PdfDictionary>(doc.File.Resolve(doc.File.Catalog.Get("StructTreeRoot")));
        root.Entries["ParentTree"] = new PdfDictionary
        {
            Entries = { ["Nums"] = new PdfArray([new PdfInteger(1), new PdfRef(8, 0)]) },
        };
        var report = PdfValidator.ValidatePdfUA(doc, part);
        var expected = part == 2 && action == "/AA << >>"
            && contents is "" or "/Contents null"
            && context is "normal" or "hidden" or "outside";
        Assert.Equal(expected, report.Has("PDFUA2-WIDGET-AA-CONTENTS"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData("inherited-contents", true)]
    [InlineData("field-parent", true)]
    [InlineData("mapped-artifact", false)]
    [InlineData("ancestor-artifact", false)]
    public void Ua2WidgetActionContextUsesOwnContentsAndResolvedArtifactAncestors(string context, bool expected)
    {
        var pdf = Ua("", "8 0 R", "/Annots [9 0 R]",
            extras: ["<< /Type /StructElem /S /Form /P 7 0 R /K << /Type /OBJR /Obj 9 0 R /Pg 3 0 R >> >>",
                "<< /Type /Annot /Subtype /Widget /Parent 10 0 R /StructParent 1 /Rect [0 0 10 10] /AA << >> >>",
                "<< /FT /Tx /T (name) /Contents (not the widget description) /Kids [9 0 R] >>"]);
        var doc = PdfDocument.Load(pdf);
        var root = Assert.IsType<PdfDictionary>(doc.File.Resolve(doc.File.Catalog.Get("StructTreeRoot")));
        root.Entries["ParentTree"] = new PdfDictionary
        {
            Entries = { ["Nums"] = new PdfArray([new PdfInteger(1), new PdfRef(8, 0)]) },
        };
        var form = Assert.IsType<PdfDictionary>(doc.File.GetObject(8));
        if (context == "mapped-artifact")
        {
            var pdf2 = new PdfDictionary { Entries = { ["NS"] = new PdfString(Encoding.ASCII.GetBytes("http://iso.org/pdf2/ssn")) } };
            form.Entries["S"] = new PdfName("CustomArtifact");
            form.Entries["NS"] = new PdfDictionary
            {
                Entries =
                {
                    ["NS"] = new PdfString(Encoding.ASCII.GetBytes("urn:widget-test")),
                    ["RoleMapNS"] = new PdfDictionary
                    {
                        Entries = { ["CustomArtifact"] = new PdfArray([new PdfName("Artifact"), pdf2]) },
                    },
                },
            };
        }
        if (context == "ancestor-artifact")
        {
            var document = Assert.IsType<PdfDictionary>(doc.File.GetObject(7));
            document.Entries["S"] = new PdfName("Artifact");
            document.Entries["NS"] = new PdfDictionary
            {
                Entries = { ["NS"] = new PdfString(Encoding.ASCII.GetBytes("http://iso.org/pdf2/ssn")) },
            };
        }
        if (context == "field-parent")
            Assert.IsType<PdfDictionary>(doc.File.GetObject(10)).Entries.Remove("Contents");
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.Equal(expected, report.Has("PDFUA2-WIDGET-AA-CONTENTS"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
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

    public static IEnumerable<object[]> OptionalContentNameCases()
    {
        string[] configurations = ["", "/Configs null", "/Configs []",
            "/Configs [9 0 R]", "/Configs [<< /Name (Alternative) >>]"];
        foreach (var part in new[] { 1, 2 })
        for (var index = 0; index < configurations.Length; index++)
        foreach (var name in new[] { "", "/Name ()", "/Name (Default)", "/Name /NotAString" })
        foreach (var automaticState in new[] { "", "/AS []", "/AS null", "/AS 10 0 R", "/AS 11 0 R" })
            yield return [part, configurations[index], name, automaticState,
                (part == 1 || index >= 3) && name != "/Name (Default)"];
    }

    [Theory]
    [MemberData(nameof(OptionalContentNameCases))]
    public void ConfigurationNameRequirementIsConditionalOnlyForUa2(
        int part, string configurations, string name, string automaticState, bool missingName)
    {
        var properties = $"/OCProperties << /OCGs [] /D << {name} {automaticState} >> {configurations} >>";
        var pdf = Ua(TaggedText, "8 0 R", catalogExtra: properties,
            extras: [Para, "<< /Name (Alternative) >>", "null", "[]"]);
        var report = PdfValidator.Validate(PdfDocument.Load(pdf),
            part == 1 ? PdfValidationProfile.PdfUA1 : PdfValidationProfile.PdfUA2);
        Assert.Equal(missingName, report.Has("PDFUA-OC-NAME"));
        Assert.Equal(automaticState is "/AS []" or "/AS 11 0 R", report.Has("PDFUA-OC-AS"));
    }

    [Theory]
    [InlineData(1, "", true)]
    [InlineData(1, "/Name ()", true)]
    [InlineData(1, "/Name /NotAString", true)]
    [InlineData(1, "/Name (Alternative)", false)]
    [InlineData(2, "", true)]
    [InlineData(2, "/Name ()", true)]
    [InlineData(2, "/Name /NotAString", true)]
    [InlineData(2, "/Name (Alternative)", false)]
    public void NonDefaultConfigurationStillRequiresNameAndForbidsAutomaticState(int part, string name, bool missingName)
    {
        var pdf = Ua(TaggedText, "8 0 R",
            catalogExtra: "/OCProperties << /OCGs [] /D << /Name (Default) >> /Configs [9 0 R] >>",
            extras: [Para, $"<< {name} /AS [] >>"]);
        var report = PdfValidator.Validate(PdfDocument.Load(pdf),
            part == 1 ? PdfValidationProfile.PdfUA1 : PdfValidationProfile.PdfUA2);
        Assert.Equal(missingName, report.Has("PDFUA-OC-NAME"));
        Assert.True(report.Has("PDFUA-OC-AS"));
    }

    [Theory]
    [InlineData(1, "<FEFF>", true)]
    [InlineData(1, "<FEFF004E>", false)]
    [InlineData(2, "<FEFF>", true)]
    [InlineData(2, "<FEFF004E>", false)]
    public void RequiredConfigurationNameUsesDecodedTextLength(int part, string name, bool missingName)
    {
        var pdf = Ua(TaggedText, "8 0 R",
            catalogExtra: $"/OCProperties << /OCGs [] /D << /Name {name} >> /Configs [9 0 R] >>",
            extras: [Para, "<< /Name (Alternative) >>"]);
        var report = PdfValidator.Validate(PdfDocument.Load(pdf),
            part == 1 ? PdfValidationProfile.PdfUA1 : PdfValidationProfile.PdfUA2);
        Assert.Equal(missingName, report.Has("PDFUA-OC-NAME"));
    }

    [Fact]
    public void EmbeddedFileSpecificationNeedsFAndUf()
    {
        Assert.True(U(Ua(TaggedText, "8 0 R", extras: [Para, "<< /Type /Filespec /F (a.txt) /EF << /F 10 0 R >> >>", ObjPdf.Stream("x", "/Type /EmbeddedFile")])).Has("PDFUA-FILESPEC-UF"));
        Assert.False(U(Ua(TaggedText, "8 0 R", extras: [Para, "<< /Type /Filespec /F (a.txt) /UF (a.txt) /EF << /F 10 0 R >> >>", ObjPdf.Stream("x", "/Type /EmbeddedFile")])).Has("PDFUA-FILESPEC-UF"));
    }

    public static IEnumerable<object[]> FontEmbeddingUseCases()
    {
        (string Text, bool Rendered)[] cases =
        [
            ("", false), ("BT /UF 12 Tf ET", false),
            ("BT /UF 12 Tf 3 Tr (x) Tj ET", false),
            ("BT /UF 12 Tf 3 Tr q 0 Tr Q (x) Tj ET", false),
            ("BT /UF 12 Tf () Tj ET", true),
            ("BT /UF 12 Tf [100] TJ ET", true),
            ("BT /UF 12 Tf (x) Tj ET", true),
            ("BT /UF 12 Tf 1 Tr (x) Tj ET", true),
            ("BT /UF 12 Tf 2 Tr (x) Tj ET", true),
            ("BT /UF 12 Tf 4 Tr (x) Tj ET", true),
            ("BT /UF 12 Tf 5 Tr (x) Tj ET", true),
            ("BT /UF 12 Tf 6 Tr (x) Tj ET", true),
            ("BT /UF 12 Tf 7 Tr (x) Tj ET", true),
            ("BT /UF 12 Tf 3 Tr (x) Tj 0 Tr (y) Tj ET", true),
            ("BT /UF 12 Tf q 3 Tr Q (x) Tj ET", true),
            ("BT /UF 12 Tf (x) ' ET", true),
            ("BT /UF 12 Tf 0 0 (x) \" ET", true),
        ];
        foreach (var part in new[] { 1, 2 })
        foreach (var surface in new[] { "page", "form", "appearance", "uninvoked-form" })
        foreach (var (text, rendered) in cases)
            yield return [part, surface, text, rendered];
    }

    [Theory]
    [MemberData(nameof(FontEmbeddingUseCases))]
    public void UaFontEmbeddingRequiresRenderingUse(int part, string surface, string text, bool rendered)
    {
        const string fontResources = "/Font << /UF 9 0 R >>";
        var resources = $"/Resources << {fontResources} /XObject << /FontForm 10 0 R >> >>";
        var pageExtra = resources;
        var content = surface == "uninvoked-form" ? "" : text;
        if (surface == "form") content = "/FontForm Do";
        if (surface == "appearance")
        {
            content = "";
            pageExtra += " /Annots [11 0 R]";
        }
        var pdf = Ua($"/P << /MCID 0 >> BDC {content} EMC", "8 0 R", pageExtra,
            extras: [Para, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
                ObjPdf.Stream(text, $"/Type /XObject /Subtype /Form /BBox [0 0 100 100] /Resources << {fontResources} >>"),
                "<< /Type /Annot /Subtype /Stamp /Rect [0 0 10 10] /AP << /N 10 0 R >> >>"]);
        var report = PdfValidator.Validate(PdfDocument.Load(pdf),
            part == 1 ? PdfValidationProfile.PdfUA1 : PdfValidationProfile.PdfUA2);
        Assert.Equal(rendered && surface != "uninvoked-form", report.Has("PDFUA-FONT-EMBEDDED"));
        Assert.DoesNotContain(report.Findings, f => f.RuleId is "PDFUA-FONT-USAGE-INCOMPLETE" or "VALIDATION-INTERNAL");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void UaFontEmbeddingCannotExemptUninspectedUse(int part)
    {
        var pdf = Ua("/P << /MCID 0 >> BDC /Missing Do EMC", "8 0 R",
            "/Resources << /Font << /UF 9 0 R >> >>",
            extras: [Para, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"]);
        var report = PdfValidator.Validate(PdfDocument.Load(pdf),
            part == 1 ? PdfValidationProfile.PdfUA1 : PdfValidationProfile.PdfUA2);
        Assert.Single(report.Findings, f => f.RuleId == "PDFUA-FONT-USAGE-INCOMPLETE" && f.Severity == ValidationSeverity.Error);
        Assert.False(report.Has("PDFUA-FONT-EMBEDDED"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void UaFontScanLimitDoesNotEstablishUnusedExemption(int part)
    {
        var pdf = Ua("BT /UF 12 Tf (x) Tj ET", "8 0 R",
            "/Resources << /Font << /UF 9 0 R >> >>",
            extras: [Para, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"]);
        var context = new ValidationContext(PdfDocument.Load(pdf),
            part == 1 ? PdfValidationProfile.PdfUA1 : PdfValidationProfile.PdfUA2, contentScanTokenLimit: 2);
        PdfUAChecks.Run(context);
        Assert.False(context.FontUsageComplete);
        Assert.Single(context.Findings, f => f.RuleId == "PDFUA-FONT-USAGE-INCOMPLETE" && f.Severity == ValidationSeverity.Error);
    }

    [Theory]
    [InlineData("BT /UF 12 Tf 3 Tr (unterminated")]
    [InlineData("BT /Missing 12 Tf (x) Tj ET")]
    [InlineData("BT /UF 12 Tf /Bad Tr ET")]
    [InlineData("BT /UF 12 Tf [true] TJ ET")]
    [InlineData("/Missing Do")]
    public void UaFontEmbeddingReportsUnknownRatherThanUnused(string content)
    {
        foreach (var part in new[] { 1, 2 })
        {
            var pdf = Ua(content, "8 0 R", "/Resources << /Font << /UF 9 0 R >> >>",
                extras: [Para, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"]);
            var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), part);
            Assert.Single(report.Findings, f => f.RuleId == "PDFUA-FONT-USAGE-INCOMPLETE" && f.Severity == ValidationSeverity.Error);
            Assert.False(report.Has("PDFUA-FONT-EMBEDDED"));
            Assert.False(report.Has("VALIDATION-INTERNAL"));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void UaFontEmbeddingReportsMalformedFlateContent(int part)
    {
        var pdf = Ua("", "8 0 R", "/Resources << /Font << /UF 9 0 R >> >>",
            extras: [Para, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"]);
        var doc = PdfDocument.Load(pdf);
        doc.Pages[0].Dict.Entries["Contents"] = new PdfStream(new PdfDictionary
        {
            Entries = { ["Filter"] = new PdfName("FlateDecode") },
        }, System.Text.Encoding.ASCII.GetBytes("BT /UF 12 Tf 3 Tr (x) Tj ET"));
        var report = PdfValidator.ValidatePdfUA(doc, part);
        Assert.True(report.Has("PDFUA-FONT-USAGE-INCOMPLETE"));
        Assert.True(report.Has("PDFUA-CONTENT-SCAN-INCOMPLETE"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(1, "pattern")]
    [InlineData(2, "pattern")]
    [InlineData(1, "type3")]
    [InlineData(2, "type3")]
    public void UaFontEmbeddingDoesNotExemptUninspectedPatternOrCharProcUse(int part, string surface)
    {
        var resources = "/Resources << /Font << /UF 9 0 R /T3 10 0 R >> /Pattern << /Pat 11 0 R >> >>";
        var content = surface == "pattern" ? "/Pattern cs /Pat scn 0 0 10 10 re f" : "BT /T3 12 Tf (x) Tj ET";
        var pdf = Ua(content, "8 0 R", resources,
            extras: [Para, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
                "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1 1] /FontMatrix [1 0 0 1 0 0] /CharProcs << >> >>",
                ObjPdf.Stream("BT /UF 12 Tf (x) Tj ET", "/Type /Pattern /PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 10 10] /XStep 10 /YStep 10")]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), part);
        Assert.Single(report.Findings, f => f.RuleId == "PDFUA-FONT-USAGE-INCOMPLETE" && f.Severity == ValidationSeverity.Error);
        Assert.False(report.Has("PDFUA-FONT-EMBEDDED"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    const string XfaConfigNamespace = "http://www.xfa.org/schema/xci/3.0/";

    public static IEnumerable<object[]> DynamicXfaCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var stream in new[] { false, true })
        foreach (var needs in new[] { "", "/NeedsRendering false", "/NeedsRendering true" })
        foreach (var value in new[] { "required", "forbidden", "", "Required", " required " })
            yield return [part, stream, needs, value, part == 1 && value == "required"];
    }

    [Theory]
    [MemberData(nameof(DynamicXfaCases))]
    public void Ua1DynamicXfaUsesConfigurationInsteadOfRenderingFlag(
        int part, bool stream, string needs, string value, bool dynamic)
    {
        var config = $"<config xmlns='{XfaConfigNamespace}'><acrobat><acrobat7><dynamicRender>{value}</dynamicRender></acrobat7></acrobat></config>";
        var xml = stream ? $"<xdp:xdp xmlns:xdp='http://ns.adobe.com/xdp/'>{config}</xdp:xdp>" : config;
        var pdf = Ua(TaggedText, "8 0 R",
            catalogExtra: $"{needs} /AcroForm << /Fields [] /XFA {(stream ? "9 0 R" : "[(config) 9 0 R]")} >>",
            extras: [Para, ObjPdf.Stream(xml)]);
        var report = PdfValidator.Validate(PdfDocument.Load(pdf),
            part == 1 ? PdfValidationProfile.PdfUA1 : PdfValidationProfile.PdfUA2);
        Assert.Equal(dynamic, report.Has("PDFUA-XFA-DYNAMIC"));
        Assert.Equal(part == 2, report.Has("PDFUA2-XFA"));
        Assert.DoesNotContain(report.Findings, f => f.RuleId is "PDFUA-XFA-INCOMPLETE" or "VALIDATION-INTERNAL");
    }

    public static IEnumerable<object[]> XfaPresenceCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var value in new[] { "", "/XFA null", "/XFA 9 0 R", "/XFA 999 0 R", "/XFA []" })
        foreach (var needs in new[] { "", "/NeedsRendering false", "/NeedsRendering true" })
            yield return [part, value, needs];
    }

    [Theory]
    [MemberData(nameof(XfaPresenceCases))]
    public void Ua2XfaPresenceDoesNotDependOnRenderingFlag(int part, string value, string needs)
    {
        var pdf = Ua("", "8 0 R", catalogExtra: $"{needs} /AcroForm << /Fields [] {value} >>",
            extras: [Para, "null"]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), part);
        Assert.Equal(part == 2 && value == "/XFA []", report.Has("PDFUA2-XFA"));
        Assert.False(report.Has("PDFUA-XFA-DYNAMIC"));
        Assert.DoesNotContain(report.Findings, f => f.RuleId is "PDFUA-XFA-INCOMPLETE" or "VALIDATION-INTERNAL");
    }

    [Theory]
    [InlineData("<config xmlns='urn:foreign'><acrobat><acrobat7><dynamicRender>required</dynamicRender></acrobat7></acrobat></config>")]
    [InlineData("<config><acrobat><acrobat7><dynamicRender>required</dynamicRender></acrobat7></acrobat></config>")]
    [InlineData("<config xmlns='http://www.xfa.org/schema/xci/3.0/'><dynamicRender>required</dynamicRender></config>")]
    [InlineData("<config xmlns='http://www.xfa.org/schema/xci/3.0/'><acrobat><acrobat7><dynamicRender xmlns='urn:foreign'>required</dynamicRender></acrobat7></acrobat></config>")]
    public void LookalikeXfaElementsAreNotDynamicConfiguration(string xml)
    {
        var pdf = Ua(TaggedText, "8 0 R",
            catalogExtra: "/NeedsRendering true /AcroForm << /XFA [(config) 9 0 R] >>",
            extras: [Para, ObjPdf.Stream(xml)]);
        var report = U(pdf);
        Assert.False(report.Has("PDFUA-XFA-DYNAMIC"));
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "VALIDATION-INTERNAL");
    }

    [Theory]
    [InlineData("")]
    [InlineData("<config>")]
    [InlineData("<!DOCTYPE config SYSTEM 'https://invalid.example/never-load'><config/>")]
    public void MalformedXfaConfigurationIsExplicitlyIncomplete(string xml)
    {
        var pdf = Ua(TaggedText, "8 0 R", catalogExtra: "/AcroForm << /XFA [(config) 9 0 R] >>",
            extras: [Para, ObjPdf.Stream(xml)]);
        var report = U(pdf);
        Assert.True(report.Has("PDFUA-XFA-INCOMPLETE"));
        Assert.False(report.Has("PDFUA-XFA-DYNAMIC"));
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "VALIDATION-INTERNAL");
    }

    [Theory]
    [InlineData("null", false)]
    [InlineData("9 0 R", false)]
    [InlineData("99 0 R", false)]
    [InlineData("[]", false)]
    [InlineData("[(config)]", true)]
    [InlineData("[(config) null]", true)]
    [InlineData("[(config) 99 0 R]", true)]
    [InlineData("(not packets)", true)]
    public void XfaPacketBoundariesDoNotSilentlyLookComplete(string xfa, bool incomplete)
    {
        var pdf = Ua(TaggedText, "8 0 R", catalogExtra: $"/AcroForm << /XFA {xfa} >>",
            extras: [Para, "null"]);
        var report = U(pdf);
        Assert.Equal(incomplete, report.Has("PDFUA-XFA-INCOMPLETE"));
        Assert.False(report.Has("PDFUA-XFA-DYNAMIC"));
    }

    [Fact]
    public void XfaConfigurationInspectionIsBounded()
    {
        var xml = $"<config xmlns='{XfaConfigNamespace}'>" + string.Concat(Enumerable.Repeat("<a>", 70))
            + string.Concat(Enumerable.Repeat("</a>", 70)) + "</config>";
        var pdf = Ua(TaggedText, "8 0 R", catalogExtra: "/AcroForm << /XFA [(config) 9 0 R] >>",
            extras: [Para, ObjPdf.Stream(xml)]);
        Assert.True(U(pdf).Has("PDFUA-XFA-INCOMPLETE"));
        var packets = string.Concat(Enumerable.Repeat("(config) 9 0 R ", 1001));
        pdf = Ua(TaggedText, "8 0 R", catalogExtra: $"/AcroForm << /XFA [{packets}] >>",
            extras: [Para, ObjPdf.Stream($"<config xmlns='{XfaConfigNamespace}'/>")]);
        Assert.Single(U(pdf).Findings, f => f.RuleId == "PDFUA-XFA-INCOMPLETE");
    }

    public static IEnumerable<object[]> FileSpecificationNameCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var key in new[] { "F", "UF" })
        foreach (var indirect in new[] { false, true })
        foreach (var value in new[] { "(a.txt)", "<FEFF0061>", "<FEFF>", "()", "null", "( )" })
            yield return [part, key, indirect, value,
                value is "()" or "null" || part == 1 && key == "UF" && value == "<FEFF>"];
    }

    [Theory]
    [MemberData(nameof(FileSpecificationNameCases))]
    public void EmbeddedFileUnicodeNameMustHaveDecodedText(
        int part, string key, bool indirect, string value, bool missing)
    {
        var names = "/F (a.txt) /UF (a.txt)".Replace($"/{key} (a.txt)", $"/{key} {(indirect ? "11 0 R" : value)}");
        var pdf = Ua(TaggedText, "8 0 R", extras:
            [Para, $"<< /Type /Filespec {names} /EF << /F 10 0 R >> >>",
             ObjPdf.Stream("x", "/Type /EmbeddedFile"), value]);
        var report = PdfValidator.Validate(PdfDocument.Load(pdf),
            part == 1 ? PdfValidationProfile.PdfUA1 : PdfValidationProfile.PdfUA2);
        Assert.Equal(missing, report.Has("PDFUA-FILESPEC-UF"));
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "VALIDATION-INTERNAL");
    }

    public static IEnumerable<object[]> FileSpecificationEmbeddedEntryCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var names in new[] { false, true })
        foreach (var ef in new[] { "", "/EF null", "/EF 11 0 R", "/EF 99 0 R",
            "/EF << /F 10 0 R >>", "/EF 12 0 R" })
            yield return [part, names, ef,
                !names && ef is "/EF << /F 10 0 R >>" or "/EF 12 0 R"];
    }

    [Theory]
    [MemberData(nameof(FileSpecificationEmbeddedEntryCases))]
    public void UaNullEmbeddedFileEntryIsEquivalentToOmission(int part, bool names, string ef, bool missing)
    {
        var pdf = Ua(TaggedText, "8 0 R", extras:
            [Para, $"<< /Type /Filespec {(names ? "/F (a.txt) /UF (a.txt)" : "")} {ef} >>",
             ObjPdf.Stream("x", "/Type /EmbeddedFile"), "null", "<< /F 10 0 R >>"]);
        var report = PdfValidator.Validate(PdfDocument.Load(pdf),
            part == 1 ? PdfValidationProfile.PdfUA1 : PdfValidationProfile.PdfUA2);
        Assert.Equal(missing, report.Has("PDFUA-FILESPEC-UF"));
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "VALIDATION-INTERNAL");
    }

    [Theory]
    [InlineData("default", true)]
    [InlineData("mapped-parent", true)]
    [InlineData("mapped-children", true)]
    [InlineData("mapped-both", true)]
    [InlineData("parent-div", false)]
    [InlineData("child-p", false)]
    public void Ua2TocContainmentUsesNamespaceNestingNotLegacyRoles(string kind, bool valid)
    {
        var doc = PdfDocument.Load(Ua("", "8 0 R", extras:
        [
            "<< /Type /StructElem /S /TOC /P 7 0 R /K 9 0 R >>",
            "<< /Type /StructElem /S /TOCI /P 8 0 R /Ref [10 0 R] /K 10 0 R >>",
            "<< /Type /StructElem /S /P /P 9 0 R >>",
        ]));
        var pdf17 = new PdfDictionary { Entries = { ["NS"] = new PdfString(Encoding.ASCII.GetBytes("http://iso.org/pdf/ssn")) } };
        void Map(int number, string role, string target)
        {
            var element = Assert.IsType<PdfDictionary>(doc.File.GetObject(number));
            element.Entries["S"] = new PdfName(role);
            element.Entries["NS"] = new PdfDictionary
            {
                Entries =
                {
                    ["NS"] = new PdfString(Encoding.ASCII.GetBytes("urn:toc-test")),
                    ["RoleMapNS"] = new PdfDictionary { Entries = { [role] = new PdfArray([new PdfName(target), pdf17]) } },
                },
            };
        }
        if (kind is "mapped-parent" or "mapped-both" or "parent-div") Map(8, "Contents", kind == "parent-div" ? "Div" : "TOC");
        if (kind is "mapped-children" or "mapped-both" or "child-p") Map(9, "Entry", kind == "child-p" ? "P" : "TOCI");
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.False(report.Has("PDFUA-TOC-CHILD"));
        Assert.False(report.Has("PDFUA-TOCI-PARENT"));
        Assert.Equal(!valid, report.Has("PDFUA2-NESTING"));
        Assert.False(report.Has("PDFUA2-TOCI-REF"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    public static IEnumerable<object[]> ListAttributeCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var kind in new[] { "owner-list", "owner-layout", "owner-omitted", "class-valid", "class-none",
            "class-layout", "local-valid-class-none", "local-none-class-valid", "local-null-class-valid" })
            yield return [part, kind];
    }

    [Theory]
    [MemberData(nameof(ListAttributeCases))]
    public void Ua2ListNumberingUsesOwnedLocalAndClassAttributes(int part, string kind)
    {
        var owner = kind == "owner-layout" ? "/O /Layout" : kind == "owner-omitted" ? "" : "/O /List";
        var localValue = kind == "local-none-class-valid" ? "/None" : kind == "local-null-class-valid" ? "null" : "/Decimal";
        var local = kind.StartsWith("class", StringComparison.Ordinal) ? "" : $"/A << {owner} /ListNumbering {localValue} >>";
        var classValue = kind is "class-none" or "local-valid-class-none" ? "/None" : "/Decimal";
        var pdf = Ua("", "8 0 R", extras:
        [
            $"<< /Type /StructElem /S /L /P 7 0 R /K 9 0 R {local} /C /First >>",
            "<< /Type /StructElem /S /LI /P 8 0 R /K [10 0 R 11 0 R] >>",
            "<< /Type /StructElem /S /Lbl /P 9 0 R >>",
            "<< /Type /StructElem /S /LBody /P 9 0 R >>",
        ]);
        var doc = PdfDocument.Load(pdf);
        doc.Structure!.Root.Entries["ClassMap"] = new PdfDictionary
        {
            Entries =
            {
                ["First"] = new PdfDictionary
                {
                    Entries =
                    {
                        ["O"] = new PdfName(kind == "class-layout" ? "Layout" : "List"),
                        ["ListNumbering"] = new PdfName(classValue[1..]),
                    },
                },
            },
        };
        if (!kind.Contains("class", StringComparison.Ordinal))
            Assert.IsType<PdfDictionary>(doc.File.GetObject(8)).Entries.Remove("C");
        var report = PdfValidator.ValidatePdfUA(doc, part);
        var required = kind is "owner-layout" or "owner-omitted" or "class-none" or "class-layout" or "local-none-class-valid";
        Assert.Equal(part == 2 && required, report.Has("PDFUA2-LIST-NUMBERING"));
        Assert.False(report.Has("PDFUA2-LIST-ATTRIBUTE-INCOMPLETE"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("C")]
    [InlineData("ClassMap")]
    public void Ua2ListCyclicAttributeLookupIsIncomplete(string key)
    {
        var pdf = Ua("", "8 0 R", extras:
        [
            $"<< /Type /StructElem /S /L /P 7 0 R /K 9 0 R {(key == "A" ? "/A 12 0 R" : key == "C" ? "/C 12 0 R" : "/C /First")} >>",
            "<< /Type /StructElem /S /LI /P 8 0 R /K [10 0 R 11 0 R] >>",
            "<< /Type /StructElem /S /Lbl /P 9 0 R >>",
            "<< /Type /StructElem /S /LBody /P 9 0 R >>",
            "12 0 R",
        ]);
        var doc = PdfDocument.Load(pdf);
        doc.Structure!.Root.Entries["ClassMap"] = key == "ClassMap" ? new PdfRef(12, 0) : new PdfDictionary();
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.True(report.Has("PDFUA2-LIST-ATTRIBUTE-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-LIST-NUMBERING"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(199999, false)]
    [InlineData(200000, true)]
    public void Ua2ListClassAndAttributeWorkShareOneBound(int count, bool incomplete)
    {
        var doc = PdfDocument.Load(Ua("", "8 0 R", extras:
        [
            "<< /Type /StructElem /S /L /P 7 0 R /K 9 0 R /C /First >>",
            "<< /Type /StructElem /S /LI /P 8 0 R /K [10 0 R 11 0 R] >>",
            "<< /Type /StructElem /S /Lbl /P 9 0 R >>",
            "<< /Type /StructElem /S /LBody /P 9 0 R >>",
        ]));
        var attributes = new PdfArray(Enumerable.Repeat<PdfObject>(new PdfDictionary(), count - 1)
            .Append(new PdfDictionary { Entries = { ["O"] = new PdfName("List"), ["ListNumbering"] = new PdfName("Decimal") } }).ToList());
        doc.Structure!.Root.Entries["ClassMap"] = new PdfDictionary { Entries = { ["First"] = attributes } };
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.Equal(incomplete, report.Has("PDFUA2-LIST-ATTRIBUTE-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-LIST-NUMBERING"));
    }

    [Theory]
    [InlineData("/C /First /A << /O /List /ListNumbering /Decimal >>")]
    [InlineData("/C [/First /Second] /A [<< /O /Layout /ListNumbering /None >> << /O /List /ListNumbering /Decimal >>]")]
    public void Ua2ListLocalValueSkipsUnusedCyclicClassMap(string attributes)
    {
        var doc = PdfDocument.Load(Ua("", "8 0 R", extras:
        [
            $"<< /Type /StructElem /S /L /P 7 0 R /K 9 0 R {attributes} >>",
            "<< /Type /StructElem /S /LI /P 8 0 R /K [10 0 R 11 0 R] >>",
            "<< /Type /StructElem /S /Lbl /P 9 0 R >>",
            "<< /Type /StructElem /S /LBody /P 9 0 R >>",
            "12 0 R",
        ]));
        doc.Structure!.Root.Entries["ClassMap"] = new PdfRef(12, 0);
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.False(report.Has("PDFUA2-LIST-ATTRIBUTE-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-LIST-NUMBERING"));
    }

    public static IEnumerable<object[]> ListNamespaceCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var kind in new[] { "default", "mapped-list", "mapped-item", "mapped-label", "mapped-body", "mapped-all",
            "foreign-list", "mapped-div-list", "mapped-span-label" })
        foreach (var numbering in new[] { "", "/ListNumbering /None", "/ListNumbering /Decimal" })
            yield return [part, kind, numbering];
    }

    [Theory]
    [MemberData(nameof(ListNamespaceCases))]
    public void Ua2ListNumberingAndListChildrenUseNamespaceRoles(int part, string kind, string numbering)
    {
        var pdf = Ua("", "8 0 R", extras:
        [
            $"<< /Type /StructElem /S /L /P 7 0 R /A << /O /List {numbering} >> /K 9 0 R >>",
            "<< /Type /StructElem /S /LI /P 8 0 R /K [10 0 R 11 0 R] >>",
            "<< /Type /StructElem /S /Lbl /P 9 0 R >>",
            "<< /Type /StructElem /S /LBody /P 9 0 R >>",
        ]);
        var doc = PdfDocument.Load(pdf);
        var pdf2 = new PdfDictionary { Entries = { ["NS"] = new PdfString(Encoding.ASCII.GetBytes("http://iso.org/pdf2/ssn")) } };
        void Map(int number, string role, string? target)
        {
            var element = Assert.IsType<PdfDictionary>(doc.File.GetObject(number));
            var ns = new PdfDictionary { Entries = { ["NS"] = new PdfString(Encoding.ASCII.GetBytes("urn:list-test")) } };
            if (target is not null)
                ns.Entries["RoleMapNS"] = new PdfDictionary
                {
                    Entries = { [role] = new PdfArray([new PdfName(target), pdf2]) },
                };
            element.Entries["S"] = new PdfName(role);
            element.Entries["NS"] = ns;
        }
        if (kind is "mapped-list" or "mapped-all") Map(8, "CustomList", "L");
        if (kind is "mapped-item" or "mapped-all") Map(9, "CustomItem", "LI");
        if (kind is "mapped-label" or "mapped-all") Map(10, "CustomLabel", "Lbl");
        if (kind is "mapped-body" or "mapped-all") Map(11, "CustomBody", "LBody");
        if (kind == "foreign-list") Map(8, "L", null);
        if (kind == "mapped-div-list") Map(8, "L", "Div");
        if (kind == "mapped-span-label") Map(10, "Lbl", "Span");
        var report = PdfValidator.ValidatePdfUA(doc, part);
        var list = kind is not ("foreign-list" or "mapped-div-list");
        var label = kind != "mapped-span-label";
        Assert.Equal(part == 2 && list && label && numbering != "/ListNumbering /Decimal",
            report.Has("PDFUA2-LIST-NUMBERING"));
        if (part == 2 && kind.StartsWith("mapped-", StringComparison.Ordinal) && kind is not ("mapped-div-list" or "mapped-span-label"))
            Assert.DoesNotContain(report.Findings, f => f.RuleId is "PDFUA-LIST-CHILD" or "PDFUA-LI-PARENT" or "PDFUA-LI-CHILD" or "PDFUA-LBODY-PARENT");
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void Ua2MappedListItemDirectContentIsRejected(int part, bool expected)
    {
        var pdf = Ua("/CustomItem << /MCID 0 >> BDC 0 0 10 10 re f EMC", "8 0 R", extras:
        [
            "<< /Type /StructElem /S /L /P 7 0 R /K 9 0 R >>",
            "<< /Type /StructElem /S /CustomItem /P 8 0 R /K 0 /NS << /NS (urn:list-content) /RoleMapNS << /CustomItem [/LI << /NS (http://iso.org/pdf2/ssn) >>] >> >> >>",
        ]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), part);
        Assert.Equal(expected, report.Has("PDFUA2-LI-CONTENT"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData("L", "P", "PDFUA-LIST-CHILD")]
    [InlineData("LI", "P", "PDFUA-LI-CHILD")]
    [InlineData("Document", "LI", "PDFUA-LI-PARENT")]
    [InlineData("Document", "LBody", "PDFUA2-NESTING")]
    public void Ua2MappedListRolesRetainInvalidContainmentFindings(string parent, string child, string rule)
    {
        var ns = $"/NS << /NS (urn:list-containment) /RoleMapNS << /CustomParent [/{parent} << /NS (http://iso.org/pdf2/ssn) >>] /CustomChild [/{child} << /NS (http://iso.org/pdf2/ssn) >>] >> >>";
        var pdf = Ua("", "8 0 R", extras: parent == "Document"
            ? [$"<< /Type /StructElem /S /CustomChild /P 7 0 R {ns} >>"]
            : [$"<< /Type /StructElem /S /CustomParent /P 7 0 R /K 9 0 R {ns} >>",
               $"<< /Type /StructElem /S /CustomChild /P 8 0 R {ns} >>"]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.True(report.Has(rule));
        Assert.True(report.Has("PDFUA2-NESTING"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData("L", "Custom", "PDFUA-LIST-CHILD", false)]
    [InlineData("L", "Custom", "PDFUA-LIST-CHILD", true)]
    [InlineData("Custom", "LI", "PDFUA-LI-PARENT", false)]
    [InlineData("Custom", "LI", "PDFUA-LI-PARENT", true)]
    [InlineData("Custom", "LBody", "PDFUA-LBODY-PARENT", false)]
    [InlineData("Custom", "LBody", "PDFUA-LBODY-PARENT", true)]
    public void Ua2UnresolvedListRolesRetainIncompleteWithoutKnownShapeErrors(string parent, string child, string rule, bool cyclic)
    {
        var ns = $"/NS << /NS (urn:list-unresolved) /RoleMapNS << {(cyclic ? "/Custom /Custom" : "")} >> >>";
        var pdf = Ua("", "8 0 R", extras:
        [
            $"<< /Type /StructElem /S /{parent} /P 7 0 R /K 9 0 R {(parent == "Custom" ? ns : "")} >>",
            $"<< /Type /StructElem /S /{child} /P 8 0 R {(child == "Custom" ? ns : "")} >>",
        ]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.False(report.Has(rule));
        Assert.True(report.Has("PDFUA2-NESTING-INCOMPLETE"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    public static IEnumerable<object[]> ReferenceXObjectCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var subtype in new[] { "Form", "Image" })
        foreach (var reference in new[] { "", "/Ref null", "/Ref 10 0 R", "/Ref 99 0 R",
            "/Ref << /F (other.pdf) /Page 0 >>", "/Ref 11 0 R" })
            yield return [part, subtype, reference];
    }

    [Theory]
    [MemberData(nameof(ReferenceXObjectCases))]
    public void Ua1ReferenceXObjectNullIsEquivalentToOmission(int part, string subtype, string reference)
    {
        var pdf = Ua("", "8 0 R", "/Resources << /XObject << /Form 9 0 R >> >>", extras:
        [
            Para, ObjPdf.Stream("", $"/Type /XObject /Subtype /{subtype} /BBox [0 0 10 10] {reference}"),
            "null", "<< /F (other.pdf) /Page 0 >>",
        ]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), part);
        Assert.Equal(part == 1 && subtype == "Form" && reference is "/Ref << /F (other.pdf) /Page 0 >>" or "/Ref 11 0 R",
            report.Has("PDFUA-XOBJECT-REF"));
        Assert.DoesNotContain(report.Findings, f => f.RuleId is "PDFUA-XOBJECT-REF-INCOMPLETE" or "VALIDATION-INTERNAL");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Ua1CyclicReferenceXObjectIsIncompleteNotNull(int part)
    {
        var pdf = Ua("", "8 0 R", "/Resources << /XObject << /Form 9 0 R >> >>",
            extras: [Para, ObjPdf.Stream("", "/Type /XObject /Subtype /Form /BBox [0 0 10 10] /Ref 10 0 R"), "10 0 R"]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), part);
        Assert.Equal(part == 1, report.Has("PDFUA-XOBJECT-REF-INCOMPLETE"));
        Assert.False(report.Has("PDFUA-XOBJECT-REF"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(1, 32, false)]
    [InlineData(1, 33, true)]
    [InlineData(2, 32, false)]
    [InlineData(2, 33, false)]
    public void Ua1ReferenceXObjectReferenceDepthBoundaryIsExplicit(int part, int length, bool incomplete)
    {
        var chain = Enumerable.Range(10, length).Select(n => n == 9 + length ? "null" : $"{n + 1} 0 R");
        var pdf = Ua("", "8 0 R", "/Resources << /XObject << /Form 9 0 R >> >>",
            extras: new[] { Para, ObjPdf.Stream("", "/Type /XObject /Subtype /Form /BBox [0 0 10 10] /Ref 10 0 R") }.Concat(chain).ToArray());
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), part);
        Assert.Equal(incomplete, report.Has("PDFUA-XOBJECT-REF-INCOMPLETE"));
        Assert.False(report.Has("PDFUA-XOBJECT-REF"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(false, "Hello", false)]
    [InlineData(false, "Goodbye", true)]
    [InlineData(true, "Hello", false)]
    [InlineData(true, "Goodbye", true)]
    public void Ua2RichFieldPlainValueMatchesSingleParagraphText(bool stream, string plain, bool mismatch)
    {
        const string xml = "<body xmlns='http://www.w3.org/1999/xhtml'><p>Hel<span>lo</span></p></body>";
        var rich = stream ? "/RV 12 0 R" : $"/RV ({xml})";
        var pdf = Ua("", "8 0 R", catalogExtra: "/AcroForm << /Fields [9 0 R] >>", extras:
        [
            Para, $"<< /FT /Tx /T (field) {rich} /V ({plain}) >>", "null", "null",
            ObjPdf.Stream(xml),
        ]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.Equal(mismatch, report.Has("PDFUA2-FORM-RV-V"));
        Assert.DoesNotContain(report.Findings, f => f.RuleId is "PDFUA2-FORM-VALUE-INCOMPLETE" or "VALIDATION-INTERNAL");
    }

    public static IEnumerable<object[]> RichFieldEqualityCases()
    {
        (string Content, string Plain, bool Mismatch)[] cases =
        [
            ("<p>Hello</p>", "Hello", false),
            ("<p>Hello</p>", "Goodbye", true),
            ("<p>Hel<span>lo</span></p>", "Hello", false),
            ("<p>A&amp;B</p>", "A&B", false),
            ("<p></p>", "", false),
            ("<p> Hello </p>", " Hello ", false),
            ("<p> Hello </p>", "Hello", true),
            ("<p>A</p><p>B</p>", "AB", false),
            ("<p>A</p><p>B</p>", "A\nB", true),
            ("<p>A</p><p>B</p>", "A\rB", true),
            ("<p>A</p><p>B</p>", "A\r\nB", true),
            ("<p>A<br/>B</p>", "AB", false),
            ("<p>A<br/>B</p>", "A\nB", true),
            ("<p>A<br/>B</p>", "A\rB", true),
            ("<p>A<br/>B</p>", "A\r\nB", true),
            ("\n  <p>Hello</p>\n", "Hello", true),
            ("<p>A&#x20;B</p>", "A B", false),
            ("<p>caf&#xE9;</p>", "caf\u00e9", false),
        ];
        foreach (var part in new[] { 1, 2 })
        foreach (var stream in new[] { false, true })
        foreach (var test in cases)
            yield return [part, stream, test.Content, test.Plain, test.Mismatch];
    }

    [Theory]
    [MemberData(nameof(RichFieldEqualityCases))]
    public void RichFieldEqualityPreservesXmlTextOrderAndWhitespace(int part, bool stream, string content, string plain, bool mismatch)
    {
        var xml = $"<body xmlns='http://www.w3.org/1999/xhtml'>{content}</body>";
        var rich = stream ? "/RV 12 0 R" : $"/RV <{Convert.ToHexString(Encoding.UTF8.GetBytes(xml))}>";
        var value = Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes("\uFEFF" + plain));
        var pdf = Ua("", "8 0 R", catalogExtra: "/AcroForm << /Fields [9 0 R] >>", extras:
        [
            Para, $"<< /FT /Tx /T (field) {rich} /V <{value}> >>", "null", "null", ObjPdf.Stream(xml),
        ]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), part);
        Assert.Equal(part == 2 && mismatch, report.Has("PDFUA2-FORM-RV-V"));
        Assert.DoesNotContain(report.Findings, f => f.RuleId is "PDFUA2-FORM-VALUE-INCOMPLETE" or "VALIDATION-INTERNAL");
    }

    [Theory]
    [InlineData("<body xmlns='http://www.w3.org/1999/xhtml'><p>text</body>")]
    [InlineData("<body xmlns='urn:foreign'><p>text</p></body>")]
    [InlineData("<!DOCTYPE body [<!ENTITY x 'text'>]><body xmlns='http://www.w3.org/1999/xhtml'>&x;</body>")]
    [InlineData("<!DOCTYPE body SYSTEM 'file:///does-not-exist'><body xmlns='http://www.w3.org/1999/xhtml'>text</body>")]
    public void RichFieldEqualityMalformedOrDtdXmlIsIncomplete(string xml)
    {
        var pdf = Ua("", "8 0 R", catalogExtra: "/AcroForm << /Fields [9 0 R] >>", extras:
            [Para, $"<< /FT /Tx /T (field) /RV <{Convert.ToHexString(Encoding.UTF8.GetBytes(xml))}> /V (text) >>"]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.Single(report.Findings, f => f.RuleId == "PDFUA2-FORM-VALUE-INCOMPLETE" && f.Severity == ValidationSeverity.Error);
        Assert.False(report.Has("PDFUA2-FORM-RV-V"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(62, false)]
    [InlineData(63, true)]
    public void RichFieldEqualityXmlDepthBoundaryIsExplicit(int spans, bool incomplete)
    {
        var xml = "<body xmlns='http://www.w3.org/1999/xhtml'><p>" + string.Concat(Enumerable.Repeat("<span>", spans))
            + "text" + string.Concat(Enumerable.Repeat("</span>", spans)) + "</p></body>";
        var pdf = Ua("", "8 0 R", catalogExtra: "/AcroForm << /Fields [9 0 R] >>", extras:
            [Para, $"<< /FT /Tx /T (field) /RV <{Convert.ToHexString(Encoding.UTF8.GetBytes(xml))}> /V (text) >>"]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.Equal(incomplete, report.Has("PDFUA2-FORM-VALUE-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-FORM-RV-V"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(199998, false)]
    [InlineData(199999, true)]
    public void RichFieldEqualityXmlWorkBoundaryIsExplicit(int spans, bool incomplete)
    {
        var xml = "<body xmlns='http://www.w3.org/1999/xhtml'><p>"
            + string.Concat(Enumerable.Repeat("<span/>", spans)) + "text</p></body>";
        var pdf = Ua("", "8 0 R", catalogExtra: "/AcroForm << /Fields [9 0 R] >>", extras:
            [Para, "<< /FT /Tx /T (field) /RV 12 0 R /V (text) >>", "null", "null", ObjPdf.Stream(xml)]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.Equal(incomplete, report.Has("PDFUA2-FORM-VALUE-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-FORM-RV-V"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData("/Filter /Unknown")]
    [InlineData("/Filter /DCTDecode")]
    [InlineData("/Filter /FlateDecode")]
    [InlineData("/Filter << >>")]
    [InlineData("/Filter /ASCIIHexDecode /DecodeParms 11 0 R")]
    public void RichFieldEqualityUnsupportedMalformedOrCyclicFiltersAreIncomplete(string filter)
    {
        var pdf = Ua("", "8 0 R", catalogExtra: "/AcroForm << /Fields [9 0 R] >>", extras:
        [
            Para, "<< /FT /Tx /T (field) /RV 12 0 R /V (text) >>", "null", "11 0 R",
            ObjPdf.Stream("<body xmlns='http://www.w3.org/1999/xhtml'><p>text</p></body>", filter),
        ]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.Single(report.Findings, f => f.RuleId == "PDFUA2-FORM-VALUE-INCOMPLETE" && f.Severity == ValidationSeverity.Error);
        Assert.False(report.Has("PDFUA2-FORM-RV-V"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RichFieldEqualityXfaBodyAttributesDoNotChangeStreamText(bool stream, bool unicode)
    {
        const string xml = "<body xmlns='http://www.w3.org/1999/xhtml' xmlns:xfa='http://www.xfa.org/schema/xfa-data/1.0/' xfa:contentType='text/html' xfa:APIVersion='Control:1.0' xfa:spec='2.4'><p>Hello</p></body>";
        var rich = stream ? "/RV 12 0 R" : $"/RV ({xml})";
        var value = unicode ? "<FEFF00480065006C006C006F>" : "(Hello)";
        var pdf = Ua("", "8 0 R", catalogExtra: "/AcroForm << /Fields [9 0 R] >>", extras:
            [Para, $"<< /FT /Tx /T (field) {rich} /V {value} >>", "null", "null", ObjPdf.Stream(xml)]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.False(report.Has("PDFUA2-FORM-RV-V"));
        Assert.False(report.Has("PDFUA2-FORM-VALUE-INCOMPLETE"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(33554432, false)]
    [InlineData(33554433, true)]
    public void RichFieldEqualityPlainByteBoundaryIsExplicit(int bytes, bool incomplete)
    {
        var pdf = Ua("", "8 0 R", catalogExtra: "/AcroForm << /Fields [9 0 R] >>", extras:
            [Para, "<< /FT /Tx /T (field) /RV (<body xmlns='http://www.w3.org/1999/xhtml'><p>a</p></body>) /V (a) >>"]);
        var doc = PdfDocument.Load(pdf);
        var value = new byte[bytes];
        Array.Fill(value, (byte)'a');
        doc.GetFormFieldDetails().Fields.Single().Dictionary.Entries["V"] = new PdfString(value);
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.Equal(incomplete, report.Has("PDFUA2-FORM-VALUE-INCOMPLETE"));
        Assert.Equal(!incomplete, report.Has("PDFUA2-FORM-RV-V"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData("math", "P", false, true)]
    [InlineData("Math", "P", false, true)]
    [InlineData("mi", "P", false, true)]
    [InlineData("math", "Formula", false, false)]
    [InlineData("mi", "Formula", false, false)]
    [InlineData("mi", "math", false, false)]
    [InlineData("Custom", "Formula", true, false)]
    [InlineData("Custom", "P", true, true)]
    [InlineData("Math", "P", true, false)]
    public void Ua2MathParentUsesResolvedNamespace(string role, string parent, bool mapped, bool invalid)
    {
        var mathNamespace = "<< /Type /Namespace /NS (http://www.w3.org/1998/Math/MathML) >>";
        var mapping = role == "Math" ? "/Math [/P 12 0 R]" : "/Custom [/math 11 0 R]";
        var pdf = Ua("", "8 0 R", extras:
        [
            $"<< /Type /StructElem /S /{parent} /NS {(parent == "math" ? 11 : 12)} 0 R /P 7 0 R /K [9 0 R] /Alt (formula) >>",
            $"<< /Type /StructElem /S /{role} /NS {(mapped ? 13 : 11)} 0 R /P 8 0 R /K [] >>",
            "null", mathNamespace,
            "<< /Type /Namespace /NS (http://iso.org/pdf2/ssn) >>",
            $"<< /Type /Namespace /NS (urn:math-test) /RoleMapNS << {mapping} >> >>",
        ]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        // The outer math under Document is invalid independently of its valid mi child.
        var expected = invalid || parent == "math";
        Assert.Equal(expected, report.Has("PDFUA2-MATH-PARENT"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Fact]
    public void Ua2MathMlDescendantUnderFormulaIsValid()
    {
        var pdf = Ua("", "8 0 R", extras:
        [
            "<< /Type /StructElem /S /Formula /P 7 0 R /K [9 0 R] /Alt (formula) >>",
            "<< /Type /StructElem /S /math /NS 11 0 R /P 8 0 R /K [10 0 R] >>",
            "<< /Type /StructElem /S /mi /NS 11 0 R /P 9 0 R /K [] >>",
            "<< /Type /Namespace /NS (http://www.w3.org/1998/Math/MathML) >>",
        ]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.False(report.Has("PDFUA2-MATH-PARENT"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    public static IEnumerable<object[]> StructureKidNullCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var root in new[] { false, true })
        foreach (var kids in new[] { "null", "[]", "[null]", "[99 0 R]", "[9 0 R]",
            "[8 0 R null]", "[[null]]" })
            yield return [part, root, kids];
    }

    [Theory]
    [MemberData(nameof(StructureKidNullCases))]
    public void Ua2StructureKidArraysMustNotContainNull(int part, bool root, string kids)
    {
        var doc = PdfDocument.Load(Ua("", "8 0 R", extras:
            ["<< /Type /StructElem /S /P /P 7 0 R /K [] >>", "null"]));
        var dictionary = Assert.IsType<PdfDictionary>(doc.File.GetObject(root ? 5 : 7));
        dictionary.Entries["K"] = new PdfLexer(Encoding.ASCII.GetBytes(kids)).ReadObject()!;
        var report = PdfValidator.ValidatePdfUA(doc, part);
        Assert.Equal(part == 2 && kids is "[null]" or "[99 0 R]" or "[9 0 R]" or "[8 0 R null]",
            report.Has("PDFUA2-STRUCT-K-NULL"));
        Assert.False(report.Has("PDFUA2-STRUCT-K-INCOMPLETE"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(false, "9 0 R")]
    [InlineData(false, "[9 0 R]")]
    [InlineData(true, "9 0 R")]
    [InlineData(true, "[9 0 R]")]
    public void Ua2StructureKidReferenceCyclesAreIncompleteNotGuessedNull(bool root, string kids)
    {
        var doc = PdfDocument.Load(Ua("", "8 0 R", extras:
            ["<< /Type /StructElem /S /P /P 7 0 R /K [] >>", "9 0 R"]));
        Assert.IsType<PdfDictionary>(doc.File.GetObject(root ? 5 : 7)).Entries["K"] =
            new PdfLexer(Encoding.ASCII.GetBytes(kids)).ReadObject()!;
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.True(report.Has("PDFUA2-STRUCT-K-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-STRUCT-K-NULL"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(false, 200000, false)]
    [InlineData(false, 200001, true)]
    [InlineData(true, 200000, false)]
    [InlineData(true, 200001, true)]
    public void Ua2StructureKidNullInspectionIsBounded(bool root, int count, bool incomplete)
    {
        var doc = PdfDocument.Load(Ua("", "8 0 R", extras:
            ["<< /Type /StructElem /S /P /P 7 0 R /K [] >>"]));
        Assert.IsType<PdfDictionary>(doc.File.GetObject(root ? 5 : 7)).Entries["K"] =
            new PdfArray(Enumerable.Repeat<PdfObject>(PdfNull.Instance, count).ToList());
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.Equal(incomplete, report.Has("PDFUA2-STRUCT-K-INCOMPLETE"));
        Assert.True(report.Has("PDFUA2-STRUCT-K-NULL"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ua2StructureKidKnownNullSurvivesLaterIncompleteReference(bool root)
    {
        var doc = PdfDocument.Load(Ua("", "8 0 R", extras:
            ["<< /Type /StructElem /S /P /P 7 0 R /K [] >>", "9 0 R"]));
        Assert.IsType<PdfDictionary>(doc.File.GetObject(root ? 5 : 7)).Entries["K"] =
            new PdfArray([PdfNull.Instance, new PdfRef(9, 0)]);
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.True(report.Has("PDFUA2-STRUCT-K-INCOMPLETE"));
        Assert.True(report.Has("PDFUA2-STRUCT-K-NULL"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(false, 32, false)]
    [InlineData(false, 33, true)]
    [InlineData(true, 32, false)]
    [InlineData(true, 33, true)]
    public void Ua2StructureKidReferenceDepthIsBounded(bool root, int count, bool incomplete)
    {
        var chain = Enumerable.Range(9, count).Select(n =>
            n == 8 + count ? "[]" : $"{n + 1} 0 R");
        var doc = PdfDocument.Load(Ua("", "8 0 R", extras:
            new[] { "<< /Type /StructElem /S /P /P 7 0 R /K [] >>" }.Concat(chain).ToArray()));
        Assert.IsType<PdfDictionary>(doc.File.GetObject(root ? 5 : 7)).Entries["K"] = new PdfRef(9, 0);
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.Equal(incomplete, report.Has("PDFUA2-STRUCT-K-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-STRUCT-K-NULL"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    public static IEnumerable<object[]> StructureAttributeNullCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var key in new[] { "A", "C" })
        foreach (var value in new[] { "null", "[]", "[0]", "[null]", "[99 0 R]", "[9 0 R]", "[[null]]" })
            yield return [part, key, value];
    }

    [Theory]
    [MemberData(nameof(StructureAttributeNullCases))]
    public void Ua2StructureAttributeArraysMustNotContainNull(int part, string key, string value)
    {
        var doc = PdfDocument.Load(Ua("", "8 0 R", extras:
            ["<< /Type /StructElem /S /P /P 7 0 R /K [] >>", "null"]));
        Assert.IsType<PdfDictionary>(doc.File.GetObject(8)).Entries[key] =
            new PdfLexer(Encoding.ASCII.GetBytes(value)).ReadObject()!;
        var report = PdfValidator.ValidatePdfUA(doc, part);
        Assert.Equal(part == 2 && value is "[null]" or "[99 0 R]" or "[9 0 R]",
            report.Has($"PDFUA2-STRUCT-{key}-NULL"));
        Assert.False(report.Has($"PDFUA2-STRUCT-{key}-INCOMPLETE"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData("A", false)]
    [InlineData("C", false)]
    [InlineData("A", true)]
    [InlineData("C", true)]
    public void Ua2StructureAttributeNullAndIncompleteAreDistinct(string key, bool knownNull)
    {
        var doc = PdfDocument.Load(Ua("", "8 0 R", extras:
            ["<< /Type /StructElem /S /P /P 7 0 R /K [] >>", "9 0 R"]));
        Assert.IsType<PdfDictionary>(doc.File.GetObject(8)).Entries[key] =
            new PdfArray(knownNull ? [PdfNull.Instance, new PdfRef(9, 0)] : [new PdfRef(9, 0)]);
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.True(report.Has($"PDFUA2-STRUCT-{key}-INCOMPLETE"));
        Assert.Equal(knownNull, report.Has($"PDFUA2-STRUCT-{key}-NULL"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData("A", 200000, false)]
    [InlineData("A", 200001, true)]
    [InlineData("C", 200000, false)]
    [InlineData("C", 200001, true)]
    public void Ua2StructureAttributeNullInspectionIsBounded(string key, int count, bool incomplete)
    {
        var doc = PdfDocument.Load(Ua("", "8 0 R", extras:
            ["<< /Type /StructElem /S /P /P 7 0 R /K [] >>"]));
        Assert.IsType<PdfDictionary>(doc.File.GetObject(8)).Entries[key] =
            new PdfArray(Enumerable.Repeat<PdfObject>(PdfNull.Instance, count).ToList());
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.Equal(incomplete, report.Has($"PDFUA2-STRUCT-{key}-INCOMPLETE"));
        Assert.True(report.Has($"PDFUA2-STRUCT-{key}-NULL"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    public static IEnumerable<object[]> StructureRevisionCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var value in new[] { "null", "0", "7", "-1", "1.0", "1.5", "/Revision", "[]", "99 0 R", "9 0 R" })
            yield return [part, value];
    }

    [Theory]
    [MemberData(nameof(StructureRevisionCases))]
    public void Ua2StructureRevisionMustBeNonNegativeInteger(int part, string value)
    {
        var doc = PdfDocument.Load(Ua("", "8 0 R", extras:
            ["<< /Type /StructElem /S /P /P 7 0 R /K [] >>", "7"]));
        Assert.IsType<PdfDictionary>(doc.File.GetObject(8)).Entries["R"] =
            new PdfLexer(Encoding.ASCII.GetBytes(value)).ReadObject()!;
        var report = PdfValidator.ValidatePdfUA(doc, part);
        Assert.Equal(part == 2 && value is "-1" or "1.0" or "1.5" or "/Revision" or "[]",
            report.Has("PDFUA2-STRUCT-R-TYPE"));
        Assert.False(report.Has("PDFUA2-STRUCT-R-INCOMPLETE"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(32, false)]
    [InlineData(33, true)]
    public void Ua2StructureRevisionReferenceDepthIsBounded(int count, bool incomplete)
    {
        var chain = Enumerable.Range(9, count).Select(n => n == 8 + count ? "7" : $"{n + 1} 0 R");
        var doc = PdfDocument.Load(Ua("", "8 0 R", extras:
            new[] { "<< /Type /StructElem /S /P /P 7 0 R /K [] /R 9 0 R >>" }.Concat(chain).ToArray()));
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.Equal(incomplete, report.Has("PDFUA2-STRUCT-R-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-STRUCT-R-TYPE"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Fact]
    public void Ua2StructureRevisionReferenceCycleIsIncompleteNotInvalidType()
    {
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(Ua("", "8 0 R", extras:
            ["<< /Type /StructElem /S /P /P 7 0 R /K [] /R 9 0 R >>", "9 0 R"])), 2);
        Assert.True(report.Has("PDFUA2-STRUCT-R-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-STRUCT-R-TYPE"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    public static IEnumerable<object[]> NamespaceIndirectCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var value in new[] { "null", "99 0 R", "9 0 R",
            "<< /Type /Namespace /NS (http://iso.org/pdf/ssn) >>",
            "<< /Type /Namespace /NS (urn:indirect) /RoleMapNS << /P [/P 9 0 R] >> >>" })
            yield return [part, value];
    }

    [Theory]
    [MemberData(nameof(NamespaceIndirectCases))]
    public void Ua2ElementNamespaceDictionaryMustBeIndirect(int part, string value)
    {
        var doc = PdfDocument.Load(Ua("", "8 0 R", extras:
            ["<< /Type /StructElem /S /P /P 7 0 R /K [] >>",
             "<< /Type /Namespace /NS (http://iso.org/pdf/ssn) >>"]));
        Assert.IsType<PdfDictionary>(doc.File.GetObject(8)).Entries["NS"] =
            new PdfLexer(Encoding.ASCII.GetBytes(value)).ReadObject()!;
        var report = PdfValidator.ValidatePdfUA(doc, part);
        Assert.Equal(part == 2 && value.StartsWith("<<", StringComparison.Ordinal),
            report.Has("PDFUA2-NS-INDIRECT"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(1, "null")]
    [InlineData(1, "[]")]
    [InlineData(2, "null")]
    [InlineData(2, "[]")]
    [InlineData(2, "[9 0 R]")]
    [InlineData(2, "[10 0 R]")]
    [InlineData(2, "[9 0 R 10 0 R]")]
    public void Ua2NamespaceRegistrationUsesDictionaryIdentity(int part, string registry)
    {
        var doc = PdfDocument.Load(Ua("", "8 0 R", extras:
            ["<< /Type /StructElem /S /P /P 7 0 R /K [] /NS 9 0 R >>",
             "<< /Type /Namespace /NS (http://iso.org/pdf/ssn) >>",
             "<< /Type /Namespace /NS (http://iso.org/pdf/ssn) >>"]));
        Assert.IsType<PdfDictionary>(doc.File.GetObject(5)).Entries["Namespaces"] =
            new PdfLexer(Encoding.ASCII.GetBytes(registry)).ReadObject()!;
        var report = PdfValidator.ValidatePdfUA(doc, part);
        Assert.Equal(part == 2 && !registry.Contains("9 0 R", StringComparison.Ordinal),
            report.Has("PDFUA2-NS-UNREGISTERED"));
        Assert.False(report.Has("PDFUA2-NS-REGISTRY-INCOMPLETE"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData("11 0 R")]
    [InlineData("[11 0 R]")]
    public void Ua2IncompleteNamespaceRegistryDoesNotGuessMissingMembership(string registry)
    {
        var doc = PdfDocument.Load(Ua("", "8 0 R", extras:
            ["<< /Type /StructElem /S /P /P 7 0 R /K [] /NS 9 0 R >>",
             "<< /Type /Namespace /NS (http://iso.org/pdf/ssn) >>", "null", "11 0 R"]));
        Assert.IsType<PdfDictionary>(doc.File.GetObject(5)).Entries["Namespaces"] =
            new PdfLexer(Encoding.ASCII.GetBytes(registry)).ReadObject()!;
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.True(report.Has("PDFUA2-NS-REGISTRY-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-NS-UNREGISTERED"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(200000, false)]
    [InlineData(200001, true)]
    public void Ua2NamespaceRegistryInspectionIsBounded(int count, bool incomplete)
    {
        var doc = PdfDocument.Load(Ua("", "8 0 R", extras:
            ["<< /Type /StructElem /S /P /P 7 0 R /K [] /NS 9 0 R >>",
             "<< /Type /Namespace /NS (http://iso.org/pdf/ssn) >>"]));
        Assert.IsType<PdfDictionary>(doc.File.GetObject(5)).Entries["Namespaces"] =
            new PdfArray(Enumerable.Repeat<PdfObject>(new PdfRef(9, 0), count).ToList());
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.Equal(incomplete, report.Has("PDFUA2-NS-REGISTRY-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-NS-UNREGISTERED"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData("/Bad")]
    [InlineData("[/Bad]")]
    public void Ua2MalformedNamespaceRegistryIsExplicitlyIncomplete(string registry)
    {
        var doc = PdfDocument.Load(Ua("", "8 0 R", extras:
            ["<< /Type /StructElem /S /P /P 7 0 R /K [] /NS 9 0 R >>",
             "<< /Type /Namespace /NS (http://iso.org/pdf/ssn) >>"]));
        Assert.IsType<PdfDictionary>(doc.File.GetObject(5)).Entries["Namespaces"] =
            new PdfLexer(Encoding.ASCII.GetBytes(registry)).ReadObject()!;
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.True(report.Has("PDFUA2-NS-REGISTRY-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-NS-UNREGISTERED"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(32, false)]
    [InlineData(33, true)]
    public void Ua2NamespaceRegistryReferenceDepthIsBounded(int count, bool incomplete)
    {
        var chain = Enumerable.Range(10, count).Select(n => n == 9 + count ? "[9 0 R]" : $"{n + 1} 0 R");
        var doc = PdfDocument.Load(Ua("", "8 0 R", extras:
            new[] { "<< /Type /StructElem /S /P /P 7 0 R /K [] /NS 9 0 R >>",
                "<< /Type /Namespace /NS (http://iso.org/pdf/ssn) >>" }.Concat(chain).ToArray()));
        Assert.IsType<PdfDictionary>(doc.File.GetObject(5)).Entries["Namespaces"] = new PdfRef(10, 0);
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.Equal(incomplete, report.Has("PDFUA2-NS-REGISTRY-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-NS-UNREGISTERED"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Fact]
    public void Ua2NamespaceRegistrationIsOptionalWhenNoElementReferencesANamespace()
    {
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(Ua("", "8 0 R", extras:
            ["<< /Type /StructElem /S /P /P 7 0 R /K [] >>"])), 2);
        Assert.False(report.Has("PDFUA2-NS-UNREGISTERED"));
        Assert.False(report.Has("PDFUA2-NS-REGISTRY-INCOMPLETE"));
    }

    public static IEnumerable<object[]> StructureReferenceShapeCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var role in new[] { "P", "TOCI" })
        foreach (var reference in new[] { "", "/Ref null", "/Ref []", "/Ref 42", "/Ref << >>",
            "/Ref [null]", "/Ref [99 0 R]", "/Ref [42]", "/Ref [(id)]" })
            yield return [part, role, reference];
    }

    [Theory]
    [MemberData(nameof(StructureReferenceShapeCases))]
    public void Ua2StructureRefRequiresArrayWithoutNullElements(int part, string role, string reference)
    {
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(Ua("", "8 0 R", extras:
            [$"<< /Type /StructElem /S /{role} /P 7 0 R /K [] {reference} >>"])), part);
        Assert.Equal(part == 2 && reference is "/Ref 42" or "/Ref << >>", report.Has("PDFUA2-STRUCT-REF-TYPE"));
        Assert.Equal(part == 2 && reference is "/Ref [null]" or "/Ref [99 0 R]", report.Has("PDFUA2-STRUCT-REF-NULL"));
        Assert.False(report.Has("PDFUA2-STRUCT-REF-INCOMPLETE"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData("9 0 R")]
    [InlineData("[9 0 R]")]
    public void Ua2StructureRefCycleIsIncompleteNotGuessedNull(string reference)
    {
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(Ua("", "8 0 R", extras:
            [$"<< /Type /StructElem /S /P /P 7 0 R /K [] /Ref {reference} >>", "9 0 R"])), 2);
        Assert.True(report.Has("PDFUA2-STRUCT-REF-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-STRUCT-REF-TYPE"));
        Assert.False(report.Has("PDFUA2-STRUCT-REF-NULL"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(200000, false)]
    [InlineData(200001, true)]
    public void Ua2StructureRefArrayWorkIsBounded(int count, bool incomplete)
    {
        var doc = PdfDocument.Load(Ua("", "8 0 R", extras:
            ["<< /Type /StructElem /S /P /P 7 0 R /K [] >>"]));
        Assert.IsType<PdfDictionary>(doc.File.GetObject(8)).Entries["Ref"] =
            new PdfArray(Enumerable.Repeat<PdfObject>(new PdfInteger(42), count).ToList());
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.Equal(incomplete, report.Has("PDFUA2-STRUCT-REF-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-STRUCT-REF-TYPE"));
        Assert.False(report.Has("PDFUA2-STRUCT-REF-NULL"));
    }

    public static IEnumerable<object[]> TociReferenceCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var mapped in new[] { false, true })
        foreach (var descendant in new[] { false, true })
        foreach (var reference in new[] { "", "/Ref null", "/Ref []", "/Ref [10 0 R]", "/Ref 42", "/Ref [99 0 R]", "/Ref [null]" })
            yield return [part, mapped, descendant, reference];
    }

    [Theory]
    [MemberData(nameof(TociReferenceCases))]
    public void Ua2TociRequiresReferencePresenceOnSelfOrDescendant(int part, bool mapped, bool descendant, string reference)
    {
        var role = mapped ? "Entry" : "TOCI";
        var pdf = Ua("", "8 0 R", extras:
        [
            $"<< /Type /StructElem /S /{role} /P 7 0 R /NS {(mapped ? 12 : 11)} 0 R /K [9 0 R] {(descendant ? "" : reference)} >>",
            $"<< /Type /StructElem /S /P /P 8 0 R /K [] {(descendant ? reference : "")} >>",
            "<< /Type /StructElem /S /P /P 7 0 R /K [] >>",
            "<< /Type /Namespace /NS (http://iso.org/pdf/ssn) >>",
            "<< /Type /Namespace /NS (urn:toci-test) /RoleMapNS << /Entry [/TOCI 11 0 R] >> >>",
        ]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), part);
        Assert.Equal(part == 2 && reference is "" or "/Ref null", report.Has("PDFUA2-TOCI-REF"));
        Assert.False(report.Has("PDFUA2-TOCI-REF-INCOMPLETE"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Fact]
    public void Ua2TociCyclicReferenceIsIncomplete()
    {
        var pdf = Ua("", "8 0 R", extras:
            ["<< /Type /StructElem /S /TOCI /P 7 0 R /K [] /Ref 9 0 R >>", "9 0 R"]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.True(report.Has("PDFUA2-TOCI-REF-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-TOCI-REF"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(32, false)]
    [InlineData(33, true)]
    public void Ua2TociReferenceResolutionBoundaryIsExplicit(int length, bool incomplete)
    {
        var chain = Enumerable.Range(10, length).Select(n => n == 9 + length ? "[]" : $"{n + 1} 0 R");
        var pdf = Ua("", "8 0 R", extras:
            new[] { "<< /Type /StructElem /S /TOCI /P 7 0 R /K [] /Ref 10 0 R >>", "null" }.Concat(chain).ToArray());
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.Equal(incomplete, report.Has("PDFUA2-TOCI-REF-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-TOCI-REF"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    public static IEnumerable<object[]> CaptionNamespaceCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var parent in new[] { "Figure", "Table", "L", "TOC" })
        foreach (var mapping in new[] { "direct", "mapped", "foreign-to-p" })
        foreach (var position in new[] { 0, 1, 2 })
            yield return [part, parent, mapping, position];
    }

    [Theory]
    [MemberData(nameof(CaptionNamespaceCases))]
    public void Ua2CaptionOrderUsesResolvedRolesForEveryParent(int part, string parent, string mapping, int position)
    {
        var role = mapping == "mapped" ? "Custom" : "Caption";
        var ns = mapping == "direct" ? 12 : 13;
        var children = Enumerable.Range(0, 3).Select(i =>
            $"<< /Type /StructElem /S /{(i == position ? role : "P")} /NS {(i == position ? ns : 12)} 0 R /P 8 0 R /K [] >>").ToArray();
        var pdf = Ua("", "8 0 R", extras:
        [
            $"<< /Type /StructElem /S /{parent} /NS 12 0 R /P 7 0 R /K [9 0 R 10 0 R 11 0 R] /Alt (illustration) >>",
            .. children,
            "<< /Type /Namespace /NS (http://iso.org/pdf2/ssn) >>",
            $"<< /Type /Namespace /NS (urn:caption-test) /RoleMapNS << /{role} [/{(mapping == "mapped" ? "Caption" : "P")} 12 0 R] >> >>",
        ]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), part);
        Assert.Equal(part == 2 && mapping != "foreign-to-p" && position == 1, report.Has("PDFUA2-CAPTION-POSITION"));
        if (part == 2)
            Assert.DoesNotContain(report.Findings, f => f.RuleId is "PDFUA-LIST-CAPTION" or "PDFUA-TABLE-CAPTION" or "PDFUA-TOC-CAPTION");
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData("pdfdoc", false)]
    [InlineData("utf16", false)]
    [InlineData("utf8-bom", false)]
    [InlineData("utf8", true)]
    public void RichXmlStringUsesPdfTextEncodingBeforeParsing(string encoding, bool mismatch)
    {
        const string xml = "<body xmlns='http://www.w3.org/1999/xhtml'><p>caf\u00e9</p></body>";
        byte[] bytes = encoding switch
        {
            "pdfdoc" => Encoding.Latin1.GetBytes(xml),
            "utf16" => Encoding.BigEndianUnicode.GetBytes("\uFEFF" + xml),
            "utf8-bom" => Encoding.UTF8.GetBytes("\uFEFF" + xml),
            _ => Encoding.UTF8.GetBytes(xml),
        };
        var pdf = Ua("", "8 0 R", catalogExtra: "/AcroForm << /Fields [9 0 R] >>", extras:
            [Para, $"<< /FT /Tx /T (field) /RV <{Convert.ToHexString(bytes)}> /V <FEFF00630061006600E9> >>"]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.Equal(mismatch, report.Has("PDFUA2-FORM-RV-V"));
        Assert.False(report.Has("PDFUA2-FORM-VALUE-INCOMPLETE"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    public static IEnumerable<object[]> RichFieldPdfDocEncodingCases()
    {
        int[] accents = [0x02D8, 0x02C7, 0x02C6, 0x02D9, 0x02DD, 0x02DB, 0x02DA, 0x02DC];
        int[] symbols = [0x2022, 0x2020, 0x2021, 0x2026, 0x2014, 0x2013, 0x0192, 0x2044,
            0x2039, 0x203A, 0x2212, 0x2030, 0x201E, 0x201C, 0x201D, 0x2018, 0x2019,
            0x201A, 0x2122, 0xFB01, 0xFB02, 0x0141, 0x0152, 0x0160, 0x0178, 0x017D,
            0x0131, 0x0142, 0x0153, 0x0161, 0x017E];
        foreach (var part in new[] { 1, 2 })
        foreach (var mismatch in new[] { false, true })
        {
            for (int i = 0; i < accents.Length; i++) yield return [part, i + 0x18, accents[i], mismatch];
            for (int i = 0; i < symbols.Length; i++) yield return [part, i + 0x80, symbols[i], mismatch];
            yield return [part, 0xA0, 0x20AC, mismatch];
        }
    }

    [Theory]
    [MemberData(nameof(RichFieldPdfDocEncodingCases))]
    public void RichFieldEqualityUsesPdfDocEncoding(int part, int value, int codepoint, bool mismatch)
    {
        var xml = $"<body xmlns='http://www.w3.org/1999/xhtml'><p>&#x{(mismatch ? 0x58 : codepoint):X};</p></body>";
        var pdf = Ua("", "8 0 R", catalogExtra: "/AcroForm << /Fields [9 0 R] >>", extras:
            [Para, $"<< /FT /Tx /T (field) /RV ({xml}) /V <{value:X2}> >>"]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), part);
        Assert.Equal(part == 2 && mismatch, report.Has("PDFUA2-FORM-RV-V"));
        Assert.False(report.Has("PDFUA2-FORM-VALUE-INCOMPLETE"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData("00")]
    [InlineData("7F")]
    [InlineData("9F")]
    [InlineData("AD")]
    [InlineData("FEFF00")]
    [InlineData("FEFFD800")]
    [InlineData("EFBBBF80")]
    public void RichFieldEqualityInvalidPlainEncodingIsIncomplete(string hex)
    {
        var xml = "<body xmlns='http://www.w3.org/1999/xhtml'><p>&#xFFFD;</p></body>";
        var pdf = Ua("", "8 0 R", catalogExtra: "/AcroForm << /Fields [9 0 R] >>", extras:
            [Para, $"<< /FT /Tx /T (field) /RV ({xml}) /V <{hex}> >>"]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.Single(report.Findings, f => f.RuleId == "PDFUA2-FORM-VALUE-INCOMPLETE");
        Assert.False(report.Has("PDFUA2-FORM-RV-V"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    public static IEnumerable<object[]> RichFieldValuePresenceCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var type in new[] { "Tx", "Ch" })
        foreach (var rich in new[] { "", "/RV null", "/RV 11 0 R", "/RV 99 0 R", "/RV (rich)", "/RV 12 0 R" })
        foreach (var value in new[] { "", "/V null", "/V 11 0 R", "/V 99 0 R", "/V ()", "/V (plain)", "/V 13 0 R" })
            yield return [part, type, rich, value];
    }

    [Theory]
    [MemberData(nameof(RichFieldValuePresenceCases))]
    public void Ua2RichTextFieldRequiresPlainValuePresence(int part, string type, string rich, string value)
    {
        var xml = $"<body xmlns='http://www.w3.org/1999/xhtml'><p>{(value == "/V ()" ? "" : "plain")}</p></body>";
        var pdf = Ua("", "8 0 R", catalogExtra: "/AcroForm << /Fields [9 0 R] >>", extras:
        [
            Para, $"<< /FT /{type} /T (field) {rich.Replace("/RV (rich)", $"/RV ({xml})")} {value} >>", "null", "null",
            ObjPdf.Stream(xml),
            ObjPdf.Stream("plain"),
        ]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), part);
        var missing = part == 2 && type == "Tx" && rich is "/RV (rich)" or "/RV 12 0 R"
            && value is not ("/V ()" or "/V (plain)" or "/V 13 0 R");
        Assert.Equal(missing, report.Has("PDFUA2-FORM-RV-V"));
        Assert.DoesNotContain(report.Findings, f => f.RuleId is "PDFUA2-FORM-VALUE-INCOMPLETE" or "VALIDATION-INTERNAL");
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("/V null", true)]
    [InlineData("/V 12 0 R", true)]
    [InlineData("/V (inherited)", false)]
    public void Ua2RichFieldNullLocalValueDoesNotShadowInheritedValue(string parentValue, bool missing)
    {
        var pdf = Ua("", "8 0 R", catalogExtra: "/AcroForm << /Fields [9 0 R] >>", extras:
        [
            Para, $"<< /FT /Tx /T (parent) {parentValue} /Kids [10 0 R] >>",
            "<< /T (child) /Parent 9 0 R /RV (<body xmlns='http://www.w3.org/1999/xhtml'><p>inherited</p></body>) /V null >>", "null", "null",
        ]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.Equal(missing, report.Has("PDFUA2-FORM-RV-V"));
        Assert.False(report.Has("PDFUA2-FORM-VALUE-INCOMPLETE"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData("RV")]
    [InlineData("V")]
    public void Ua2RichFieldCyclicValueReferenceIsIncomplete(string key)
    {
        var entry = key == "RV" ? "/RV 11 0 R" : "/RV (rich) /V 11 0 R";
        var pdf = Ua("", "8 0 R", catalogExtra: "/AcroForm << /Fields [9 0 R] >>",
            extras: [Para, $"<< /FT /Tx /T (field) {entry} >>", "null", "11 0 R"]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.Single(report.Findings, f => f.RuleId == "PDFUA2-FORM-VALUE-INCOMPLETE" && f.Severity == ValidationSeverity.Error);
        Assert.False(report.Has("PDFUA2-FORM-RV-V"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData("RV", 32, false)]
    [InlineData("RV", 33, true)]
    [InlineData("V", 32, false)]
    [InlineData("V", 33, true)]
    public void Ua2RichFieldReferenceBoundaryIsExplicit(string key, int length, bool incomplete)
    {
        const string rich = "(<body xmlns='http://www.w3.org/1999/xhtml'><p>plain</p></body>)";
        var entries = key == "RV" ? "/RV 14 0 R /V (plain)" : $"/RV {rich} /V 14 0 R";
        var chain = Enumerable.Range(14, length).Select(n => n == 13 + length ? key == "RV" ? rich : "(plain)" : $"{n + 1} 0 R");
        var pdf = Ua("", "8 0 R", catalogExtra: "/AcroForm << /Fields [9 0 R] >>",
            extras: new[] { Para, $"<< /FT /Tx /T (field) {entries} >>", "null", "null", "null", "null" }.Concat(chain).ToArray());
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.Equal(incomplete, report.Has("PDFUA2-FORM-VALUE-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-FORM-RV-V"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(64, false)]
    [InlineData(65, true)]
    public void Ua2RichFieldValueInheritanceDepthBoundaryIsExplicit(int depth, bool incomplete)
    {
        var chain = Enumerable.Range(14, depth).Select(n => n == 13 + depth
            ? "<< /V (plain) >>" : $"<< /Parent {n + 1} 0 R >>");
        var pdf = Ua("", "8 0 R", catalogExtra: "/AcroForm << /Fields [9 0 R] >>",
            extras: new[] { Para, "<< /FT /Tx /T (field) /RV (<body xmlns='http://www.w3.org/1999/xhtml'><p>plain</p></body>) /Parent 14 0 R >>", "null", "null", "null", "null" }.Concat(chain).ToArray());
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.Equal(incomplete, report.Has("PDFUA2-FORM-VALUE-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-FORM-RV-V"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData("/RV 42 /V (plain)")]
    [InlineData("/RV (rich) /V 42")]
    public void Ua2RichFieldMalformedValueTypesAreIncomplete(string entries)
    {
        var pdf = Ua("", "8 0 R", catalogExtra: "/AcroForm << /Fields [9 0 R] >>",
            extras: [Para, $"<< /FT /Tx /T (field) {entries} >>"]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.True(report.Has("PDFUA2-FORM-VALUE-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-FORM-RV-V"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(64, false)]
    [InlineData(65, true)]
    public void Ua2RichFieldReaderDepthTruncationIsExplicit(int depth, bool incomplete)
    {
        var chain = Enumerable.Range(14, depth + 1).Select(n => n == 14 + depth
            ? "<< /T (field) /RV (<body xmlns='http://www.w3.org/1999/xhtml'><p>plain</p></body>) /V (plain) >>"
            : $"<< /FT /Tx /T (group) /Kids [{n + 1} 0 R] >>");
        var pdf = Ua("", "8 0 R", catalogExtra: "/AcroForm << /Fields [14 0 R] >>",
            extras: new[] { Para, "null", "null", "null", "null", "null" }.Concat(chain).ToArray());
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.Equal(incomplete, report.Has("PDFUA2-FORM-VALUE-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-FORM-RV-V"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(100000, false)]
    [InlineData(100001, true)]
    public void Ua2RichFieldReaderWorkTruncationIsExplicit(int fields, bool incomplete)
    {
        var entries = string.Concat(Enumerable.Repeat("9 0 R ", fields));
        var pdf = Ua("", "8 0 R", catalogExtra: $"/AcroForm << /Fields [{entries}] >>",
            extras: [Para, "<< /FT /Tx /T (field) /RV (<body xmlns='http://www.w3.org/1999/xhtml'><p>plain</p></body>) /V (plain) >>"]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.Equal(incomplete, report.Has("PDFUA2-FORM-VALUE-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-FORM-RV-V"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    public static IEnumerable<object[]> AttachmentRelationshipCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var direct in new[] { false, true })
        foreach (var relationship in new[] { "", "/AFRelationship null", "/AFRelationship 11 0 R",
            "/AFRelationship 99 0 R", "/AFRelationship /Data", "/AFRelationship 12 0 R" })
            yield return [part, direct, relationship];
    }

    [Theory]
    [MemberData(nameof(AttachmentRelationshipCases))]
    public void Ua2AttachmentRelationshipNullIsEquivalentToOmission(int part, bool direct, string relationship)
    {
        var spec = $"<< /Type /Filespec /F (file) /UF (file) {relationship} >>";
        var pdf = Ua("", "8 0 R", "/Annots [9 0 R]", extras:
        [
            Para, $"<< /Type /Annot /Subtype /FileAttachment /FS {(direct ? spec : "10 0 R")} /Rect [0 0 10 10] /Contents (file) >>",
            spec, "null", "/Data",
        ]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), part);
        Assert.Equal(part == 2 && relationship is not ("/AFRelationship /Data" or "/AFRelationship 12 0 R"),
            report.Has("PDFUA2-FILEATTACH-AFRELATIONSHIP"));
        Assert.DoesNotContain(report.Findings, f => f.RuleId is "PDFUA2-FILEATTACH-INCOMPLETE" or "VALIDATION-INTERNAL");
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void Ua2AttachmentRelationshipCyclicReferencesAreIncomplete(int part, bool fileSpecification)
    {
        var pdf = Ua("", "8 0 R", "/Annots [9 0 R]", extras:
        [
            Para, $"<< /Type /Annot /Subtype /FileAttachment /FS {(fileSpecification ? "11 0 R" : "10 0 R")} /Rect [0 0 10 10] /Contents (file) >>",
            "<< /Type /Filespec /F (file) /UF (file) /AFRelationship 11 0 R >>", "11 0 R",
        ]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), part);
        Assert.Equal(part == 2, report.Has("PDFUA2-FILEATTACH-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-FILEATTACH-AFRELATIONSHIP"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(1, 32, false)]
    [InlineData(1, 33, false)]
    [InlineData(2, 32, false)]
    [InlineData(2, 33, true)]
    public void Ua2AttachmentRelationshipReferenceBoundaryIsExplicit(int part, int length, bool incomplete)
    {
        var nodes = Enumerable.Range(13, length).Select(n => n == 12 + length ? "/Data" : $"{n + 1} 0 R");
        var pdf = Ua("", "8 0 R", "/Annots [9 0 R]", extras:
            new[] { Para, "<< /Type /Annot /Subtype /FileAttachment /FS 10 0 R /Rect [0 0 10 10] /Contents (file) >>",
                "<< /Type /Filespec /F (file) /UF (file) /AFRelationship 13 0 R >>", "null", "null" }.Concat(nodes).ToArray());
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), part);
        Assert.Equal(incomplete, report.Has("PDFUA2-FILEATTACH-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-FILEATTACH-AFRELATIONSHIP"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    public static IEnumerable<object[]> FileDescriptionCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var location in new[] { "orphan", "associated", "name-tree", "nested" })
        foreach (var ef in new[] { "", "/EF null", "/EF 11 0 R", "/EF 99 0 R",
            "/EF << /F 10 0 R >>", "/EF 12 0 R" })
        foreach (var description in new[] { "", "/Desc (description)" })
            yield return [part, location, ef, description];
    }

    [Theory]
    [InlineData(1, 32, false)]
    [InlineData(1, 33, true)]
    [InlineData(2, 32, false)]
    [InlineData(2, 33, true)]
    public void UaFileSpecificationReferenceBoundaryDoesNotBecomeNull(int part, int length, bool incomplete)
    {
        var chain = Enumerable.Range(11, length).Select(n => n == 10 + length ? "null" : $"{n + 1} 0 R");
        var pdf = Ua("", "8 0 R", extras:
            new[] { Para, "<< /Type /Filespec /EF 11 0 R >>", "null" }.Concat(chain).ToArray());
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), part);
        Assert.Equal(incomplete, report.Has("PDFUA-FILESPEC-INCOMPLETE"));
        Assert.False(report.Has("PDFUA-FILESPEC-UF"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void UaFileSpecificationCyclicEmbeddedFileEntryIsIncomplete(int part)
    {
        var pdf = Ua("", "8 0 R", extras: [Para, "<< /Type /Filespec /EF 10 0 R >>", "10 0 R"]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), part);
        Assert.Single(report.Findings, f => f.RuleId == "PDFUA-FILESPEC-INCOMPLETE" && f.Severity == ValidationSeverity.Error);
        Assert.False(report.Has("PDFUA-FILESPEC-UF"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [MemberData(nameof(FileDescriptionCases))]
    public void Ua2FileDescriptionsFollowEmbeddedFilesNameTree(int part, string location, string ef, string description)
    {
        var catalog = location switch
        {
            "associated" => "/AF [9 0 R]",
            "name-tree" => "/Names << /EmbeddedFiles << /Names [(file) 9 0 R] >> >>",
            "nested" => "/Names << /EmbeddedFiles << /Kids [13 0 R] >> >>",
            _ => "",
        };
        var pdf = Ua("", "8 0 R", catalogExtra: catalog, extras:
        [
            Para, $"<< /Type /Filespec /F (file) /UF (file) {ef} {description} >>",
            ObjPdf.Stream("x", "/Type /EmbeddedFile"), "null", "<< /F 10 0 R >>",
            "<< /Limits [(file) (file)] /Names [(file) 9 0 R] >>",
        ]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), part);
        Assert.Equal(part == 2 && location is "name-tree" or "nested" && description == "",
            report.Has("PDFUA2-FILESPEC-DESC"));
        Assert.DoesNotContain(report.Findings, f => f.RuleId is "PDFUA2-FILES-INCOMPLETE" or "VALIDATION-INTERNAL");
    }

    [Theory]
    [InlineData("/Names << /EmbeddedFiles 13 0 R >>", "<< /Kids [13 0 R 14 0 R] >>")]
    [InlineData("/Names << /EmbeddedFiles 13 0 R >>", "<< /Names [(file) 9 0 R (orphan)] >>")]
    public void Ua2FileDescriptionInspectionReportsIncompleteAndRetainsKnownMissingDescription(string catalog, string tree)
    {
        var pdf = Ua("", "8 0 R", catalogExtra: catalog, extras:
        [
            Para, "<< /Type /Filespec /F (file) /UF (file) >>",
            "null", "null", "null", tree, "<< /Names [(file) 9 0 R] >>",
        ]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.Single(report.Findings, f => f.RuleId == "PDFUA2-FILESPEC-DESC");
        Assert.Single(report.Findings, f => f.RuleId == "PDFUA2-FILES-INCOMPLETE" && f.Severity == ValidationSeverity.Error);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(64, false)]
    [InlineData(65, true)]
    public void Ua2FileDescriptionTreeDepthBoundaryIsExplicit(int depth, bool incomplete)
    {
        var nodes = Enumerable.Range(13, depth + 1).Select(n => n == 13 + depth
            ? "<< /Names [(file) 9 0 R] >>" : $"<< /Kids [{n + 1} 0 R] >>");
        var pdf = Ua("", "8 0 R", catalogExtra: "/Names << /EmbeddedFiles 13 0 R >>",
            extras: new[] { Para, "<< /Type /Filespec /F (file) /UF (file) >>", "null", "null", "null" }.Concat(nodes).ToArray());
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.Equal(incomplete, report.Has("PDFUA2-FILES-INCOMPLETE"));
        Assert.Equal(!incomplete, report.Has("PDFUA2-FILESPEC-DESC"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData("/Names 13 0 R")]
    [InlineData("/Names << /EmbeddedFiles 13 0 R >>")]
    [InlineData("/Names << /EmbeddedFiles << /Names 13 0 R >> >>")]
    [InlineData("/Names << /EmbeddedFiles << /Names [(file) 13 0 R] >> >>")]
    [InlineData("/Names << /EmbeddedFiles << /Names [13 0 R 9 0 R] >> >>")]
    [InlineData("/Names << /EmbeddedFiles << /Kids 13 0 R >> >>")]
    public void Ua2FileDescriptionUnresolvedTreeReferencesAreIncomplete(string catalog)
    {
        var pdf = Ua("", "8 0 R", catalogExtra: catalog, extras:
            [Para, "<< /Type /Filespec /F (file) /UF (file) /Desc (description) >>", "null", "null", "null", "13 0 R"]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.Single(report.Findings, f => f.RuleId == "PDFUA2-FILES-INCOMPLETE");
        Assert.False(report.Has("PDFUA2-FILESPEC-DESC"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(32, false)]
    [InlineData(33, true)]
    public void Ua2FileDescriptionReferenceDepthBoundaryIsExplicit(int length, bool incomplete)
    {
        var nodes = Enumerable.Range(13, length).Select(n => n == 12 + length
            ? "<< /Names [(file) 9 0 R] >>" : $"{n + 1} 0 R");
        var pdf = Ua("", "8 0 R", catalogExtra: "/Names << /EmbeddedFiles 13 0 R >>",
            extras: new[] { Para, "<< /Type /Filespec /F (file) /UF (file) >>", "null", "null", "null" }.Concat(nodes).ToArray());
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.Equal(incomplete, report.Has("PDFUA2-FILES-INCOMPLETE"));
        Assert.Equal(!incomplete, report.Has("PDFUA2-FILESPEC-DESC"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(199999, false)]
    [InlineData(200000, true)]
    public void Ua2FileDescriptionTreeWorkBoundaryIsExplicit(int pairs, bool incomplete)
    {
        var entries = string.Concat(Enumerable.Repeat("(file) 9 0 R ", pairs));
        var pdf = Ua("", "8 0 R", catalogExtra: $"/Names << /EmbeddedFiles << /Names [{entries}] >> >>",
            extras: [Para, "<< /Type /Filespec /F (file) /UF (file) /Desc (description) >>"]);
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);
        Assert.Equal(incomplete, report.Has("PDFUA2-FILES-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-FILESPEC-DESC"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
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
