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

/// <summary>Synchronous source over a directory (desktop, tests).</summary>
public sealed class DirectoryAssetSource : IFallbackAssetSource
{
    readonly string _root;

    /// <param name="root">The folder holding the assets; defaults to <c>fallback-fonts</c> beside the application.</param>
    public DirectoryAssetSource(string? root = null) => _root = Path.GetFullPath(root ?? Path.Combine(AppContext.BaseDirectory, "fallback-fonts"));

    public bool TryRead(string path, out byte[]? data)
    {
        data = null;
        if (path.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(path)) return true;
        var full = Path.Combine(_root, path);
        try { if (File.Exists(full)) data = File.ReadAllBytes(full); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
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
            _resolved[path] = bytes is { Length: > 0 } ? bytes : null;
            _unresolved.Remove(path);
        }
    }
}
