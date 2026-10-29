using PortableDocument.Core;
using PortableDocument.Document;
using Xunit;

namespace AdoBye.Tests;

public class DocumentFeatureTests
{
    static PdfDocument Load() => PdfDocument.Open(Path.Combine(AppContext.BaseDirectory, "fixtures", "features", "features.pdf"));

    [Fact]
    public void Outlines_resolve_to_pages()
    {
        var o = Load().GetOutlines();
        Assert.Equal(3, o.Count);
        Assert.Equal("Chapter 2", o[1].Title);
        Assert.Equal(1, o[1].PageIndex);
    }

    [Fact]
    public void Link_annotation_has_uri()
    {
        var a = Load().Pages[0].GetAnnotations();
        Assert.Contains(a, x => x.Subtype == "Link" && x.Uri == "https://example.com");
    }

    [Fact]
    public void Form_fields_are_listed()
    {
        var f = Load().GetFormFields();
        Assert.Contains(f, x => x.FullName == "firstName" && x.FieldType == "Tx" && x.Value == "Ada");
        Assert.Contains(f, x => x.FullName == "agree" && x.FieldType == "Btn");
    }

    [Fact]
    public void Page_labels_follow_ranges()
    {
        var d = Load();
        Assert.Equal("i", d.GetPageLabel(0));
        Assert.Equal("A-5", d.GetPageLabel(1));
        Assert.Equal("A-6", d.GetPageLabel(2));
    }

    [Fact]
    public void Xmp_and_attachments()
    {
        var d = Load();
        Assert.Contains("xmpmeta", d.GetXmpMetadata());
        var e = Assert.Single(d.GetEmbeddedFiles());
        Assert.Equal("hello.txt", e.Name);
        Assert.Equal("hello attachment", System.Text.Encoding.ASCII.GetString(e.Data));
    }

