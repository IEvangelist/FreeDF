namespace PortableDocument.Content;

/// <summary>
/// Explicit environment for ISO 32000-2:2020 8.11.4.4 Language and User usage rules.
/// Omitted values leave the corresponding rules unapplied; no host settings are read.
/// User strings match exactly (ordinal); language tags match without regard to ASCII case.
/// Usage language controls visibility only; it does not change content's natural language.
/// </summary>
public sealed record OptionalContentContext
{
    /// <summary>Application language tag, for example en-US; empty means unknown. No host locale is inferred.</summary>
    public string? Language { get; init; }
    /// <summary>Identity to compare with User dictionaries of Type Ind.</summary>
    public string? UserName { get; init; }
    /// <summary>Position to compare with User dictionaries of Type Ttl.</summary>
    public string? UserTitle { get; init; }
    /// <summary>Organization to compare with User dictionaries of Type Org.</summary>
    public string? UserOrganization { get; init; }
}
