using System.IO.Compression;
using PortableDocument.Core;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class EmptyFlateEncodingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_encoding_is_a_complete_independent_zlib_stream(bool appearance)
    {
        var encoded = Encode([], appearance);
        Assert.Equal(new byte[] { 0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01 }, encoded);
        Assert.Empty(StrictDecode(encoded));
        encoded[0] = 0;
        Assert.Equal(0x78, Encode([], appearance)[0]);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 256)]
    [InlineData(true, 256)]
    [InlineData(false, 81921)]
    [InlineData(true, 81921)]
    public void Nonempty_encoding_is_byte_identical_to_existing_compressor(bool appearance, int length)
    {
        var source = new byte[length];
        new Random(42).NextBytes(source);
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, true)) zlib.Write(source);
        var encoded = Encode(source, appearance);
        Assert.Equal(output.ToArray(), encoded);
        Assert.Equal(source, StrictDecode(encoded));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Empty_attachment_has_valid_encoded_bytes_after_full_or_incremental_save(bool objectStreams, bool incremental)
    {
        var original = PdfEditor.Open(TestPdf.Build("")).Save(options: new PdfWriteOptions { ObjectStreams = objectStreams });
        var editor = PdfEditor.Open(original);
        editor.AddAnnotation(0, new PdfAnnotationSpec
        {
            Subtype = "FileAttachment", Rect = [10, 10, 30, 30],
            Attachment = new PdfAnnotationAttachment("empty.bin", [])
        }, new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero));
        var document = PdfDocument.Load(incremental ? editor.SaveIncremental() : editor.Save());
        var file = document.File;
        Assert.False(file.Repaired);
        var annotation = Assert.IsType<PdfDictionary>(file.Resolve(
            Assert.IsType<PdfArray>(file.Resolve(document.Pages[0].Dict.Get("Annots")))[0]));
        var specification = Assert.IsType<PdfDictionary>(file.Resolve(annotation.Get("FS")));
        var embedded = Assert.IsType<PdfDictionary>(file.Resolve(specification.Get("EF")));
        var stream = Assert.IsType<PdfStream>(file.Resolve(embedded.Get("F")));
        Assert.Equal("FlateDecode", stream.Dict.Get("Filter").AsName());
        Assert.Equal(stream.RawData.Length, stream.Dict.Get("Length").AsNumber());
        Assert.Empty(StrictDecode(stream.RawData));
        Assert.Empty(file.DecodeStream(stream));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Writer_preserves_unfiltered_empty_stream_without_adding_compression(bool objectStreams)
    {
        var editor = PdfEditor.Create();
        var reference = editor.AddObject(new PdfStream(new PdfDictionary(), []));
        editor.SetCatalogEntry("Probe", reference);
        var file = PdfFile.Load(editor.Save(options: new PdfWriteOptions { Compress = true, ObjectStreams = objectStreams }));
        var stream = Assert.IsType<PdfStream>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Null(stream.Dict.Get("Filter"));
        Assert.Empty(stream.RawData);
        Assert.False(file.Repaired);
    }

    [Theory]
    [InlineData("page")]
    [InlineData("form")]
    [InlineData("embedded")]
    public void Sample_authoring_emits_complete_empty_zlib_streams(string kind)
    {
        var source = new FreeDF.Samples.Kit.Doc();
        source.Page();
        if (kind != "page")
        {
            var number = kind == "form" ? new FreeDF.Samples.Kit.XForm(source, [0, 0, 100, 100]).Build()
                : source.Stream("/Type /EmbeddedFile", []);
            source.Cat.Add($"/Probe {number} 0 R");
        }
        var document = PdfDocument.Load(source.Build());
        var file = document.File;
        var stream = Assert.IsType<PdfStream>(file.Resolve(
            kind == "page" ? document.Pages[0].Dict.Get("Contents") : file.Catalog.Get("Probe")));
        Assert.Equal("FlateDecode", stream.Dict.Get("Filter").AsName());
        Assert.Empty(StrictDecode(stream.RawData));
        Assert.False(file.Repaired);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(81921)]
    public void Sample_authoring_retains_smallest_size_encoding_for_nonempty_data(int length)
    {
        var source = new FreeDF.Samples.Kit.Doc();
        source.Page();
        var payload = new byte[length];
        new Random(42).NextBytes(payload);
        source.Cat.Add($"/Probe {source.Stream("/Type /EmbeddedFile", payload)} 0 R");
        var file = PdfFile.Load(source.Build());
        var stream = Assert.IsType<PdfStream>(file.Resolve(file.Catalog.Get("Probe")));
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, true)) zlib.Write(payload);
        Assert.Equal(output.ToArray(), stream.RawData);
        Assert.Equal(payload, StrictDecode(stream.RawData));
    }

    static byte[] Encode(byte[] source, bool appearance) =>
        appearance ? AnnotationAppearance.Deflate(source) : PdfFilters.DeflateEncode(source);

    static byte[] StrictDecode(byte[] encoded) =>
        PdfFilters.Decode(encoded, [("FlateDecode", null)], x => x, strictContentFilters: true).Data;
}
