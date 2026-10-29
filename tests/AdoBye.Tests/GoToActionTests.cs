using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;

namespace AdoBye.Tests;

public class GoToActionTests
{
    static PdfDocument LoadAction(string entries, string objects = "")
    {
        var pdf = "%PDF-2.0\n" +
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R /OpenAction 4 0 R " +
            "/Dests << /Chapter [3 0 R /Fit] >> /Names << /Dests << /Names [(Chapter) [3 0 R /Fit]] >> >> >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>\nendobj\n" +
            $"4 0 obj\n<< {entries} >>\nendobj\n" +
            objects +
            "trailer\n<< /Root 1 0 R /Size 10 >>\n%%EOF\n";
        return PdfDocument.Load(Encoding.ASCII.GetBytes(pdf));
    }

    static PdfGoToAction ReadDetails(PdfDocument doc)
    {
        var dictionary = Assert.IsType<PdfDictionary>(doc.File.Resolve(doc.File.Catalog.Get("OpenAction")));
        var originalEntries = dictionary.Entries.ToArray();
        var action = Assert.Single(doc.GetActions());
        Assert.Equal("GoTo", action.ActionType);
        Assert.Null(action.JavaScript);
        Assert.Null(action.RemoteGoToDetails);
        Assert.Null(action.UriDetails);
        var details = Assert.IsType<PdfGoToAction>(action.GoToDetails);
        Assert.Same(action.Dictionary, details.Dictionary);
        Assert.Same(dictionary, details.Dictionary);
        Assert.Equal(originalEntries, details.Dictionary.Entries.ToArray());
        return details;
    }

    [Theory]
    [InlineData("[3 0 R /XYZ null 12 1.5]", "XYZ", 5)]
    [InlineData("[3 0 R /Fit]", "Fit", 2)]
    [InlineData("[3 0 R /FitH null]", "FitH", 3)]
    [InlineData("[3 0 R /FitV 12]", "FitV", 3)]
    [InlineData("[3 0 R /FitR 0 0 100 100]", "FitR", 6)]
    [InlineData("[3 0 R /FitB]", "FitB", 2)]
    [InlineData("[3 0 R /FitBH 12]", "FitBH", 3)]
    [InlineData("[3 0 R /FitBV null]", "FitBV", 3)]
    public void Explicit_destinations_preserve_local_page_references_and_parameters(string value, string mode, int count)
    {
        var details = ReadDetails(LoadAction($"/S /GoTo /D {value}"));
        var destination = Assert.IsType<PdfArray>(details.Destination);

        Assert.Same(details.Dictionary.Get("D"), destination);
        Assert.Equal(count, destination.Count);
        Assert.Equal(3, Assert.IsType<PdfRef>(destination[0]).Number);
        Assert.Equal(mode, Assert.IsType<PdfName>(destination[1]).Value);
    }

    [Theory]
    [InlineData("/Chapter", true, false)]
    [InlineData("(Chapter)", false, false)]
    [InlineData("<43686170746572>", false, false)]
    [InlineData("/Chapter", true, true)]
    [InlineData("(Chapter)", false, true)]
    [InlineData("<43686170746572>", false, true)]
    public void Named_destinations_remain_names_or_byte_strings_even_when_defined_locally(string value, bool isName, bool indirect)
    {
        var doc = LoadAction($"/S /GoTo /D {(indirect ? "5 0 R" : value)}",
            $"5 0 obj\n{value}\nendobj\n");
        var details = ReadDetails(doc);

        if (isName)
            Assert.Equal("Chapter", Assert.IsType<PdfName>(details.Destination).Value);
        else
            Assert.Equal(Encoding.ASCII.GetBytes("Chapter"), Assert.IsType<PdfString>(details.Destination).Bytes);
        Assert.Same(doc.File.Resolve(details.Dictionary.Get("D")), details.Destination);
        if (indirect)
            Assert.IsType<PdfRef>(details.Dictionary.Get("D"));
    }

