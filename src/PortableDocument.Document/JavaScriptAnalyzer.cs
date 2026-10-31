using System.Text;
using PortableDocument.Core;

namespace PortableDocument.Document;

public enum PdfJsRisk { None, Low, Medium, High }

/// <summary>A call expression of the form a.b.c( seen in the source, e.g. app.launchURL. Never resolved or invoked.</summary>
public sealed record PdfJsApiCall(string Name, string Category, int Line, int Count);

/// <summary>A suspicious pattern with its category (Obfuscation, DynamicCode, HeapSpray, Exploit, Network, File, Timer), name and first line.</summary>
public sealed record PdfJsFinding(string Category, string Name, int Line, int Weight);

/// <summary>
/// Static, non-executing analysis of one script: tokenizer-based declaration/URL/API collection plus heuristics.
/// <see cref="Truncated"/> means the length or token budget was reached and only the prefix was analysed. The risk score is a deterministic sum of finding
/// weights (distinct findings only); it is a triage hint, not a verdict.
/// </summary>
public sealed record PdfJsAnalysis(
    int Length,
    int Tokens,
    IReadOnlyList<string> Functions,
    IReadOnlyList<string> Urls,
    IReadOnlyList<PdfJsApiCall> ApiCalls,
    IReadOnlyList<PdfJsFinding> Findings,
    int RiskScore,
    PdfJsRisk Risk,
    bool Truncated);

public static class PdfJavaScriptAnalyzer
{
    public const int MaxLength = 4 * 1024 * 1024;
    public const int MaxTokens = 1_000_000;
    public const int MaxStringLength = 64 * 1024;
    public const int MaxItems = 1024;

    static readonly Dictionary<string, (string Category, int Weight)> Notable = new(StringComparer.Ordinal)
    {
        ["eval"] = ("DynamicCode", 3),
        ["unescape"] = ("Obfuscation", 2),
        ["String.fromCharCode"] = ("Obfuscation", 1),
        ["app.launchURL"] = ("Network", 3),
        ["this.submitForm"] = ("Network", 3),
        ["this.getURL"] = ("Network", 3),
        ["this.mailDoc"] = ("Network", 2),
        ["this.mailForm"] = ("Network", 2),
        ["app.mailMsg"] = ("Network", 2),
        ["Collab.collectEmailInfo"] = ("Exploit", 4),
        ["Collab.getIcon"] = ("Exploit", 4),
        ["util.printf"] = ("Exploit", 2),
        ["media.newPlayer"] = ("Exploit", 4),
        ["spell.customDictionaryOpen"] = ("Exploit", 4),
        ["this.getAnnots"] = ("Exploit", 1),
        ["this.importDataObject"] = ("File", 3),
        ["this.exportDataObject"] = ("File", 3),
        ["this.createDataObject"] = ("File", 2),
        ["this.saveAs"] = ("File", 3),
        ["this.exportAsFDF"] = ("File", 2),
        ["this.importAnFDF"] = ("File", 2),
        ["this.openDoc"] = ("File", 3),
        ["app.openDoc"] = ("File", 3),
        ["app.execMenuItem"] = ("DynamicCode", 2),
        ["app.setTimeOut"] = ("Timer", 1),
        ["app.setInterval"] = ("Timer", 1),
        ["setTimeout"] = ("Timer", 1),
        ["setInterval"] = ("Timer", 1),
    };

    static readonly string[] InterestingRoots = ["app", "util", "Collab", "this", "media", "SOAP", "Net", "ADBC", "event", "spell", "doc", "security", "identity", "String"];
    static readonly string[] UrlPrefixes = ["http://", "https://", "ftp://", "file://", "javascript:", "mailto:", "\\\\"];

    enum Kind { Ident, Number, String, Punct }
    readonly record struct Token(Kind Kind, string Text, int Line);

