namespace PortableDocument.Core;

/// <summary>Access permissions of a certification signature (ISO 32000-2 §12.8.2.2, DocMDP transform parameter /P, Table 257).</summary>
public enum PdfDocMdpPermission
{
    /// <summary>P = 1: no changes are permitted; any change invalidates the certification.</summary>
    NoChanges = 1,
    /// <summary>P = 2: form filling, instantiating page templates and signing are permitted.</summary>
    FormFillAndSign = 2,
    /// <summary>P = 3: as 2, plus creating, deleting and modifying annotations.</summary>
    FormFillSignAndAnnotate = 3,
}

/// <summary>Outcome of the DocMDP change check for a certification signature.</summary>
public enum PdfDocMdpStatus
{
    /// <summary>The signature is not an effective certification (no DocMDP transform, or not referenced from /Perms /DocMDP).</summary>
    NotCertification,
    /// <summary>The certified revision is the whole file.</summary>
    Unmodified,
    /// <summary>Every object change after the certified revision was classified as permitted by /P.</summary>
    ChangesPermitted,
    /// <summary>At least one later change is not permitted by /P or could not be classified as permitted (see the findings).</summary>
    Violated,
    /// <summary>The certified revision could not be re-read, so later changes were not compared.</summary>
    Undetermined,
}

/// <summary>Structural (value) equality of parsed PDF objects; indirect references compare by object and generation number.</summary>
public static class PdfObjectEquality
{
    public static bool DeepEquals(PdfObject? a, PdfObject? b)
    {
        if (ReferenceEquals(a, b)) return true;
        a = a is PdfNull ? null : a; b = b is PdfNull ? null : b;
        if (a is null || b is null) return a is null && b is null;
        switch (a)
        {
            case PdfBoolean x when b is PdfBoolean y: return x.Value == y.Value;
            case PdfInteger or PdfReal when b is PdfInteger or PdfReal: return a.AsNumber() == b.AsNumber();
            case PdfString x when b is PdfString y: return x.Bytes.AsSpan().SequenceEqual(y.Bytes);
            case PdfName x when b is PdfName y: return x.Value == y.Value;
            case PdfRef x when b is PdfRef y: return x.Number == y.Number && x.Generation == y.Generation;
            case PdfArray x when b is PdfArray y:
                if (x.Count != y.Count) return false;
                for (var i = 0; i < x.Count; i++) if (!DeepEquals(x[i], y[i])) return false;
                return true;
            case PdfStream x when b is PdfStream y: return DeepEquals(x.Dict, y.Dict) && x.RawData.AsSpan().SequenceEqual(y.RawData);
            case PdfDictionary x when b is PdfDictionary y:
                foreach (var k in x.Entries.Keys.Union(y.Entries.Keys)) if (!DeepEquals(x.Get(k), y.Get(k))) return false;
                return true;
            default: return false;
        }
    }
}

/// <summary>
/// Object-level comparison of a file against the revision a certification signature covers. It classifies every changed object as permitted or not
/// for the signature's /P value. This is a conservative structural check, not a full ISO 32000-2 conformance validator: changes it cannot positively
/// classify as permitted (page content, page tree, document information, XMP, field flags, shared resources...) are reported as violations, while
/// permitted changes are limited to form-field values and appearances, new signature fields and signatures, annotations (P = 3), and DSS content.
/// </summary>
static class PdfDocMdpChanges
{
    sealed class Ctx
    {
        public required PdfFile Old;
        public required PdfFile Cur;
        public required PdfDocMdpPermission P;
        public readonly List<string> Findings = [];
        public int RootNum, AcroFormNum, FieldsArrayNum;
        public readonly HashSet<int> Pages = [], AnnotsArrays = [], Fields = [], WidgetAnnots = [], OtherAnnots = [], WidgetAp = [], AnnotAp = [], Dss = [];
    }

