using PortableDocument.Core;

namespace PortableDocument.Content;

/// <summary>Target event used to evaluate optional-content auto states (ISO 32000-2 8.11.4.4 /AS).</summary>
public enum OptionalContentEvent { View, Print, Export }

/// <summary>
/// Evaluates optional-content visibility (ISO 32000-2 8.11): default configuration /D (BaseState, ON, OFF, Intent, AS),
/// OCG /Usage view/print/export states, OCMD policies (/P) and visibility expressions (/VE).
/// </summary>
public sealed class OptionalContentEvaluator
{
    const int MaxExpressionDepth = 32, MaxExpressionNodes = 4096;
    readonly PdfFile _f;
    readonly PdfDictionary? _cfg;
    readonly OptionalContentEvent _event;
    readonly Dictionary<PdfDictionary, bool> _cache = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<string> _configIntents;

    public OptionalContentEvaluator(PdfFile file, OptionalContentEvent target = OptionalContentEvent.View)
    {
        _f = file; _event = target;
        _cfg = R(R(file.Catalog.Get("OCProperties")) is PdfDictionary ocp ? ocp.Get("D") : null) as PdfDictionary;
        _configIntents = Intents(_cfg?.Get("Intent"));
    }

    PdfObject? R(PdfObject? o) => _f.Resolve(o);

    /// <summary>True when content tagged with the given OCG or OCMD dictionary is hidden.</summary>
    public bool IsHidden(PdfDictionary oc) => !IsVisible(oc);

    public bool IsVisible(PdfDictionary oc)
    {
        if (_cache.TryGetValue(oc, out var v)) return v;
        v = R(oc.Get("Type")).AsName() == "OCMD" ? Membership(oc) : GroupOn(oc);
        _cache[oc] = v;
        return v;
    }

    bool Membership(PdfDictionary ocmd)
    {
        if (R(ocmd.Get("VE")) is PdfArray ve)
        {
            var nodes = 0;
            return Expression(ve, 0, ref nodes);
        }
        var ocgs = R(ocmd.Get("OCGs"));
        var list = ocgs is PdfArray oa ? oa.Items.Select(R).OfType<PdfDictionary>().Take(MaxExpressionNodes).ToList() : ocgs is PdfDictionary od ? [od] : [];
        if (list.Count == 0) return true;
        var states = list.Select(GroupOn).ToList();
        return R(ocmd.Get("P")).AsName() switch
        {
            "AllOn" => states.All(x => x),
            "AnyOff" => states.Any(x => !x),
            "AllOff" => states.All(x => !x),
            _ => states.Any(x => x),
        };
    }

    // [/And|/Or|/Not operand...]; an operand is an OCG dictionary or a nested expression. Malformed nodes evaluate to true (visible).
    bool Expression(PdfArray e, int depth, ref int nodes)
    {
        if (depth > MaxExpressionDepth || ++nodes > MaxExpressionNodes || e.Count == 0) return true;
        var op = R(e[0]).AsName();
        bool Operand(PdfObject? o, ref int n) => R(o) switch
        {
            PdfArray sub => Expression(sub, depth + 1, ref n),
            PdfDictionary d => R(d.Get("Type")).AsName() == "OCMD" ? Membership(d) : GroupOn(d),
            _ => true,
        };
        switch (op)
        {
            case "Not": return e.Count < 2 || !Operand(e[1], ref nodes);
            case "And":
                {
                    var r = true;
                    for (var i = 1; i < e.Count; i++) { if (++nodes > MaxExpressionNodes) return true; r &= Operand(e[i], ref nodes); }
                    return r;
                }
            case "Or":
                {
                    var r = false;
                    for (var i = 1; i < e.Count; i++) { if (++nodes > MaxExpressionNodes) return true; r |= Operand(e[i], ref nodes); }
                    return e.Count < 2 || r;
                }
            default: return true;
        }
    }

    HashSet<string> Intents(PdfObject? o)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        switch (R(o))
        {
            case PdfName n: set.Add(n.Value); break;
            case PdfArray a: foreach (var x in a.Items) if (R(x).AsName() is { } s) set.Add(s); break;
        }
        if (set.Count == 0) set.Add("View");
        return set;
    }

    bool InArray(string key, PdfDictionary ocg) => R(_cfg?.Get(key)) is PdfArray arr && arr.Items.Any(x => ReferenceEquals(R(x), ocg));

    bool GroupOn(PdfDictionary ocg)
    {
        if (_cfg is null) return true;
        // Groups whose intent does not overlap the configuration's intent do not affect visibility (8.11.4.3).
        var gi = Intents(ocg.Get("Intent"));
        if (!gi.Contains("All") && !_configIntents.Contains("All") && !gi.Overlaps(_configIntents)) return true;

        var on = R(_cfg.Get("BaseState")).AsName() != "OFF";
        if (InArray("ON", ocg)) on = true;
        if (InArray("OFF", ocg)) on = false;

        // Auto states: the usage-application dictionaries matching the target event override for the groups they list.
        if (R(_cfg.Get("AS")) is PdfArray autoStates)
        {
            var ev = _event.ToString();
            foreach (var ao in autoStates.Items)
            {
                if (R(ao) is not PdfDictionary ad || R(ad.Get("Event")).AsName() != ev) continue;
                if (R(ad.Get("OCGs")) is not PdfArray ags || !ags.Items.Any(x => ReferenceEquals(R(x), ocg))) continue;
                if (R(ad.Get("Category")) is not PdfArray cats || R(ocg.Get("Usage")) is not PdfDictionary usage) continue;
                foreach (var co in cats.Items)
                {
                    var cat = R(co).AsName();
                    if (cat is not ("View" or "Print" or "Export")) continue;
                    if (R(usage.Get(cat)) is PdfDictionary cd && R(cd.Get(cat + "State")).AsName() is { } st) on = st != "OFF";
                }
            }
        }
        return on;
    }
}
