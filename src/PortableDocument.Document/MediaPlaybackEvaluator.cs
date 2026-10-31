using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>
/// Caller-supplied viewer environment for the inert "would play" evaluation (ISO 32000-2 Tables 266-270 media criteria).
/// Null numeric values mean "unknown"; unknown values never satisfy a must-honor criterion.
/// </summary>
public sealed record PdfMediaEnvironment
{
    public bool AudioDescriptions { get; init; }
    public bool Captions { get; init; }
    public bool Overdubs { get; init; }
    public bool Subtitles { get; init; }
    public long? BitRate { get; init; }
    public long? BitDepth { get; init; }
    public long? ScreenWidth { get; init; }
    public long? ScreenHeight { get; init; }
    public string? PdfVersion { get; init; }
    public IReadOnlyList<string> Languages { get; init; } = [];
    public IReadOnlyList<string> ViewerSoftwareUris { get; init; } = [];
    public string? OperatingSystem { get; init; }
}

public enum PdfMediaCheckOutcome { Satisfied, Unsatisfied, Unknown }

/// <summary>One evaluated criterion, for example Criterion "R" (minimum bandwidth) required 1000 actual 500 in the MH or BE set.</summary>
public sealed record PdfMediaCheck(string Set, string Criterion, string Required, string? Actual, PdfMediaCheckOutcome Outcome);

/// <summary>
/// Pure "would play" report. <see cref="Selected"/> is the media rendition a viewer following 12.6.4.2 would choose (the first selector child whose
/// must-honor criteria hold). Nothing is opened, fetched or played; <see cref="ExternalReference"/> only flags that the clip data is not embedded.
/// </summary>
public sealed record PdfMediaPlaybackReport(
    bool WouldPlay,
    PdfRendition? Selected,
    IReadOnlyList<PdfMediaCheck> Checks,
    IReadOnlyList<string> Reasons,
    string? TemporaryFilePermission,
    bool ExternalReference,
    bool Truncated);

public static class MediaPlaybackEvaluator
{
    const int MaxNodes = 1024;

    public static PdfMediaPlaybackReport Evaluate(PdfRendition rendition, PdfMediaEnvironment env)
    {
        var budget = 0;
        var truncated = false;
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var reasons = new List<string>();
        var selected = Select(rendition, env, visited, ref budget, ref truncated, reasons);
        var checks = new List<PdfMediaCheck>();
        if (selected is null)
        {
            if (reasons.Count == 0) reasons.Add("no rendition satisfies its must-honor criteria");
            return new PdfMediaPlaybackReport(false, null, checks, reasons, null, false, truncated);
        }
        CollectChecks(selected, env, checks);
        var clip = selected.Clip;
        while (clip?.Next is { } next && clip.Data is null) clip = next;
        var external = clip?.Data is { } data && !IsEmbedded(data);
        if (clip is null) reasons.Add("media rendition has no clip");
        return new PdfMediaPlaybackReport(clip is not null, selected, checks, reasons, clip?.TemporaryFilePermission, external, truncated);
    }

    static bool IsEmbedded(PdfObject data) =>
        data is PdfDictionary d && d.Get("EF") is not null || data is PdfStream;

    static PdfRendition? Select(PdfRendition r, PdfMediaEnvironment env, HashSet<PdfDictionary> visited, ref int budget, ref bool truncated, List<string> reasons)
    {
        if (++budget > MaxNodes) { truncated = true; return null; }
        if (!visited.Add(r.Dictionary)) return null;
        try
        {
            var checks = new List<PdfMediaCheck>();
            if (r.MustHonorCriteria is { } mh)
                AddChecks("MH", mh, env, checks);
            if (checks.Any(c => c.Outcome != PdfMediaCheckOutcome.Satisfied))
            {
                reasons.Add($"rendition '{r.Name}' fails must-honor criteria: " +
                    string.Join(", ", checks.Where(c => c.Outcome != PdfMediaCheckOutcome.Satisfied).Select(c => c.Criterion)));
                return null;
            }
            if (r.Subtype != "SR") return r;
            foreach (var child in r.Renditions)
            {
                if (Select(child, env, visited, ref budget, ref truncated, reasons) is { } hit) return hit;
                if (truncated) break;
            }
            return null;
        }
        finally { visited.Remove(r.Dictionary); }
    }

    static void CollectChecks(PdfRendition r, PdfMediaEnvironment env, List<PdfMediaCheck> checks)
    {
        if (r.MustHonorCriteria is { } mh) AddChecks("MH", mh, env, checks);
        if (r.BestEffortCriteria is { } be) AddChecks("BE", be, env, checks);
    }

