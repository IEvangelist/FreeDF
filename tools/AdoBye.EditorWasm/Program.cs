using System.Text.Json;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using PortableDocument.Content;

return;

[SupportedOSPlatform("browser")]
public static partial class BrowserEditor
{
    static readonly EditorSession Session = new(new FontCatalog(), new BrowserFontFallback());
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public sealed record Command(string Method, string Path, Dictionary<string, string> Query, string? Body, string? FileName);

    [JSExport]
    public static string Request(string command)
    {
        Command? c = null;
        try
        {
            c = JsonSerializer.Deserialize<Command>(command, Json) ?? throw new ArgumentException("Missing editor command.");
            var reply = Session.Handle(c.Method, c.Path, c.Query, c.Body is null ? null : Convert.FromBase64String(c.Body), c.FileName);
            return JsonSerializer.Serialize(new { data = Convert.ToBase64String(reply.Data), contentType = reply.ContentType, fileName = reply.FileName }, Json);
        }

        catch (Exception e)
        {
            if (e is System.Security.Cryptography.CryptographicException or PlatformNotSupportedException && c?.Method == "POST" && c.Path is "/api/open" or "/api/import" or "/api/inspect")
            {
                var message = "Encrypted PDFs require the server editor. This browser cannot open this protection; no decrypted recovery copy was stored. " + e.Message;
                Console.Error.WriteLine(message);
                return JsonSerializer.Serialize(new { error = message }, Json);
            }
            Console.Error.WriteLine(e);
            return JsonSerializer.Serialize(new { error = e.Message }, Json);
        }
    }
}

/// <summary>Licensed in-repo substitutes; embedded PDF fonts still take precedence.</summary>
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
