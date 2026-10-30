using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;

namespace AdoBye.Tests;

public class FileSpecificationMetadataTests
{
    static PdfDocument Load(string metadata, string objects = "") =>
        PdfDocument.Load(Encoding.ASCII.GetBytes(
            "%PDF-2.0\n" +
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R /Names << /EmbeddedFiles << /Names [(attachment) 3 0 R] >> >> >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n" +
            $"3 0 obj\n<< /Type /Filespec /F (https://example.invalid/file) /UF (unicode.txt) /EF << /F 4 0 R /UF 5 0 R >> {metadata} >>\nendobj\n" +
            "4 0 obj\n<< /Type /EmbeddedFile /Subtype /text#2Fplain /Params << /Size 5 >> /Length 5 >>\nstream\nhello\nendstream\nendobj\n" +
            "5 0 obj\n<< /Type /EmbeddedFile /Subtype /application#2Foctet-stream /Params << /Size 7 >> /Length 7 >>\nstream\nunicode\nendstream\nendobj\n" +
            objects +
            "trailer\n<< /Root 1 0 R /Size 20 >>\n%%EOF\n"));

    const string ThumbnailObject =
        "7 0 obj\n<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /Filter /UnsupportedThumbnailFilter /Length 3 >>\nstream\nraw\nendstream\nendobj\n";

    [Theory]
    [InlineData("URL")]
    [InlineData("CustomFileSystem")]
    public void File_specification_metadata_is_exposed_without_interpreting_active_content(string fileSystem)
    {
        var doc = Load($"/FS /{fileSystem} /Thumb 7 0 R /EP << /Type /EncryptedPayload /Subtype /CustomCrypt >>", ThumbnailObject);
        var spec = Assert.Single(doc.GetFileSpecifications());

        Assert.Equal(fileSystem, spec.FileSystem);
        var thumbnail = Assert.IsType<PdfStream>(spec.Thumbnail);
        Assert.Equal("raw", Encoding.ASCII.GetString(thumbnail.RawData));
        Assert.Same(doc.File.Resolve(spec.Dictionary.Get("Thumb")), thumbnail);
        var payload = Assert.IsType<PdfDictionary>(spec.EncryptedPayload);
        Assert.Equal("CustomCrypt", payload.Get("Subtype").AsName());
        Assert.Same(doc.File.Resolve(spec.Dictionary.Get("EP")), payload);
        Assert.Equal("https://example.invalid/file", spec.FileName);
        Assert.Equal("hello", Encoding.ASCII.GetString(spec.EmbeddedFiles["F"].Data));

        var legacy = Assert.Single(doc.GetEmbeddedFiles());
        var (name, mimeType, size, data) = legacy;
        Assert.Equal("attachment", name);
        Assert.Equal("application/octet-stream", mimeType);
        Assert.Equal(7, size);
        Assert.Equal("unicode", Encoding.ASCII.GetString(data));
    }

    [Fact]
    public void File_specification_metadata_resolves_indirect_values()
    {
        var doc = Load("/FS 6 0 R /Thumb 7 0 R /EP 8 0 R",
            "6 0 obj\n/URL\nendobj\n" +
            ThumbnailObject +
            "8 0 obj\n<< /Type /EncryptedPayload /Subtype 9 0 R /PrivateMetadata (opaque) >>\nendobj\n" +
            "9 0 obj\n/CustomCrypt\nendobj\n");
        var spec = Assert.Single(doc.GetFileSpecifications());

        Assert.Equal("URL", spec.FileSystem);
        Assert.IsType<PdfStream>(spec.Thumbnail);
        var payload = Assert.IsType<PdfDictionary>(spec.EncryptedPayload);
        Assert.IsType<PdfRef>(payload.Get("Subtype"));
        Assert.Equal("CustomCrypt", doc.File.Resolve(payload.Get("Subtype")).AsName());
        Assert.Equal("opaque", Assert.IsType<PdfString>(payload.Get("PrivateMetadata")).Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/FS null /Thumb null /EP null")]
    [InlineData("/FS (URL) /Thumb << /Subtype /Image >> /EP (not a dictionary)")]
    [InlineData("/FS 12 /Thumb /Image /EP [1 2]")]
    [InlineData("/FS 16 0 R /Thumb 17 0 R /EP 18 0 R")]
    public void Missing_or_wrong_type_metadata_remains_null_without_losing_attachment_data(string metadata)
    {
        var doc = Load(metadata);
        var spec = Assert.Single(doc.GetFileSpecifications());

        Assert.Null(spec.FileSystem);
        Assert.Null(spec.Thumbnail);
        Assert.Null(spec.EncryptedPayload);
        Assert.NotNull(spec.Dictionary);
        Assert.Equal("unicode", Encoding.ASCII.GetString(Assert.Single(doc.GetEmbeddedFiles()).Data));
    }

    static PdfEmbeddedFile LoadParams(string parameters)
    {
        var doc = PdfDocument.Load(Encoding.ASCII.GetBytes(
            "%PDF-2.0\n" +
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R /Names << /EmbeddedFiles << /Names [(a) 3 0 R] >> >> >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n" +
            "3 0 obj\n<< /Type /Filespec /F (a.txt) /EF << /F 4 0 R >> >>\nendobj\n" +
            $"4 0 obj\n<< /Type /EmbeddedFile {parameters} /Length 5 >>\nstream\nhello\nendstream\nendobj\n" +
            "trailer\n<< /Root 1 0 R /Size 5 >>\n%%EOF\n"));
        return Assert.Single(doc.GetEmbeddedFiles());
    }

    [Theory]
    [InlineData("/Params << /Size 5 /CheckSum <5d41402abc4b2a76b9719d911017c592> >>", true, true)]
    [InlineData("/Params << /Size 6 /CheckSum <5d41402abc4b2a76b9719d911017c592> >>", false, true)]
    [InlineData("/Params << /Size 5 /CheckSum <5d41402abc4b2a76b9719d911017c593> >>", true, false)]
    [InlineData("/Params << /CheckSum <5d41> >>", null, false)]
    [InlineData("/Params << /CheckSum (not a digest) /Size (5) >>", null, false)]
    [InlineData("/Params << /ModDate (D:20250101000000Z) >>", null, null)]
    [InlineData("", null, null)]
    public void Embedded_file_size_and_md5_checksum_are_verified_without_altering_the_payload(string parameters, bool? size, bool? checksum)
    {
        var file = LoadParams(parameters);
        Assert.Equal(size, file.SizeMatches);
        Assert.Equal(checksum, file.ChecksumMatches);
        Assert.Equal("hello", Encoding.ASCII.GetString(file.Data));
    }
}
