using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>Placement of a visible signature on a page, in default user-space units (origin bottom-left).</summary>
public sealed record PdfSignatureRect(double X, double Y, double Width, double Height);

public sealed class PdfSignOptions
{
    /// <summary>Signing certificate; must have a private key (RSA or ECDSA).</summary>
    public required X509Certificate2 Certificate { get; init; }
    public string FieldName { get; init; } = "Signature1";
    public string? Reason { get; init; }
    public string? Location { get; init; }
    public string? ContactInfo { get; init; }
    public DateTimeOffset? SigningTime { get; init; }
    /// <summary>Digest algorithm for the CMS signature (SHA-256, SHA-384 or SHA-512).</summary>
    public HashAlgorithmName Hash { get; init; } = HashAlgorithmName.SHA256;
    /// <summary>Page that carries the widget (zero-based).</summary>
    public int PageIndex { get; init; }
    /// <summary>Rectangle of a visible appearance; null produces an invisible signature.</summary>
    public PdfSignatureRect? Rect { get; init; }
    /// <summary>Text lines of the visible appearance (default: signer, date, reason, location).</summary>
    public string? AppearanceText { get; init; }
    /// <summary>Bytes reserved for the DER-encoded CMS in /Contents; signing fails if the signature (with its certificate chain) is larger.</summary>
    public int ContentsSize { get; init; } = 16384;
    /// <summary>
    /// Makes this a certification signature (§12.8.2.2): adds a DocMDP signature reference dictionary with this /P value and registers the signature in the
    /// catalog /Perms /DocMDP. It must be the first signature in the document, and a document can have only one.
    /// </summary>
    public PdfDocMdpPermission? Certify { get; init; }
}

public sealed partial class PdfEditor
{
    /// <summary>When true, editing operations the document's permissions forbid (for the credentials used to open it) throw <see cref="PdfPermissionException"/>.</summary>
    public bool EnforcePermissions { get; set; }

    /// <summary>Permissions in force for this editor: everything for new documents and for the owner, otherwise the /P flags.</summary>
    public PdfPermissions Permissions => _src?.Decryptor is { IsOwner: false } d ? d.PermissionFlags : PdfPermissions.All;

    public bool IsAllowed(PdfPermissions p) => (Permissions & p) == p;

    void Require(PdfPermissions p)
    {
        if (EnforcePermissions && !IsAllowed(p)) throw new PdfPermissionException(p);
    }

    static PdfDictionary With(PdfDictionary d, string key, PdfObject value) => new(new Dictionary<string, PdfObject>(d.Entries) { [key] = value });

    /// <summary>True when <see cref="Sign"/> can append a signature update to the original bytes: an intact source (encrypted ones included) that has not requested a full rewrite.</summary>
    public bool CanSignIncrementally => !_requiresFullRewrite && _src is { Repaired: false };

    /// <summary>
    /// Signs the editor's current state (§12.8): appends an incremental update with a /Sig field whose /Contents holds a detached CMS signature
    /// (adbe.pkcs7.detached) over everything except that string. Intact sources, including encrypted ones (the update is encrypted with the file's key and
    /// the signature /Contents left clear), are signed incrementally; objects the editor leaves structurally unchanged are not rewritten, so earlier
    /// signatures and certifications stay valid when nothing else was edited. New, repaired or redacted documents are first saved in full (the result of
    /// an encrypted-source rewrite is unencrypted). Edits made through the editor after a certification are written as ordinary changes; use
    /// <see cref="PdfSignatures.Verify(byte[])"/> to see whether they violate DocMDP.
    /// </summary>
    public byte[] Sign(PdfSignOptions o)
    {
        Validate(o);
        if (!CanSignIncrementally) return Open(Save()).Sign(o);
        Check(o.PageIndex);
        var objs = BuildObjects(true, true, out var next);
        return SignCore(_src!, objs, next, _catalog, _info == 0 ? null : _info, _deleted, _pages[o.PageIndex], o, null);
    }

    /// <summary>
    /// Signs an existing, intact PDF without touching any other object: only the signature field, its page /Annots entry and the
    /// /AcroForm are updated, so earlier signatures stay valid for the content they covered. Encrypted files need their <paramref name="password"/>;
    /// the update is encrypted with the file's key.
    /// </summary>
    public static byte[] SignFile(byte[] pdf, PdfSignOptions o, string password = "")
    {
        Validate(o);
        return SignExisting(pdf, password, o.PageIndex, o, null);
    }

    /// <summary>Supplies an RFC 3161 TimeStampToken (DER) for the digest of the bytes a document timestamp covers.</summary>
    public delegate byte[] TimeStampProvider(HashAlgorithmName hash, byte[] digest);

