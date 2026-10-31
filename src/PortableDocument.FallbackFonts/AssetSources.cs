using System.Security.Cryptography;
using System.Text.Json;
using PortableDocument.Content;

namespace PortableDocument.FallbackFonts;

/// <summary>Reads fallback font assets (relative paths such as <c>latin/LiberationSans-Regular.ttf</c>).</summary>
public interface IFallbackAssetSource
{
    /// <summary>
    /// Returns true when the asset is resolved: <paramref name="data"/> holds its bytes, or null when it does not exist (or could not be fetched).
    /// Returns false while the asset is still being fetched; the caller should treat the glyph data as temporarily missing.
    /// </summary>
    bool TryRead(string path, out byte[]? data);
}

/// <summary>Size and SHA-256 of every shipped asset (the asset <c>manifest.json</c>); used to reject truncated or corrupt data.</summary>
public sealed class AssetManifest
{
    readonly Dictionary<string, (long Size, string Sha256)> _assets = new(StringComparer.Ordinal);

    public int Count => _assets.Count;

    public static AssetManifest? TryParse(byte[] json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var m = new AssetManifest();
            foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
                m._assets[a.GetProperty("path").GetString()!] = (a.GetProperty("size").GetInt64(), a.GetProperty("sha256").GetString()!);
            return m;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or NullReferenceException) { return null; }
    }

    /// <summary>True only for a listed asset whose length and hash both match.</summary>
    public bool Verify(string path, byte[] data) =>
        _assets.TryGetValue(path, out var e) && e.Size == data.Length &&
        string.Equals(Convert.ToHexString(SHA256.HashData(data)), e.Sha256, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Synchronous source over a directory (desktop, tests). Assets are checked against <c>manifest.json</c> in that directory when present.</summary>
public sealed class DirectoryAssetSource : IFallbackAssetSource
{
    readonly string _root;
    readonly Lazy<AssetManifest?> _manifest;

    /// <param name="root">The folder holding the assets; defaults to <c>fallback-fonts</c> beside the application.</param>
    public DirectoryAssetSource(string? root = null)
    {
        _root = Path.GetFullPath(root ?? Path.Combine(AppContext.BaseDirectory, "fallback-fonts"));
        _manifest = new(() =>
        {
            try { var f = Path.Combine(_root, "manifest.json"); return File.Exists(f) ? AssetManifest.TryParse(File.ReadAllBytes(f)) : null; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        });
    }

    public bool TryRead(string path, out byte[]? data)
    {
        data = null;
        if (path.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(path)) return true;
        var full = Path.Combine(_root, path);
        try { if (File.Exists(full)) data = File.ReadAllBytes(full); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (data is not null && _manifest.Value is { } m && !m.Verify(path, data)) data = null;
        return true;
    }
}

/// <summary>
/// Source whose bytes are supplied by the host after an asynchronous fetch (the browser). A first read of an unknown asset records a
/// request and reports "pending"; <see cref="Provide"/> resolves it and the host re-renders.
/// </summary>
public sealed class ProvidedAssetSource : IFallbackAssetSource, IFontAssetPending
{
    readonly Dictionary<string, byte[]?> _resolved = [];
    readonly HashSet<string> _unresolved = [];
    readonly List<string> _requests = [];
    readonly object _gate = new();

    /// <summary>When set, provided bytes must match it; anything else is recorded as unavailable instead of being cached.</summary>
    public AssetManifest? Manifest { get; set; }

    public bool HasPending { get { lock (_gate) return _unresolved.Count > 0; } }

    public bool TryRead(string path, out byte[]? data)
    {
        lock (_gate)
        {
            if (_resolved.TryGetValue(path, out data)) return true;
            if (_unresolved.Add(path)) _requests.Add(path);
            data = null;
            return false;
        }
    }

    /// <summary>Paths newly requested since the last call; the host fetches them and calls <see cref="Provide"/> for each.</summary>
    public string[] TakeRequests()
    {
        lock (_gate)
        {
            var r = _requests.ToArray();
            _requests.Clear();
            return r;
        }
    }

    /// <summary>Resolves a requested asset. Null (or empty) bytes record that it is unavailable, for example offline.</summary>
    public void Provide(string path, byte[]? bytes)
    {
        lock (_gate)
        {
            _resolved[path] = bytes is { Length: > 0 } && (Manifest is null || Manifest.Verify(path, bytes)) ? bytes : null;
            _unresolved.Remove(path);
        }
    }
}