    public static IReadOnlyList<string> Find(byte[] data, long signedEnd, PdfDocMdpPermission p, PdfFile cur, out bool undetermined)
    {
        undetermined = false;
        PdfFile old;
        try { old = PdfFile.Load(data[..(int)signedEnd]); }
        catch (Exception e) when (e is PdfParseException or ArgumentException or InvalidOperationException) { undetermined = true; return []; }
        if (old.Repaired || cur.Repaired) { undetermined = true; return []; }

        var c = new Ctx { Old = old, Cur = cur, P = p, RootNum = (old.Trailer.Get("Root") as PdfRef)?.Number ?? -1 };
        if ((cur.Trailer.Get("Root") as PdfRef)?.Number != c.RootNum) c.Findings.Add("The document catalog was replaced");
        if (!PdfObjectEquality.DeepEquals(old.Trailer.Get("Info"), cur.Trailer.Get("Info"))) c.Findings.Add("The document information dictionary reference changed");
        Index(c);

        foreach (var n in old.Xref.Keys.Union(cur.Xref.Keys).Order())
        {
            var oe = old.Xref.TryGetValue(n, out var a) && a.InUse ? a : (XrefEntry?)null;
            var ce = cur.Xref.TryGetValue(n, out var b) && b.InUse ? b : (XrefEntry?)null;
            if (oe is null && ce is null) continue;
            if (oe is not null && ce is not null && oe.Value == ce.Value) continue;
            var o = oe is null ? null : old.GetObject(n);
            var u = ce is null ? null : cur.GetObject(n);
            if (PdfObjectEquality.DeepEquals(o, u)) continue;
            if (o is null) continue; // new objects only matter through the permitted changes that reference them
            Classify(c, n, o, u);
        }
        return c.Findings;
    }

    static void Index(Ctx c)
    {
        var f = c.Old;
        var cat = f.Catalog;
        if (cat.Get("AcroForm") is PdfRef ar) c.AcroFormNum = ar.Number;
        var seen = new HashSet<int>();
        if (f.Resolve(cat.Get("AcroForm")) is PdfDictionary form)
        {
            if (form.Get("Fields") is PdfRef fr) c.FieldsArrayNum = fr.Number;
            void Walk(PdfObject? node, int depth)
            {
                if (node is not PdfRef r || depth > 64 || !seen.Add(r.Number) || f.Resolve(node) is not PdfDictionary d) return;
                c.Fields.Add(r.Number);
                if (f.Resolve(d.Get("Kids")) is PdfArray kids) foreach (var k in kids.Items) Walk(k, depth + 1);
            }
            if (f.Resolve(form.Get("Fields")) is PdfArray fields) foreach (var x in fields.Items) Walk(x, 0);
        }
        var visited = new HashSet<int>();
        void Pages(PdfObject? node, int depth)
        {
            if (node is not PdfRef r || depth > 64 || !visited.Add(r.Number) || f.Resolve(node) is not PdfDictionary d) return;
            if (f.Resolve(d.Get("Kids")) is PdfArray kids) { foreach (var k in kids.Items) Pages(k, depth + 1); return; }
            c.Pages.Add(r.Number);
            if (d.Get("Annots") is PdfRef ann) c.AnnotsArrays.Add(ann.Number);
            if (f.Resolve(d.Get("Annots")) is not PdfArray arr) return;
            foreach (var item in arr.Items)
                if (item is PdfRef ir && f.Resolve(item) is PdfDictionary ad)
                {
                    var widget = f.Resolve(ad.Get("Subtype")).AsName() == "Widget";
                    (widget ? c.WidgetAnnots : c.OtherAnnots).Add(ir.Number);
                    if (f.Resolve(ad.Get("AP")) is PdfDictionary ap) CollectAp(f, ap, widget ? c.WidgetAp : c.AnnotAp);
                }
        }
        Pages(cat.Get("Pages"), 0);
        // Fields that are not on a page tree annotation list still own appearances.
        foreach (var n in c.Fields)
            if (f.Resolve(new PdfRef(n, 0)) is PdfDictionary fd && f.Resolve(fd.Get("AP")) is PdfDictionary ap) CollectAp(f, ap, c.WidgetAp);
        if (cat.Get("DSS") is { } dss) Reach(f, dss, c.Dss, 0);
    }