    /// <summary>
    /// Appends a document timestamp (§12.8.5): an invisible signature field whose value is a /Type /DocTimeStamp dictionary (SubFilter ETSI.RFC3161) holding the
    /// token returned by <paramref name="provider"/> for the digest of the ByteRange bytes. Contacting a time-stamping authority is the caller's job; this
    /// library only embeds the token (the token is not checked when it is embedded; <see cref="VerifySignatures"/> checks the imprint and CMS signature).
    /// Existing signatures remain valid. Encrypted files need their <paramref name="password"/>.
    /// </summary>
    public static byte[] AddDocumentTimeStamp(byte[] pdf, TimeStampProvider provider, HashAlgorithmName? hash = null, string fieldName = "DocTimeStamp1", int contentsSize = 16384, string password = "")
    {
        if (contentsSize < 1024) throw new ArgumentOutOfRangeException(nameof(contentsSize), "ContentsSize too small");
        var h = hash ?? HashAlgorithmName.SHA256;
        if (h.Name is not ("SHA1" or "SHA256" or "SHA384" or "SHA512"))
            throw new ArgumentException("Unsupported document timestamp hash algorithm.", nameof(hash));
        return SignExisting(pdf, password, 0, null, new TimeStampPlan(fieldName, contentsSize, (bytes, range) =>
        {
            var content = new byte[range[1] + range[3]];
            Buffer.BlockCopy(bytes, (int)range[0], content, 0, (int)range[1]);
            Buffer.BlockCopy(bytes, (int)range[2], content, (int)range[1], (int)range[3]);
            var digest = h.Name switch { "SHA384" => SHA384.HashData(content), "SHA512" => SHA512.HashData(content), "SHA1" => SHA1.HashData(content), "SHA256" => SHA256.HashData(content), _ => throw new ArgumentException("Unsupported document timestamp hash algorithm.", nameof(hash)) };
            return provider(h, digest);
        }));
    }

    sealed record TimeStampPlan(string FieldName, int ContentsSize, Func<byte[], long[], byte[]> Token);

    static byte[] SignExisting(byte[] pdf, string password, int pageIndex, PdfSignOptions? o, TimeStampPlan? ts)
    {
        var f = PdfFile.Load(pdf, password);
        if (f.Repaired) throw new InvalidOperationException("Signing needs an intact file; this one required repair");
        var objs = new Dictionary<int, PdfObject>();
        foreach (var (n, e) in f.Xref)
            if (e.InUse && f.GetObject(n) is { } obj and not PdfNull) objs[n] = obj;
        var next = Math.Max(objs.Keys.Max(), (int)(f.Resolve(f.Trailer.Get("Size")).AsNumber() ?? 1) - 1) + 1;
        var leaves = new List<int>();
        var seen = new HashSet<int>();
        void Walk(PdfObject? node)
        {
            if (node is not PdfRef r || !seen.Add(r.Number) || f.Resolve(node) is not PdfDictionary d) return;
            if (f.Resolve(d.Get("Kids")) is PdfArray kids) foreach (var k in kids.Items) Walk(k); else leaves.Add(r.Number);
        }
        Walk(f.Catalog.Get("Pages"));
        if ((uint)pageIndex >= (uint)leaves.Count) throw new ArgumentOutOfRangeException(nameof(o), "PageIndex");
        var root = (f.Trailer.Get("Root") as PdfRef ?? throw new PdfParseException("no catalog reference")).Number;
        return SignCore(f, objs, next, root, (f.Trailer.Get("Info") as PdfRef)?.Number, new HashSet<int>(), leaves[pageIndex], o, ts);
    }

    static void Validate(PdfSignOptions o)
    {
        if (!o.Certificate.HasPrivateKey) throw new ArgumentException("The signing certificate needs a private key", nameof(o));
        if (o.ContentsSize < 1024) throw new ArgumentOutOfRangeException(nameof(o), "ContentsSize too small");
        if (o.Certify is { } c && !Enum.IsDefined(c)) throw new ArgumentOutOfRangeException(nameof(o), "Certify");
    }

