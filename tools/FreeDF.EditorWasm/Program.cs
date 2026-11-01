using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using PortableDocument.Content;
using PortableDocument.FallbackFonts;

return;

[SupportedOSPlatform("browser")]
public static partial class BrowserEditor
{
    static readonly CultureInfo Culture = BrowserCulture.UseInvariant();
    // Fallback fonts and CJK glyph chunks are fetched by the host on first use (see worker.js) and handed over through ProvideFontAsset.
    static readonly ProvidedAssetSource FontAssets = new();
    static readonly EditorSession Session = new(new FontCatalog(), new BundledFontFallback(FontAssets, new BrowserFontFallback()));
    public sealed record Command(string Method, string Path, Dictionary<string, string> Query, string? Body, string? FileName);
    sealed record DiagnosticsReply(string Culture, string DecimalFormat, bool NormalizesDecomposed, bool Decomposes,
        string Upper, bool InvariantGlobalization);
    sealed record BinaryReply(string Data, string ContentType, string? FileName, EditorSession.RenderMetrics? Performance);
    sealed record ErrorReply(string Error);

    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(Command))]
    [JsonSerializable(typeof(DiagnosticsReply))]
    [JsonSerializable(typeof(BinaryReply))]
    [JsonSerializable(typeof(ErrorReply))]
    sealed partial class BrowserJsonContext : JsonSerializerContext;

    [JSExport]
    public static string TakeFontRequests() => JsonSerializer.Serialize(FontAssets.TakeRequests(), EditorSession.JsonType<string[]>());

    [JSExport]
    public static void ProvideFontAsset(string path, JSObject body)
    {
        using var b = body;
        FontAssets.Provide(path, b.GetPropertyAsByteArray("bytes"));
    }

    [JSExport]
    public static void FontAssetUnavailable(string path) => FontAssets.Provide(path, null);

    [JSExport]
    public static void ProvideFontManifest(JSObject body)
    {
        using var b = body;
        FontAssets.Manifest = AssetManifest.TryParse(b.GetPropertyAsByteArray("bytes") ?? []);
    }

    [JSExport]
    public static string Request(string command)
    {
        Command? c = null;
        try
        {
            c = JsonSerializer.Deserialize(command, BrowserJsonContext.Default.Command) ?? throw new ArgumentException("Missing editor command.");
            return Execute(c, c.Body is null ? null : Convert.FromBase64String(c.Body));
        }
        catch (Exception e)
        {
            return Error(c, e);
        }
    }

    [JSExport]
    public static byte[] RequestBytes(string method, string path, string query, string? body, JSObject exchange, string? fileName)
    {
        Command? c = null;
        using var metadata = exchange;
        try
        {
            c = new(method, path, JsonSerializer.Deserialize(query, EditorSession.JsonType<Dictionary<string, string>>()) ?? [], null, fileName);
            var bytes = body is null ? metadata.GetPropertyAsByteArray("bytes") : Encoding.UTF8.GetBytes(body);
            var reply = path == "/api/_diagnostics"
                ? new EditorSession.Reply(Encoding.UTF8.GetBytes(Diagnostics()), "application/json")
                : Session.Handle(method, path, c.Query, bytes, fileName);
            metadata.SetProperty("contentType", reply.ContentType);
            metadata.SetProperty("fileName", reply.FileName);
            if (Session.LastRenderMetrics is { } metrics)
                metadata.SetProperty("performance", JsonSerializer.Serialize(metrics, EditorSession.JsonType<EditorSession.RenderMetrics>()));
            return reply.Data;
        }
        catch (Exception e)
        {
            metadata.SetProperty("error", Error(c, e));
            return [];
        }
    }

    [JSExport]
    public static string RequestBinary(
        string method,
        string path,
        string query,
        JSObject body,
        string? fileName)
    {
        Command? c = null;
        using var requestBody = body;
        try
        {
            c = new(method, path, JsonSerializer.Deserialize(query, EditorSession.JsonType<Dictionary<string, string>>()) ?? [], null, fileName);
            return Execute(c, requestBody.GetPropertyAsByteArray("bytes") ?? throw new ArgumentException("Missing binary request body."));
        }
        catch (Exception e)
        {
            return Error(c, e);
        }
    }

    static string Diagnostics() => JsonSerializer.Serialize(new DiagnosticsReply(
        CultureInfo.CurrentCulture.Name,
        1234.5.ToString("0.##"),
        "e\u0301".Normalize(NormalizationForm.FormC) == "\u00e9",
        "\u00e9".Normalize(NormalizationForm.FormD) == "e\u0301",
        "i".ToUpper(),
        AppContext.TryGetSwitch("System.Globalization.Invariant", out var invariant) && invariant),
        BrowserJsonContext.Default.DiagnosticsReply);

    static string Execute(Command c, byte[]? body)
    {
        try
        {
            var reply = c.Path == "/api/_diagnostics"
                ? new EditorSession.Reply(Encoding.UTF8.GetBytes(Diagnostics()), "application/json")
                : Session.Handle(c.Method, c.Path, c.Query, body, c.FileName);
            return JsonSerializer.Serialize(new BinaryReply(Convert.ToBase64String(reply.Data), reply.ContentType, reply.FileName, Session.LastRenderMetrics), BrowserJsonContext.Default.BinaryReply);
        }

        catch (Exception e)
        {
            return Error(c, e);
        }
    }

    static string Error(Command? c, Exception e)
    {
        if (e is System.Security.Cryptography.CryptographicException or PlatformNotSupportedException && c?.Method == "POST" && c.Path is "/api/open" or "/api/import" or "/api/inspect")
        {
            var message = "This PDF uses encryption the browser editor cannot process; no decrypted recovery copy was stored. " + e.Message;
            Console.Error.WriteLine(message);
            return JsonSerializer.Serialize(new ErrorReply(message), BrowserJsonContext.Default.ErrorReply);
        }
        if (e is ArgumentException && e.Message.StartsWith("password-", StringComparison.Ordinal)) return JsonSerializer.Serialize(new ErrorReply(e.Message), BrowserJsonContext.Default.ErrorReply);
        Console.Error.WriteLine(e is NotSupportedException ? e.Message : e);
        return JsonSerializer.Serialize(new ErrorReply(e.Message), BrowserJsonContext.Default.ErrorReply);
    }
}

static class BrowserCulture
{
    public static CultureInfo UseInvariant()
    {
        var culture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        return culture;
    }
}

/// <summary>Last-resort substitute (Bitstream Vera) used only when the lazily fetched fallback fonts cannot be loaded, for example offline on first use.</summary>
sealed class BrowserFontFallback : IFontFallback
{
    readonly TrueTypeFont regular = Load("Vera.ttf"), bold = Load("VeraBd.ttf");
    static TrueTypeFont Load(string name)
    {
        using var stream = typeof(BrowserFontFallback).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing bundled font: " + name);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return new(ms.ToArray());
    }
    public TrueTypeFont Find(string baseFont, bool isBold, bool italic, bool fixedPitch, bool serif) => isBold ? bold : regular;
}