    static void AddChecks(string set, PdfMediaCriteria c, PdfMediaEnvironment env, List<PdfMediaCheck> checks)
    {
        void Flag(string key, bool? required, bool actual)
        {
            if (required is { } v)
                checks.Add(new PdfMediaCheck(set, key, v.ToString(), actual.ToString(),
                    v == actual ? PdfMediaCheckOutcome.Satisfied : PdfMediaCheckOutcome.Unsatisfied));
        }
        Flag("A", c.AudioDescriptions, env.AudioDescriptions);
        Flag("C", c.Captions, env.Captions);
        Flag("O", c.Overdubs, env.Overdubs);
        Flag("S", c.Subtitles, env.Subtitles);
        if (c.MinBandwidth is { } bw)
            checks.Add(AtLeast(set, "R", bw, env.BitRate));
        if (c.MinBitDepth?.Value is { } depth)
            checks.Add(AtLeast(set, "D", depth, env.BitDepth));
        if (c.MinScreenSize is { Width: { } w, Height: { } h })
        {
            var ok = env.ScreenWidth is null || env.ScreenHeight is null
                ? PdfMediaCheckOutcome.Unknown
                : env.ScreenWidth >= w && env.ScreenHeight >= h ? PdfMediaCheckOutcome.Satisfied : PdfMediaCheckOutcome.Unsatisfied;
            checks.Add(new PdfMediaCheck(set, "Z", $"{w}x{h}", env.ScreenWidth is null ? null : $"{env.ScreenWidth}x{env.ScreenHeight}", ok));
        }
        if (c.Viewers.Count > 0)
        {
            var match = c.Viewers.Any(v => v.Uri is { } u && env.ViewerSoftwareUris.Contains(u, StringComparer.Ordinal)
                && OsMatches(v, env.OperatingSystem));
            checks.Add(new PdfMediaCheck(set, "V", string.Join(";", c.Viewers.Select(v => v.Uri)), string.Join(";", env.ViewerSoftwareUris),
                match ? PdfMediaCheckOutcome.Satisfied : PdfMediaCheckOutcome.Unsatisfied));
        }
        if (c.PdfVersions is { Count: > 0 } versions)
        {
            var list = versions.Items.Select(NameOrString).Where(s => s is not null).ToList();
            checks.Add(new PdfMediaCheck(set, "P", string.Join(",", list), env.PdfVersion,
                env.PdfVersion is null ? PdfMediaCheckOutcome.Unknown
                : list.Contains(env.PdfVersion, StringComparer.Ordinal) ? PdfMediaCheckOutcome.Satisfied : PdfMediaCheckOutcome.Unsatisfied));
        }
        if (c.Languages is { Count: > 0 } langs)
        {
            var list = langs.Items.Select(NameOrString).Where(s => s is not null).Select(s => s!).ToList();
            var ok = env.Languages.Count == 0 ? PdfMediaCheckOutcome.Unknown
                : list.Any(l => env.Languages.Any(e => LanguageMatches(l, e))) ? PdfMediaCheckOutcome.Satisfied : PdfMediaCheckOutcome.Unsatisfied;
            checks.Add(new PdfMediaCheck(set, "L", string.Join(",", list), string.Join(",", env.Languages), ok));
        }
    }

    static PdfMediaCheck AtLeast(string set, string key, long required, long? actual) =>
        new(set, key, required.ToString(), actual?.ToString(),
            actual is null ? PdfMediaCheckOutcome.Unknown : actual >= required ? PdfMediaCheckOutcome.Satisfied : PdfMediaCheckOutcome.Unsatisfied);

    static bool OsMatches(PdfSoftwareIdentifier v, string? os)
    {
        if (v.OperatingSystems is not { Count: > 0 } list || os is null) return true;
        return list.Items.Any(i => NameOrString(i) is { } s && s.Equals(os, StringComparison.OrdinalIgnoreCase));
    }

    static bool LanguageMatches(string required, string actual) =>
        required.Equals(actual, StringComparison.OrdinalIgnoreCase)
        || actual.StartsWith(required + "-", StringComparison.OrdinalIgnoreCase)
        || required.StartsWith(actual + "-", StringComparison.OrdinalIgnoreCase);

    static string? NameOrString(PdfObject o) => o switch
    {
        PdfName n => n.Value,
        PdfString s => s.Text,
        _ => null,
    };
}
