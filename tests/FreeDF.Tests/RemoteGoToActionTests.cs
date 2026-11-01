using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class RemoteGoToActionTests
{
    static PdfDocument LoadAction(string entries, string objects = "")
    {
        var pdf = "%PDF-2.0\n" +
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R /OpenAction 4 0 R >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>\nendobj\n" +
            $"4 0 obj\n<< {entries} >>\nendobj\n" +
            objects +
            "trailer\n<< /Root 1 0 R /Size 10 >>\n%%EOF\n";
        return PdfDocument.Load(Encoding.ASCII.GetBytes(pdf));
    }

    static PdfRemoteGoToAction ReadDetails(PdfDocument doc)
    {
        var action = Assert.Single(doc.GetActions());
        Assert.Equal("GoToR", action.ActionType);
        Assert.Null(action.JavaScript);
        var details = Assert.IsType<PdfRemoteGoToAction>(action.RemoteGoToDetails);
        Assert.Same(action.Dictionary, details.Dictionary);
        Assert.Same(doc.File.Resolve(doc.File.Catalog.Get("OpenAction")), details.Dictionary);
        return details;
    }

    [Theory]
    [InlineData("(unopened.pdf)")]
    [InlineData("<756e6f70656e65642e706466>")]
    public void Remote_file_strings_are_preserved_without_opening_a_file(string file)
    {
        var doc = LoadAction($"/S /GoToR /F {file} /D [0 /Fit]");
        var details = ReadDetails(doc);

        Assert.Equal("unopened.pdf", Assert.IsType<PdfString>(details.FileSpecification).Text);
        Assert.Same(details.Dictionary.Get("F"), details.FileSpecification);
        var destination = Assert.IsType<PdfArray>(details.Destination);
        Assert.Equal(0L, Assert.IsType<PdfInteger>(destination[0]).Value);
        Assert.Equal("Fit", Assert.IsType<PdfName>(destination[1]).Value);
        Assert.Null(details.NewWindow);
        Assert.Null(details.StructureDestination);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void File_specification_dictionaries_and_local_indirect_values_remain_inert(bool indirect)
    {
        const string spec = "<< /Type /Filespec /F (unopened.pdf) /UF (unicode.pdf) /EF << /F 8 0 R >> >>";
        var doc = LoadAction(
            $"/S /GoToR /F {(indirect ? "5 0 R" : spec)} /D 6 0 R /NewWindow 7 0 R",
            $"5 0 obj\n{spec}\nendobj\n" +
            "6 0 obj\n[999 /XYZ null 12 1.5]\nendobj\n" +
            "7 0 obj\nfalse\nendobj\n" +
            "8 0 obj\n<< /Filter /Unsupported /Length 4 >>\nstream\ndata\nendstream\nendobj\n");
        var details = ReadDetails(doc);

        var file = Assert.IsType<PdfDictionary>(details.FileSpecification);
        Assert.Same(doc.File.Resolve(details.Dictionary.Get("F")), file);
        Assert.Equal("unicode.pdf", Assert.IsType<PdfString>(file.Get("UF")).Text);
        Assert.IsType<PdfRef>(Assert.IsType<PdfDictionary>(file.Get("EF")).Get("F"));
        var destination = Assert.IsType<PdfArray>(details.Destination);
        Assert.Same(doc.File.Resolve(details.Dictionary.Get("D")), destination);
        Assert.Equal(999L, Assert.IsType<PdfInteger>(destination[0]).Value);
        Assert.IsType<PdfNull>(destination[2]);
        Assert.False(details.NewWindow);
        Assert.IsType<PdfRef>(details.Dictionary.Get("D"));
    }

    [Theory]
    [InlineData("/Chapter", true)]
    [InlineData("(Chapter)", false)]
    [InlineData("<43686170746572>", false)]
    public void Named_destinations_are_not_resolved_in_the_current_document(string destination, bool isName)
    {
        var doc = LoadAction($"/S /GoToR /F (unopened.pdf) /D {destination} /NewWindow true");
        var details = ReadDetails(doc);

        if (isName)
            Assert.Equal("Chapter", Assert.IsType<PdfName>(details.Destination).Value);
        else
            Assert.Equal("Chapter", Assert.IsType<PdfString>(details.Destination).Text);
        Assert.Same(details.Dictionary.Get("D"), details.Destination);
        Assert.True(details.NewWindow);
    }

    [Fact]
    public void Structure_destination_is_preserved_without_interpreting_remote_element_id()
    {
        var doc = LoadAction("/S /GoToR /F (unopened.pdf) /D /Fallback /SD 5 0 R",
            "5 0 obj\n[(remote-element) /Fit]\nendobj\n");
        var details = ReadDetails(doc);

        var structure = Assert.IsType<PdfArray>(details.StructureDestination);
        Assert.Same(doc.File.Resolve(details.Dictionary.Get("SD")), structure);
        Assert.Equal("remote-element", Assert.IsType<PdfString>(structure[0]).Text);
        Assert.Equal("Fallback", Assert.IsType<PdfName>(details.Destination).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/F null /D null /SD null /NewWindow null")]
    [InlineData("/F /Wrong /D 1 /SD (wrong) /NewWindow (true)")]
    [InlineData("/F [] /D << /D [0 /Fit] >> /SD false /NewWindow 1")]
    [InlineData("/F 9 0 R /D 9 0 R /SD 9 0 R /NewWindow 9 0 R")]
    public void Missing_or_wrong_type_entries_are_nullable_without_losing_raw_values(string entries)
    {
        var doc = LoadAction($"/S /GoToR {entries}");
        var action = Assert.Single(doc.GetActions());
        var originalEntries = action.Dictionary.Entries.ToArray();
        var details = ReadDetails(doc);

        Assert.Null(details.FileSpecification);
        Assert.Null(details.Destination);
        Assert.Null(details.StructureDestination);
        Assert.Null(details.NewWindow);
        Assert.Equal(originalEntries, details.Dictionary.Entries.ToArray());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[3 0 R /Fit]")]
    [InlineData("[-1 /Unknown]")]
    public void Malformed_destination_arrays_are_retained_without_normalising_or_resolving_contents(string destination)
    {
        var details = ReadDetails(LoadAction($"/S /GoToR /F << /Vendor (opaque) >> /D {destination}"));

        Assert.IsType<PdfDictionary>(details.FileSpecification);
        Assert.IsType<PdfArray>(details.Destination);
        Assert.Same(details.Dictionary.Get("D"), details.Destination);
    }

    [Theory]
    [InlineData("GoTo")]
    [InlineData("GoToE")]
    [InlineData("Launch")]
    [InlineData("gotor")]
    public void Other_action_types_do_not_gain_remote_go_to_metadata(string type)
    {
        var action = Assert.Single(LoadAction($"/S /{type} /F (unopened.pdf) /D [0 /Fit]").GetActions());

        Assert.Null(action.RemoteGoToDetails);
        Assert.IsType<PdfString>(action.Dictionary.Get("F"));
    }

    [Fact]
    public void Next_chains_expose_remote_metadata_and_keep_original_action_dictionaries()
    {
        var doc = LoadAction("/S /URI /URI (https://example.com) /Next 5 0 R",
            "5 0 obj\n<< /S /GoToR /F (unopened.pdf) /D [0 /Fit] /Next 4 0 R >>\nendobj\n");
        var actions = doc.GetActions();

        Assert.Equal(2, actions.Count);
        Assert.Null(actions[0].RemoteGoToDetails);
        Assert.Equal("OpenAction/Next", actions[1].Trigger);
        var details = Assert.IsType<PdfRemoteGoToAction>(actions[1].RemoteGoToDetails);
        Assert.Same(actions[1].Dictionary, details.Dictionary);
        Assert.Same(doc.File.Resolve(actions[0].Dictionary.Get("Next")), details.Dictionary);
        Assert.IsType<PdfRef>(details.Dictionary.Get("Next"));
    }
}