    static byte[] FileSpecificationPdf()
    {
        const string pdf = "%PDF-2.0\n" +
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R /Names << /EmbeddedFiles << /Names [(attachment) 5 0 R] >> >> /AF [5 0 R] /AcroForm << /AF [5 0 R] /Fields [7 0 R] >> /Collection << /View /D /Schema << /Title << /Subtype /S /N (Title) >> /Date << /Subtype /D /N (Date) /O 1 /V false /E true >> >> /Sort << /S [/Title /Date] /A [true false] >> >> /StructTreeRoot 23 0 R >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] /AF [5 0 R] /Annots [4 0 R] >>\nendobj\n" +
            "4 0 obj\n<< /Type /Annot /Subtype /FileAttachment /Rect [10 10 20 20] /AF [5 0 R] >>\nendobj\n" +
            "5 0 obj\n<< /Type /Filespec /F (fallback.txt) /UF (unicode.txt) /DOS (DOS.TXT) /Mac (Mac.txt) /Unix (unix.txt) /Desc (attachment description) /ID [(first-id) (second-id)] /V true /AFRelationship /Data /CI << /Title (A report) >> /RF << /F [22 0 R] >> /EF << /F 6 0 R /UF 6 0 R >> >>\nendobj\n" +
            "6 0 obj\n<< /Type /EmbeddedFile /Subtype /text#2Fplain /Params << /Size 5 /CreationDate (D:20260101000000Z) /ModDate (D:20260102000000Z) /CheckSum <01020304> >> /Length 5 >>\nstream\nhello\nendstream\nendobj\n" +
            "7 0 obj\n<< /T (attachedField) /AF [5 0 R] /Kids [7 0 R] >>\nendobj\n" +
            "22 0 obj\n<< /Type /Filespec /F (source.dat) /RF << /F [5 0 R] >> >>\nendobj\n" +
            "23 0 obj\n<< /Type /StructTreeRoot /K 24 0 R >>\nendobj\n" +
            "24 0 obj\n<< /Type /StructElem /S /Figure /AF [5 0 R] /K 24 0 R >>\nendobj\n" +
            "trailer\n<< /Root 1 0 R /Size 25 >>\n%%EOF\n";
        return System.Text.Encoding.ASCII.GetBytes(pdf);
    }

    [Fact]
    public void File_specifications_expose_metadata_and_associated_files_without_losing_legacy_api()
    {
        var doc = PdfDocument.Load(FileSpecificationPdf());
        var spec = Assert.Single(doc.GetFileSpecifications());

        Assert.Equal("attachment", spec.Name);
        Assert.Equal("fallback.txt", spec.FileName);
        Assert.Equal("unicode.txt", spec.UnicodeFileName);
        Assert.Equal("DOS.TXT", spec.DosFileName);
        Assert.Equal("Mac.txt", spec.MacFileName);
        Assert.Equal("unix.txt", spec.UnixFileName);
        Assert.Equal("attachment description", spec.Description);
        Assert.Equal(new[] { "first-id", "second-id" }, spec.Id);
        Assert.True(spec.IsVolatile);
        Assert.Equal("Data", spec.AFRelationship);
        var related = Assert.Single(spec.RelatedFiles);
        Assert.Equal("F", related.Platform);
        Assert.Equal("source.dat", related.FileName);
        Assert.NotNull(related.Dictionary.Get("RF"));
        var nestedRelated = Assert.Single(related.RelatedFiles);
        Assert.Equal("unicode.txt", nestedRelated.FileName);
        Assert.Empty(nestedRelated.RelatedFiles);
        Assert.Equal("A report", doc.File.Resolve(spec.CollectionItem?.Get("Title")) is PdfString title ? title.Text : null);

        var embedded = spec.EmbeddedFiles["UF"];
        Assert.Equal("text/plain", embedded.MimeType);
        Assert.Equal(5, embedded.Size);
        Assert.Equal("hello", System.Text.Encoding.ASCII.GetString(embedded.Data));
        Assert.Equal("D:20260101000000Z", embedded.CreationDate);
        Assert.Equal("D:20260102000000Z", embedded.ModificationDate);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, embedded.Checksum);
        Assert.Same(spec.EmbeddedFiles["F"].Stream, embedded.Stream);
        Assert.NotNull(embedded.Parameters);
        Assert.NotNull(embedded.Stream);

        var associated = doc.GetAssociatedFiles();
        Assert.Equal(6, associated.Count);
        Assert.Contains(associated, file => file.Scope == PdfAssociatedFileScope.Catalog);
        Assert.Contains(associated, file => file.Scope == PdfAssociatedFileScope.AcroForm);
        Assert.Contains(associated, file => file.Scope == PdfAssociatedFileScope.FormField && file.FieldName == "attachedField");
        Assert.Contains(associated, file => file.Scope == PdfAssociatedFileScope.StructureElement && file.StructureRole == "Figure");
        Assert.Contains(associated, file => file.Scope == PdfAssociatedFileScope.Page && file.PageIndex == 0);
        Assert.Contains(associated, file => file.Scope == PdfAssociatedFileScope.Annotation && file.PageIndex == 0 && file.AnnotationIndex == 0);

        var collection = Assert.IsType<PdfDocumentCollection>(doc.GetCollection());
        Assert.Equal("D", collection.View);
        Assert.NotNull(collection.Schema?.Get("Title"));
        Assert.NotNull(collection.Sort?.Get("S"));
        Assert.Collection(collection.SchemaFields,
            field => Assert.Equal("Title", field.Key),
            field =>
            {
                Assert.Equal("Date", field.Key);
                Assert.Equal("D", field.Subtype);
                Assert.Equal("Date", field.Name);
                Assert.Equal(1d, field.Order);
                Assert.False(field.Visible);
                Assert.True(field.Editable);
            });
        Assert.Equal(new[] { "Title", "Date" }, collection.SortDefinition?.Fields);
        Assert.Equal(new bool?[] { true, false }, collection.SortDefinition?.Ascending);
        Assert.Same(collection.Dictionary, doc.File.Resolve(doc.File.Catalog.Get("Collection")));

        var legacy = Assert.Single(doc.GetEmbeddedFiles());
        Assert.Equal("attachment", legacy.Name);
        Assert.Equal("hello", System.Text.Encoding.ASCII.GetString(legacy.Data));
    }

    static byte[] ActiveContentPdf()
    {
        const string pdf = "%PDF-2.0\n" +
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R /OpenAction 4 0 R /Names << /JavaScript << /Names [(named) 5 0 R] >> >> /AA << /WC 18 0 R >> /Outlines 25 0 R /AcroForm << /Fields [7 0 R] /XFA [(template) 8 0 R (datasets) 9 0 R] >> >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] /Annots [6 0 R 19 0 R] /AA << /O 10 0 R >> >>\nendobj\n" +
            "4 0 obj\n<< /S /JavaScript /JS (open-action-code) /Next [12 0 R 13 0 R] >>\nendobj\n" +
            "5 0 obj\n<< /S /JavaScript /JS 14 0 R >>\nendobj\n" +
            "6 0 obj\n<< /Type /Annot /Subtype /Text /Rect [10 10 20 20] /A 11 0 R /AA << /E 15 0 R >> >>\nendobj\n" +
            "7 0 obj\n<< /T (form) /A 16 0 R /AA << /K 17 0 R >> >>\nendobj\n" +
            "8 0 obj\n<< /Type /EmbeddedFile /Subtype /application#2Fxml /Length 10 >>\nstream\n<template>\nendstream\nendobj\n" +
            "9 0 obj\n<< /Type /EmbeddedFile /Subtype /application#2Fxml /Length 10 >>\nstream\n<datasets>\nendstream\nendobj\n" +
            "10 0 obj\n<< /S /URI /URI (https://example.com) >>\nendobj\n" +
            "11 0 obj\n<< /S /JavaScript /JS (annotation-action-code) >>\nendobj\n" +
            "12 0 obj\n<< /S /URI /URI (https://example.org) >>\nendobj\n" +
            "13 0 obj\n<< /S /JavaScript /JS (next-action-code) >>\nendobj\n" +
            "14 0 obj\n<< /Length 17 >>\nstream\nnamed-action-code\nendstream\nendobj\n" +
            "15 0 obj\n<< /S /Launch /F (opaque-target) >>\nendobj\n" +
            "16 0 obj\n<< /S /JavaScript /JS (field-action-code) >>\nendobj\n" +
            "17 0 obj\n<< /S /JavaScript /JS (field-key-action-code) >>\nendobj\n" +
            "18 0 obj\n<< /S /URI /URI (https://cycle.example) /Next 18 0 R >>\nendobj\n" +
            "19 0 obj\n<< /Type /Annot /Subtype /RichMedia /Rect [20 20 100 100] /RichMediaContent << /Assets << /Names [(movie) 20 0 R] >> /Configurations [22 0 R] >> /RichMediaSettings << /Activation << /Condition /XA >> >> >>\nendobj\n" +
            "20 0 obj\n<< /Type /Filespec /F (movie.mp4) /EF << /F 21 0 R >> >>\nendobj\n" +
            "21 0 obj\n<< /Type /EmbeddedFile /Subtype /video#2Fmp4 /Length 4 >>\nstream\ndata\nendstream\nendobj\n" +
            "22 0 obj\n<< /Type /RichMediaConfiguration /Subtype /Video /Name (Movie) /Instances [23 0 R] >>\nendobj\n" +
            "23 0 obj\n<< /Type /RichMediaInstance /Subtype /Video /Asset 20 0 R /Params << /Volume 0.5 >> >>\nendobj\n" +
            "25 0 obj\n<< /Type /Outlines /First 26 0 R >>\nendobj\n" +
            "26 0 obj\n<< /Title (scripted outline) /A 27 0 R >>\nendobj\n" +
            "27 0 obj\n<< /S /JavaScript /JS (outline-action-code) >>\nendobj\n" +
            "trailer\n<< /Root 1 0 R /Size 28 >>\n%%EOF\n";
        return System.Text.Encoding.ASCII.GetBytes(pdf);
    }

    [Fact]
    public void Actions_and_xfa_are_exposed_as_opaque_data_without_execution()
    {
        var doc = PdfDocument.Load(ActiveContentPdf());
        var actions = doc.GetActions();

        Assert.Equal(11, actions.Count);
        Assert.Contains(actions, action => action.Scope == PdfActionScope.Catalog
            && action.Trigger == "OpenAction" && action.ActionType == "JavaScript"
            && action.JavaScript is PdfString { Text: "open-action-code" });
        Assert.Contains(actions, action => action.Trigger == "OpenAction/Next[1]"
            && action.JavaScript is PdfString { Text: "next-action-code" });
        Assert.Contains(actions, action => action.Scope == PdfActionScope.NamedJavaScript
            && action.Name == "named" && action.JavaScript is PdfStream);
        Assert.Contains(actions, action => action.Scope == PdfActionScope.FormField
            && action.FieldName == "form" && action.JavaScript is PdfString { Text: "field-action-code" });
        Assert.Contains(actions, action => action.Scope == PdfActionScope.Annotation
            && action.JavaScript is PdfString { Text: "annotation-action-code" });
        Assert.Contains(actions, action => action.Scope == PdfActionScope.Outline
            && action.Name == "scripted outline" && action.JavaScript is PdfString { Text: "outline-action-code" });
        Assert.Contains(actions, action => action.ActionType == "Launch"
            && action.Dictionary.Get("F") is PdfString { Text: "opaque-target" });

        var xfa = Assert.IsType<PdfArray>(doc.GetXfaData());
        Assert.Equal("template", Assert.IsType<PdfString>(xfa[0]).Text);
        Assert.Equal("<template>", System.Text.Encoding.ASCII.GetString(Assert.IsType<PdfStream>(doc.File.Resolve(xfa[1])).RawData));
        var xfaPackets = doc.GetXfaPackets();
        Assert.Equal(2, xfaPackets.Count);
        Assert.Equal("template", xfaPackets[0].Name);
        Assert.Equal("<template>", System.Text.Encoding.ASCII.GetString(doc.File.DecodeStream(xfaPackets[0].Stream)));
        Assert.Equal("datasets", xfaPackets[1].Name);

        var singleStreamPdf = System.Text.Encoding.ASCII.GetString(ActiveContentPdf())
            .Replace("/XFA [(template) 8 0 R (datasets) 9 0 R]", "/XFA 8 0 R", StringComparison.Ordinal);
        var singleDoc = PdfDocument.Load(System.Text.Encoding.ASCII.GetBytes(singleStreamPdf));
        var singlePacket = Assert.Single(singleDoc.GetXfaPackets());
        Assert.Null(singlePacket.Name);
        Assert.Equal("<template>", System.Text.Encoding.ASCII.GetString(singleDoc.File.DecodeStream(singlePacket.Stream)));

        var richMedia = Assert.Single(doc.Pages[0].GetAnnotations(), annotation => annotation.Subtype == "RichMedia");
        Assert.NotNull(richMedia.Dict.Get("RichMediaContent"));
        var richMediaAsset = Assert.Single(doc.GetRichMediaAssets());
        Assert.Equal("movie", richMediaAsset.Name);
        Assert.Equal("movie.mp4", richMediaAsset.FileSpecification.FileName);
        Assert.Equal("video/mp4", richMediaAsset.FileSpecification.EmbeddedFiles["F"].MimeType);
        var richMediaConfiguration = Assert.Single(doc.GetRichMediaConfigurations());
        Assert.Equal("Movie", richMediaConfiguration.Name);
        Assert.Equal("Video", richMediaConfiguration.Subtype);
        var richMediaInstance = Assert.Single(richMediaConfiguration.Instances);
        Assert.Equal("Video", richMediaInstance.Subtype);
        Assert.Same(richMediaAsset.FileSpecification.Dictionary, richMediaInstance.Asset);
        Assert.Equal(0.5, doc.File.Resolve(richMediaInstance.Parameters?.Get("Volume")).AsNumber());
        var richMediaSettings = Assert.Single(doc.GetRichMediaSettings());
        Assert.Equal("XA", doc.File.Resolve(richMediaSettings.Activation?.Get("Condition")).AsName());
        var roundTrip = PdfDocument.Load(PdfEditor.Open(doc).Save());
        Assert.Equal("<template>", System.Text.Encoding.ASCII.GetString(Assert.IsType<PdfStream>(
            roundTrip.File.Resolve(Assert.IsType<PdfArray>(roundTrip.GetXfaData())[1])).RawData));
        Assert.Equal(new[] { "template", "datasets" }, roundTrip.GetXfaPackets().Select(packet => packet.Name));
        Assert.Contains(roundTrip.GetActions(), action => action.JavaScript is PdfString { Text: "open-action-code" });
        Assert.NotNull(Assert.Single(roundTrip.Pages[0].GetAnnotations(), annotation => annotation.Subtype == "RichMedia").Dict.Get("RichMediaContent"));
        Assert.Equal("movie.mp4", Assert.Single(roundTrip.GetRichMediaAssets()).FileSpecification.FileName);
        Assert.Equal("Movie", Assert.Single(roundTrip.GetRichMediaConfigurations()).Name);
        Assert.Equal("XA", roundTrip.File.Resolve(Assert.Single(roundTrip.GetRichMediaSettings()).Activation?.Get("Condition")).AsName());
    }
}
