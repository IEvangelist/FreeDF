using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class UriActionTests
{
    static PdfDocument LoadAction(string entries, string objects = "")
    {
        var pdf = "%PDF-2.0\n" +
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R /OpenAction 4 0 R /URI << /Base (https://base.invalid/) >> >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>\nendobj\n" +
            $"4 0 obj\n<< {entries} >>\nendobj\n" +
            objects +
            "trailer\n<< /Root 1 0 R /Size 10 >>\n%%EOF\n";
        return PdfDocument.Load(Encoding.ASCII.GetBytes(pdf));
    }

    static PdfUriAction ReadDetails(PdfDocument doc)
    {
        var action = Assert.Single(doc.GetActions());
        Assert.Equal("URI", action.ActionType);
        Assert.Null(action.JavaScript);
        Assert.Null(action.RemoteGoToDetails);
        var details = Assert.IsType<PdfUriAction>(action.UriDetails);
        Assert.Same(action.Dictionary, details.Dictionary);
        Assert.Same(doc.File.Resolve(doc.File.Catalog.Get("OpenAction")), details.Dictionary);
        return details;
    }

    [Theory]
    [InlineData("(https://unopened.invalid/path?query=1#fragment)", "https://unopened.invalid/path?query=1#fragment")]
    [InlineData("<68747470733a2f2f756e6f70656e65642e696e76616c69642f>", "https://unopened.invalid/")]
    [InlineData("(../relative%20path)", "../relative%20path")]
    [InlineData("(not a valid URI)", "not a valid URI")]
    [InlineData("()", "")]
    public void Uri_strings_are_preserved_without_access_or_base_resolution(string uri, string expected)
    {
        var details = ReadDetails(LoadAction($"/S /URI /URI {uri}"));

        Assert.Equal(expected, Assert.IsType<PdfString>(details.Uri).Text);
        Assert.Same(details.Dictionary.Get("URI"), details.Uri);
        Assert.Null(details.IsMap);
    }

    [Theory]
    [InlineData("(\\303\\251)")]
    [InlineData("<c3a9>")]
    public void Utf8_uri_bytes_are_preserved_without_text_string_decoding(string uri)
    {
        var details = ReadDetails(LoadAction($"/S /URI /URI {uri}"));

        Assert.Equal(new byte[] { 0xc3, 0xa9 }, Assert.IsType<PdfString>(details.Uri).Bytes);
        Assert.Same(details.Dictionary.Get("URI"), details.Uri);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Direct_and_local_indirect_entries_preserve_strings_and_boolean_flags(bool indirect, bool isMap)
    {
        var flag = isMap ? "true" : "false";
        var doc = LoadAction(
            $"/S /URI /URI {(indirect ? "5 0 R" : "(https://unopened.invalid/)")} /IsMap {(indirect ? "6 0 R" : flag)}",
            "5 0 obj\n(https://unopened.invalid/)\nendobj\n" +
            $"6 0 obj\n{flag}\nendobj\n");
        var details = ReadDetails(doc);

        Assert.Equal("https://unopened.invalid/", Assert.IsType<PdfString>(details.Uri).Text);
        Assert.Same(doc.File.Resolve(details.Dictionary.Get("URI")), details.Uri);
        Assert.Equal(isMap, details.IsMap);
        if (indirect)
        {
            Assert.IsType<PdfRef>(details.Dictionary.Get("URI"));
            Assert.IsType<PdfRef>(details.Dictionary.Get("IsMap"));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("/URI null /IsMap null")]
    [InlineData("/URI /Wrong /IsMap (true)")]
    [InlineData("/URI [] /IsMap 1")]
    [InlineData("/URI <<>> /IsMap /False")]
    [InlineData("/URI true /IsMap []")]
    [InlineData("/URI 42 /IsMap <<>>")]
    [InlineData("/URI 9 0 R /IsMap 9 0 R")]
    [InlineData("/URI 5 0 R /IsMap 6 0 R")]
    public void Missing_or_wrong_type_entries_are_nullable_without_losing_raw_values(string entries)
    {
        var doc = LoadAction($"/S /URI {entries} /Vendor (opaque)",
            "5 0 obj\n/Wrong\nendobj\n6 0 obj\n(false)\nendobj\n");
        var dictionary = Assert.IsType<PdfDictionary>(doc.File.Resolve(doc.File.Catalog.Get("OpenAction")));
        var originalEntries = dictionary.Entries.ToArray();
        var details = ReadDetails(doc);

        Assert.Null(details.Uri);
        Assert.Null(details.IsMap);
        Assert.Equal(originalEntries, details.Dictionary.Entries.ToArray());
    }

    [Fact]
    public void Wrong_type_entries_do_not_discard_other_valid_metadata()
    {
        var missingUri = ReadDetails(LoadAction("/S /URI /IsMap true"));
        var wrongUri = ReadDetails(LoadAction("/S /URI /URI /Wrong /IsMap false"));
        var wrongIsMap = ReadDetails(LoadAction("/S /URI /URI (relative) /IsMap (true)"));

        Assert.Null(missingUri.Uri);
        Assert.True(missingUri.IsMap);
        Assert.Null(wrongUri.Uri);
        Assert.False(wrongUri.IsMap);
        Assert.Equal("relative", Assert.IsType<PdfString>(wrongIsMap.Uri).Text);
        Assert.Null(wrongIsMap.IsMap);
    }

    [Theory]
    [InlineData("GoToR")]
    [InlineData("JavaScript")]
    [InlineData("Launch")]
    [InlineData("uri")]
    public void Other_action_types_do_not_gain_uri_metadata(string type)
    {
        var action = Assert.Single(LoadAction($"/S /{type} /URI (https://unopened.invalid/) /IsMap true").GetActions());

        Assert.Null(action.UriDetails);
        Assert.IsType<PdfString>(action.Dictionary.Get("URI"));
    }

    [Fact]
    public void Next_chains_expose_uri_metadata_and_retain_original_dictionaries()
    {
        var doc = LoadAction("/S /GoToR /F (unopened.pdf) /D [0 /Fit] /Next 5 0 R",
            "5 0 obj\n<< /S /URI /URI (relative) /IsMap true /Next 4 0 R >>\nendobj\n");
        var actions = doc.GetActions();

        Assert.Equal(2, actions.Count);
        Assert.Null(actions[0].UriDetails);
        Assert.NotNull(actions[0].RemoteGoToDetails);
        Assert.Equal("OpenAction/Next", actions[1].Trigger);
        var details = Assert.IsType<PdfUriAction>(actions[1].UriDetails);
        Assert.Same(actions[1].Dictionary, details.Dictionary);
        Assert.Same(doc.File.Resolve(actions[0].Dictionary.Get("Next")), details.Dictionary);
        Assert.Equal("relative", Assert.IsType<PdfString>(details.Uri).Text);
        Assert.True(details.IsMap);
        Assert.IsType<PdfRef>(details.Dictionary.Get("Next"));
    }
}
