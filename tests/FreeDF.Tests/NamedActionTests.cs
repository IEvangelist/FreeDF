using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class NamedActionTests
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

    static PdfNamedAction ReadDetails(PdfDocument doc)
    {
        var dictionary = Assert.IsType<PdfDictionary>(doc.File.Resolve(doc.File.Catalog.Get("OpenAction")));
        var originalEntries = dictionary.Entries.ToArray();
        var action = Assert.Single(doc.GetActions());
        Assert.Equal("Named", action.ActionType);
        Assert.Null(action.JavaScript);
        Assert.Null(action.GoToDetails);
        Assert.Null(action.RemoteGoToDetails);
        Assert.Null(action.UriDetails);
        var details = Assert.IsType<PdfNamedAction>(action.NamedDetails);
        Assert.Same(action.Dictionary, details.Dictionary);
        Assert.Same(dictionary, details.Dictionary);
        Assert.Equal(originalEntries, details.Dictionary.Entries.ToArray());
        return details;
    }

    [Theory]
    [InlineData("NextPage")]
    [InlineData("PrevPage")]
    [InlineData("FirstPage")]
    [InlineData("LastPage")]
    [InlineData("VendorAction")]
    [InlineData("nextpage")]
    [InlineData("")]
    public void Standard_and_unknown_names_are_preserved_without_dispatch(string name)
    {
        var details = ReadDetails(LoadAction($"/S /Named /N /{name} /Vendor (opaque)"));

        Assert.Equal(name, Assert.IsType<PdfName>(details.Name).Value);
        Assert.Same(details.Dictionary.Get("N"), details.Name);
        Assert.Equal("opaque", Assert.IsType<PdfString>(details.Dictionary.Get("Vendor")).Text);
    }

    [Fact]
    public void Escaped_name_is_decoded_by_the_parser_without_losing_the_name_object()
    {
        var details = ReadDetails(LoadAction("/S /Named /N /Next#50age"));

        Assert.Equal("NextPage", Assert.IsType<PdfName>(details.Name).Value);
        Assert.Same(details.Dictionary.Get("N"), details.Name);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Subtype_and_name_support_local_indirect_entries(bool indirectSubtype, bool indirectName)
    {
        var doc = LoadAction(
            $"/S {(indirectSubtype ? "5 0 R" : "/Named")} /N {(indirectName ? "6 0 R" : "/LastPage")}",
            "5 0 obj\n/Named\nendobj\n6 0 obj\n/LastPage\nendobj\n");
        var details = ReadDetails(doc);

        Assert.Equal("LastPage", Assert.IsType<PdfName>(details.Name).Value);
        Assert.Same(doc.File.Resolve(details.Dictionary.Get("N")), details.Name);
        if (indirectSubtype)
            Assert.IsType<PdfRef>(details.Dictionary.Get("S"));
        if (indirectName)
            Assert.IsType<PdfRef>(details.Dictionary.Get("N"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/N null")]
    [InlineData("/N (NextPage)")]
    [InlineData("/N <4e65787450616765>")]
    [InlineData("/N 42")]
    [InlineData("/N 1.5")]
    [InlineData("/N true")]
    [InlineData("/N []")]
    [InlineData("/N << /N /NextPage >>")]
    [InlineData("/N 9 0 R")]
    [InlineData("/N 5 0 R")]
    [InlineData("/N 6 0 R")]
    [InlineData("/N 7 0 R")]
    public void Missing_or_wrong_type_names_are_nullable_and_raw_entries_are_unchanged(string entries)
    {
        var details = ReadDetails(LoadAction($"/S /Named {entries} /Vendor (opaque)",
            "5 0 obj\n(NextPage)\nendobj\n" +
            "6 0 obj\n<< /Filter /Unsupported /Length 4 >>\nstream\ndata\nendstream\nendobj\n" +
            "7 0 obj\n7 0 R\nendobj\n"));

        Assert.Null(details.Name);
        Assert.Equal("opaque", Assert.IsType<PdfString>(details.Dictionary.Get("Vendor")).Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/S null")]
    [InlineData("/S (Named)")]
    [InlineData("/S 42")]
    [InlineData("/S 9 0 R")]
    [InlineData("/S /named")]
    [InlineData("/S /GoTo")]
    [InlineData("/S /GoToR")]
    [InlineData("/S /URI")]
    [InlineData("/S /JavaScript")]
    [InlineData("/S /Launch")]
    public void Only_the_exact_named_subtype_gains_named_metadata(string subtype)
    {
        var action = Assert.Single(LoadAction($"{subtype} /N /NextPage").GetActions());

        Assert.Null(action.NamedDetails);
        Assert.Equal("NextPage", Assert.IsType<PdfName>(action.Dictionary.Get("N")).Value);
    }

    [Theory]
    [InlineData("5 0 R", "OpenAction/Next")]
    [InlineData("[5 0 R]", "OpenAction/Next[0]")]
    [InlineData("<< /S /Named /N /PrevPage /Next 4 0 R >>", "OpenAction/Next")]
    public void Next_chains_expose_named_metadata_and_stop_cycles(string next, string trigger)
    {
        var doc = LoadAction($"/S /URI /URI (relative) /Next {next}",
            "5 0 obj\n<< /S /Named /N /PrevPage /Next 4 0 R >>\nendobj\n");
        var actions = doc.GetActions();

        Assert.Equal(2, actions.Count);
        Assert.Null(actions[0].NamedDetails);
        Assert.NotNull(actions[0].UriDetails);
        Assert.Equal(trigger, actions[1].Trigger);
        var details = Assert.IsType<PdfNamedAction>(actions[1].NamedDetails);
        Assert.Same(actions[1].Dictionary, details.Dictionary);
        var nextValue = doc.File.Resolve(actions[0].Dictionary.Get("Next"));
        var dictionary = nextValue is PdfArray array ? doc.File.Resolve(array[0]) : nextValue;
        Assert.Same(dictionary, details.Dictionary);
        Assert.Equal("PrevPage", Assert.IsType<PdfName>(details.Name).Value);
        Assert.Same(details.Dictionary.Get("N"), details.Name);
        Assert.IsType<PdfRef>(details.Dictionary.Get("Next"));
    }

    [Fact]
    public void Invalid_named_metadata_does_not_prevent_next_siblings_or_change_their_order()
    {
        var doc = LoadAction("/S /Named /N (WrongType) /Next [5 0 R 6 0 R]",
            "5 0 obj\n<< /S /Named /N /VendorAction /Next 4 0 R >>\nendobj\n" +
            "6 0 obj\n<< /S /URI /URI (relative) >>\nendobj\n");
        var actions = doc.GetActions();

        Assert.Equal(3, actions.Count);
        Assert.Null(Assert.IsType<PdfNamedAction>(actions[0].NamedDetails).Name);
        Assert.IsType<PdfString>(actions[0].Dictionary.Get("N"));
        Assert.Equal("OpenAction/Next[0]", actions[1].Trigger);
        Assert.Equal("VendorAction", Assert.IsType<PdfName>(Assert.IsType<PdfNamedAction>(actions[1].NamedDetails).Name).Value);
        Assert.Equal("OpenAction/Next[1]", actions[2].Trigger);
        Assert.Null(actions[2].NamedDetails);
        Assert.Equal("relative", Assert.IsType<PdfString>(Assert.IsType<PdfUriAction>(actions[2].UriDetails).Uri).Text);
    }
}
