namespace PortableDocument.Document;

public enum PdfValidationProfile { PdfA1b, PdfA2b, PdfA3b, PdfUA1, PdfUA2 }

public enum ValidationSeverity { Info, Warning, Error }

/// <param name="RuleId">Stable identifier, e.g. "PDFA-FONT-EMBEDDED".</param>
/// <param name="Clause">The governing standard clause, when one is cited.</param>
public sealed record ValidationFinding(PdfValidationProfile Profile, string RuleId, ValidationSeverity Severity, string Message, int? PageIndex = null, string? Clause = null)
{
    public override string ToString() => $"[{Severity}] {Profile} {RuleId}: {Message}" + (PageIndex is { } p ? $" (page {p + 1})" : "") + (Clause is null ? "" : $" [{Clause}]");
}

public sealed class ValidationReport
{
    public PdfValidationProfile Profile { get; }
    public IReadOnlyList<ValidationFinding> Findings { get; }

    internal ValidationReport(PdfValidationProfile profile, List<ValidationFinding> findings) { Profile = profile; Findings = findings; }

    public IEnumerable<ValidationFinding> Errors => Findings.Where(f => f.Severity == ValidationSeverity.Error);
    public IEnumerable<ValidationFinding> Warnings => Findings.Where(f => f.Severity == ValidationSeverity.Warning);

    /// <summary>True when no Error findings were produced. This is an approximation of conformance: only the checks listed in docs/spec-matrix.md are performed.</summary>
    public bool IsCompliant => !Errors.Any();

    public bool Has(string ruleId) => Findings.Any(f => f.RuleId == ruleId);

    public override string ToString() => $"{Profile}: {(IsCompliant ? "no errors" : $"{Errors.Count()} error(s)")}, {Warnings.Count()} warning(s)";
}

/// <summary>Basic PDF/A (1b/2b/3b) and PDF/UA (1/2) validation. Not a certified validator: it reports the checks it implements and nothing else.</summary>
public static class PdfValidator
{
    public static ValidationReport Validate(PdfDocument doc, PdfValidationProfile profile)
    {
        var ctx = new ValidationContext(doc, profile);
        switch (profile)
        {
            case PdfValidationProfile.PdfA1b or PdfValidationProfile.PdfA2b or PdfValidationProfile.PdfA3b: PdfAChecks.Run(ctx); break;
            default: PdfUAChecks.Run(ctx); break;
        }
        return new ValidationReport(profile, ctx.Findings);
    }

    public static ValidationReport ValidatePdfA(PdfDocument doc, int part = 2) =>
        Validate(doc, part switch { 1 => PdfValidationProfile.PdfA1b, 3 => PdfValidationProfile.PdfA3b, _ => PdfValidationProfile.PdfA2b });

    public static ValidationReport ValidatePdfUA(PdfDocument doc, int part = 1) =>
        Validate(doc, part == 2 ? PdfValidationProfile.PdfUA2 : PdfValidationProfile.PdfUA1);
}