    [Fact]
    public void Indirect_action_type_and_destination_are_read_without_resolving_array_elements()
    {
        var doc = LoadAction("/S 5 0 R /D 6 0 R",
            "5 0 obj\n/GoTo\nendobj\n" +
            "6 0 obj\n[3 0 R /XYZ null 12 1.5]\nendobj\n");
        var details = ReadDetails(doc);
        var destination = Assert.IsType<PdfArray>(details.Destination);

        Assert.Same(doc.File.Resolve(details.Dictionary.Get("D")), destination);
        Assert.IsType<PdfRef>(details.Dictionary.Get("S"));
        Assert.IsType<PdfRef>(details.Dictionary.Get("D"));
        Assert.IsType<PdfRef>(destination[0]);
        Assert.IsType<PdfNull>(destination[2]);
        Assert.Equal(12L, Assert.IsType<PdfInteger>(destination[3]).Value);
        Assert.Equal(1.5, Assert.IsType<PdfReal>(destination[4]).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/D null")]
    [InlineData("/D 42")]
    [InlineData("/D 1.5")]
    [InlineData("/D true")]
    [InlineData("/D << /D [3 0 R /Fit] >>")]
    [InlineData("/D 9 0 R")]
    [InlineData("/D 5 0 R")]
    [InlineData("/D 6 0 R")]
    [InlineData("/D 7 0 R")]
    public void Missing_or_wrong_type_destinations_are_nullable_and_raw_entries_are_unchanged(string entries)
    {
        var details = ReadDetails(LoadAction($"/S /GoTo {entries} /Vendor (opaque)",
            "5 0 obj\nfalse\nendobj\n" +
            "6 0 obj\n<< /Filter /Unsupported /Length 4 >>\nstream\ndata\nendstream\nendobj\n" +
            "7 0 obj\n7 0 R\nendobj\n"));

        Assert.Null(details.Destination);
        Assert.Equal("opaque", Assert.IsType<PdfString>(details.Dictionary.Get("Vendor")).Text);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[0 /Fit]")]
    [InlineData("[9 0 R /Unknown << /Vendor true >>]")]
    public void Malformed_arrays_are_retained_without_validation_or_normalisation(string value)
    {
        var details = ReadDetails(LoadAction($"/S /GoTo /D {value}"));

        Assert.IsType<PdfArray>(details.Destination);
        Assert.Same(details.Dictionary.Get("D"), details.Destination);
    }

    [Theory]
    [InlineData("GoToR")]
    [InlineData("GoToE")]
    [InlineData("URI")]
    [InlineData("JavaScript")]
    [InlineData("Launch")]
    [InlineData("goto")]
    public void Unrelated_action_types_do_not_gain_local_go_to_metadata(string type)
    {
        var action = Assert.Single(LoadAction($"/S /{type} /D [3 0 R /Fit]").GetActions());

        Assert.Null(action.GoToDetails);
        Assert.IsType<PdfArray>(action.Dictionary.Get("D"));
    }

    [Theory]
    [InlineData("5 0 R", "OpenAction/Next")]
    [InlineData("[5 0 R]", "OpenAction/Next[0]")]
    public void Next_chains_expose_local_metadata_and_preserve_dictionaries_without_following_cycles(string next, string trigger)
    {
        var doc = LoadAction($"/S /URI /URI (https://unopened.invalid/) /Next {next}",
            "5 0 obj\n<< /S /GoTo /D [3 0 R /Fit] /Next 4 0 R >>\nendobj\n");
        var actions = doc.GetActions();

        Assert.Equal(2, actions.Count);
        Assert.Null(actions[0].GoToDetails);
        Assert.NotNull(actions[0].UriDetails);
        Assert.Equal(trigger, actions[1].Trigger);
        var details = Assert.IsType<PdfGoToAction>(actions[1].GoToDetails);
        Assert.Same(actions[1].Dictionary, details.Dictionary);
        Assert.Same(doc.File.GetObject(5), details.Dictionary);
        Assert.Same(details.Dictionary.Get("D"), details.Destination);
        Assert.IsType<PdfRef>(details.Dictionary.Get("Next"));
    }
}