    public static PdfJsAnalysis Analyze(string source)
    {
        var truncated = false;
        var length = source.Length;
        if (source.Length > MaxLength) { source = source[..MaxLength]; truncated = true; }
        var tokens = Tokenize(source, ref truncated);

        var functions = new List<string>();
        var urls = new List<string>();
        var urlSet = new HashSet<string>(StringComparer.Ordinal);
        var calls = new Dictionary<string, (int Line, int Count)>(StringComparer.Ordinal);
        var findings = new Dictionary<string, PdfJsFinding>(StringComparer.Ordinal);
        void Add(string category, string name, int line, int weight) => findings.TryAdd(category + ":" + name, new PdfJsFinding(category, name, line, weight));

        var hasLoop = false;
        var hasConcat = false;
        var hasLength = false;
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            switch (t.Kind)
            {
                case Kind.String:
                    foreach (var u in ExtractUrls(t.Text))
                        if (urls.Count < MaxItems && urlSet.Add(u)) urls.Add(u);
                    if (IsNopSled(t.Text)) Add("HeapSpray", "NopSledLiteral", t.Line, 4);
                    if (t.Text.Length >= 10_000) Add("HeapSpray", "VeryLargeStringLiteral", t.Line, 2);
                    if (EscapeDensity(t.Text) > 0.5 && t.Text.Length >= 64) Add("Obfuscation", "DenseEscapedString", t.Line, 2);
                    break;
                case Kind.Ident:
                    if (t.Text is "while" or "for") hasLoop = true;
                    if (t.Text == "length") hasLength = true;
                    if (t.Text == "function" && i + 1 < tokens.Count && tokens[i + 1].Kind == Kind.Ident && functions.Count < MaxItems)
                        functions.Add(tokens[i + 1].Text);
                    else if (t.Text == "new" && i + 1 < tokens.Count && tokens[i + 1].Text == "Function")
                        Add("DynamicCode", "new Function", t.Line, 3);
                    else if (t.Text == "new" && i + 3 < tokens.Count && tokens[i + 1].Text == "Array" && tokens[i + 2].Text == "(" &&
                        tokens[i + 3].Kind == Kind.Number && double.TryParse(tokens[i + 3].Text, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 100_000)
                        Add("HeapSpray", "LargeArrayAllocation", t.Line, 2);
                    if (TryCallPath(tokens, i, out var path, out var end) && (path.IndexOf('.') < 0 || IsInteresting(path)))
                    {
                        if (Notable.TryGetValue(path, out var info) || path.IndexOf('.') >= 0 && Notable.TryGetValue(path.Replace("doc.", "this."), out info))
                        {
                            if (calls.TryGetValue(path, out var c)) calls[path] = (c.Line, c.Count + 1);
                            else if (calls.Count < MaxItems) calls[path] = (t.Line, 1);
                            Add(info.Category, path, t.Line, info.Weight);
                        }
                        else if (path.StartsWith("app.", StringComparison.Ordinal) || path.StartsWith("util.", StringComparison.Ordinal)
                            || path.StartsWith("Collab.", StringComparison.Ordinal) || path.StartsWith("this.", StringComparison.Ordinal))
                        {
                            if (calls.TryGetValue(path, out var c)) calls[path] = (c.Line, c.Count + 1);
                            else if (calls.Count < MaxItems) calls[path] = (t.Line, 1);
                        }
                        i = Math.Max(i, end - 1);
                    }
                    break;
                case Kind.Punct:
                    if (t.Text == "+=") hasConcat = true;
                    break;
            }
        }
        if (hasLoop && hasConcat && hasLength && findings.Keys.Any(k => k.StartsWith("HeapSpray:", StringComparison.Ordinal)))
            Add("HeapSpray", "LoopedConcatenationWithSledOrLargeLiteral", 1, 3);

        var api = calls.OrderBy(kv => kv.Value.Line).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new PdfJsApiCall(kv.Key, Notable.TryGetValue(kv.Key, out var n) ? n.Category : "Api", kv.Value.Line, kv.Value.Count)).ToList();
        var ordered = findings.Values.OrderBy(f => f.Category, StringComparer.Ordinal).ThenBy(f => f.Name, StringComparer.Ordinal).ToList();
        var score = ordered.Sum(f => f.Weight);
        var risk = score >= 8 ? PdfJsRisk.High : score >= 3 ? PdfJsRisk.Medium : score >= 1 ? PdfJsRisk.Low : PdfJsRisk.None;
        return new PdfJsAnalysis(length, tokens.Count, functions, urls, api, ordered, score, risk, truncated);
    }

    static bool IsInteresting(string path)
    {
        var dot = path.IndexOf('.');
        var root = path[..dot];
        return Array.IndexOf(InterestingRoots, root) >= 0;
    }

    static bool TryCallPath(List<Token> tokens, int start, out string path, out int end)
    {
        path = "";
        end = start;
        var sb = new StringBuilder(tokens[start].Text);
        var i = start + 1;
        while (i + 1 < tokens.Count && tokens[i].Text == "." && tokens[i + 1].Kind == Kind.Ident)
        {
            sb.Append('.').Append(tokens[i + 1].Text);
            i += 2;
            if (sb.Length > 256) return false;
        }
        if (i >= tokens.Count || tokens[i].Text != "(") return false;
        if (start > 0 && tokens[start - 1].Text is "function" or ".") return false;
        path = sb.ToString();
        end = i;
        return true;
    }

    static IEnumerable<string> ExtractUrls(string s)
    {
        foreach (var prefix in UrlPrefixes)
        {
            var from = 0;
            while (from < s.Length)
            {
                var at = s.IndexOf(prefix, from, StringComparison.OrdinalIgnoreCase);
                if (at < 0) break;
                var end = at;
                while (end < s.Length && !char.IsWhiteSpace(s[end]) && s[end] is not ('"' or '\'' or '<' or '>')) end++;
                yield return s[at..end];
                from = Math.Max(end, at + 1);
            }
        }
    }

    static bool IsNopSled(string s)
    {
        foreach (var marker in new[] { "%u9090", "%u0c0c", "%u0a0a", "\u9090\u9090", "\u0c0c\u0c0c", "\u0a0a\u0a0a", "\\x90\\x90", "%90%90" })
        {
            var count = 0;
            var from = 0;
            while ((from = s.IndexOf(marker, from, StringComparison.OrdinalIgnoreCase)) >= 0 && count < 8) { count++; from += marker.Length; }
            if (count >= 8) return true;
        }
        return false;
    }

    static double EscapeDensity(string s)
    {
        var escapes = 0;
        for (var i = 0; i + 1 < s.Length; i++)
            if (s[i] == '%' && s[i + 1] == 'u' || s[i] == '\\' && s[i + 1] is 'x' or 'u') escapes++;
        return s.Length == 0 ? 0 : escapes * 4.0 / s.Length;
    }

    static List<Token> Tokenize(string s, ref bool truncated)
    {
        var tokens = new List<Token>();
        var line = 1;
        var i = 0;
        while (i < s.Length)
        {
            if (tokens.Count >= MaxTokens) { truncated = true; break; }
            var c = s[i];
            if (c == '\n') { line++; i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) { if (s[i] == '\n') line++; i++; }
                i = Math.Min(s.Length, i + 2);
                continue;
            }
            if (c is '"' or '\'' or '`')
            {
                var sb = new StringBuilder();
                var startLine = line;
                i++;
                while (i < s.Length && s[i] != c)
                {
                    if (s[i] == '\n') { line++; if (c != '`') break; }
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        i++;
                        Unescape(s, ref i, sb);
                        continue;
                    }
                    if (sb.Length < MaxStringLength) sb.Append(s[i]);
                    i++;
                }
                i++;
                tokens.Add(new Token(Kind.String, sb.ToString(), startLine));
                continue;
            }
            if (char.IsLetter(c) || c is '_' or '$')
            {
                var st = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '_' or '$')) i++;
                tokens.Add(new Token(Kind.Ident, s[st..i], line));
                continue;
            }
            if (char.IsDigit(c))
            {
                var st = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '.')) i++;
                tokens.Add(new Token(Kind.Number, s[st..i], line));
                continue;
            }
            if (c == '+' && i + 1 < s.Length && s[i + 1] == '=') { tokens.Add(new Token(Kind.Punct, "+=", line)); i += 2; continue; }
            tokens.Add(new Token(Kind.Punct, c.ToString(), line));
            i++;
        }
        return tokens;
    }

    static void Unescape(string s, ref int i, StringBuilder sb)
    {
        var e = s[i++];
        void Put(char ch) { if (sb.Length < MaxStringLength) sb.Append(ch); }
        switch (e)
        {
            case 'n': Put('\n'); break;
            case 'r': Put('\r'); break;
            case 't': Put('\t'); break;
            case 'x' when i + 1 < s.Length && Uri.IsHexDigit(s[i]) && Uri.IsHexDigit(s[i + 1]):
                Put((char)Convert.ToInt32(s.Substring(i, 2), 16)); i += 2; break;
            case 'u' when i + 3 < s.Length && s.AsSpan(i, 4).ToString() is var h && h.All(Uri.IsHexDigit):
                Put((char)Convert.ToInt32(h, 16)); i += 4; break;
            case '\n': break;
            default: Put(e); break;
        }
    }
}

