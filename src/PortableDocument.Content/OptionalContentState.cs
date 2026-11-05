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
    readonly Action<string> _warn;
    public PdfDocument Document { get; }
    internal PdfFile File { get; }
    internal PdfDictionary? Configuration { get; }
    internal PdfDictionary? DefaultConfiguration { get; }
    public IReadOnlyList<PdfDictionary> Groups { get; }
    public IReadOnlyList<IReadOnlyList<PdfDictionary>> RadioButtonGroups { get; }
    public IReadOnlyList<string> Warnings => _warnings.AsReadOnly();

    public OptionalContentState(PdfDocument document, string? configurationName = null)
    {
        _warn = Warn;
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

    PdfDictionary? ResolveGroup(PdfObject value) => ResolveGroup(File, value);

    static PdfDictionary? ResolveGroup(PdfFile file, PdfObject value) =>
        value is PdfRef reference
        && file.Xref.TryGetValue(reference.Number, out var entry) && entry.InUse
        && reference.Generation == (entry.Compressed ? 0 : entry.GenOrIndex)
        && file.Resolve(value) is PdfDictionary group && file.Resolve(group.Get("Type")).AsName() == "OCG"
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
        ContentInterpreter.OptionalContentEvent displayEvent = ContentInterpreter.OptionalContentEvent.View, double zoom = 1,
        OptionalContentContext? context = null)
    {
        RequireGroup(group);
        return IsVisibleCore(group, displayEvent, zoom, context);
    }

    /// <summary>
    /// Applies a UI visibility change atomically, respecting radio groups and locks.
    /// Supply the current render event, zoom and context when checking usage-driven locked peers.
    /// Turning a layer OFF never selects another layer. Locked changes throw without modifying state.
    /// </summary>
    public void SetVisibility(PdfDictionary group, bool visible,
        ContentInterpreter.OptionalContentEvent displayEvent = ContentInterpreter.OptionalContentEvent.View, double zoom = 1,
        OptionalContentContext? context = null)
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

        if (peers.Any(peer => _locked.Contains(peer) && IsVisibleCore(peer, displayEvent, zoom, context)))
            throw new InvalidOperationException("A visible locked radio-group layer prevents this selection.");
        foreach (var peer in peers)
            if (!_locked.Contains(peer)) _visibility[peer] = false;
        _visibility[group] = visible;
    }

    void Warn(string warning)
    {
        if (!_warnings.Contains(warning)) _warnings.Add(warning);
    }

    internal bool IsVisibleCore(PdfDictionary group, ContentInterpreter.OptionalContentEvent displayEvent, double zoom,
        OptionalContentContext? context = null) =>
        _visibility.TryGetValue(group, out var visible) ? visible
            : !IsOff(File, group, Configuration, DefaultConfiguration, displayEvent, zoom, context, _warn);

    static bool ContainsGroup(PdfFile file, PdfArray groups, PdfDictionary group)
    {
        foreach (var value in groups.Items)
            if (ReferenceEquals(file.Resolve(value), group)) return true;
        return false;
    }

    internal static bool IsOff(PdfFile file, PdfDictionary ocg, PdfDictionary? cfg,
        PdfDictionary? defaultConfiguration, ContentInterpreter.OptionalContentEvent displayEvent, double zoom,
        OptionalContentContext? context = null, Action<string>? warn = null)
    {
        if (cfg is null) return false;
        PdfObject? R(PdfObject? value) => file.Resolve(value);
        var baseState = R(cfg.Get("BaseState")).AsName();
        bool off;
        if (baseState == "Unchanged" && !ReferenceEquals(cfg, defaultConfiguration)
            && defaultConfiguration is not null)
            off = IsOff(file, ocg, defaultConfiguration, defaultConfiguration, displayEvent, zoom, context, warn);
        else
        {
            var baseOff = baseState == "OFF";
            off = R(cfg.Get("OFF")) is PdfArray offGroups && ContainsGroup(file, offGroups, ocg)
                || !(R(cfg.Get("ON")) is PdfArray onGroups && ContainsGroup(file, onGroups, ocg)) && baseOff;
        }

        if (R(cfg.Get("AS")) is not PdfArray applications) return off;
        var eventName = displayEvent switch
        {
            ContentInterpreter.OptionalContentEvent.View => "View",
            ContentInterpreter.OptionalContentEvent.Print => "Print",
            ContentInterpreter.OptionalContentEvent.Export => "Export",
            _ => displayEvent.ToString()
        };
        bool? recommendations = null;
        foreach (var application in applications.Items)
        {
            if (R(application) is not PdfDictionary app
                || R(app.Get("Event")).AsName() != eventName
                || R(app.Get("Category")) is not PdfArray categories)
                continue;
            var applicationGroups = R(app.Get("OCGs"));
            if (applicationGroups is null or PdfNull) continue;
            if (applicationGroups is not PdfArray groups)
            {
                warn?.Invoke("invalid optional-content application OCGs array");
                continue;
            }
            if (!ContainsGroup(file, groups, ocg))
            {
                foreach (var value in groups.Items)
                    if (ResolveGroup(file, value) is null)
                    {
                        warn?.Invoke("invalid optional-content application OCGs reference");
                        break;
                    }
                continue;
            }
            if (R(ocg.Get("Usage")) is not PdfDictionary usage) continue;
            foreach (var categoryValue in categories.Items)
            {
                var category = R(categoryValue).AsName();
                bool? recommendation = null;
                if (category is "View" or "Print" or "Export"
                    && R(usage.Get(category)) is PdfDictionary eventUsage)
                {
                    var stateKey = category switch
                    {
                        "View" => "ViewState",
                        "Print" => "PrintState",
                        _ => "ExportState"
                    };
                    recommendation = R(eventUsage.Get(stateKey)).AsName() switch
                    {
                        "ON" => true,
                        "OFF" => false,
                        _ => null,
                    };
                }
                else if (category == "Zoom" && R(usage.Get("Zoom")) is PdfDictionary zoomUsage)
                {
                    var min = R(zoomUsage.Get("min")).AsNumber() ?? 0;
                    var max = R(zoomUsage.Get("max")).AsNumber() ?? double.PositiveInfinity;
                    recommendation = zoom >= min && zoom < max;
                }
                else if (category is "Language" or "User")
                    recommendation = UsageRecommendation(file, ocg, usage, app, category, context, warn);
                else if (category is not ("View" or "Print" or "Export" or "Zoom"))
                    warn?.Invoke($"unsupported optional-content usage category {category ?? "(non-name)"}");
                if (recommendation is { } on)
                    recommendations = (recommendations ?? true) && on;
            }
        }
        return recommendations is { } visible ? !visible : off;
    }

    static bool? UsageRecommendation(PdfFile file, PdfDictionary ocg, PdfDictionary usage, PdfDictionary application,
        string category, OptionalContentContext? context, Action<string>? warn)
    {
        PdfObject? R(PdfObject? value) => file.Resolve(value);
        var value = R(usage.Get(category));
        if (value is null or PdfNull)
        {
            if (usage.Get(category) is PdfRef)
                warn?.Invoke($"invalid optional-content {category} usage dictionary");
            return null;
        }
        var applicationGroups = R(application.Get("OCGs"));
        if (applicationGroups is null or PdfNull && application.Get("OCGs") is not PdfRef) return null;
        if (applicationGroups is not PdfArray groups)
        {
            warn?.Invoke($"invalid optional-content {category} application OCGs array");
            return null;
        }
        var selected = false;
        foreach (var member in groups.Items)
            if (ResolveGroup(file, member) is { } group)
                selected |= ReferenceEquals(group, ocg);
            else warn?.Invoke($"invalid optional-content {category} application OCGs reference");
        if (!selected) return null;
        if (value is not PdfDictionary rule)
        {
            warn?.Invoke($"invalid optional-content {category} usage dictionary");
            return null;
        }

        if (category == "User")
        {
            var type = R(rule.Get("Type")).AsName();
            var names = R(rule.Get("Name"));
            if (type is not ("Ind" or "Ttl" or "Org")
                || names is not (PdfString or PdfArray)
                || names is PdfArray array && (array.Count == 0 || array.Items.Any(x => R(x) is not PdfString)))
            {
                warn?.Invoke("invalid optional-content User Type or Name");
                return null;
            }
            var identity = type switch
            {
                "Ind" => context?.UserName,
                "Ttl" => context?.UserTitle,
                _ => context?.UserOrganization,
            };
            if (identity is null)
            {
                warn?.Invoke($"unapplied optional-content User {type} rule: caller identity context required");
                return null;
            }
            return names is PdfString name ? name.Text == identity
                : ((PdfArray)names).Items.Any(x => ((PdfString)R(x)!).Text == identity);
        }

        if (ReadLanguage(file, rule, warn) is not { } language) return null;
        if (context?.Language is not { } requested)
        {
            warn?.Invoke("unapplied optional-content Language rule: caller language context required");
            return null;
        }
        if (!IsLanguageTag(requested))
        {
            warn?.Invoke("invalid optional-content Language context");
            return null;
        }
        if (language.Equals(requested, StringComparison.OrdinalIgnoreCase)) return true;
        if (R(rule.Get("Preferred")).AsName() != "ON"
            || !PrimaryLanguage(language).Equals(PrimaryLanguage(requested), StringComparison.OrdinalIgnoreCase))
            return false;

        // Exact matches suppress preferred partial matches only within this application's OCGs list.
        foreach (var member in groups.Items)
            if (ResolveGroup(file, member) is { } peer && !ReferenceEquals(peer, ocg)
                && R(peer.Get("Usage")) is PdfDictionary peerUsage
                && R(peerUsage.Get("Language")) is PdfDictionary peerRule
                && ReadLanguage(file, peerRule, warn) is { } peerLanguage
                && peerLanguage.Equals(requested, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    static string? ReadLanguage(PdfFile file, PdfDictionary rule, Action<string>? warn)
    {
        var language = (file.Resolve(rule.Get("Lang")) as PdfString)?.Text;
        var preferred = file.Resolve(rule.Get("Preferred"));
        if (language is null || !IsLanguageTag(language)
            || preferred is not (null or PdfNull) && preferred.AsName() is not ("ON" or "OFF"))
        {
            warn?.Invoke("invalid optional-content Language Lang or Preferred");
            return null;
        }
        return language;
    }

    static bool IsLanguageTag(string language)
    {
        var length = 0;
        var primary = true;
        foreach (var c in language)
        {
            if (c == '-')
            {
                if (length is < 1 or > 8) return false;
                primary = false;
                length = 0;
            }
            else
            {
                if (primary ? !char.IsAsciiLetter(c) : !char.IsAsciiLetterOrDigit(c)) return false;
                if (++length > 8) return false;
            }
        }
        return language.Length == 0 || length > 0;
    }

    static ReadOnlySpan<char> PrimaryLanguage(string language)
    {
        var separator = language.IndexOf('-');
        return separator >= 0 ? language.AsSpan(0, separator) : language.AsSpan();
    }
}