    static byte[] SignCore(PdfFile f, Dictionary<int, PdfObject> objs, int next, int catalogNum, int? info, ISet<int> deleted, int pageNum, PdfSignOptions? o, TimeStampPlan? ts)
    {
        var time = o?.SigningTime ?? DateTimeOffset.UtcNow;
        var fieldName = o?.FieldName ?? ts!.FieldName;
        var contentsSize = o?.ContentsSize ?? ts!.ContentsSize;
        var catalog = (PdfDictionary)objs[catalogNum];
        if (o?.Certify is not null)
        {
            if (PdfSignatures.Verify(f).Count > 0) throw new InvalidOperationException("A certification signature must be the first signature in the document");
            var permsRaw = catalog.Get("Perms");
            var existing = (permsRaw is PdfRef pr ? objs.GetValueOrDefault(pr.Number) : permsRaw) as PdfDictionary;
            if (existing?.Get("DocMDP") is not null) throw new InvalidOperationException("The document already has a DocMDP certification signature");
        }
        var sigNum = next++; var widNum = next++;
        var widRef = new PdfRef(widNum, 0);

        var signer = o?.Certificate.GetNameInfo(X509NameType.SimpleName, false);
        var sig = new Dictionary<string, PdfObject>
        {
            ["Type"] = new PdfName(ts is null ? "Sig" : "DocTimeStamp"), ["Filter"] = new PdfName("Adobe.PPKLite"), ["SubFilter"] = new PdfName(ts is null ? "adbe.pkcs7.detached" : "ETSI.RFC3161"),
            ["ByteRange"] = new PdfArray([new PdfInteger(1_000_000_000), new PdfInteger(1_000_000_000), new PdfInteger(1_000_000_000), new PdfInteger(1_000_000_000)]),
            ["Contents"] = new PdfString(new byte[contentsSize], true),
        };
        if (o is not null)
        {
            sig["M"] = new PdfString(Encoding.ASCII.GetBytes(PdfDate(time)));
            if (!string.IsNullOrEmpty(signer)) sig["Name"] = new PdfString(EncodeText(signer));
            if (o.Reason is not null) sig["Reason"] = new PdfString(EncodeText(o.Reason));
            if (o.Location is not null) sig["Location"] = new PdfString(EncodeText(o.Location));
            if (o.ContactInfo is not null) sig["ContactInfo"] = new PdfString(EncodeText(o.ContactInfo));
            if (o.Certify is { } perm)
                sig["Reference"] = new PdfArray([new PdfDictionary(new Dictionary<string, PdfObject>
                {
                    ["Type"] = new PdfName("SigRef"), ["TransformMethod"] = new PdfName("DocMDP"),
                    ["TransformParams"] = new PdfDictionary(new Dictionary<string, PdfObject>
                    {
                        ["Type"] = new PdfName("TransformParams"), ["P"] = new PdfInteger((int)perm), ["V"] = new PdfName("1.2"),
                    }),
                })]);
        }
        objs[sigNum] = new PdfDictionary(sig);

        var r = o?.Rect;
        var widget = new Dictionary<string, PdfObject>
        {
            ["Type"] = new PdfName("Annot"), ["Subtype"] = new PdfName("Widget"), ["FT"] = new PdfName("Sig"),
            ["T"] = new PdfString(EncodeText(fieldName)), ["V"] = new PdfRef(sigNum, 0), ["F"] = new PdfInteger(132),
            ["P"] = new PdfRef(pageNum, 0),
            ["Rect"] = r is null ? new PdfArray([new PdfInteger(0), new PdfInteger(0), new PdfInteger(0), new PdfInteger(0)])
                : new PdfArray([new PdfReal(r.X), new PdfReal(r.Y), new PdfReal(r.X + r.Width), new PdfReal(r.Y + r.Height)]),
        };
        if (r is not null)
        {
            var apNum = next++;
            objs[apNum] = SignatureAppearance(o!, r, signer, time);
            widget["AP"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["N"] = new PdfRef(apNum, 0) });
        }
        objs[widNum] = new PdfDictionary(widget);

        // Register the widget on the page and the field in /AcroForm /Fields, preserving direct or indirect containers.
        PdfObject Append(PdfObject? arr, PdfObject item, out int indirect)
        {
            indirect = 0;
            if (arr is PdfRef ar && objs.TryGetValue(ar.Number, out var t) && t is PdfArray ta) { indirect = ar.Number; return new PdfArray([.. ta.Items, item]); }
            return arr is PdfArray a ? new PdfArray([.. a.Items, item]) : new PdfArray([item]);
        }
        var page = (PdfDictionary)objs[pageNum];
        var annots = Append(page.Get("Annots"), widRef, out var annotsNum);
        if (annotsNum != 0) objs[annotsNum] = annots; else objs[pageNum] = With(page, "Annots", annots);

        if (o?.Certify is not null)
        {
            var permsRaw = catalog.Get("Perms");
            var permsNum = permsRaw is PdfRef pr ? pr.Number : 0;
            var perms = (permsNum != 0 ? objs.GetValueOrDefault(permsNum) : permsRaw) as PdfDictionary ?? new PdfDictionary();
            perms = With(perms, "DocMDP", new PdfRef(sigNum, 0));
            if (permsNum != 0) objs[permsNum] = perms; else catalog = With(catalog, "Perms", perms);
        }
        var formRaw = catalog.Get("AcroForm");
        var formNum = formRaw is PdfRef fr ? fr.Number : 0;
        var form = (formNum != 0 ? objs.GetValueOrDefault(formNum) : formRaw) as PdfDictionary ?? new PdfDictionary();
        var fields = Append(form.Get("Fields"), widRef, out var fieldsNum);
        if (fieldsNum != 0) objs[fieldsNum] = fields; else form = With(form, "Fields", fields);
        form = With(form, "SigFlags", new PdfInteger(3));
        if (formNum != 0) objs[formNum] = form; else catalog = With(catalog, "AcroForm", form);
        objs[catalogNum] = catalog;

