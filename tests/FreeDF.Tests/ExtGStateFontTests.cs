using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class ExtGStateFontTests
{
    public static IEnumerable<object[]> Uses()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b,
            PdfValidationProfile.PdfA3b, PdfValidationProfile.PdfUA1, PdfValidationProfile.PdfUA2 })
            foreach (var state in new[] { "unselected", "selected", "visible", "invisible", "clipping" })
                foreach (var embedded in new[] { false, true })
                    foreach (var map in new[] { "/Identity", "/Other", "null" })
                        yield return [profile, state, embedded, map];
    }

    [Theory]
    [MemberData(nameof(Uses))]
    public void MetadataChecksIncludeShownExtGStateFontsButNotUnusedStateDefinitions(
        PdfValidationProfile profile, string state, bool embedded, string map)
    {
        var doc = Document(embedded, map);
        var mode = state == "invisible" ? 3 : state == "clipping" ? 7 : 0;
        var shown = state is "visible" or "invisible" or "clipping";
        var content = (state == "unselected" ? "" : "/GS0 gs ")
            + "BT " + (shown ? $"{mode} Tr <0001> Tj " : "") + "ET";
        doc.Pages[0].Dict.Entries["Contents"] = new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(content));
        var report = PdfValidator.Validate(doc, profile);
        var included = shown && (profile != PdfValidationProfile.PdfA1b || mode != 3);
        var prefix = profile is PdfValidationProfile.PdfUA1 or PdfValidationProfile.PdfUA2 ? "PDFUA" : "PDFA";
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-USAGE-INCOMPLETE"));
        var requiresEmbedding = included && (prefix != "PDFUA" || mode != 3);
        Assert.Equal(requiresEmbedding && !embedded, report.Has(prefix + "-FONT-EMBEDDED"));
        Assert.Equal(included && embedded && map != "/Identity", report.Has(prefix + "-FONT-CIDTOGID"));
    }

    public static IEnumerable<object[]> StateUses()
    {
        foreach (var profile in Uses().Select(row => (PdfValidationProfile)row[0]).Distinct())
            foreach (var mode in new[] { 0, 3 })
                foreach (var surface in new[] { "restore", "form-inherited", "form-local" })
                    yield return [profile, mode, surface];
    }

    [Theory]
    [MemberData(nameof(StateUses))]
    public void DiscoveryFollowsRestoredAndInheritedFontState(PdfValidationProfile profile, int mode, string surface)
    {
        var doc = Document(true, "/Other");
        var resources = doc.Pages[0].Resources;
        var states = Assert.IsType<PdfDictionary>(resources.Get("ExtGState"));
        var state = Assert.IsType<PdfDictionary>(states.Get("GS0"));
        var originalFont = Assert.IsType<PdfDictionary>(doc.File.Resolve(Assert.IsType<PdfArray>(state.Get("Font"))[0]));
        var originalCid = Assert.IsType<PdfDictionary>(doc.File.Resolve(Assert.IsType<PdfArray>(originalFont.Get("DescendantFonts"))[0]));
        var font = new PdfDictionary();
        var cid = new PdfDictionary();
        foreach (var entry in originalFont.Entries) font.Entries.Add(entry.Key, entry.Value);
        foreach (var entry in originalCid.Entries) cid.Entries.Add(entry.Key, entry.Value);
        cid.Entries["CIDToGIDMap"] = new PdfName("Identity");
        font.Entries["DescendantFonts"] = new PdfArray([cid]);
        states.Entries["GS1"] = new PdfDictionary { Entries = { ["Font"] = new PdfArray([font, new PdfInteger(12)]) } };
        var content = surface == "restore"
            ? $"/GS0 gs BT {mode} Tr q /GS1 gs Q <0001> Tj ET"
            : surface == "form-inherited"
                ? $"/GS1 gs BT {mode} Tr ET /Fm Do /GS0 gs /Fm Do"
                : $"BT {mode} Tr ET /Fm Do";
        if (surface != "restore")
            resources.Entries["XObject"] = new PdfDictionary
            {
                Entries =
                {
                    ["Fm"] = new PdfStream(new PdfDictionary { Entries = { ["Subtype"] = new PdfName("Form") } },
                        Encoding.ASCII.GetBytes((surface == "form-local" ? "/GS0 gs " : "") + "BT <0001> Tj ET")),
                },
            };
        doc.Pages[0].Dict.Entries["Contents"] = new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(content));
        var report = PdfValidator.Validate(doc, profile);
        var prefix = profile is PdfValidationProfile.PdfUA1 or PdfValidationProfile.PdfUA2 ? "PDFUA" : "PDFA";
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(profile != PdfValidationProfile.PdfA1b || mode != 3, report.Has(prefix + "-FONT-CIDTOGID"));
    }

    static PdfDocument Document(bool embedded, string map)
    {
        var doc = PdfACidToGidTests.Document(embedded, "CIDFontType2", 0, map, "null");
        var page = doc.Pages[0];
        var fonts = Assert.IsType<PdfDictionary>(doc.File.Resolve(page.Resources.Get("Font")));
        var font = fonts.Get("F1")!;
        fonts.Entries.Clear();
        page.Resources.Entries["ExtGState"] = new PdfDictionary
        {
            Entries =
            {
                ["GS0"] = new PdfDictionary
                {
                    Entries = { ["Font"] = new PdfArray([font, new PdfInteger(12)]) },
                },
            },
        };
        return doc;
    }
}
