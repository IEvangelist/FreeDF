namespace PortableDocument.Content;

/// <summary>PDF 2.0 /UseBlackPtComp state. This renderer deterministically treats Default as Off.</summary>
public enum BlackPointCompensation
{
    Default,
    Off,
    On
}
