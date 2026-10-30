using System.Globalization;
using System.Text;
using System.Text.Json;

static class EditorPerformanceReport
{
    internal sealed record Row(string Document, string Phase, string Metric, int Samples, double P50, double P95,
        double Maximum, double? P50Budget, double? P95Budget, bool Passed);

    public static async Task<IReadOnlyList<string>> Write(string path)
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var measurements = new List<(string Document, string Phase, string Metric, double Value)>();
        foreach (var record in json.RootElement.EnumerateArray())
        {
            var document = record.GetProperty("document").GetString()!;
            if (document.StartsWith("adobye-editor-perf-", StringComparison.Ordinal)) document = "large-12-page.pdf";
            var phase = record.GetProperty("phase").GetString()!;
            if (!record.TryGetProperty("result", out var result)) continue;
            if (result.TryGetProperty("ms", out var total)) Add("total-ms", total.GetDouble());
            if (result.TryGetProperty("interactions", out var interactions))
                foreach (var interaction in interactions.EnumerateArray())
                    Add(interaction.GetProperty("phase").GetString()! + "-ms", interaction.GetProperty("ms").GetDouble());
            if (result.TryGetProperty("presentation", out var presentation))
                Add("event-frame-max-ms", presentation.EnumerateArray().Select(e => e.GetProperty("ms").GetDouble()).DefaultIfEmpty().Max());
            if (result.TryGetProperty("heartbeatDelays", out var heartbeat))
                Add("heartbeat-max-ms", heartbeat.EnumerateArray().Select(e => e.GetDouble()).DefaultIfEmpty().Max());
            if (result.TryGetProperty("longTasks", out var tasks))
                Add("long-task-max-ms", tasks.EnumerateArray().Select(e => e.GetProperty("ms").GetDouble()).DefaultIfEmpty().Max());
            if (record.TryGetProperty("inputAckMs", out var input) && input.ValueKind == JsonValueKind.Number)
                Add("input-ack-ms", input.GetDouble());
            if (result.TryGetProperty("workerStages", out var stages))
            {
                var engines = stages.EnumerateArray().Where(e => e.TryGetProperty("engine", out var engine)
                    && engine.ValueKind == JsonValueKind.Object).Select(e => e.GetProperty("engine")).ToArray();
                foreach (var metric in new[] { "rasterMs", "pngEncodeMs", "allocatedBytes" })
                    Add(metric, engines.Sum(e => e.GetProperty(metric).GetDouble()));
                Add("raster-misses", engines.Count(e => !e.GetProperty("cacheHit").GetBoolean()));
                if (phase == "overlay-cache")
                    Add("bare-raster-misses", stages.EnumerateArray().Count(e =>
                        e.GetProperty("url").GetString()!.Contains("bare=true", StringComparison.Ordinal)
                        && e.TryGetProperty("engine", out var engine) && engine.ValueKind == JsonValueKind.Object
                        && !engine.GetProperty("cacheHit").GetBoolean()));
            }
            if (result.TryGetProperty("activeRenders", out var active)) Add("active-renders", active.GetArrayLength());
            void Add(string metric, double value) => measurements.Add((document, phase, metric, value));
        }
        var rows = measurements.GroupBy(m => (m.Document, m.Phase, m.Metric)).Select(group =>
        {
            var values = group.Select(m => m.Value).Order().ToArray();
            var (medianBudget, tailBudget) = Budget(group.Key.Phase, group.Key.Metric);
            var p50 = Percentile(values, .5);
            var p95 = Percentile(values, .95);
            return new Row(group.Key.Document, group.Key.Phase, group.Key.Metric, values.Length,
                p50, p95, values[^1], medianBudget, tailBudget,
                (medianBudget is null || p50 < medianBudget) && (tailBudget is null || p95 < tailBudget));
        }).OrderBy(r => r.Document).ThenBy(r => r.Phase).ThenBy(r => r.Metric).ToArray();
        if (rows.Length == 0) throw new InvalidDataException("The profile contains no measurements.");
        await File.WriteAllTextAsync(path + ".summary.json", JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        var csv = new StringBuilder("Document,Phase,Metric,Samples,P50,P95,Maximum,P50Budget,P95Budget,Passed\n");
        foreach (var row in rows)
            csv.AppendLine(string.Join(",", Csv(row.Document), Csv(row.Phase), Csv(row.Metric), row.Samples,
                Number(row.P50), Number(row.P95), Number(row.Maximum), Number(row.P50Budget), Number(row.P95Budget), row.Passed));
        await File.WriteAllTextAsync(path + ".summary.csv", csv.ToString());
        var misses = rows.Where(r => !r.Passed).Select(r =>
            $"{r.Document} {r.Phase} {r.Metric}: p50={Number(r.P50)}, p95={Number(r.P95)}; budgets {Number(r.P50Budget)}/{Number(r.P95Budget)}").ToArray();
        await File.WriteAllTextAsync(path + ".budgets.json", JsonSerializer.Serialize(misses, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Profile: {rows.Length} metric groups, {misses.Length} budget misses. Report: {path}.summary.csv");
        return misses;
    }

    static string Number(double? value) => value?.ToString("F3", CultureInfo.InvariantCulture) ?? "";
    static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    static double Percentile(double[] sorted, double p) => sorted[(int)Math.Ceiling(p * sorted.Length) - 1];

    static (double?, double?) Budget(string phase, string metric) => (phase, metric) switch
    {
        (_, "heartbeat-max-ms" or "input-ack-ms") => (200, 200),
        (_, "long-task-max-ms") => (50, 50),
        (_, "event-frame-max-ms") => (100, 200),
        ("overlay-cache", "bare-raster-misses") => (1, 1),
        (_, "active-renders") => (4, 4),
        ("open", "total-ms") => (250, 500),
        ("next-page" or "cached-next-page", "total-ms") => (100, 200),
        ("edit-text", "text-edit-commit-ms") => (250, 500),
        ("zoom" or "undo", "zoom-ms" or "undo-ms") => (100, 200),
        ("cached-zoom", "total-ms") => (100, 200),
        ("fresh-render-input", "total-ms") => (1000, 1500),
        ("fresh-render-input", "allocatedBytes") => (200_000_000, 200_000_000),
        ("cached-zoom" or "cached-next-page", "raster-misses") => (1, 1),
        _ => (null, null),
    };
}
