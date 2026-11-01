using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class SubmitFormActionTests
{
    static PdfDocument LoadAction(string entries, string objects = "")
    {
        var pdf = "%PDF-2.0\n" +
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R /OpenAction 4 0 R >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>\nendobj\n" +
            $"4 0 obj\n<< {entries} >>\nendobj\n" +
            objects +
            "trailer\n<< /Root 1 0 R /Size 20 >>\n%%EOF\n";
        return PdfDocument.Load(Encoding.ASCII.GetBytes(pdf));
    }

    static PdfSubmitFormAction ReadDetails(PdfDocument doc)
    {
        var action = Assert.Single(doc.GetActions());
        Assert.Equal("SubmitForm", action.ActionType);
        Assert.Null(action.JavaScript);
        Assert.Null(action.GoToDetails);
        Assert.Null(action.RemoteGoToDetails);
        Assert.Null(action.EmbeddedGoToDetails);
        Assert.Null(action.GoToDpDetails);
        Assert.Null(action.UriDetails);
        Assert.Null(action.NamedDetails);
        var details = Assert.IsType<PdfSubmitFormAction>(action.SubmitFormDetails);
        Assert.Same(action.Dictionary, details.Dictionary);
        Assert.Same(doc.File.Resolve(doc.File.Catalog.Get("OpenAction")), details.Dictionary);
        return details;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Direct_and_local_indirect_entries_are_resolved_without_changing_raw_entries(bool indirect)
    {
        const string target = "(https://unopened.invalid/submit?query=%FF)";
        const string fields = "[(parent.child) 9 0 R]";
        var doc = LoadAction(
            $"/S {(indirect ? "5 0 R" : "/SubmitForm")} " +
            $"/F {(indirect ? "6 0 R" : target)} " +
            $"/Fields {(indirect ? "7 0 R" : fields)} " +
            $"/Flags {(indirect ? "8 0 R" : "13")} " +
            $"/CharSet {(indirect ? "10 0 R" : "(utf-8)")}",
            "5 0 obj\n/SubmitForm\nendobj\n" +
            $"6 0 obj\n{target}\nendobj\n" +
            $"7 0 obj\n{fields}\nendobj\n" +
            "8 0 obj\n13\nendobj\n" +
            "9 0 obj\n<< /FT /Tx /T (unrelated) /V (not collected) >>\nendobj\n" +
            "10 0 obj\n(utf-8)\nendobj\n");
        var dictionary = Assert.IsType<PdfDictionary>(doc.File.Resolve(doc.File.Catalog.Get("OpenAction")));
        var originalEntries = dictionary.Entries.ToArray();
        var details = ReadDetails(doc);

        Assert.Equal("https://unopened.invalid/submit?query=%FF", Assert.IsType<PdfString>(details.FileSpecification).Text);
        Assert.Same(doc.File.Resolve(dictionary.Get("F")), details.FileSpecification);
        Assert.Same(doc.File.Resolve(dictionary.Get("Fields")), details.Fields);
        var array = Assert.IsType<PdfArray>(details.Fields);
        Assert.Equal("parent.child", Assert.IsType<PdfString>(array[0]).Text);
        Assert.Equal(new PdfRef(9, 0), Assert.IsType<PdfRef>(array[1]));
        Assert.Equal(13L, details.Flags);
        Assert.Equal("utf-8", Assert.IsType<PdfString>(details.CharSet).Text);
        Assert.Same(doc.File.Resolve(dictionary.Get("CharSet")), details.CharSet);
        Assert.Equal(originalEntries, dictionary.Entries.ToArray());
        if (indirect)
            foreach (var key in new[] { "S", "F", "Fields", "Flags", "CharSet" })
                Assert.IsType<PdfRef>(dictionary.Get(key));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Url_file_specification_dictionaries_are_retained_without_accessing_embedded_data(bool indirect)
    {
        const string target = "<< /Type /Filespec /FS /URL /F <68747470733a2f2fff00> /EF << /F 6 0 R >> /Vendor (opaque) >>";
        var doc = LoadAction($"/S /SubmitForm /F {(indirect ? "5 0 R" : target)}",
            $"5 0 obj\n{target}\nendobj\n" +
            "6 0 obj\n<< /Filter /Unsupported /Length 4 >>\nstream\ndata\nendstream\nendobj\n");
        var details = ReadDetails(doc);

        var file = Assert.IsType<PdfDictionary>(details.FileSpecification);
        Assert.Same(doc.File.Resolve(details.Dictionary.Get("F")), file);
        Assert.Equal("URL", Assert.IsType<PdfName>(file.Get("FS")).Value);
        Assert.Equal(new byte[] { 0x68, 0x74, 0x74, 0x70, 0x73, 0x3a, 0x2f, 0x2f, 0xff, 0x00 },
            Assert.IsType<PdfString>(file.Get("F")).Bytes);
        Assert.IsType<PdfRef>(Assert.IsType<PdfDictionary>(file.Get("EF")).Get("F"));
        Assert.Equal("opaque", Assert.IsType<PdfString>(file.Get("Vendor")).Text);
    }

    [Theory]
    [InlineData("(\\150\\164\\164\\160\\163\\072\\057\\057\\377\\000)")]
    [InlineData("<68747470733a2f2fff00>")]
    public void External_target_bytes_are_not_decoded_normalised_or_accessed(string target)
    {
        var details = ReadDetails(LoadAction($"/S /SubmitForm /F {target}"));

        Assert.Equal(new byte[] { 0x68, 0x74, 0x74, 0x70, 0x73, 0x3a, 0x2f, 0x2f, 0xff, 0x00 },
            Assert.IsType<PdfString>(details.FileSpecification).Bytes);
        Assert.Same(details.Dictionary.Get("F"), details.FileSpecification);
    }

    [Theory]
    [InlineData("[]", 0)]
    [InlineData("[(parent.child) <feff0041>]", 2)]
    [InlineData("[9 0 R 11 0 R]", 2)]
    [InlineData("[/NotAFieldName (missing.field) 9 0 R null 42 << /T (direct) >>]", 6)]
    public void Field_arrays_preserve_names_references_and_malformed_elements_without_lookup(string fields, int count)
    {
        var details = ReadDetails(LoadAction($"/S /SubmitForm /F (unopened) /Fields {fields}"));

        var array = Assert.IsType<PdfArray>(details.Fields);
        Assert.Equal(count, array.Count);
        Assert.Same(details.Dictionary.Get("Fields"), array);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/F null /Fields null /Flags null /CharSet null")]
    [InlineData("/F /Wrong /Fields (name) /Flags (0) /CharSet /utf-8")]
    [InlineData("/F [] /Fields <<>> /Flags 1.5 /CharSet 1")]
    [InlineData("/F 42 /Fields /Name /Flags true /CharSet []")]
    [InlineData("/F false /Fields true /Flags [] /CharSet <<>>")]
    [InlineData("/F /Wrong /Fields null /Flags /Zero /CharSet false")]
    [InlineData("/F null /Fields null /Flags 0.0 /CharSet null")]
    [InlineData("/F 19 0 R /Fields 19 0 R /Flags 19 0 R /CharSet 19 0 R")]
    [InlineData("/F 5 0 R /Fields 6 0 R /Flags 7 0 R /CharSet 8 0 R")]
    public void Missing_or_wrong_type_entries_are_nullable_without_losing_raw_values(string entries)
    {
        var doc = LoadAction($"/S /SubmitForm {entries} /Vendor (opaque)",
            "5 0 obj\n/Wrong\nendobj\n6 0 obj\n(name)\nendobj\n" +
            "7 0 obj\n0.5\nendobj\n8 0 obj\n/utf-8\nendobj\n");
        var dictionary = Assert.IsType<PdfDictionary>(doc.File.Resolve(doc.File.Catalog.Get("OpenAction")));
        var originalEntries = dictionary.Entries.ToArray();
        var details = ReadDetails(doc);

        Assert.Null(details.FileSpecification);
        Assert.Null(details.Fields);
        Assert.Null(details.Flags);
        Assert.Null(details.CharSet);
        Assert.Equal(originalEntries, details.Dictionary.Entries.ToArray());
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(16384L)]
    [InlineData(-1L)]
    [InlineData(-long.MaxValue)]
    [InlineData(long.MaxValue)]
    public void Integer_flags_including_negative_and_reserved_bits_are_not_masked_or_validated(long flags)
    {
        var details = ReadDetails(LoadAction($"/S /SubmitForm /Flags {flags}"));

        Assert.Equal(flags, details.Flags);
        Assert.Equal(flags, Assert.IsType<PdfInteger>(details.Dictionary.Get("Flags")).Value);
        Assert.Null(details.FileSpecification);
    }

    [Fact]
    public void Omitted_flags_are_not_replaced_by_the_pdf_default_and_wrong_types_do_not_hide_valid_metadata()
    {
        var omitted = ReadDetails(LoadAction("/S /SubmitForm /Fields [] /CharSet (utf-8)"));
        var malformed = ReadDetails(LoadAction("/S /SubmitForm /F (unopened) /Fields /Wrong /Flags <<>> /CharSet /utf-8"));

        Assert.Null(omitted.Flags);
        Assert.Null(omitted.Dictionary.Get("Flags"));
        Assert.IsType<PdfArray>(omitted.Fields);
        Assert.IsType<PdfString>(omitted.CharSet);
        Assert.IsType<PdfString>(malformed.FileSpecification);
        Assert.Null(malformed.Fields);
        Assert.Null(malformed.Flags);
        Assert.IsType<PdfDictionary>(malformed.Dictionary.Get("Flags"));
        Assert.Null(malformed.CharSet);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("Shift-JIS")]
    [InlineData("BigFive")]
    [InlineData("GBK")]
    [InlineData("UHC")]
    [InlineData("vendor-encoding")]
    [InlineData("")]
    public void CharSet_labels_are_preserved_without_restricting_or_applying_them(string label)
    {
        var details = ReadDetails(LoadAction($"/S /SubmitForm /CharSet ({label})"));

        Assert.Equal(Encoding.ASCII.GetBytes(label), Assert.IsType<PdfString>(details.CharSet).Bytes);
        Assert.Same(details.Dictionary.Get("CharSet"), details.CharSet);
    }

    [Fact]
    public void Malformed_charset_bytes_are_retained_without_decoding()
    {
        var details = ReadDetails(LoadAction("/S /SubmitForm /CharSet <ff00fe>"));

        Assert.Equal(new byte[] { 0xff, 0x00, 0xfe }, Assert.IsType<PdfString>(details.CharSet).Bytes);
    }

    [Theory]
    [InlineData("GoTo")]
    [InlineData("GoToR")]
    [InlineData("GoToE")]
    [InlineData("GoToDp")]
    [InlineData("URI")]
    [InlineData("Named")]
    [InlineData("JavaScript")]
    [InlineData("ResetForm")]
    [InlineData("Launch")]
    [InlineData("submitform")]
    public void Other_action_types_do_not_gain_submit_form_metadata(string type)
    {
        var action = Assert.Single(LoadAction($"/S /{type} /F (unopened) /Fields [] /Flags 0 /CharSet (utf-8)").GetActions());

        Assert.Null(action.SubmitFormDetails);
        Assert.IsType<PdfString>(action.Dictionary.Get("F"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/S (SubmitForm)")]
    [InlineData("/S null")]
    public void Missing_or_wrong_type_subtypes_do_not_gain_submit_form_metadata(string subtype)
    {
        var action = Assert.Single(LoadAction($"{subtype} /F (unopened) /Flags 0").GetActions());

        Assert.Null(action.SubmitFormDetails);
    }

    [Fact]
    public void Next_arrays_keep_depth_first_order_cycles_and_shared_actions_without_executing_them()
    {
        var doc = LoadAction("/S /URI /URI (https://unopened.invalid/) /Next [5 0 R 6 0 R 5 0 R]",
            "5 0 obj\n<< /S /SubmitForm /F (unopened) /Flags 13 /Next [4 0 R 7 0 R] >>\nendobj\n" +
            "6 0 obj\n<< /S /Named /N /NextPage >>\nendobj\n" +
            "7 0 obj\n<< /S /SubmitForm /CharSet (utf-8) /Next 7 0 R >>\nendobj\n");
        var actions = doc.GetActions();

        Assert.Equal(new[] { "URI", "SubmitForm", "SubmitForm", "Named", "SubmitForm", "SubmitForm" },
            actions.Select(action => action.ActionType).ToArray());
        Assert.Equal(new[]
        {
            "OpenAction",
            "OpenAction/Next[0]",
            "OpenAction/Next[0]/Next[1]",
            "OpenAction/Next[1]",
            "OpenAction/Next[2]",
            "OpenAction/Next[2]/Next[1]",
        }, actions.Select(action => action.Trigger).ToArray());
        Assert.NotNull(actions[0].UriDetails);
        Assert.Null(actions[0].SubmitFormDetails);
        Assert.NotNull(actions[3].NamedDetails);
        Assert.Null(actions[3].SubmitFormDetails);
        foreach (var index in new[] { 1, 2, 4, 5 })
        {
            var details = Assert.IsType<PdfSubmitFormAction>(actions[index].SubmitFormDetails);
            Assert.Same(actions[index].Dictionary, details.Dictionary);
        }
        Assert.Same(actions[1].Dictionary, actions[4].Dictionary);
        Assert.Same(actions[2].Dictionary, actions[5].Dictionary);
        Assert.IsType<PdfArray>(actions[1].Dictionary.Get("Next"));
        Assert.Equal(new PdfRef(7, 0), Assert.IsType<PdfRef>(actions[2].Dictionary.Get("Next")));
    }
}
