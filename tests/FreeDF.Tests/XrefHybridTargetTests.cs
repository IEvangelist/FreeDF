using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefHybridTargetTests
{
    internal static byte[] Build(string shape, bool fixedEntries)
    {
        var original = XrefStreamHeaderRangeTests.Build("3", "0", false);
        using var data = new MemoryStream();
        data.Write(original);
        void Write(string text) => data.Write(Encoding.ASCII.GetBytes(text));
        var text = Encoding.ASCII.GetString(original);
        var root = text.IndexOf("1 0 obj", StringComparison.Ordinal);
        var target = text.IndexOf("2 0 obj", StringComparison.Ordinal);
        var stream = text.IndexOf("3 0 obj", StringComparison.Ordinal);
        string Entry(long offset, bool free = false) => fixedEntries
            ? $"{offset:D10} {(free ? 65535 : 0):D5} {(free ? "f" : "n")} \n"
            : $"{offset} {(free ? 65535 : 0)} {(free ? "f" : "n")}\n";
        var main = data.Position;
        Write($"xref\n0 4\n{Entry(2, true)}{Entry(root)}{Entry(0, true)}{Entry(stream)}" +
            $"trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{main}\n%%EOF\n");
        var update = data.Position;
        var supplement = shape switch
        {
            "previous-table" => main,
            "self-table" => update,
            "stream" or "shared-stream" or "stream-prev" or "same-section-free" => stream,
            "ordinary-object" => target,
            _ => throw new ArgumentException("Unknown hybrid target shape", nameof(shape))
        };
        var previous = shape == "stream-prev" ? stream : main;
        var entries = shape is "stream" or "shared-stream" or "stream-prev"
            ? $"1 1\n{Entry(root)}" : $"2 1\n{Entry(0, true)}";
        Write($"xref\n{entries}trailer\n<< /Size 4 /Root 1 0 R /XRefStm {supplement} /Prev {previous} >>\n");
        Write($"startxref\n{update}\n%%EOF\n");
        if (shape == "shared-stream")
        {
            var latest = data.Position;
            Write($"xref\n1 1\n{Entry(root)}trailer\n<< /Size 4 /Root 1 0 R /XRefStm {stream} /Prev {update} >>\n");
            Write($"startxref\n{latest}\n%%EOF\n");
        }
        return data.ToArray();
    }

    [Theory]
    [InlineData("previous-table", true)]
    [InlineData("previous-table", false)]
    [InlineData("self-table", true)]
    [InlineData("self-table", false)]
    [InlineData("ordinary-object", true)]
    [InlineData("ordinary-object", false)]
    public void Non_stream_hybrid_targets_require_explicit_recovery(string shape, bool fixedEntries)
    {
        var file = PdfFile.Load(Build(shape, fixedEntries));
        Assert.True(file.Repaired);
        AssertTarget(file);
        Assert.True(file.Repaired);
    }

    [Theory]
    [InlineData("stream", true)]
    [InlineData("stream", false)]
    [InlineData("shared-stream", true)]
    [InlineData("shared-stream", false)]
    [InlineData("stream-prev", true)]
    [InlineData("stream-prev", false)]
    [InlineData("same-section-free", true)]
    [InlineData("same-section-free", false)]
    public void Supplemental_streams_keep_hybrid_precedence_and_shared_offsets(string shape, bool fixedEntries)
    {
        var file = PdfFile.Load(Build(shape, fixedEntries));
        Assert.False(file.Repaired);
        AssertTarget(file);
        Assert.False(file.Repaired);
    }

    static void AssertTarget(PdfFile file)
    {
        var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.GetObject(new PdfRef(2, 0)));
    }
}
