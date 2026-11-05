using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfReferenceGenerationTests
{
    static PdfFile Load(int generation = 0, bool repaired = false, int rootGeneration = 0)
    {
        var text = new StringBuilder("%PDF-2.0\n");
        var offsets = new List<(int Offset, int Generation)>();
        void Object(int number, int gen, string body)
        {
            offsets.Add((text.Length, gen));
            text.Append($"{number} {gen} obj\n{body}\nendobj\n");
        }
        Object(1, rootGeneration, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, 0, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object(3, 0, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>");
        Object(4, generation, "<< /Type /GenerationTarget >>");
        Object(5, 0, $"4 {generation} R");
        Object(6, 0, $"4 {(generation == 0 ? 1 : 0)} R");
        Object(7, 0, ObjPdf.Stream("8 0 42", "/Type /ObjStm /N 1 /First 4"));
        var xref = text.Length;
        text.Append("xref\n0 8\n0000000000 65535 f \n");
        foreach (var (offset, gen) in offsets) text.Append($"{offset:D10} {gen:D5} n \n");
        text.Append($"trailer\n<< /Size 9 /Root 1 {rootGeneration} R >>\nstartxref\n{(repaired ? 0 : xref)}\n%%EOF\n");
        var file = PdfFile.Load(Encoding.ASCII.GetBytes(text.ToString()));
        Assert.Equal(repaired, file.Repaired);
        return file;
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(7, false)]
    [InlineData(65535, false)]
    [InlineData(0, true)]
    [InlineData(7, true)]
    public void Resolution_honors_generation_before_and_after_caching(int generation, bool repaired)
    {
        var file = Load(generation, repaired);
        var matching = new PdfRef(4, generation);
        var stale = new PdfRef(4, generation == 0 ? 1 : 0);
        Assert.IsType<PdfNull>(file.Resolve(stale));
        Assert.Same(file.GetObject(4), file.Resolve(matching));
        Assert.IsType<PdfNull>(file.Resolve(stale));
        Assert.Same(file.Resolve(matching), file.Resolve(matching));
        Assert.IsType<PdfNull>(file.Resolve(new PdfRef(99, generation)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Every_reference_chain_hop_must_match_generation(int generation)
    {
        var file = Load(generation);
        Assert.Same(file.GetObject(4), file.Resolve(new PdfRef(5, 0)));
        Assert.IsType<PdfNull>(file.Resolve(new PdfRef(5, 1)));
        Assert.IsType<PdfNull>(file.Resolve(new PdfRef(6, 0)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(9)]
    public void Compressed_references_use_generation_zero_not_the_member_index(int index)
    {
        var file = Load();
        file.Xref[8] = new(true, true, 7, index);
        Assert.Equal(new PdfInteger(42), file.Resolve(new PdfRef(8, 0)));
        Assert.IsType<PdfNull>(file.Resolve(new PdfRef(8, 1)));
        Assert.IsType<PdfNull>(file.Resolve(new PdfRef(8, index == 0 ? 9 : index)));
    }

    [Fact]
    public void Editor_incremental_metadata_preserves_nonzero_catalog_and_info_generations()
    {
        var source = Load(7, rootGeneration: 3);
        source.Catalog.Entries["Probe"] = new PdfRef(4, 7);
        var original = PdfWriter.WriteIncremental(source.Data, StartXref(source), new Dictionary<int, PdfObject>
        {
            [1] = source.Catalog,
        }, 9, 1, 4, null, false, null, null, source.Xref);
        var editor = PdfEditor.Open(original);
        editor.SetInfo("Title", "updated");
        var file = PdfFile.Load(editor.SaveIncremental());
        Assert.False(file.Repaired);
        Assert.Equal(new PdfRef(1, 3), file.Trailer.Get("Root"));
        Assert.Equal(new PdfRef(4, 7), file.Trailer.Get("Info"));
        Assert.Equal(7, file.Xref[4].GenOrIndex);
        Assert.Equal("updated", Assert.IsType<PdfString>(Assert.IsType<PdfDictionary>(
            file.Resolve(file.Trailer.Get("Info"))).Get("Title")).Text);
        Assert.Same(file.Resolve(file.Trailer.Get("Info")), file.Resolve(file.Catalog.Get("Probe")));
    }

    static long StartXref(PdfFile file)
    {
        var text = Encoding.Latin1.GetString(file.Data);
        return long.Parse(text[(text.LastIndexOf("startxref", StringComparison.Ordinal) + 9)..]
            .Trim().Split('\n', '\r')[0], System.Globalization.CultureInfo.InvariantCulture);
    }

    [Theory]
    [InlineData(false, 0, 0)]
    [InlineData(true, 0, 0)]
    [InlineData(false, 7, 3)]
    [InlineData(true, 7, 3)]
    [InlineData(false, 65535, 7)]
    [InlineData(true, 65535, 7)]
    public void Incremental_xref_and_object_headers_preserve_generation(bool xrefStream, int generation, int rootGeneration)
    {
        var source = Load(generation, rootGeneration: rootGeneration);
        source.Catalog.Entries["Probe"] = new PdfRef(4, generation);
        source.Catalog.Entries["Stale"] = new PdfRef(4, generation == 0 ? 1 : 0);
        var value = new PdfString(Encoding.ASCII.GetBytes("updated"));
        var bytes = PdfWriter.WriteIncremental(source.Data, StartXref(source), new Dictionary<int, PdfObject>
        {
            [1] = source.Catalog, [4] = value, [9] = new PdfRef(4, generation),
        }, 10, 1, 4, null, xrefStream, null, null, source.Xref);
        Assert.Equal(source.Data, bytes[..source.Data.Length]);
        var file = PdfFile.Load(bytes);
        Assert.False(file.Repaired);
        Assert.Equal(new PdfRef(1, rootGeneration), file.Trailer.Get("Root"));
        Assert.Equal(new PdfRef(4, generation), file.Trailer.Get("Info"));
        Assert.Equal(generation, file.Xref[4].GenOrIndex);
        Assert.Equal(rootGeneration, file.Xref[1].GenOrIndex);
        Assert.Equal(value.Text, Assert.IsType<PdfString>(file.Resolve(file.Catalog.Get("Probe"))).Text);
        Assert.Equal(value.Text, Assert.IsType<PdfString>(file.Resolve(new PdfRef(9, 0))).Text);
        Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Stale")));
        Assert.Contains($"4 {generation} obj", Encoding.ASCII.GetString(bytes[source.Data.Length..]));
    }

    [Theory]
    [InlineData(PdfEncryptionMethod.Rc4_40)]
    [InlineData(PdfEncryptionMethod.Rc4_128)]
    [InlineData(PdfEncryptionMethod.Aes128)]
    [InlineData(PdfEncryptionMethod.Aes256)]
    public void Encrypted_incremental_strings_and_streams_use_the_emitted_generation(PdfEncryptionMethod method)
    {
        var catalog = new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["Type"] = new PdfName("Catalog"), ["Probe"] = new PdfRef(2, 0), ["Data"] = new PdfRef(3, 0),
        });
        var text = new PdfString(Encoding.ASCII.GetBytes("secret"));
        var stream = new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("private stream"));
        var original = PdfWriter.Write(new Dictionary<int, PdfObject>
        {
            [1] = catalog, [2] = text, [3] = stream,
        }, 1, null, options: new() { Encryption = new() { UserPassword = "u", OwnerPassword = "o", Method = method } });
        var source = PdfFile.Load(original, "u");
        var xref = new Dictionary<int, XrefEntry>(source.Xref)
        {
            [2] = source.Xref[2] with { GenOrIndex = 7 },
            [3] = source.Xref[3] with { GenOrIndex = 9 },
        };
        var changedCatalog = new PdfDictionary(new Dictionary<string, PdfObject>(catalog.Entries)
        {
            ["Probe"] = new PdfRef(2, 7), ["Data"] = new PdfRef(3, 9),
        });
        var updated = PdfWriter.WriteIncremental(original, StartXref(source), new Dictionary<int, PdfObject>
        {
            [1] = changedCatalog, [2] = text, [3] = stream,
        }, (int)source.Trailer.Get("Size").AsNumber()!.Value, 1, null, source.Trailer.Get("ID"),
            false, source.Decryptor, source.Trailer.Get("Encrypt"), xref);
        var file = PdfFile.Load(updated, "u");
        Assert.False(file.Repaired);
        Assert.Equal(7, file.Xref[2].GenOrIndex);
        Assert.Equal(9, file.Xref[3].GenOrIndex);
        Assert.Equal(text.Bytes, Assert.IsType<PdfString>(file.Resolve(file.Catalog.Get("Probe"))).Bytes);
        Assert.Equal(stream.RawData, Assert.IsType<PdfStream>(file.Resolve(file.Catalog.Get("Data"))).RawData);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Full_rewrite_still_renumbers_references_and_objects_to_generation_zero(bool objectStreams)
    {
        var source = Load(7, rootGeneration: 3);
        source.Catalog.Entries["Probe"] = new PdfRef(4, 7);
        var objects = source.Xref.Where(e => e.Value.InUse).ToDictionary(e => e.Key, e => source.GetObject(e.Key));
        var file = PdfFile.Load(PdfWriter.Write(objects, 1, null, options: new() { ObjectStreams = objectStreams }));
        Assert.False(file.Repaired);
        Assert.Equal(0, Assert.IsType<PdfRef>(file.Trailer.Get("Root")).Generation);
        Assert.Equal(0, Assert.IsType<PdfRef>(file.Catalog.Get("Probe")).Generation);
        Assert.Equal("GenerationTarget", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
    }

    [Fact]
    public void Bounded_validation_resolution_uses_generation_identity_without_spurious_incomplete()
    {
        var file = Load(7);
        var incomplete = false;
        Assert.Same(file.GetObject(4), PdfUA2Checks.ResolveBounded(file, new PdfRef(5, 0), () => incomplete = true));
        Assert.IsType<PdfNull>(PdfUA2Checks.ResolveBounded(file, new PdfRef(5, 1), () => incomplete = true));
        Assert.IsType<PdfNull>(PdfUA2Checks.ResolveBounded(file, new PdfRef(6, 0), () => incomplete = true));
        Assert.False(incomplete);
    }

    [Fact]
    public void One_hop_resolution_preserves_indirection_and_rechecks_after_repair()
    {
        var file = Load(7);
        Assert.Equal(new PdfRef(4, 7), file.GetObject(new PdfRef(5, 0)));
        Assert.IsType<PdfNull>(file.GetObject(new PdfRef(5, 1)));
        file.Xref[4] = file.Xref[4] with { OffsetOrStream = 0, GenOrIndex = 0 };
        Assert.IsType<PdfNull>(file.GetObject(new PdfRef(4, 0)));
        Assert.True(file.Repaired);
        Assert.Same(file.GetObject(4), file.GetObject(new PdfRef(4, 7)));
        Assert.IsType<PdfNull>(file.Resolve(new PdfRef(4, 0)));
    }

    [Fact]
    public void Free_entries_stay_null_even_when_their_generation_matches()
    {
        var file = Load();
        file.GetObject(4);
        file.Xref[4] = file.Xref[4] with { InUse = false, GenOrIndex = 1 };
        Assert.IsType<PdfNull>(file.Resolve(new PdfRef(4, 0)));
        Assert.IsType<PdfNull>(file.Resolve(new PdfRef(4, 1)));
    }
}
