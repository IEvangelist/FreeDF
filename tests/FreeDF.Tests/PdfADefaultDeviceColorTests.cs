using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfADefaultDeviceColorTests
{
    static PdfDocument Load(string content, string resources, params string[] extras)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Resources << {resources} >> >>",
            ObjPdf.Stream(content),
        };
        objects.AddRange(extras);
        return PdfDocument.Load(ObjPdf.Build(objects.ToArray()));
    }

    static string Icc(string signature, int components)
    {
        var bytes = new char[132];
        bytes[3] = (char)bytes.Length;
        bytes[8] = (char)2;
        bytes[9] = (char)0x20;
        "mntr".CopyTo(0, bytes, 12, 4);
        signature.CopyTo(0, bytes, 16, 4);
        "XYZ ".CopyTo(0, bytes, 20, 4);
        "acsp".CopyTo(0, bytes, 36, 4);
        return ObjPdf.Stream(new string(bytes), $"/N {components}");
    }

    public static IEnumerable<object[]> Colors()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var space in new[] { "RGB", "CMYK" })
                yield return [profile, space, space == "RGB" ? "RGB " : "CMYK", space == "RGB" ? 3 : 4];
    }

    [Theory]
    [MemberData(nameof(Colors))]
    public void IccDefaultsCoverOperatorsNamedSpacesAndImageXObjects(PdfValidationProfile profile, string space, string signature, int components)
    {
        var resources = $"/ColorSpace << /Default{space} [/ICCBased 5 0 R] /Alias /Device{space} >> /XObject << /Im 6 0 R >>";
        var values = space == "RGB" ? "1 0 0" : "0 1 0 0";
        var op = space == "RGB" ? "rg" : "k";
        foreach (var content in new[] { $"{values} {op} 0 0 10 10 re f", $"{values} {op.ToUpperInvariant()} 0 0 10 10 re S",
            $"/Alias cs {values} sc 0 0 10 10 re f", $"/Device{space} cs {values} sc 0 0 10 10 re f", "/Im Do" })
        {
            var doc = Load(content, resources, Icc(signature, components),
                ObjPdf.Stream("abcd", $"/Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /Device{space}"));
            var report = PdfValidator.Validate(doc, profile);
            Assert.False(report.Has("VALIDATION-INTERNAL"));
            Assert.False(report.Has("PDFA-OUTPUTINTENT-MISSING"), $"{space}: {content}");
        }
    }

    [Theory]
    [MemberData(nameof(Colors))]
    public void InvalidOrWrongComponentDefaultsDoNotHideDeviceColor(PdfValidationProfile profile, string space, string signature, int components)
    {
        foreach (var replacement in new[] { "/Device" + space, "[/ICCBased]", "[/ICCBased 5 0 R /Extra]", "/Cycle", "[/ICCBased 6 0 R]" })
        {
            var resources = $"/ColorSpace << /Default{space} {replacement} /Cycle /Cycle >> /XObject << /Im 7 0 R >>";
            var doc = Load("/Im Do", resources, Icc(signature, components + 1), ObjPdf.Stream("not an ICC profile", $"/N {components}"),
                ObjPdf.Stream("abcd", $"/Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /Device{space}"));
            Assert.True(PdfValidator.Validate(doc, profile).Has("PDFA-OUTPUTINTENT-MISSING"), $"{space}: {replacement}");
        }

        var wrongSignature = Load("/Im Do", $"/ColorSpace << /Default{space} [/ICCBased 5 0 R] >> /XObject << /Im 6 0 R >>",
            Icc(space == "RGB" ? "CMYK" : "RGB ", components),
            ObjPdf.Stream("abcd", $"/Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /Device{space}"));
        Assert.True(PdfValidator.Validate(wrongSignature, profile).Has("PDFA-OUTPUTINTENT-MISSING"));

        var values = space == "RGB" ? "1 0 0" : "0 1 0 0";
        var selectedSpace = Load($"/Device{space} cs {values} sc 0 0 10 10 re f", "");
        Assert.True(PdfValidator.Validate(selectedSpace, profile).Has("PDFA-OUTPUTINTENT-MISSING"));
    }

    [Theory]
    [MemberData(nameof(Colors))]
    public void DefaultsResolveAliasesAndIndirectArrays(PdfValidationProfile profile, string space, string signature, int components)
    {
        foreach (var replacement in new[] { "/Cal", "7 0 R" })
        {
            var resources = $"/ColorSpace << /Default{space} {replacement} /Cal [/ICCBased 5 0 R] >> /XObject << /Im 6 0 R >>";
            var doc = Load("/Im Do", resources, Icc(signature, components),
                ObjPdf.Stream("abcd", $"/Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /Device{space}"),
                "[/ICCBased 5 0 R]");
            Assert.False(PdfValidator.Validate(doc, profile).Has("PDFA-OUTPUTINTENT-MISSING"));
        }
    }

    [Fact]
    public void SharedImageColorIsCheckedInEachPageResourceContext()
    {
        foreach (var mappedFirst in new[] { true, false })
        {
            const string mapped = "/ColorSpace << /DefaultRGB [/ICCBased 7 0 R] >>";
            var doc = PdfDocument.Load(ObjPdf.Build(
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] /Contents 5 0 R /Resources << {(mappedFirst ? mapped : "")} /XObject << /Im 8 0 R >> >> >>",
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] /Contents 6 0 R /Resources << {(mappedFirst ? "" : mapped)} /XObject << /Im 8 0 R >> >> >>",
                ObjPdf.Stream("/Im Do"), ObjPdf.Stream("/Im Do"), Icc("RGB ", 3),
                ObjPdf.Stream("abc", "/Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceRGB")));
            Assert.True(PdfValidator.Validate(doc, PdfValidationProfile.PdfA2b).Has("PDFA-OUTPUTINTENT-MISSING"));
        }
    }

    [Fact]
    public void CalRgbDefaultsValidateTheirParametersAndCannotReplaceCmyk()
    {
        const string white = "/WhitePoint [0.9505 1 1.089]";
        foreach (var parameters in new[] { white, white + " /Gamma [0.5 1 2.2]", white + " /Matrix [1 0 0 0 1 0 0 0 1]" })
        {
            var doc = Load("1 0 0 rg 0 0 10 10 re f", $"/ColorSpace << /DefaultRGB [/CalRGB << {parameters} >>] >>");
            Assert.False(PdfValidator.Validate(doc, PdfValidationProfile.PdfA2b).Has("PDFA-OUTPUTINTENT-MISSING"));
        }
        foreach (var parameters in new[] { "", "/WhitePoint [1 2 3]", white + " /Gamma [1 2]",
            white + " /Gamma [1 0 2]", white + " /Gamma [1 (1) 2]", white + " /Matrix [1 0 0]",
            white + " /Matrix [1 0 0 0 (1) 0 0 0 1]", white + " /BlackPoint [-1 0 0]" })
        {
            var doc = Load("1 0 0 rg 0 0 10 10 re f", $"/ColorSpace << /DefaultRGB [/CalRGB << {parameters} >>] >>");
            Assert.True(PdfValidator.Validate(doc, PdfValidationProfile.PdfA2b).Has("PDFA-OUTPUTINTENT-MISSING"), parameters);
        }
        var cmyk = Load("0 1 0 0 k 0 0 10 10 re f", $"/ColorSpace << /DefaultCMYK [/CalRGB << {white} >>] >>");
        Assert.True(PdfValidator.Validate(cmyk, PdfValidationProfile.PdfA2b).Has("PDFA-OUTPUTINTENT-MISSING"));
    }

    [Fact]
    public void FormDefaultsAreLocalAndDoNotCoverOtherResourcesOrDeviceGray()
    {
        const string rgb = "/ColorSpace << /DefaultRGB [/ICCBased 5 0 R] >>";
        const string image = "/Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceRGB";
        var mappedForm = ObjPdf.Stream("/Im Do", $"/Subtype /Form /BBox [0 0 10 10] /Resources << {rgb} /XObject << /Im 7 0 R >> >>");
        var doc = Load("/Fm Do", "/XObject << /Fm 6 0 R >>", Icc("RGB ", 3), mappedForm, ObjPdf.Stream("abc", image));
        Assert.False(PdfValidator.Validate(doc, PdfValidationProfile.PdfA2b).Has("PDFA-OUTPUTINTENT-MISSING"));

        doc = Load("/Fm Do 0 0 10 10 re f", "/XObject << /Fm 6 0 R >>", Icc("RGB ", 3), mappedForm, ObjPdf.Stream("abc", image));
        Assert.True(PdfValidator.Validate(doc, PdfValidationProfile.PdfA2b).Has("PDFA-OUTPUTINTENT-MISSING"));

        doc = Load("/Fm Do /Im Do", "/XObject << /Fm 6 0 R /Im 7 0 R >>", Icc("RGB ", 3), mappedForm, ObjPdf.Stream("abc", image));
        Assert.True(PdfValidator.Validate(doc, PdfValidationProfile.PdfA2b).Has("PDFA-OUTPUTINTENT-MISSING"));
    }
}
