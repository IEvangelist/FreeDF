using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfAWidgetAppearanceTests
{
    public static IEnumerable<object[]> Widgets()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var (local, parent, grandparent, button) in new (string?, string?, string?, bool)[]
            {
                (null, null, null, false), ("/Btn", null, null, true), ("/Tx", null, null, false),
                (null, "/Btn", null, true), (null, "/Tx", null, false), (null, null, "/Btn", true),
                ("/Tx", "/Btn", null, false), ("/Btn", "/Tx", null, true),
                ("null", "/Btn", null, true), ("1", "/Btn", null, false),
                ("8 0 R", null, null, true), ("9 0 R", "/Btn", null, true),
            })
                foreach (var (normal, stream, states, absent) in new (string, bool, bool, bool)[]
                {
                    ("7 0 R", true, false, false), ("<< >>", false, false, false),
                    ("<< /Off 7 0 R >>", false, true, false), ("<< /Off 1 >>", false, false, false),
                    ("<< /Off null >>", false, false, false), ("<< /Off [] >>", false, false, false),
                    ("<< /Off 7 0 R /On 1 >>", false, true, false), ("<< /Off 7 0 R /On null >>", false, true, false),
                    ("1", false, false, false), ("null", false, false, true),
                })
                    yield return [profile, local ?? "", parent ?? "", grandparent ?? "", normal,
                        !absent && !button && !stream, !absent && button && !states];
    }

    [Theory]
    [MemberData(nameof(Widgets))]
    public void WidgetAppearanceTypeUsesResolvedInheritedFieldType(
        PdfValidationProfile profile, string local, string parent, string grandparent, string normal,
        bool invalidStream, bool invalidButton)
    {
        var hasParent = parent.Length > 0 || grandparent.Length > 0;
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Annots [5 0 R] >>",
            ObjPdf.Stream(""),
            $"<< /Type /Annot /Subtype /Widget /Rect [10 10 20 20] /F 4 /AP 6 0 R {(local.Length > 0 ? "/FT " + local : "")} {(hasParent ? "/Parent 10 0 R" : "")} >>",
            $"<< /N {normal} >>",
            ObjPdf.Stream("", "/Type /XObject /Subtype /Form /BBox [0 0 10 10]"),
            "/Btn", "null",
            $"<< /Kids [5 0 R] {(parent.Length > 0 ? "/FT " + parent : "")} {(grandparent.Length > 0 ? "/Parent 11 0 R" : "")} >>",
            $"<< /Kids [10 0 R] /FT {(grandparent.Length > 0 ? grandparent : "/Tx")} >>"));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-ANNOT-APPEARANCE-INCOMPLETE"));
        Assert.Equal(invalidStream, report.Has("PDFA-ANNOT-APPEARANCE-STREAM"));
        Assert.Equal(invalidButton, report.Has("PDFA-ANNOT-APPEARANCE-BUTTON"));
        Assert.All(report.Findings.Where(f => f.RuleId is "PDFA-ANNOT-APPEARANCE-STREAM" or "PDFA-ANNOT-APPEARANCE-BUTTON"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(0, finding.PageIndex);
            Assert.Equal(profile switch
            {
                PdfValidationProfile.PdfA1b => "ISO 19005-1 6.5.3",
                PdfValidationProfile.PdfA2b => "ISO 19005-2 6.3.3",
                _ => "ISO 19005-3 6.3.3",
            }, finding.Clause);
        });
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void FieldTraversalBoundsAndCyclesAreExplicit(PdfValidationProfile profile)
    {
        foreach (var (depth, button, cycle, incomplete) in new[]
        {
            (64, true, false, false), (65, true, false, true),
            (64, false, false, false), (65, false, false, true), (1, false, true, true),
        })
        {
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Annots [5 0 R] >>",
                ObjPdf.Stream(""),
            };
            for (var n = 0; n < depth; n++)
                objects.Add($"<< {(n == 0 ? "/Subtype /Widget /F 4 /Rect [10 10 20 20] /AP << /N 100 0 R >>" : "")} "
                    + $"{(n < depth - 1 ? $"/Parent {n + 6} 0 R" : cycle ? "/Parent 5 0 R" : button ? "/FT /Btn" : "")} >>");
            while (objects.Count < 99) objects.Add("null");
            objects.Add(ObjPdf.Stream("", "/Type /XObject /Subtype /Form /BBox [0 0 10 10]"));
            var report = PdfValidator.Validate(PdfDocument.Load(ObjPdf.Build(objects.ToArray())), profile);
            Assert.False(report.Has("VALIDATION-INTERNAL"));
            Assert.Equal(incomplete, report.Has("PDFA-ANNOT-APPEARANCE-INCOMPLETE"));
            Assert.Equal(button && !incomplete, report.Has("PDFA-ANNOT-APPEARANCE-BUTTON"));
        }
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void MalformedParentIsIncompleteUnlessLocalTypeAlreadyResolves(PdfValidationProfile profile)
    {
        foreach (var parent in new[] { "1", "[]", "true", "(field)" })
            foreach (var localType in new[] { "", "/FT /Btn" })
            {
                var doc = PdfDocument.Load(ObjPdf.Build(
                    "<< /Type /Catalog /Pages 2 0 R >>",
                    "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                    "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Annots [5 0 R] >>",
                    ObjPdf.Stream(""),
                    $"<< /Subtype /Widget {localType} /Parent {parent} /F 4 /Rect [10 10 20 20] /AP << /N 6 0 R >> >>",
                    ObjPdf.Stream("", "/Type /XObject /Subtype /Form /BBox [0 0 10 10]")));
                var report = PdfValidator.Validate(doc, profile);
                Assert.False(report.Has("VALIDATION-INTERNAL"));
                Assert.Equal(localType.Length == 0, report.Has("PDFA-ANNOT-APPEARANCE-INCOMPLETE"));
                Assert.Equal(localType.Length > 0, report.Has("PDFA-ANNOT-APPEARANCE-BUTTON"));
                Assert.All(report.Findings.Where(f => f.RuleId == "PDFA-ANNOT-APPEARANCE-INCOMPLETE"), finding =>
                {
                    Assert.Equal(ValidationSeverity.Error, finding.Severity);
                    Assert.Equal(0, finding.PageIndex);
                });
            }
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void StateSearchBoundDoesNotSilentlyClaimMissingAppearances(PdfValidationProfile profile)
    {
        foreach (var (count, streamAt, incomplete, invalid) in new[]
        {
            (10000, -1, false, true), (10001, -1, true, false),
            (10001, 0, false, false), (10001, 10000, true, false),
        })
        {
            var states = string.Join(" ", Enumerable.Range(0, count).Select(i => $"/S{i} {(i == streamAt ? "6 0 R" : "null")}"));
            var doc = PdfDocument.Load(ObjPdf.Build(
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Annots [5 0 R] >>",
                ObjPdf.Stream(""),
                $"<< /Subtype /Widget /FT /Btn /F 4 /Rect [10 10 20 20] /AP << /N << {states} >> >> >>",
                ObjPdf.Stream("", "/Type /XObject /Subtype /Form /BBox [0 0 10 10]")));
            var report = PdfValidator.Validate(doc, profile);
            Assert.False(report.Has("VALIDATION-INTERNAL"));
            Assert.Equal(incomplete, report.Has("PDFA-ANNOT-APPEARANCE-INCOMPLETE"));
            Assert.Equal(invalid, report.Has("PDFA-ANNOT-APPEARANCE-BUTTON"));
        }
    }
}
