using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefChainCycleTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var firstStream in new[] { false, true })
            foreach (var latestStream in new[] { false, true })
                foreach (var shape in new[] { "self", "two-node", "terminated", "forward-terminated" })
                    yield return [firstStream, latestStream, shape];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Repeated_primary_offsets_repair_without_rejecting_terminating_mixed_chains(
        bool firstStream, bool latestStream, string shape)
    {
        var file = PdfFile.Load(Build(firstStream, latestStream, shape));
        var repaired = shape is "self" or "two-node";
        Assert.Equal(repaired, file.Repaired);
        var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.GetObject(new PdfRef(2, 0)));
        Assert.Equal(repaired, file.Repaired);
    }

    internal static byte[] Build(bool firstStream, bool latestStream, string shape)
    {
        var original = XrefClassicGenerationTests.Build(0, fixedEntry: true);
        var text = Encoding.ASCII.GetString(original);
        var root = text.IndexOf("1 0 obj", StringComparison.Ordinal);
        var main = text.IndexOf("\nxref\n", StringComparison.Ordinal) + 1;
        using var data = new MemoryStream();
        data.Write(original);
        void Write(string s) => data.Write(Encoding.ASCII.GetBytes(s));
        long Section(bool stream, int number, string previous)
        {
            var revision = data.Position;
            if (!stream) Write($"{number} 0 obj\n<< /Type /Revision >>\nendobj\n");
            var offset = data.Position;
            if (previous == "self") previous = $"{offset:D10}";
            if (stream)
            {
                using var rows = new MemoryStream();
                foreach (var position in new[] { (long)root, offset })
                {
                    rows.WriteByte(1);
                    for (var shift = 24; shift >= 0; shift -= 8) rows.WriteByte((byte)(position >> shift));
                    rows.WriteByte(0);
                    rows.WriteByte(0);
                }
                Write($"{number} 0 obj\n<< /Type /XRef /Size {number + 1} /Root 1 0 R /Prev {previous} /Index [1 1 {number} 1] /W [1 4 2] /Length {rows.Length} >>\nstream\n");
                data.Write(rows.ToArray());
                Write("\nendstream\nendobj\n");
            }
            else Write($"xref\n1 1\n{root:D10} 00000 n \n{number} 1\n{revision:D10} 00000 n \ntrailer\n<< /Size {number + 1} /Root 1 0 R /Prev {previous} >>\n");
            Write($"startxref\n{offset}\n%%EOF\n");
            return offset;
        }
        var first = Section(firstStream, 3, shape is "two-node" or "forward-terminated" ? "@PREVIOUS@" : $"{main:D10}");
        var latest = Section(latestStream, 4, shape == "self" ? "self"
            : $"{(shape == "forward-terminated" ? main : first):D10}");
        if (shape == "forward-terminated") Write($"startxref\n{first}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(data.ToArray())
            .Replace("@PREVIOUS@", $"{latest:D10}", StringComparison.Ordinal));
    }
}
