namespace AdoBye.Samples;

/// <summary>Authors the sample PDFs (and the merged 27-full-collection) with this repo's own writer/editor; see Kit/ and Gen/.</summary>
public static class SampleGenerator
{
    /// <summary>Writes every sample whose file name contains <paramref name="fragment"/> into <paramref name="outDir"/> in name order (27 needs the others already there) and returns the file names.</summary>
    public static IReadOnlyList<string> Generate(string outDir, string fragment = "")
    {
        var gens = typeof(SampleGenerator).Assembly.GetTypes()
            .Where(t => t.Namespace == "AdoBye.Samples.Gen" && t.GetField("Out") is not null && t.GetMethod("Build") is not null)
            .Select(t => (Out: (string)t.GetField("Out")!.GetRawConstantValue()!, Build: t.GetMethod("Build")!))
            .Where(g => g.Out.Contains(fragment, StringComparison.Ordinal))
            .OrderBy(g => g.Out, StringComparer.Ordinal)
            .ToList();
        foreach (var g in gens) g.Build.Invoke(null, [Path.Combine(outDir, g.Out)]);
        return gens.Select(g => g.Out).ToList();
    }
}
