using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class FormDataActionTests
{
    static byte[] Source(string action, string objects = "") => Encoding.ASCII.GetBytes(
        "%PDF-2.0\n" +
        "1 0 obj\n<< /Type /Catalog /Pages 2 0 R /OpenAction 4 0 R /AcroForm << /Fields [9 0 R] >> >>\nendobj\n" +
        "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
        "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>\nendobj\n" +
        $"4 0 obj\n<< {action} >>\nendobj\n" +
        "9 0 obj\n<< /FT /Tx /T (field) /V (unchanged) /DV (default) >>\nendobj\n" +
        objects + "trailer\n<< /Root 1 0 R /Size 20 >>\n%%EOF\n");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetFormRetainsFieldsAndFlagsWithoutResolvingElementsOrResettingValues(bool indirect)
    {
        var doc = PdfDocument.Load(Source(
            $"/S {(indirect ? "5 0 R" : "/ResetForm")} /Fields {(indirect ? "6 0 R" : "[(field) 9 0 R null /Bad]")} /Flags {(indirect ? "7 0 R" : "1")}",
            "5 0 obj\n/ResetForm\nendobj\n6 0 obj\n[(field) 9 0 R null /Bad]\nendobj\n7 0 obj\n1\nendobj\n"));
        var action = Assert.Single(doc.GetActions());
        var details = Assert.IsType<PdfResetFormAction>(action.ResetFormDetails);
        Assert.Null(action.ImportDataDetails);
        Assert.Same(action.Dictionary, details.Dictionary);
        Assert.Same(doc.File.Resolve(action.Dictionary.Get("Fields")), details.Fields);
        var fields = Assert.IsType<PdfArray>(details.Fields);
        Assert.Equal(4, fields.Count);
        Assert.Equal(new PdfRef(9, 0), Assert.IsType<PdfRef>(fields[1]));
        Assert.IsType<PdfName>(fields[3]);
        Assert.Equal(1L, details.Flags);
        Assert.Equal("unchanged", Assert.Single(doc.GetFormFields()).Value);
        if (indirect)
            foreach (var key in new[] { "S", "Fields", "Flags" }) Assert.IsType<PdfRef>(action.Dictionary.Get(key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/Fields null /Flags null")]
    [InlineData("/Fields (field) /Flags 0.0")]
    [InlineData("/Fields <<>> /Flags true")]
    [InlineData("/Fields /Bad /Flags (1)")]
    [InlineData("/Fields 19 0 R /Flags 19 0 R")]
    public void ResetFormPreservesMissingAndMalformedEntriesWithoutApplyingDefaults(string entries)
    {
        var action = Assert.Single(PdfDocument.Load(Source("/S /ResetForm " + entries)).GetActions());
        var before = action.Dictionary.Entries.ToArray();
        var details = Assert.IsType<PdfResetFormAction>(action.ResetFormDetails);
        Assert.Null(details.Fields);
        Assert.Null(details.Flags);
        Assert.Equal(before, details.Dictionary.Entries.ToArray());
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(2L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    public void ResetFormDoesNotMaskOrValidateIntegerFlagBits(long flags)
    {
        var action = Assert.Single(PdfDocument.Load(Source($"/S /ResetForm /Flags {flags}")).GetActions());
        Assert.Equal(flags, Assert.IsType<PdfResetFormAction>(action.ResetFormDetails).Flags);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportDataRetainsLocalFileSpecificationsWithoutDecodingEmbeddedData(bool indirect)
    {
        const string file = "<< /Type /Filespec /F <ff00666466> /EF << /F 6 0 R >> /Vendor (opaque) >>";
        var doc = PdfDocument.Load(Source($"/S /ImportData /F {(indirect ? "5 0 R" : file)}",
            $"5 0 obj\n{file}\nendobj\n6 0 obj\n<< /Length 4 /Filter /Unsupported >>\nstream\ndata\nendstream\nendobj\n"));
        var action = Assert.Single(doc.GetActions());
        var details = Assert.IsType<PdfImportDataAction>(action.ImportDataDetails);
        Assert.Null(action.ResetFormDetails);
        Assert.Same(action.Dictionary, details.Dictionary);
        Assert.Same(doc.File.Resolve(action.Dictionary.Get("F")), details.FileSpecification);
        var dictionary = Assert.IsType<PdfDictionary>(details.FileSpecification);
        Assert.Equal(new byte[] { 255, 0, 102, 100, 102 }, Assert.IsType<PdfString>(dictionary.Get("F")).Bytes);
        Assert.IsType<PdfRef>(Assert.IsType<PdfDictionary>(dictionary.Get("EF")).Get("F"));
        Assert.Equal("unchanged", Assert.Single(doc.GetFormFields()).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/F null")]
    [InlineData("/F []")]
    [InlineData("/F /Bad")]
    [InlineData("/F 42")]
    [InlineData("/F true")]
    [InlineData("/F 19 0 R")]
    public void ImportDataRetainsMissingAndWrongTypeFileSpecificationsInRawDictionary(string entries)
    {
        var action = Assert.Single(PdfDocument.Load(Source("/S /ImportData " + entries)).GetActions());
        var details = Assert.IsType<PdfImportDataAction>(action.ImportDataDetails);
        Assert.Null(details.FileSpecification);
        Assert.Same(action.Dictionary, details.Dictionary);
    }

    [Theory]
    [InlineData("SubmitForm")]
    [InlineData("JavaScript")]
    [InlineData("resetform")]
    [InlineData("importdata")]
    public void OtherSubtypesDoNotGainFormDataMetadata(string type)
    {
        var action = Assert.Single(PdfDocument.Load(Source($"/S /{type} /Fields [] /Flags 1 /F (unopened)")).GetActions());
        Assert.Null(action.ResetFormDetails);
        Assert.Null(action.ImportDataDetails);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CyclicNextChainsAndSaveRoundTripsRemainInert(bool incremental)
    {
        var bytes = Source("/S /ResetForm /Fields [9 0 R] /Next [5 0 R 4 0 R]",
            "5 0 obj\n<< /S /ImportData /F <68747470733a2f2fff00> /Next 4 0 R >>\nendobj\n");
        bytes = PdfEditor.Open(bytes).Save();
        var editor = PdfEditor.Open(bytes);
        var saved = incremental ? editor.SaveIncremental() : editor.Save();
        foreach (var source in new[] { bytes, saved })
        {
            var doc = PdfDocument.Load(source);
            var actions = doc.GetActions();
            Assert.Equal(new[] { "ResetForm", "ImportData" }, actions.Select(a => a.ActionType).ToArray());
            Assert.IsType<PdfResetFormAction>(actions[0].ResetFormDetails);
            var details = Assert.IsType<PdfImportDataAction>(actions[1].ImportDataDetails);
            Assert.Equal(new byte[] { 104, 116, 116, 112, 115, 58, 47, 47, 255, 0 }, Assert.IsType<PdfString>(details.FileSpecification).Bytes);
            Assert.Equal("unchanged", Assert.Single(doc.GetFormFields()).Value);
        }
    }
}
