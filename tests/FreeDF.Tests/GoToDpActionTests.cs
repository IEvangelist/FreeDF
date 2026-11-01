using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class GoToDpActionTests
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

    static PdfGoToDpAction ReadDetails(PdfDocument doc)
    {
        var dictionary = Assert.IsType<PdfDictionary>(doc.File.Resolve(doc.File.Catalog.Get("OpenAction")));
        var originalEntries = dictionary.Entries.ToArray();
        var action = Assert.Single(doc.GetActions());
        Assert.Equal("GoToDp", action.ActionType);
        Assert.Null(action.JavaScript);
        Assert.Null(action.GoToDetails);
        Assert.Null(action.RemoteGoToDetails);
        Assert.Null(action.EmbeddedGoToDetails);
        Assert.Null(action.UriDetails);
        Assert.Null(action.NamedDetails);
        var details = Assert.IsType<PdfGoToDpAction>(action.GoToDpDetails);
        Assert.Same(action.Dictionary, details.Dictionary);
        Assert.Same(dictionary, details.Dictionary);
        Assert.Equal(originalEntries, details.Dictionary.Entries.ToArray());
        return details;
    }

    [Fact]
    public void Indirect_dpart_reference_is_exposed_without_resolving_its_start_or_end_pages()
    {
        var doc = LoadAction("/S /GoToDp /Dp 5 0 R",
            "5 0 obj\n<< /Type /DPart /Start 6 0 R /End 7 0 R /Vendor (opaque) >>\nendobj\n" +
            "6 0 obj\n<< /Type /Page /Parent 2 0 R >>\nendobj\n" +
            "7 0 obj\n<< /Type /Page /Parent 2 0 R >>\nendobj\n");
        var details = ReadDetails(doc);
        var dPart = Assert.IsType<PdfDictionary>(details.DPart);

        Assert.Same(doc.File.Resolve(details.Dictionary.Get("Dp")), dPart);
        Assert.Same(doc.File.GetObject(5), dPart);
        Assert.Equal("DPart", Assert.IsType<PdfName>(dPart.Get("Type")).Value);
        Assert.IsType<PdfRef>(dPart.Get("Start"));
        Assert.IsType<PdfRef>(dPart.Get("End"));
        Assert.Equal("opaque", Assert.IsType<PdfString>(dPart.Get("Vendor")).Text);
        Assert.IsType<PdfRef>(details.Dictionary.Get("Dp"));
    }

    [Fact]
    public void Indirect_dpart_dictionary_is_exposed_when_optional_type_is_absent()
    {
        var doc = LoadAction("/S /GoToDp /Dp 5 0 R",
            "5 0 obj\n<< /Start 6 0 R /End 7 0 R /Vendor (opaque) >>\nendobj\n");
        var details = ReadDetails(doc);
        var dPart = Assert.IsType<PdfDictionary>(details.DPart);

        Assert.Null(dPart.Get("Type"));
        Assert.IsType<PdfRef>(dPart.Get("Start"));
        Assert.IsType<PdfRef>(dPart.Get("End"));
        Assert.Equal("opaque", Assert.IsType<PdfString>(dPart.Get("Vendor")).Text);
        Assert.IsType<PdfRef>(details.Dictionary.Get("Dp"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/Dp null")]
    [InlineData("/Dp 42")]
    [InlineData("/Dp 1.5")]
    [InlineData("/Dp true")]
    [InlineData("/Dp /DPart")]
    [InlineData("/Dp (direct)")]
    [InlineData("/Dp []")]
    [InlineData("/Dp << /Type /DPart >>")]
    [InlineData("/Dp 6 0 R")]
    [InlineData("/Dp 7 0 R")]
    [InlineData("/Dp 8 0 R")]
    public void Missing_direct_or_wrong_type_dpart_values_remain_in_the_original_dictionary(string entries)
    {
        var details = ReadDetails(LoadAction($"/S /GoToDp {entries} /Vendor (opaque)",
            "6 0 obj\nfalse\nendobj\n" +
            "7 0 obj\n<< /Type /Page >>\nendobj\n" +
            "8 0 obj\n8 0 R\nendobj\n"));

        Assert.Null(details.DPart);
        Assert.Equal("opaque", Assert.IsType<PdfString>(details.Dictionary.Get("Vendor")).Text);
        if (entries.Contains("/Dp "))
            Assert.NotNull(details.Dictionary.Get("Dp"));
    }

    [Theory]
    [InlineData("GoTo")]
    [InlineData("GoToR")]
    [InlineData("GoToE")]
    [InlineData("URI")]
    [InlineData("Named")]
    [InlineData("Launch")]
    public void Unrelated_action_subtypes_do_not_gain_go_to_dpart_metadata(string subtype)
    {
        var action = Assert.Single(LoadAction($"/S /{subtype} /Dp 5 0 R").GetActions());

        Assert.Null(action.GoToDpDetails);
        Assert.IsType<PdfRef>(action.Dictionary.Get("Dp"));
    }

    [Fact]
    public void Next_chains_preserve_action_order_and_cycle_handling()
    {
        var doc = LoadAction("/S /GoToDp /Dp 6 0 R /Next 5 0 R",
            "5 0 obj\n<< /S /URI /URI (opaque) /Next 4 0 R >>\nendobj\n" +
            "6 0 obj\n<< /Type /DPart /Start 3 0 R >>\nendobj\n");
        var actions = doc.GetActions();

        Assert.Equal(2, actions.Count);
        Assert.Equal("GoToDp", actions[0].ActionType);
        Assert.Equal("OpenAction", actions[0].Trigger);
        Assert.Same(actions[0].Dictionary, Assert.IsType<PdfGoToDpAction>(actions[0].GoToDpDetails).Dictionary);
        Assert.Same(doc.File.GetObject(6), Assert.IsType<PdfGoToDpAction>(actions[0].GoToDpDetails).DPart);
        Assert.IsType<PdfRef>(actions[0].Dictionary.Get("Next"));
        Assert.Equal("URI", actions[1].ActionType);
        Assert.Equal("OpenAction/Next", actions[1].Trigger);
        Assert.NotNull(actions[1].UriDetails);
        Assert.Null(actions[1].GoToDpDetails);
        Assert.IsType<PdfRef>(actions[1].Dictionary.Get("Next"));
    }
}