    static void CollectAp(PdfFile f, PdfDictionary ap, HashSet<int> into)
    {
        foreach (var v in ap.Entries.Values)
        {
            if (v is PdfRef r) { into.Add(r.Number); if (f.Resolve(v) is PdfDictionary sub) CollectAp(f, sub, into); }
            else if (v is PdfDictionary d) CollectAp(f, d, into);
        }
    }

    static void Reach(PdfFile f, PdfObject? o, HashSet<int> into, int depth)
    {
        if (depth > 32 || into.Count > 20000) return;
        switch (o)
        {
            case PdfRef r when into.Add(r.Number): Reach(f, f.GetObject(r.Number), into, depth + 1); break;
            case PdfArray a: foreach (var i in a.Items) Reach(f, i, into, depth + 1); break;
            case PdfStream s: Reach(f, s.Dict, into, depth + 1); break;
            case PdfDictionary d: foreach (var v in d.Entries.Values) Reach(f, v, into, depth + 1); break;
        }
    }

    static void Classify(Ctx c, int n, PdfObject old, PdfObject? cur)
    {
        var p = c.P;
        if (cur is null)
        {
            if (!c.Dss.Contains(n) && !(p == PdfDocMdpPermission.FormFillSignAndAnnotate && c.OtherAnnots.Contains(n))) c.Findings.Add($"Object {n} was removed");
            return;
        }
        if (c.Dss.Contains(n)) return;
        if (old is PdfDictionary od && cur is PdfDictionary cd)
        {
            if (n == c.RootNum) { Catalog(c, od, cd); return; }
            if (n == c.AcroFormNum) { AcroForm(c, od, cd); return; }
            if (c.Pages.Contains(n)) { Page(c, n, od, cd); return; }
            if (c.Fields.Contains(n) || c.WidgetAnnots.Contains(n)) { Field(c, n, od, cd); return; }
            if (c.OtherAnnots.Contains(n)) { if (p != PdfDocMdpPermission.FormFillSignAndAnnotate) c.Findings.Add($"Annotation object {n} was modified"); return; }
        }
        else if (old is PdfArray oa && cur is PdfArray ca)
        {
            if (n == c.FieldsArrayNum) { FieldsArray(c, oa, ca); return; }
            if (c.AnnotsArrays.Contains(n)) { AnnotsArray(c, oa, ca); return; }
        }
        if (c.WidgetAp.Contains(n) && p != PdfDocMdpPermission.NoChanges) return;
        if (c.AnnotAp.Contains(n) && p == PdfDocMdpPermission.FormFillSignAndAnnotate) return;
        c.Findings.Add($"Object {n} was modified");
    }

    static IEnumerable<string> Differing(PdfDictionary a, PdfDictionary b)
        => a.Entries.Keys.Union(b.Entries.Keys).Where(k => !PdfObjectEquality.DeepEquals(a.Get(k), b.Get(k)));

    static void Catalog(Ctx c, PdfDictionary o, PdfDictionary u)
    {
        foreach (var k in Differing(o, u))
            switch (k)
            {
                case "DSS": break;
                case "AcroForm":
                    if ((o.Get(k) is null || o.Get(k) is PdfDictionary) && u.Get(k) is PdfDictionary ud) AcroForm(c, o.Get(k) as PdfDictionary ?? new PdfDictionary(), ud);
                    else c.Findings.Add("The /AcroForm entry of the catalog was replaced");
                    break;
                default: c.Findings.Add($"Catalog entry /{k} changed"); break;
            }
    }

    static void AcroForm(Ctx c, PdfDictionary o, PdfDictionary u)
    {
        foreach (var k in Differing(o, u))
            switch (k)
            {
                case "SigFlags": break;
                case "NeedAppearances" when c.P != PdfDocMdpPermission.NoChanges: break;
                case "Fields":
                    if ((o.Get(k) is null || o.Get(k) is PdfArray) && u.Get(k) is PdfArray ua) FieldsArray(c, o.Get(k) as PdfArray ?? new PdfArray([]), ua);
                    else c.Findings.Add("The /Fields entry of the AcroForm was replaced");
                    break;
                default: c.Findings.Add($"AcroForm entry /{k} changed"); break;
            }
    }