        var bytes = AppendUpdate(f, objs, next, catalogNum, info, deleted);
        return Finish(bytes, f.Data.Length, contentsSize, ts?.Token ?? ((b, range) => PdfSignatures.ComputeCms(b, range, o!.Certificate, time, o.Hash)));
    }

    static byte[] Finish(byte[] bytes, int origLen, int contentsSize, Func<byte[], long[], byte[]> makeContents)
    {
        var marker = Encoding.Latin1.GetBytes("/Contents <" + new string('0', 32));
        var p = bytes.AsSpan(origLen).IndexOf(marker);
        if (p < 0) throw new InvalidOperationException("signature placeholder not found");
        var lt = origLen + p + 10;
        var gt = lt + 1 + contentsSize * 2;
        var brKey = Encoding.Latin1.GetBytes("/ByteRange [");
        var bp = origLen + bytes.AsSpan(origLen).IndexOf(brKey) + brKey.Length;
        var be = Array.IndexOf(bytes, (byte)']', bp);
        var range = new long[] { 0, lt, gt + 1, bytes.Length - (gt + 1) };
        var text = string.Create(CultureInfo.InvariantCulture, $"{range[0]} {range[1]} {range[2]} {range[3]}");
        if (text.Length > be - bp) throw new InvalidOperationException("ByteRange placeholder too small");
        Encoding.ASCII.GetBytes(text.PadRight(be - bp)).CopyTo(bytes, bp);

        var cms = makeContents(bytes, range);
        if (cms.Length > contentsSize) throw new InvalidOperationException($"The signature ({cms.Length} bytes) does not fit in ContentsSize ({contentsSize}); increase ContentsSize");
        Encoding.ASCII.GetBytes(Convert.ToHexString(cms)).CopyTo(bytes, lt + 1);
        return bytes;
    }

    static string PdfDate(DateTimeOffset t) => t.UtcDateTime.ToString("'D:'yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "+00'00'";

    static PdfStream SignatureAppearance(PdfSignOptions o, PdfSignatureRect r, string? signer, DateTimeOffset time)
    {
        var lines = o.AppearanceText is { } t ? t.Split('\n').ToList()
            : new List<string> { "Digitally signed by " + (signer ?? "unknown"), "Date: " + time.UtcDateTime.ToString("yyyy.MM.dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) };
        if (o.AppearanceText is null)
        {
            if (!string.IsNullOrEmpty(o.Reason)) lines.Add("Reason: " + o.Reason);
            if (!string.IsNullOrEmpty(o.Location)) lines.Add("Location: " + o.Location);
        }
        static string Esc(string s) => new string(s.Where(c => c < 256).ToArray()).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        string N(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append($"0.95 0.97 1 rg 0 0 {N(r.Width)} {N(r.Height)} re f 0 0.2 0.6 RG 0.75 w 0.4 0.4 {N(r.Width - 0.8)} {N(r.Height - 0.8)} re S\n");
        sb.Append($"BT /F1 9 Tf 11 TL 0 g 4 {N(r.Height - 12)} Td\n");
        foreach (var l in lines) sb.Append('(').Append(Esc(l)).Append(") Tj T*\n");
        sb.Append("ET\n");
        var data = Encoding.Latin1.GetBytes(sb.ToString());
        var font = new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["Type"] = new PdfName("Font"), ["Subtype"] = new PdfName("Type1"), ["BaseFont"] = new PdfName("Helvetica"), ["Encoding"] = new PdfName("WinAnsiEncoding"),
        });
        return new PdfStream(new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["Type"] = new PdfName("XObject"), ["Subtype"] = new PdfName("Form"),
            ["BBox"] = new PdfArray([new PdfInteger(0), new PdfInteger(0), new PdfReal(r.Width), new PdfReal(r.Height)]),
            ["Resources"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["Font"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["F1"] = font }) }),
        }), data);
    }

    /// <summary>Checks every signature in a PDF: digest, CMS signature, signer, whether it covers the whole file, and whether it was modified afterwards.</summary>
    public static IReadOnlyList<PdfSignatureInfo> VerifySignatures(byte[] pdf, string password = "") => PdfSignatures.Verify(pdf, password);

    /// <summary>Reads the document security store (/DSS and its /VRI entries, §12.8.4.3) without validating or using its contents; null when absent.</summary>
    public static PdfDocumentSecurityStore? ReadDocumentSecurityStore(byte[] pdf) => PdfSignatures.ReadDss(pdf);
}
