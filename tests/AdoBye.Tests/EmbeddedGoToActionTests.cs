using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;

namespace AdoBye.Tests;

public class EmbeddedGoToActionTests
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

    static PdfEmbeddedGoToAction ReadDetails(PdfDocument doc)
    {
        var dictionary = Assert.IsType<PdfDictionary>(doc.File.Resolve(doc.File.Catalog.Get("OpenAction")));
        var originalEntries = dictionary.Entries.ToArray();
        var action = Assert.Single(doc.GetActions());
        Assert.Equal("GoToE", action.ActionType);
        Assert.Null(action.JavaScript);
        Assert.Null(action.GoToDetails);
        Assert.Null(action.RemoteGoToDetails);
        Assert.Null(action.UriDetails);
        Assert.Null(action.NamedDetails);
        var details = Assert.IsType<PdfEmbeddedGoToAction>(action.EmbeddedGoToDetails);
        Assert.Same(action.Dictionary, details.Dictionary);
        Assert.Same(dictionary, details.Dictionary);
        Assert.Equal(originalEntries, details.Dictionary.Entries.ToArray());
        return details;
    }

    [Theory]
    [InlineData("(unopened.pdf)")]
    [InlineData("<756e6f70656e65642e706466>")]
    public void External_root_file_strings_remain_inert_and_do_not_require_a_target(string file)
    {
        var details = ReadDetails(LoadAction($"/S /GoToE /F {file} /D [0 /Fit]"));

        Assert.Equal("unopened.pdf", Assert.IsType<PdfString>(details.FileSpecification).Text);
        Assert.Same(details.Dictionary.Get("F"), details.FileSpecification);
        var destination = Assert.IsType<PdfArray>(details.Destination);
        Assert.Equal(0L, Assert.IsType<PdfInteger>(destination[0]).Value);
        Assert.Equal("Fit", Assert.IsType<PdfName>(destination[1]).Value);
        Assert.Null(details.Target);
        Assert.Null(details.NewWindow);
    }

    [Theory]
    [InlineData("/Chapter", true)]
    [InlineData("(Chapter)", false)]
    [InlineData("<43686170746572>", false)]
    public void Target_only_actions_preserve_named_destinations_without_local_lookup(string destination, bool isName)
    {
        var details = ReadDetails(LoadAction($"/S /GoToE /D {destination} /T << /R /C /N (embedded.pdf) >>"));

        Assert.Null(details.FileSpecification);
        if (isName)
            Assert.Equal("Chapter", Assert.IsType<PdfName>(details.Destination).Value);
        else
            Assert.Equal("Chapter", Assert.IsType<PdfString>(details.Destination).Text);
        Assert.Same(details.Dictionary.Get("D"), details.Destination);
        var target = Assert.IsType<PdfDictionary>(details.Target);
        Assert.Same(details.Dictionary.Get("T"), target);
        Assert.Equal("C", Assert.IsType<PdfName>(target.Get("R")).Value);
        Assert.Equal("embedded.pdf", Assert.IsType<PdfString>(target.Get("N")).Text);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Local_indirect_values_are_resolved_without_reading_embedded_content_or_nested_targets(bool indirect, bool newWindow)
    {
        const string file = "<< /Type /Filespec /F (unopened.pdf) /EF << /F 8 0 R >> >>";
        const string target = "<< /R /P /T 5 0 R >>";
        var doc = LoadAction(
            $"/S {(indirect ? "9 0 R" : "/GoToE")} /F {(indirect ? "6 0 R" : file)} " +
            $"/D 7 0 R /T {(indirect ? "5 0 R" : target)} /NewWindow {newWindow.ToString().ToLowerInvariant()}",
            $"5 0 obj\n{target}\nendobj\n" +
            $"6 0 obj\n{file}\nendobj\n" +
            "7 0 obj\n[999 /XYZ null 12 1.5]\nendobj\n" +
            "8 0 obj\n<< /Filter /Unsupported /Length 4 >>\nstream\ndata\nendstream\nendobj\n" +
            "9 0 obj\n/GoToE\nendobj\n");
        var details = ReadDetails(doc);

        var specification = Assert.IsType<PdfDictionary>(details.FileSpecification);
        Assert.Same(doc.File.Resolve(details.Dictionary.Get("F")), specification);
        Assert.IsType<PdfRef>(Assert.IsType<PdfDictionary>(specification.Get("EF")).Get("F"));
        var destination = Assert.IsType<PdfArray>(details.Destination);
        Assert.Same(doc.File.Resolve(details.Dictionary.Get("D")), destination);
        Assert.Equal(999L, Assert.IsType<PdfInteger>(destination[0]).Value);
        Assert.IsType<PdfNull>(destination[2]);
        var targetDictionary = Assert.IsType<PdfDictionary>(details.Target);
        Assert.Same(doc.File.Resolve(details.Dictionary.Get("T")), targetDictionary);
        Assert.IsType<PdfRef>(targetDictionary.Get("T"));
        Assert.Equal(newWindow, details.NewWindow);
        Assert.IsType<PdfRef>(details.Dictionary.Get("D"));
        if (indirect)
        {
            Assert.IsType<PdfRef>(details.Dictionary.Get("S"));
            Assert.IsType<PdfRef>(details.Dictionary.Get("T"));
        }
    }

    [Fact]
    public void Indirect_window_flag_is_resolved_without_defaulting_absence_to_false()
    {
        var details = ReadDetails(LoadAction("/S /GoToE /D [0 /Fit] /T << /R /P >> /NewWindow 5 0 R",
            "5 0 obj\nfalse\nendobj\n"));

        Assert.False(details.NewWindow);
        Assert.IsType<PdfRef>(details.Dictionary.Get("NewWindow"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/F null /D null /T null /NewWindow null")]
    [InlineData("/F /Wrong /D 42 /T (wrong) /NewWindow (true)")]
    [InlineData("/F [] /D << /D [0 /Fit] >> /T [] /NewWindow 1")]
    [InlineData("/F true /D false /T /C /NewWindow /True")]
    [InlineData("/F 9 0 R /D 9 0 R /T 9 0 R /NewWindow 9 0 R")]
    [InlineData("/F 5 0 R /D 5 0 R /T 5 0 R /NewWindow 5 0 R")]
    [InlineData("/F 6 0 R /D 6 0 R /T 6 0 R /NewWindow 6 0 R")]
    public void Missing_or_wrong_type_entries_are_nullable_without_losing_raw_values(string entries)
    {
        var details = ReadDetails(LoadAction($"/S /GoToE {entries} /Vendor (opaque) /SD [(not-a-GoToE-field) /Fit]",
            "5 0 obj\n<< /Filter /Unsupported /Length 4 >>\nstream\ndata\nendstream\nendobj\n" +
            "6 0 obj\n6 0 R\nendobj\n"));

        Assert.Null(details.FileSpecification);
        Assert.Null(details.Destination);
        Assert.Null(details.Target);
        Assert.Null(details.NewWindow);
        Assert.Equal("opaque", Assert.IsType<PdfString>(details.Dictionary.Get("Vendor")).Text);
        Assert.IsType<PdfArray>(details.Dictionary.Get("SD"));
    }

    [Theory]
    [InlineData("[]", "<< >>")]
    [InlineData("[3 0 R /Fit]", "<< /R /Unknown /N 42 /P -1 /A false /T 4 0 R >>")]
    [InlineData("[-1 /Unknown]", "<< /R /C /P (page-destination) /A (annotation-name) >>")]
    [InlineData("[0 /Fit]", "<< /R /C /P 0 /A 1 /T << /R /P >> >>")]
    public void Target_path_and_destination_contents_are_retained_without_traversal_or_normalisation(string destination, string target)
    {
        var details = ReadDetails(LoadAction($"/S /GoToE /D {destination} /T {target}"));

        Assert.Same(details.Dictionary.Get("D"), Assert.IsType<PdfArray>(details.Destination));
        Assert.Same(details.Dictionary.Get("T"), Assert.IsType<PdfDictionary>(details.Target));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/S null")]
    [InlineData("/S (GoToE)")]
    [InlineData("/S 42")]
    [InlineData("/S 9 0 R")]
    [InlineData("/S /gotoe")]
    [InlineData("/S /GoTo")]
    [InlineData("/S /GoToR")]
    [InlineData("/S /URI")]
    [InlineData("/S /Named")]
    [InlineData("/S /JavaScript")]
    [InlineData("/S /Launch")]
    public void Only_the_exact_embedded_go_to_subtype_gains_metadata(string subtype)
    {
        var action = Assert.Single(LoadAction($"{subtype} /F (unopened.pdf) /D [0 /Fit] /T << /R /P >>").GetActions());

        Assert.Null(action.EmbeddedGoToDetails);
        Assert.IsType<PdfDictionary>(action.Dictionary.Get("T"));
    }

    [Theory]
    [InlineData("5 0 R", "OpenAction/Next")]
    [InlineData("[5 0 R]", "OpenAction/Next[0]")]
    [InlineData("<< /S /GoToE /D /Chapter /T << /R /P >> /Next 4 0 R >>", "OpenAction/Next")]
    public void Next_chains_expose_embedded_metadata_and_stop_cycles(string next, string trigger)
    {
        var doc = LoadAction($"/S /URI /URI (relative) /Next {next}",
            "5 0 obj\n<< /S /GoToE /D /Chapter /T << /R /P >> /Next 4 0 R >>\nendobj\n");
        var actions = doc.GetActions();

        Assert.Equal(2, actions.Count);
        Assert.Null(actions[0].EmbeddedGoToDetails);
        Assert.NotNull(actions[0].UriDetails);
        Assert.Equal(trigger, actions[1].Trigger);
        var details = Assert.IsType<PdfEmbeddedGoToAction>(actions[1].EmbeddedGoToDetails);
        Assert.Same(actions[1].Dictionary, details.Dictionary);
        var nextValue = doc.File.Resolve(actions[0].Dictionary.Get("Next"));
        var dictionary = nextValue is PdfArray array ? doc.File.Resolve(array[0]) : nextValue;
        Assert.Same(dictionary, details.Dictionary);
        Assert.Equal("Chapter", Assert.IsType<PdfName>(details.Destination).Value);
        Assert.IsType<PdfRef>(details.Dictionary.Get("Next"));
    }

    [Fact]
    public void Invalid_embedded_metadata_does_not_prevent_next_siblings_or_change_their_order()
    {
        var doc = LoadAction("/S /GoToE /D 42 /T false /Next [5 0 R 6 0 R]",
            "5 0 obj\n<< /S /GoToE /D (Chapter) /T << /R /P >> /Next 4 0 R >>\nendobj\n" +
            "6 0 obj\n<< /S /Named /N /NextPage >>\nendobj\n");
        var actions = doc.GetActions();

        Assert.Equal(3, actions.Count);
        var invalid = Assert.IsType<PdfEmbeddedGoToAction>(actions[0].EmbeddedGoToDetails);
        Assert.Null(invalid.Destination);
        Assert.Null(invalid.Target);
        Assert.IsType<PdfInteger>(invalid.Dictionary.Get("D"));
        Assert.Equal("OpenAction/Next[0]", actions[1].Trigger);
        Assert.Equal("Chapter", Assert.IsType<PdfString>(Assert.IsType<PdfEmbeddedGoToAction>(actions[1].EmbeddedGoToDetails).Destination).Text);
        Assert.Equal("OpenAction/Next[1]", actions[2].Trigger);
        Assert.Null(actions[2].EmbeddedGoToDetails);
        Assert.NotNull(actions[2].NamedDetails);
    }
}
