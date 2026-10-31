using PortableDocument.Core;

namespace PortableDocument.Document;

public readonly record struct PdfRect(double Left, double Bottom, double Right, double Top)
{
    public double Width => Right - Left;
    public double Height => Top - Bottom;
}

public sealed class PdfPage
{
    public PdfDocument Document { get; }
    public PdfDictionary Dict { get; }
    readonly Dictionary<string, PdfObject> _inherited;

    internal PdfPage(PdfDocument doc, PdfDictionary dict, Dictionary<string, PdfObject> inherited)
    {
        Document = doc; Dict = dict; _inherited = inherited;
    }

    PdfObject? Attr(string key) => Document.File.Resolve(Dict.Get(key) ?? _inherited.GetValueOrDefault(key));

    internal IReadOnlyDictionary<string, PdfObject> Inherited => _inherited;

    PdfRect Rect(string key)
    {
        if (Attr(key) is PdfArray a && a.Count >= 4)
        {
            var v = a.Items.Select(x => Document.File.Resolve(x).AsNumber() ?? 0).ToArray();
            return new PdfRect(Math.Min(v[0], v[2]), Math.Min(v[1], v[3]), Math.Max(v[0], v[2]), Math.Max(v[1], v[3]));
        }
        return default;
    }

    public PdfRect MediaBox => Rect("MediaBox") is { Width: > 0 } m ? m : new PdfRect(0, 0, 612, 792);

    public PdfRect CropBox
    {
        get
        {
            var c = Rect("CropBox");
            if (c.Width <= 0 || c.Height <= 0) return MediaBox;
            var m = MediaBox;
            var r = new PdfRect(Math.Max(c.Left, m.Left), Math.Max(c.Bottom, m.Bottom), Math.Min(c.Right, m.Right), Math.Min(c.Top, m.Top));
            return r.Width > 0 && r.Height > 0 ? r : m;
        }
    }

    public int Rotate => (((int)(Attr("Rotate").AsNumber() ?? 0) % 360) + 360) % 360;
    public double UserUnit => Attr("UserUnit").AsNumber() ?? 1.0;
    public PdfDictionary Resources => Attr("Resources") as PdfDictionary ?? new PdfDictionary();

    public byte[] GetContents()
    {
        var c = Document.File.Resolve(Dict.Get("Contents"));
        var ms = new MemoryStream();
        void Add(PdfObject? o)
        {
            if (Document.File.Resolve(o) is PdfStream s)
            {
                ms.Write(Document.File.DecodeStream(s));
                ms.WriteByte((byte)'\n');
            }
        }
        if (c is PdfArray arr) foreach (var o in arr.Items) Add(o); else Add(c);
        return ms.ToArray();
    }
}

public sealed class PdfDocument
{
    public PdfFile File { get; }
    public List<PdfPage> Pages { get; } = [];

    PdfDocument(PdfFile f) { File = f; }

    StructureTree? _structure;
    bool _structureLoaded;

    /// <summary>Parsed logical structure tree (ISO 32000-2 14.7), or null when the document has no StructTreeRoot.</summary>
    public StructureTree? Structure
    {
        get
        {
            if (!_structureLoaded) { _structure = StructureTree.Parse(this); _structureLoaded = true; }
            return _structure;
        }
    }

    public static PdfDocument Open(string path, string password = "", System.Security.Cryptography.X509Certificates.X509Certificate2? certificate = null) => new PdfDocument(PdfFile.Open(path, password, certificate)).Init();
    public static PdfDocument Load(byte[] data, string password = "", System.Security.Cryptography.X509Certificates.X509Certificate2? certificate = null) => new PdfDocument(PdfFile.Load(data, password, certificate)).Init();

    PdfDocument Init()
    {
        var root = File.Resolve(File.Catalog.Get("Pages")) as PdfDictionary ?? throw new PdfParseException("missing page tree");
        Walk(root, [], new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance), 0);
        return this;
    }

    static readonly string[] Inheritable = ["Resources", "MediaBox", "CropBox", "Rotate"];
    const int MaxTreeDepth = 256;

    void Walk(PdfDictionary node, Dictionary<string, PdfObject> inh, HashSet<PdfDictionary> seen, int depth)
    {
        if (depth > MaxTreeDepth || !seen.Add(node)) return;
        var next = new Dictionary<string, PdfObject>(inh);
        foreach (var k in Inheritable) if (node.Get(k) is { } v) next[k] = v;
        var type = File.Resolve(node.Get("Type")).AsName();
        if (File.Resolve(node.Get("Kids")) is PdfArray kids && type != "Page")
        {
            foreach (var k in kids.Items)
                if (File.Resolve(k) is PdfDictionary kd) Walk(kd, next, seen, depth + 1);
        }
        else if (type != "Pages") Pages.Add(new PdfPage(this, node, inh));
    }

    public string? Info(string key) => File.Resolve(File.Trailer.Get("Info")) is PdfDictionary d && File.Resolve(d.Get(key)) is PdfString s ? s.Text : null;
}
