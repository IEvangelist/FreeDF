using PortableDocument.Core;
using PortableDocument.Document;

namespace PortableDocument.Content;

/// <summary>
/// Interactive layer state for one document and configuration (ISO 32000-2 8.11, Table 99).
/// Changes are in-memory only; PDF actions and scripts are never executed.
/// </summary>
public sealed class OptionalContentState
{
    readonly Dictionary<PdfDictionary, bool> _visibility = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<PdfDictionary> _groups = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<PdfDictionary> _locked = new(ReferenceEqualityComparer.Instance);
    readonly List<string> _warnings = [];
    public PdfDocument Document { get; }
    internal PdfFile File { get; }
    internal PdfDictionary? Configuration { get; }
    internal PdfDictionary? DefaultConfiguration { get; }
    public IReadOnlyList<PdfDictionary> Groups { get; }
    public IReadOnlyList<IReadOnlyList<PdfDictionary>> RadioButtonGroups { get; }
    public IReadOnlyList<string> Warnings => _warnings.AsReadOnly();

    public OptionalContentState(PdfDocument document, string? configurationName = null)
    {
        Document = document;
        File = document.File;
        var properties = R(File.Catalog.Get("OCProperties")) as PdfDictionary;
        DefaultConfiguration = R(properties?.Get("D")) as PdfDictionary;
        Configuration = DefaultConfiguration;
        if (configurationName is not null
            && (R(DefaultConfiguration?.Get("Name")) as PdfString)?.Text != configurationName)
        {
            Configuration = R(properties?.Get("Configs")) is PdfArray configs
                ? configs.Items.Select(R).OfType<PdfDictionary>().FirstOrDefault(
                    c => (R(c.Get("Name")) as PdfString)?.Text == configurationName)
                : null;
            if (Configuration is null)
                throw new ArgumentException($"No optional-content configuration named '{configurationName}' exists.", nameof(configurationName));
        }

        var groups = new List<PdfDictionary>();
        if (ReadArray(properties?.Get("OCGs"), "OCGs") is { } registered)
            foreach (var value in registered.Items)
                if (ResolveGroup(value) is { } group)
                {
                    if (_groups.Add(group)) groups.Add(group);
                }
                else _warnings.Add("invalid optional-content OCGs reference");
        Groups = groups.AsReadOnly();

        // Only RBGroups inherits from /D. An explicit empty array suppresses inheritance.
        var radioValue = Configuration?.Get("RBGroups");
        if (radioValue is null or PdfNull) radioValue = DefaultConfiguration?.Get("RBGroups");
        var radioGroups = new List<IReadOnlyList<PdfDictionary>>();
        if (ReadArray(radioValue, "RBGroups") is { } radio)
            foreach (var value in radio.Items)
                if (R(value) is PdfArray members)
                    radioGroups.Add(ReadGroups(members, "RBGroups").AsReadOnly());
                else _warnings.Add("invalid optional-content RBGroups group array");
        RadioButtonGroups = radioGroups.AsReadOnly();

        if (ReadArray(Configuration?.Get("Locked"), "Locked") is { } locked)
            _locked.UnionWith(ReadGroups(locked, "Locked"));
    }

    PdfObject? R(PdfObject? value) => File.Resolve(value);

    PdfDictionary? ResolveGroup(PdfObject value) =>
        value is PdfRef reference
        && File.Xref.TryGetValue(reference.Number, out var entry) && entry.InUse
        && reference.Generation == (entry.Compressed ? 0 : entry.GenOrIndex)
        && R(value) is PdfDictionary group && R(group.Get("Type")).AsName() == "OCG"
            ? group : null;

    PdfArray? ReadArray(PdfObject? value, string entry)
    {
        var resolved = R(value);
        if ((resolved is null or PdfNull) && value is not PdfRef) return null;
        if (resolved is PdfArray array) return array;
        _warnings.Add($"invalid optional-content {entry} array");
        return null;
    }

    List<PdfDictionary> ReadGroups(PdfArray array, string entry)
    {
        var groups = new List<PdfDictionary>();
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        foreach (var value in array.Items)
            if (ResolveGroup(value) is { } group && _groups.Contains(group))
            {
                if (seen.Add(group)) groups.Add(group);
            }
            else _warnings.Add($"invalid optional-content {entry} reference");
        return groups;
    }

    void RequireGroup(PdfDictionary group)
    {
        if (!_groups.Contains(group))
            throw new ArgumentException("The layer is not registered in this document's OCProperties/OCGs.", nameof(group));
    }