public sealed record PdfJsSource(string Origin, string Location, string? Language, PdfJsAnalysis? Analysis);

/// <summary>All discovered scripts (document-level, field/annotation/page actions, XFA) analysed statically; <see cref="Risk"/> is the maximum per-script risk.</summary>
public sealed record PdfJsReport(IReadOnlyList<PdfJsSource> Sources, PdfJsRisk Risk, bool Truncated);

public static class JavaScriptFeatures
{
    const int MaxSources = 5000;
    const int MaxStreamBytes = 4 * 1024 * 1024;

    /// <summary>Statically analyses every action and XFA script. Nothing is executed and no URL is opened.</summary>
    public static PdfJsReport GetJavaScriptReport(this PdfDocument doc)
    {
        var f = doc.File;
        var sources = new List<PdfJsSource>();
        var truncated = false;
        var report = doc.GetActionReport();
        truncated |= report.Truncated;
        foreach (var a in report.Actions)
        {
            if (a.JavaScript is null) continue;
            if (sources.Count >= MaxSources) { truncated = true; break; }
            var text = ScriptText(f, a.JavaScript, ref truncated);
            var loc = $"{a.Scope}/{a.Trigger}" + (a.FieldName is null ? "" : $"/{a.FieldName}") + (a.PageIndex is { } p ? $"/page{p}" : "") + (a.Name is null ? "" : $"/{a.Name}");
            sources.Add(new PdfJsSource("Action", loc, "JavaScript", text is null ? null : PdfJavaScriptAnalyzer.Analyze(text)));
        }
        var xfa = doc.GetXfaReport();
        truncated |= xfa.Truncated;
        foreach (var s in xfa.Scripts)
        {
            if (sources.Count >= MaxSources) { truncated = true; break; }
            var js = !s.IsFormCalc;
            sources.Add(new PdfJsSource("Xfa", s.Source + (s.Activity is null ? "" : "#" + s.Activity), js ? "JavaScript" : "FormCalc",
                js ? PdfJavaScriptAnalyzer.Analyze(s.Text) : null));
        }
        var risk = sources.Select(s => s.Analysis?.Risk ?? PdfJsRisk.None).DefaultIfEmpty().Max();
        return new PdfJsReport(sources, risk, truncated);
    }

    static string? ScriptText(PdfFile f, PdfObject script, ref bool truncated)
    {
        switch (f.Resolve(script))
        {
            case PdfString s: return s.Text;
            case PdfStream st:
                byte[] bytes;
                try { bytes = f.DecodeStream(st); }
                catch (Exception e) when (e is PdfParseException or InvalidDataException or InvalidOperationException) { return null; }
                if (bytes.Length > MaxStreamBytes) { bytes = bytes[..MaxStreamBytes]; truncated = true; }
                if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
                if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
                return Encoding.UTF8.GetString(bytes);
            default: return null;
        }
    }
}
