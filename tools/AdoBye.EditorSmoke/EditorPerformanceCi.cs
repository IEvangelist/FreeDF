using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using Row = EditorPerformanceReport.Row;

static class EditorPerformanceCi
{
    sealed record KnownMiss(string Document, string Phase, string Metric, double P50, double P95, double P50Budget, double P95Budget);
    sealed record Pixel(string Document, int Width, int Height, string Hash);
    sealed record Policy(int Version, double TimingFactor, double MinimumTimingMs, string Reference, KnownMiss[] KnownMisses, Pixel[] Pixels);
    static string Document(string name) => name.StartsWith("adobye-editor-perf-", StringComparison.Ordinal) ? "large-12-page.pdf" : name;

    public static async Task Run(string profile, string policyPath)
    {
        await EditorPerformanceReport.Write(profile);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var policy = JsonSerializer.Deserialize<Policy>(await File.ReadAllTextAsync(policyPath), options)
            ?? throw new InvalidDataException("Missing CI profile policy.");
        if (policy.Version != 1 || !double.IsFinite(policy.TimingFactor) || policy.TimingFactor < 1 ||
            !double.IsFinite(policy.MinimumTimingMs) || policy.MinimumTimingMs < 0 ||
            policy.KnownMisses is null || policy.Pixels is null || policy.Pixels.Length == 0)
            throw new InvalidDataException("Unsupported or incomplete CI profile policy.");
        var rows = JsonSerializer.Deserialize<Row[]>(await File.ReadAllTextAsync(profile + ".summary.json"))!;
        var reference = new Dictionary<(string, string, string), (double P50, double P95)>();
        using (var csv = new TextFieldParser(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(policyPath))!, policy.Reference)))
        {
            csv.SetDelimiters(",");
            if (csv.ReadFields() is not { Length: 10 }) throw new InvalidDataException("Invalid reference CSV header.");
            while (!csv.EndOfData)
            {
                var fields = csv.ReadFields()!;
                reference.Add((fields[0], fields[1], fields[2]), (
                    double.Parse(fields[4], CultureInfo.InvariantCulture), double.Parse(fields[5], CultureInfo.InvariantCulture)));
            }
        }
        var failures = new List<string>();
        var actualGroups = rows.Select(r => (r.Document, r.Phase, r.Metric)).ToHashSet();
        foreach (var key in reference.Keys)
            if (!actualGroups.Contains(key)) failures.Add($"Missing reference group: {key}.");
        var known = policy.KnownMisses.ToDictionary(m => (m.Document, m.Phase, m.Metric));
        foreach (var miss in policy.KnownMisses)
        {
            var row = rows.SingleOrDefault(r => (r.Document, r.Phase, r.Metric) == (miss.Document, miss.Phase, miss.Metric));
            if (row is null) { failures.Add($"Missing known-miss group: {miss.Document} {miss.Phase} {miss.Metric}."); continue; }
            if (row.P50Budget != miss.P50Budget || row.P95Budget != miss.P95Budget)
                failures.Add($"Original goal changed for known miss: {miss.Document} {miss.Phase} {miss.Metric}.");
            if (row.Passed) failures.Add($"Remove resolved known miss from policy: {miss.Document} {miss.Phase} {miss.Metric}.");
            if (!reference.TryGetValue((miss.Document, miss.Phase, miss.Metric), out var recorded) ||
                Math.Abs(recorded.P50 - miss.P50) > .001 || Math.Abs(recorded.P95 - miss.P95) > .001)
                failures.Add($"Known-miss values disagree with the versioned reference: {miss.Document} {miss.Phase} {miss.Metric}.");
        }
        foreach (var row in rows.Where(r => r.P50Budget is not null || r.P95Budget is not null))
        {
            if (row.Samples != 3 || !double.IsFinite(row.P50) || !double.IsFinite(row.P95))
            { failures.Add($"Incomplete/nonfinite profile group: {row.Document} {row.Phase} {row.Metric}."); continue; }
            if (!row.Metric.EndsWith("-ms", StringComparison.Ordinal))
            {
                if (!row.Passed) failures.Add($"Deterministic gate: {row.Document} {row.Phase} {row.Metric}.");
                continue;
            }
            if (!reference.TryGetValue((row.Document, row.Phase, row.Metric), out var baseline))
            { failures.Add($"New unreferenced timing group: {row.Document} {row.Phase} {row.Metric}."); continue; }
            var p50Limit = Math.Max(policy.MinimumTimingMs, baseline.P50 * policy.TimingFactor);
            var p95Limit = Math.Max(policy.MinimumTimingMs, baseline.P95 * policy.TimingFactor);
            if (row.P50 >= p50Limit || row.P95 >= p95Limit)
                failures.Add($"CI timing regression: {row.Document} {row.Phase} {row.Metric}: {row.P50:F1}/{row.P95:F1} >= {p50Limit:F1}/{p95Limit:F1} ms.");
        }
        using var raw = JsonDocument.Parse(await File.ReadAllTextAsync(profile));
        var images = raw.RootElement.EnumerateArray().Where(r => r.GetProperty("phase").GetString() == "first-page-image").ToArray();
        foreach (var expected in policy.Pixels)
        {
            var matches = images.Where(r => Document(r.GetProperty("document").GetString()!) == expected.Document).ToArray();
            if (matches.Length != 3 || matches.Select(r => r.GetProperty("repeat").GetInt32()).Distinct().Count() != 3 ||
                matches.Any(r => !r.TryGetProperty("pixelHash", out var hash) || hash.GetString() != expected.Hash ||
                    r.GetProperty("width").GetInt32() != expected.Width || r.GetProperty("height").GetInt32() != expected.Height))
                failures.Add($"Decoded pixel gate: {expected.Document}.");
        }
        if (images.Any(r => !policy.Pixels.Any(p => p.Document == Document(r.GetProperty("document").GetString()!))))
            failures.Add("Profile contains an unreferenced pixel fixture.");

        var summary = new StringBuilder("# Editor performance CI\n\n");
        summary.AppendLine($"Policy v{policy.Version}: relative timing ceilings {policy.TimingFactor:F1}x reference, minimum {policy.MinimumTimingMs:F0} ms. Original goals are unchanged.");
        summary.AppendLine("\n## KNOWN MISSES (open work)\n\n| Document / phase / metric | Current p50 / p95 | Original goal | Recorded AOT p50 / p95 |\n|---|---:|---:|---:|");
        foreach (var miss in policy.KnownMisses)
        {
            var row = rows.SingleOrDefault(r => (r.Document, r.Phase, r.Metric) == (miss.Document, miss.Phase, miss.Metric));
            summary.AppendLine($"| {miss.Document} / {miss.Phase} / {miss.Metric} | {row?.P50:F1} / {row?.P95:F1} | {miss.P50Budget} / {miss.P95Budget} | {miss.P50:F1} / {miss.P95:F1} |");
        }
        summary.AppendLine("\n## Full strict-goal miss table\n\n| Classification | Document / phase / metric | p50 / p95 | Original goal |\n|---|---|---:|---:|");
        foreach (var row in rows.Where(r => !r.Passed))
            summary.AppendLine($"| {(known.ContainsKey((row.Document, row.Phase, row.Metric)) ? "KNOWN" : "NEW GOAL MISS")} | {row.Document} / {row.Phase} / {row.Metric} | {row.P50:F1} / {row.P95:F1} | {row.P50Budget} / {row.P95Budget} |");
        summary.AppendLine("\nTiming CI uses relative ceilings, not a claim that strict goals pass. Pixel/allocation/raster-count gates are not relaxed.");
        summary.AppendLine(failures.Count == 0 ? "\nCI regression gates passed; known goals remain open." : "\n## CI gate failures\n\n" + string.Join("\n", failures.Select(f => "- " + f)));
        await File.WriteAllTextAsync(profile + ".ci.md", summary.ToString());
        await File.WriteAllTextAsync(profile + ".ci.json", JsonSerializer.Serialize(new { policy.Version, failures }, new JsonSerializerOptions { WriteIndented = true }));
        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } jobSummary)
            await File.AppendAllTextAsync(jobSummary, summary.ToString());
        Console.WriteLine(summary);
        if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
    }
}