    public bool IsLocked(PdfDictionary group)
    {
        RequireGroup(group);
        return _locked.Contains(group);
    }

    public bool IsVisible(PdfDictionary group,
        ContentInterpreter.OptionalContentEvent displayEvent = ContentInterpreter.OptionalContentEvent.View, double zoom = 1)
    {
        RequireGroup(group);
        return IsVisibleCore(group, displayEvent, zoom);
    }

    /// <summary>
    /// Applies a UI visibility change atomically, respecting radio groups and locks.
    /// Supply the current render event and zoom when checking usage-driven locked peers.
    /// Turning a layer OFF never selects another layer. Locked changes throw without modifying state.
    /// </summary>
    public void SetVisibility(PdfDictionary group, bool visible,
        ContentInterpreter.OptionalContentEvent displayEvent = ContentInterpreter.OptionalContentEvent.View, double zoom = 1)
    {
        RequireGroup(group);
        if (_locked.Contains(group))
            throw new InvalidOperationException("The optional-content layer is locked.");
        var peers = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        if (visible)
            foreach (var radioGroup in RadioButtonGroups)
                if (radioGroup.Any(g => ReferenceEquals(g, group)))
                    foreach (var peer in radioGroup)
                        if (!ReferenceEquals(peer, group)) peers.Add(peer);

        if (peers.Any(peer => _locked.Contains(peer) && IsVisibleCore(peer, displayEvent, zoom)))
            throw new InvalidOperationException("A visible locked radio-group layer prevents this selection.");
        foreach (var peer in peers)
            if (!_locked.Contains(peer)) _visibility[peer] = false;
        _visibility[group] = visible;
    }

    internal bool IsVisibleCore(PdfDictionary group, ContentInterpreter.OptionalContentEvent displayEvent, double zoom) =>
        _visibility.TryGetValue(group, out var visible) ? visible
            : !IsOff(File, group, Configuration, DefaultConfiguration, displayEvent, zoom);

    internal static bool IsOff(PdfFile file, PdfDictionary ocg, PdfDictionary? cfg,
        PdfDictionary? defaultConfiguration, ContentInterpreter.OptionalContentEvent displayEvent, double zoom)
    {
        if (cfg is null) return false;
        PdfObject? R(PdfObject? value) => file.Resolve(value);
        var baseState = R(cfg.Get("BaseState")).AsName();
        bool off;
        if (baseState == "Unchanged" && !ReferenceEquals(cfg, defaultConfiguration)
            && defaultConfiguration is not null)
            off = IsOff(file, ocg, defaultConfiguration, defaultConfiguration, displayEvent, zoom);
        else
        {
            var baseOff = baseState == "OFF";
            bool Contains(string key) => R(cfg.Get(key)) is PdfArray arr && arr.Items.Any(x => ReferenceEquals(R(x), ocg));
            off = Contains("OFF") || !Contains("ON") && baseOff;
        }

        if (R(cfg.Get("AS")) is not PdfArray applications) return off;
        var eventName = displayEvent.ToString();
        var zoomOutOfRange = false;
        foreach (var application in applications.Items)
        {
            if (R(application) is not PdfDictionary app
                || R(app.Get("Event")).AsName() != eventName
                || R(app.Get("Category")) is not PdfArray categories)
                continue;
            if (R(app.Get("OCGs")) is PdfArray groups
                && !groups.Items.Any(value => ReferenceEquals(R(value), ocg)))
                continue;
            if (R(app.Get("OCGs")) is PdfDictionary group && !ReferenceEquals(group, ocg))
                continue;
            if (R(ocg.Get("Usage")) is not PdfDictionary usage) continue;
            foreach (var categoryValue in categories.Items)
            {
                var category = R(categoryValue).AsName();
                if (category == eventName && R(usage.Get(category)) is PdfDictionary eventUsage)
                {
                    off = R(eventUsage.Get(eventName + "State")).AsName() switch
                    {
                        "ON" => false,
                        "OFF" => true,
                        _ => off,
                    };
                }
                else if (category == "Zoom" && displayEvent == ContentInterpreter.OptionalContentEvent.View
                    && R(usage.Get("Zoom")) is PdfDictionary zoomUsage)
                {
                    var min = R(zoomUsage.Get("min")).AsNumber();
                    var max = R(zoomUsage.Get("max")).AsNumber();
                    if (min is { } minZoom && zoom < minZoom
                        || max is { } maxZoom && zoom > maxZoom)
                        zoomOutOfRange = true;
                }
            }
        }
        if (zoomOutOfRange) off = true;
        return off;
    }
}