    static bool IsSigField(PdfFile f, PdfDictionary d)
    {
        for (var i = 0; i < 16 && d is not null; i++)
        {
            if (f.Resolve(d.Get("FT")).AsName() is { } ft) return ft == "Sig";
            d = f.Resolve(d.Get("Parent")) as PdfDictionary ?? null!;
        }
        return false;
    }

    static bool IsTimeStampField(PdfFile f, PdfDictionary d)
        => IsSigField(f, d) && f.Resolve(d.Get("V")) is PdfDictionary v && f.Resolve(v.Get("Type")).AsName() == "DocTimeStamp";

    static void FieldsArray(Ctx c, PdfArray o, PdfArray u)
    {
        if (!Prefix(o, u)) { c.Findings.Add("Existing entries of the AcroForm /Fields array were changed or removed"); return; }
        foreach (var item in u.Items.Skip(o.Count))
        {
            if (item is not PdfRef r || c.Cur.Resolve(item) is not PdfDictionary d) { c.Findings.Add("A malformed field was added to the AcroForm"); continue; }
            if (c.Old.Xref.TryGetValue(r.Number, out var e) && e.InUse) { c.Findings.Add($"Existing object {r.Number} was added to /Fields"); continue; }
            var ok = c.P == PdfDocMdpPermission.NoChanges ? IsTimeStampField(c.Cur, d) : IsSigField(c.Cur, d);
            if (!ok) c.Findings.Add($"A new form field (object {r.Number}) that is not a signature field was added");
        }
    }

    static void AnnotsArray(Ctx c, PdfArray o, PdfArray u)
    {
        if (c.P == PdfDocMdpPermission.FormFillSignAndAnnotate)
        {
            foreach (var item in o.Items)
                if (item is PdfRef r && c.WidgetAnnots.Contains(r.Number) && !u.Items.Any(x => x is PdfRef xr && xr.Number == r.Number))
                    c.Findings.Add($"Widget annotation {r.Number} was removed from a page");
            return;
        }
        if (!Prefix(o, u)) { c.Findings.Add("Existing page annotations were changed or removed"); return; }
        foreach (var item in u.Items.Skip(o.Count))
        {
            if (item is not PdfRef r || c.Cur.Resolve(item) is not PdfDictionary d) { c.Findings.Add("A malformed annotation was added to a page"); continue; }
            var widget = c.Cur.Resolve(d.Get("Subtype")).AsName() == "Widget";
            var ok = c.P == PdfDocMdpPermission.NoChanges ? widget && IsTimeStampField(c.Cur, d) : widget;
            if (!ok) c.Findings.Add($"A new annotation (object {r.Number}) was added to a page");
        }
    }

    static bool Prefix(PdfArray o, PdfArray u)
    {
        if (u.Count < o.Count) return false;
        for (var i = 0; i < o.Count; i++) if (!PdfObjectEquality.DeepEquals(o[i], u[i])) return false;
        return true;
    }

    static void Page(Ctx c, int n, PdfDictionary o, PdfDictionary u)
    {
        foreach (var k in Differing(o, u))
        {
            if (k != "Annots") { c.Findings.Add($"Page object {n} entry /{k} changed"); continue; }
            if (o.Get(k) is PdfArray oa && u.Get(k) is PdfArray ua) AnnotsArray(c, oa, ua);
            else if (o.Get(k) is null && u.Get(k) is PdfArray na) AnnotsArray(c, new PdfArray([]), na);
            else if (!(o.Get(k) is PdfRef && u.Get(k) is PdfRef)) c.Findings.Add($"Page object {n} /Annots was replaced");
            else c.Findings.Add($"Page object {n} /Annots now points to another array");
        }
    }

    static void Field(Ctx c, int n, PdfDictionary o, PdfDictionary u)
    {
        if (c.P == PdfDocMdpPermission.NoChanges) { c.Findings.Add($"Field or widget object {n} was modified"); return; }
        foreach (var k in Differing(o, u))
        {
            if (k is not ("V" or "AP" or "AS")) { c.Findings.Add($"Field or widget object {n} entry /{k} changed"); continue; }
            if (k == "V" && IsSigField(c.Old, o) && o.Get("V") is { } and not PdfNull) c.Findings.Add($"The signature in field object {n} was replaced");
        }
    }
}
