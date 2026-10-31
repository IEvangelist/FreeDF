using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PortableDocument.Document;

namespace AdoBye.Samples;

/// <summary>One downloadable sample as listed in the published <c>samples/manifest.json</c>.</summary>
public sealed record SampleEntry(string Name, string File, string Title, string Description, long Size, int Pages, string Sha256, bool Encrypted, bool RequiresPassword);

/// <summary>Builds the manifest the browser editor uses to lazily fetch (and integrity-check) samples. Output is a pure function of the PDF bytes.</summary>
public static class SampleManifest
{
    public const string FullCollection = "27-full-collection";
    static readonly Regex SampleFile = new(@"^\d\d-[a-z0-9-]+\.pdf$", RegexOptions.CultureInvariant);
    static readonly Dictionary<string, string> Passwords = new() { ["17-"] = "user123", ["18-"] = "owner123" };

    static readonly Dictionary<string, (string Title, string Description)> Info = new()
    {
        ["01"] = ("Spec showcase", "Cover and index of the suite, with shadings, transparency and links to the other samples."),
        ["02"] = ("Standard 14 fonts", "Specimen of the non-embedded Base-14 fonts."),
        ["03"] = ("Typography lab", "Embedded OpenType/CFF subsets, kerning, alignment, columns and drop caps."),
        ["04"] = ("Text render modes", "Fill, stroke, invisible and clipping text render modes."),
        ["05"] = ("Vector graphics", "Paths, Beziers, caps, joins, dashes, fill rules and clipping."),
        ["06"] = ("Patterns and shadings", "Tiling patterns and all seven shading types."),
        ["07"] = ("Transparency", "Opacity, blend modes, soft masks and transparency groups."),
        ["08"] = ("Colour spaces", "Device, calibrated, Lab, ICC, indexed, separation and DeviceN colour."),
        ["09"] = ("Image gallery", "JPEG, Flate predictors, masks, soft masks, indexed and inline images."),
        ["10"] = ("Annotations", "Links, notes, markup, stamps, free text, ink and shapes."),
        ["11"] = ("Fillable form", "AcroForm text, comb, check box, radio, combo, list and button fields."),
        ["12"] = ("Navigation", "Nested outlines, named destinations and page labels."),
        ["13"] = ("Page boxes and rotation", "Media, crop, bleed, trim and art boxes and page rotation."),
        ["14"] = ("Embedded files", "Attachments, file attachment annotations and associated files."),
        ["15"] = ("Optional content", "Layers with default state, radio groups and membership policies."),
        ["16"] = ("TrueType specimen", "Embedded TrueType as composite and simple fonts."),
        ["17"] = ("Encrypted: password required", "AES-256 document that needs the published sample password."),
        ["18"] = ("Encrypted: restricted", "AES-256 document that opens freely but restricts printing, copying and editing."),
        ["19"] = ("Tagged accessible document", "Structure tree, role map, marked content, alt text and table structure."),
        ["20"] = ("Report", "Six-page report with contents, columns, tables and vector charts."),
        ["21"] = ("Brochure", "Tri-fold brochure with outside and inside spreads."),
        ["22"] = ("Invoice", "Tabular invoice layout with totals block."),
        ["23"] = ("Resume", "Two-column resume with sidebar, timeline and skill bars."),
        ["24"] = ("Poster", "A3 poster with display type, layered shadings and blend modes."),
        ["25"] = ("Dashboard charts", "KPI dashboard with bar, line, donut and sparkline charts."),
        ["26"] = ("Repacked object streams", "The report rewritten with compressed object and cross-reference streams."),
        ["27"] = ("Full sample collection", "All 26 samples merged into one document with outline, page labels and working links."),
        ["28"] = ("Benchmarks, brand and spec parity", "Tagged 18-page showcase: benchmark charts, brand directions with contrast ratios and the honest ISO 32000-2 parity map."),
    };

    public static IReadOnlyList<SampleEntry> Entries(string samplesDir)
    {
        var entries = new List<SampleEntry>();
        foreach (var path in Directory.GetFiles(samplesDir, "*.pdf").Where(p => SampleFile.IsMatch(Path.GetFileName(p))).Order(StringComparer.Ordinal))
        {
            var file = Path.GetFileName(path);
            var bytes = System.IO.File.ReadAllBytes(path);
            var password = Passwords.FirstOrDefault(kv => file.StartsWith(kv.Key, StringComparison.Ordinal)).Value ?? "";
            var requiresPassword = false;
            PdfDocument doc;
            try { PdfDocument.Load(bytes, ""); doc = PdfDocument.Load(bytes, password); }
            catch when (password.Length > 0) { requiresPassword = true; doc = PdfDocument.Load(bytes, password); }
            var key = file[..2];
            var (title, description) = Info.TryGetValue(key, out var known) ? known : (Path.GetFileNameWithoutExtension(file), "");
            entries.Add(new SampleEntry(Path.GetFileNameWithoutExtension(file), file, title, description, bytes.LongLength, doc.Pages.Count,
                Convert.ToHexStringLower(SHA256.HashData(bytes)), doc.File.IsEncrypted, requiresPassword));
        }
        // The full collection is the one-click entry, so it leads; the rest keep numeric order.
        return entries.OrderBy(e => e.Name == FullCollection ? 0 : 1).ThenBy(e => e.Name, StringComparer.Ordinal).ToList();
    }

    public static string ToJson(IEnumerable<SampleEntry> entries)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            json.WriteNumber("version", 1);
            json.WriteStartArray("samples");
            foreach (var e in entries)
            {
                json.WriteStartObject();
                json.WriteString("name", e.Name);
                json.WriteString("file", e.File);
                json.WriteString("title", e.Title);
                json.WriteString("description", e.Description);
                json.WriteNumber("size", e.Size);
                json.WriteNumber("pages", e.Pages);
                json.WriteString("sha256", e.Sha256);
                json.WriteBoolean("encrypted", e.Encrypted);
                json.WriteBoolean("requiresPassword", e.RequiresPassword);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>Copies the sample PDFs into <paramref name="outDir"/> and writes <c>manifest.json</c> beside them; returns the entries.</summary>
    public static IReadOnlyList<SampleEntry> Publish(string samplesDir, string outDir)
    {
        var entries = Entries(samplesDir);
        if (!entries.Any(e => e.Name == FullCollection)) throw new FileNotFoundException($"{FullCollection}.pdf is missing; run: generate {samplesDir} 27-");
        Directory.CreateDirectory(outDir);
        foreach (var e in entries) System.IO.File.Copy(Path.Combine(samplesDir, e.File), Path.Combine(outDir, e.File), true);
        System.IO.File.WriteAllText(Path.Combine(outDir, "manifest.json"), ToJson(entries), new UTF8Encoding(false));
        return entries;
    }
}
