using System.Text;
using AdoBye.Core;
using AdoBye.Document;
using AdoBye.Render;

namespace AdoBye.Tests;

/// <summary>
/// Deterministic mutation fuzzing: every fixture is truncated/bit-flipped/patched with fixed seeds and pushed through parse, render, text
/// extraction, structure, validation and re-save. Only <see cref="PdfParseException"/> may escape; nothing may hang or allocate unboundedly.
/// Set CSHARP_PDF_FUZZ_SEEDS to run more seeds per fixture (default 60).
/// </summary>
public class FuzzTests
{
    const int TimeoutMs = 30_000;
    const long MaxAllocatedBytes = 3L << 30;

    static int SeedCount => int.TryParse(Environment.GetEnvironmentVariable("CSHARP_PDF_FUZZ_SEEDS"), out var n) ? n : 60;

    public static IEnumerable<(string Name, byte[] Data)> Corpus()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures");
        foreach (var f in Directory.GetFiles(root, "*.pdf", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            yield return (Path.GetRelativePath(root, f), File.ReadAllBytes(f));
        yield return ("synthetic/tagged", StructureTests.Tagged());
    }

    static uint Fnv(string s) { var h = 2166136261u; foreach (var c in s) h = (h ^ c) * 16777619u; return h; }

    public static byte[] Mutate(byte[] src, int seed, string name)
    {
        var rng = new Random(unchecked((int)(Fnv(name) * 31u) + seed * 7919));
        var d = (byte[])src.Clone();
        if (d.Length == 0) return d;
        switch (seed % 7)
        {
            case 0:
                for (var i = rng.Next(1, 33); i > 0; i--) d[rng.Next(d.Length)] ^= (byte)(1 << rng.Next(8));
                return d;
            case 1:
                return d.AsSpan(0, rng.Next(0, d.Length)).ToArray();
            case 2:
                {
                    var len = rng.Next(1, Math.Min(256, d.Length) + 1); var at = rng.Next(d.Length - len + 1);
                    d.AsSpan(at, len).Clear();
                    return d;
                }
            case 3:
                {
                    var len = rng.Next(1, Math.Min(64, d.Length) + 1); var at = rng.Next(d.Length - len + 1);
                    rng.NextBytes(d.AsSpan(at, len));
                    return d;
                }
            case 4:
                {
                    var len = rng.Next(1, Math.Min(512, d.Length) + 1);
                    var from = rng.Next(d.Length - len + 1); var to = rng.Next(d.Length - len + 1);
                    var chunk = d.AsSpan(from, len).ToArray();
                    return Splice(d, to, chunk);
                }
            default:
                return PatchNumbers(d, rng, seed % 7 == 5 ? 1 : 6);
        }
    }

    static byte[] Splice(byte[] d, int at, byte[] chunk)
    {
        var r = (byte[])d.Clone();
        Array.Copy(chunk, 0, r, at, Math.Min(chunk.Length, r.Length - at));
        return r;
    }

    static readonly string[] Extremes = ["0", "-1", "1", "2147483647", "4294967296", "99999999999", "-2147483648", "65536", "1000000", "0.000001", "1e30"];

    // Replace some decimal tokens with extreme values; this reaches dictionary/array-size/xref-offset handling that random bit flips rarely hit.
    static byte[] PatchNumbers(byte[] d, Random rng, int count)
    {
        var s = new StringBuilder(Encoding.Latin1.GetString(d));
        for (var n = 0; n < count; n++)
        {
            var start = rng.Next(s.Length);
            var i = start;
            while (i < s.Length && !char.IsAsciiDigit(s[i])) i++;
            if (i >= s.Length) continue;
            var j = i;
            while (j < s.Length && char.IsAsciiDigit(s[j])) j++;
            s.Remove(i, j - i).Insert(i, Extremes[rng.Next(Extremes.Length)]);
        }
        return Encoding.Latin1.GetBytes(s.ToString());
    }

    static void Exercise(byte[] data)
    {
        var doc = PdfDocument.Load(data);
        var n = Math.Min(doc.Pages.Count, 3);
        for (var i = 0; i < n; i++)
        {
            var page = doc.Pages[i];
            RenderStep(() => PdfRenderer.RenderPage(page, 24));
            RenderStep(() => PdfRenderer.Analyze(page));
            RenderStep(() => page.GetAnnotations());
        }
        RenderStep(() => doc.GetOutlines());
        RenderStep(() => doc.GetFormFields());
        RenderStep(() => doc.GetXmpMetadata());
        RenderStep(() => doc.GetEmbeddedFiles());
        RenderStep(() => doc.Structure);
        RenderStep(() => StructuredTextExtractor.Extract(doc));
        foreach (var p in Enum.GetValues<PdfValidationProfile>()) RenderStep(() => PdfValidator.Validate(doc, p));
        RenderStep(() => PdfEditor.Open(doc).Save());
    }

    static volatile string _step = "";

    // Each step is independent: a controlled parse failure in one feature must not hide a crash in another.
    static void RenderStep(Action a, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(a))] string step = "")
    {
        _step = step;
        try { a(); }
        catch (Exception e) when (e is PdfParseException or NotSupportedException) { }
    }

    static void RenderStep<T>(Func<T> f, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(f))] string step = "") => RenderStep(() => { f(); }, step);

    /// <summary>Returns a failure description, or null when the input was handled acceptably.</summary>
    static string? Run(byte[] data)
    {
        string? failure = null;
        var t = new Thread(() =>
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            try
            {
                Exercise(data);
                var used = GC.GetAllocatedBytesForCurrentThread() - before;
                if (used > MaxAllocatedBytes) failure = $"allocated {used >> 20} MB";
            }
            catch (Exception e) when (e is PdfParseException or NotSupportedException) { }
            catch (Exception e) { var x = e is AggregateException ag ? ag.Flatten().InnerExceptions[0] : e; failure = x.ToString().Split('\n')[0] + " @ " + string.Join(" <- ", x.StackTrace?.Split('\n').Take(4).Select(s => s.Trim()) ?? []); }
        }, 8 << 20) { IsBackground = true };
        t.Start();
        if (!t.Join(TimeoutMs)) return $"timeout > {TimeoutMs} ms in step '{_step}'";
        return failure;
    }

    [Fact]
    public void DumpMutationForDebugging()
    {
        var spec = Environment.GetEnvironmentVariable("CSHARP_PDF_FUZZ_DUMP");
        if (string.IsNullOrEmpty(spec)) return;
        var parts = spec.Split('|');
        var (name, data) = Corpus().First(c => c.Name == parts[0]);
        var bytes = Mutate(data, int.Parse(parts[1]), name);
        File.WriteAllBytes(parts[2], bytes);
        Assert.Null(Run(bytes));
    }

    [Fact]
    public void MutatedFixturesOnlyRaiseControlledExceptions()
    {
        var failures = new List<string>();
        foreach (var (name, data) in Corpus())
        {
            Assert.Null(Run(data));
            for (var seed = 0; seed < SeedCount; seed++)
            {
                var m = Mutate(data, seed, name);
                var r = Run(m);
                if (r is not null) failures.Add($"{name} seed {seed}: {r}");
            }
        }
        Assert.True(failures.Count == 0, $"{failures.Count} failure(s):\n" + string.Join("\n", failures.Take(25)));
    }

    [Fact]
    public void TruncatingAtEveryEighthOffsetIsControlled()
    {
        var failures = new List<string>();
        foreach (var (name, data) in Corpus().Where(c => c.Data.Length < 12_000))
            for (var len = 0; len < data.Length; len += Math.Max(8, data.Length / 150))
            {
                var r = Run(data.AsSpan(0, len).ToArray());
                if (r is not null) failures.Add($"{name} truncated to {len}: {r}");
            }
        Assert.True(failures.Count == 0, $"{failures.Count} failure(s):\n" + string.Join("\n", failures.Take(25)));
    }

    [Fact]
    public void HostilePageGeometryIsRejectedOrBounded()
    {
        foreach (var box in new[] { "[0 0 1e9 1e9]", "[0 0 99999999 99999999]", "[0 0 0 0]", "[-1e308 -1e308 1e308 1e308]", "[0 0 1e-9 1e-9]" })
        {
            var pdf = TestPdf.Build("0 0 10 10 re f", mediaBox: box);
            Assert.Null(Run(pdf));
        }
    }

    [Fact]
    public void DeeplyNestedObjectsDoNotOverflowTheStack()
    {
        var nested = new string('[', 100_000) + new string(']', 100_000);
        var pdf = TestPdf.Build("q Q", extraResources: "/Foo " + nested);
        Assert.Null(Run(pdf));
        var dicts = string.Concat(Enumerable.Repeat("<< /A ", 100_000)) + string.Concat(Enumerable.Repeat(">>", 100_000));
        Assert.Null(Run(TestPdf.Build("q Q", extraResources: "/Foo " + dicts)));
    }

    [Fact]
    public void RecursiveFormsAndPagesTerminate()
    {
        var form = "<< /Type /XObject /Subtype /Form /BBox [0 0 10 10] /Resources << /XObject << /X 5 0 R >> >> /Length 5 >>\nstream\n/X Do\nendstream";
        var pdf = TestPdf.Build("/X Do", "/XObject << /X 5 0 R >>", "5 0 obj\n" + form + "\nendobj\n");
        Assert.Null(Run(pdf));
        var loopTree = ObjPdf.Build("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [2 0 R 3 0 R] /Count 2 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] >>");
        Assert.Null(Run(loopTree));
    }
}
