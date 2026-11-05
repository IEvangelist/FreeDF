using System.Text;
using System.Xml;
using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>
/// PDF/UA-2 (ISO 14289-2) structure rules that depend on PDF 2.0 structure namespaces (ISO 32000-2 14.7.4, 14.8.6).
/// Rule choices were reconciled against veraPDF 1.30.3 over the veraPDF-corpus PDF_UA-2 files; they are not a certification.
/// </summary>
internal static class PdfUA2Checks
{
    public const string Pdf17Namespace = "http://iso.org/pdf/ssn";
    public const string Pdf2Namespace = "http://iso.org/pdf2/ssn";
    public const string MathMlNamespace = "http://www.w3.org/1998/Math/MathML";
    const string Clause = "ISO 14289-2 8.2";
    const int MaxRoleDepth = 32;

    public static void Run(ValidationContext c, StructureTree tree)
    {
        c.Guard("PDF/UA-2 version", () => Version(c));
        c.Guard("PDF/UA-2 root", () => Root(c, tree));
        c.Guard("PDF/UA-2 roles", () => Roles(c, tree));
        c.Guard("PDF/UA-2 containment", () => PdfUA2NestingChecks.Run(c, tree));
        c.Guard("PDF/UA-2 element rules", () => Elements(c, tree));
        c.Guard("PDF/UA-2 link targets", () => LinkTargets(c, tree));
        c.Guard("PDF/UA-2 destinations", () => Destinations(c));
        c.Guard("PDF/UA-2 file specifications", () => Files(c));
        c.Guard("PDF/UA-2 rich field values", () => RichFieldValues(c));
    }

    static void RichFieldValues(ValidationContext c)
    {
        var fields = c.Doc.GetFormFieldDetails();
        bool incomplete = false;
        if (fields.Truncated) Incomplete();
        foreach (var field in fields.Fields)
        {
            if (field.FieldType?.Value != "Tx") continue;
            bool complete = true;
            void Unresolved() { complete = false; Incomplete(); }
            var rich = ResolveBounded(c.F, field.Dictionary.Get("RV"), Unresolved);
            if (!complete || rich is null or PdfNull) continue;
            if (rich is not (PdfString or PdfStream)) { Incomplete(); continue; }
            var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            PdfDictionary? dictionary = field.Dictionary;
            PdfObject? value = null;
            while (dictionary is not null)
            {
                if (seen.Count > FormFieldDetailFeatures.MaxDepth || !seen.Add(dictionary)) { Unresolved(); break; }
                value = ResolveBounded(c.F, dictionary.Get("V"), Unresolved);
                if (!complete || value is not (null or PdfNull)) break;
                var parent = ResolveBounded(c.F, dictionary.Get("Parent"), Unresolved);
                if (parent is not (null or PdfNull or PdfDictionary)) { Unresolved(); break; }
                dictionary = parent as PdfDictionary;
            }
            if (!complete) continue;
            if (value is null or PdfNull)
                c.Add("PDFUA2-FORM-RV-V", ValidationSeverity.Error,
                    "a text field with /RV must also provide a plain /V value",
                    "ISO 14289-2 8.10.3.3");
            else if (value is not (PdfString or PdfStream)) Incomplete();
            else
            {
                try
                {
                    var bytes = RichFieldBytes(c, value);
                    if (bytes.Length > XfaReader.MaxBytes) { Unresolved(); continue; }
                    var richText = RichFieldText(c, rich, Unresolved);
                    if (complete && richText != RichFieldPlainText(bytes))
                        c.Add("PDFUA2-FORM-RV-V", ValidationSeverity.Error,
                            "a text field's /RV XML text content differs from its plain /V value",
                            "ISO 14289-2 8.10.3.3");
                }
                catch (Exception e) when (e is IOException or InvalidDataException or PdfParseException or XmlException or NotSupportedException or DecoderFallbackException)
                {
                    Unresolved();
                }
            }
        }

        static string RichFieldPlainText(byte[] bytes)
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return new UnicodeEncoding(true, false, true).GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return new UnicodeEncoding(false, false, true).GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3);
            const string accents = "\u02D8\u02C7\u02C6\u02D9\u02DD\u02DB\u02DA\u02DC";
            const string symbols = "\u2022\u2020\u2021\u2026\u2014\u2013\u0192\u2044\u2039\u203A\u2212\u2030\u201E\u201C\u201D\u2018\u2019\u201A\u2122\uFB01\uFB02\u0141\u0152\u0160\u0178\u017D\u0131\u0142\u0153\u0161\u017E";
            var result = new char[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
            {
                byte value = bytes[i];
                if (value is < 0x18 and not (9 or 10 or 13) or 0x7F or 0x9F or 0xAD)
                    throw new InvalidDataException("undefined PDFDocEncoding byte in rich-field plain value");
                result[i] = value switch
                {
                    >= 0x18 and <= 0x1F => accents[value - 0x18],
                    >= 0x80 and <= 0x9E => symbols[value - 0x80],
                    0xA0 => '\u20AC',
                    _ => (char)value,
                };
            }
            return new string(result);
        }

        void Incomplete()
        {
            if (incomplete) return;
            incomplete = true;
            c.Add("PDFUA2-FORM-VALUE-INCOMPLETE", ValidationSeverity.Error,
                "rich-field value inspection is truncated, cyclic, malformed or bounded",
                "ISO 14289-2 8.10.3.3");
        }
    }

    static byte[] RichFieldBytes(ValidationContext c, PdfObject value)
    {
        if (value is PdfString text) return text.Bytes;
        var stream = (PdfStream)value;
        PdfObject? Resolve(PdfObject? raw) => ResolveBounded(c.F, raw,
            () => throw new InvalidDataException("rich-field stream filter resolution did not complete"));
        var filter = Resolve(stream.Dict.Get("Filter") ?? stream.Dict.Get("F"));
        var parameters = Resolve(stream.Dict.Get("DecodeParms") ?? stream.Dict.Get("DP"));
        if (filter is not (null or PdfNull or PdfName or PdfArray)
            || filter is PdfArray array && (array.Count > XfaReader.MaxElements
                || array.Items.Any(item => Resolve(item) is not PdfName))
            || parameters is not (null or PdfNull or PdfDictionary or PdfArray))
            throw new InvalidDataException("malformed rich-field stream filters");
        var filters = PdfFilters.GetFilters(stream.Dict, Resolve);
        if (filter is PdfArray && parameters is PdfDictionary
            || parameters is PdfArray parms && (parms.Count != filters.Count
                || parms.Items.Any(item => Resolve(item) is not (null or PdfNull or PdfDictionary))))
            throw new InvalidDataException("malformed rich-field stream filter parameters");
        var (bytes, imageFilter, _) = PdfFilters.Decode(stream.RawData, filters, Resolve, strictContentFilters: true);
        if (imageFilter is not null) throw new NotSupportedException("image filter on rich-field text stream");
        return bytes;
    }

    static string? RichFieldText(ValidationContext c, PdfObject rich, Action incomplete)
    {
        var bytes = RichFieldBytes(c, rich);
        if (bytes.Length > XfaReader.MaxBytes) { incomplete(); return null; }
        using var reader = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = XfaReader.MaxBytes,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        });
        var result = new StringBuilder();
        bool body = false;
        int elements = 0;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (++elements > XfaReader.MaxElements || reader.Depth >= XfaReader.MaxDepth)
                { incomplete(); return null; }
                if (!body)
                {
                    if (reader.LocalName != "body" || reader.NamespaceURI != "http://www.w3.org/1999/xhtml")
                    { incomplete(); return null; }
                    body = true;
                }
            }
            else if (body && reader.Depth > 0 && reader.NodeType is (XmlNodeType.Text or XmlNodeType.CDATA
                or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace))
                result.Append(reader.Value);
        }
        if (!body) { incomplete(); return null; }
        return result.ToString();
    }

    internal static bool LanguagePrivateUse(ValidationContext c, string language, int? page = null)
    {
        if (c.Part == 2 && language.EnumerateRunes().Any(r =>
            r.Value is >= 0xE000 and <= 0xF8FF or >= 0xF0000 and <= 0xFFFFD or >= 0x100000 and <= 0x10FFFD))
        {
            c.Add("PDFUA2-TEXT-STRING-PUA", ValidationSeverity.Error,
                "language text string contains a Unicode private-use codepoint", "ISO 14289-2 8.6", page);
            return true;
        }
        return false;
    }

    /// <summary>8.14.1-1 (Desc on EmbeddedFiles name-tree specifications) and 8.9.2.4.10-1 (AFRelationship on file-attachment targets).</summary>
    static void Files(ValidationContext c)
    {
        var f = c.F;
        FileDescriptions(c);
        bool rel = false, incomplete = false;
        foreach (var o in c.AllObjects())
        {
            if (rel || o is not PdfDictionary d || f.Resolve(d.Get("Subtype")).AsName() != "FileAttachment") continue;
            if (ResolveBounded(f, d.Get("FS"), Incomplete) is not PdfDictionary fs) continue;
            bool complete = true;
            var relationship = ResolveBounded(f, fs.Get("AFRelationship"), () => { complete = false; Incomplete(); });
            if (complete && relationship is null or PdfNull)
            { rel = true; c.Add("PDFUA2-FILEATTACH-AFRELATIONSHIP", ValidationSeverity.Error, "a file-attachment annotation's file specification has no /AFRelationship (veraPDF PDFUA-2 8.9.2.4.10-1)", "ISO 14289-2 8.9.2.4.10"); }
        }

        void Incomplete()
        {
            if (incomplete) return;
            incomplete = true;
            c.Add("PDFUA2-FILEATTACH-INCOMPLETE", ValidationSeverity.Error,
                "file-attachment relationship inspection encountered a cyclic or bounded reference",
                "ISO 14289-2 8.9.2.4.10");
        }
    }

    static void FileDescriptions(ValidationContext c)
    {
        var f = c.F;
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        int work = 0;
        bool incomplete = false, missing = false;
        PdfObject? Resolve(PdfObject? value) => ResolveBounded(f, value, Incomplete);
        var names = Resolve(f.Catalog.Get("Names"));
        if (names is null or PdfNull) return;
        if (names is not PdfDictionary dictionary) Incomplete();
        else if (Resolve(dictionary.Get("EmbeddedFiles")) is { } tree && tree is not PdfNull) Visit(tree, 0);

        void Incomplete()
        {
            if (incomplete) return;
            incomplete = true;
            c.Add("PDFUA2-FILES-INCOMPLETE", ValidationSeverity.Error,
                "EmbeddedFiles name-tree inspection is cyclic, malformed or bounded; file descriptions could not be fully checked",
                "ISO 14289-2 8.14.1");
        }

        void Visit(PdfObject raw, int depth)
        {
            if (++work > PdfTreeDiagnostics.MaxWork || depth > PdfTreeDiagnostics.MaxDepth) { Incomplete(); return; }
            if (Resolve(raw) is not PdfDictionary node || !seen.Add(node)) { Incomplete(); return; }
            var names = Resolve(node.Get("Names"));
            if (names is PdfArray pairs)
            {
                if (pairs.Count % 2 != 0) Incomplete();
                for (var i = 0; i + 1 < pairs.Count; i += 2)
                {
                    if (++work > PdfTreeDiagnostics.MaxWork) { Incomplete(); return; }
                    if (Resolve(pairs[i]) is not PdfString || Resolve(pairs[i + 1]) is not PdfDictionary spec)
                    { Incomplete(); continue; }
                    var description = Resolve(spec.Get("Desc"));
                    if (!missing && description is not PdfString)
                    {
                        missing = true;
                        c.Add("PDFUA2-FILESPEC-DESC", ValidationSeverity.Error,
                            "a file specification in the EmbeddedFiles name tree has no /Desc (veraPDF PDFUA-2 8.14.1-1)",
                            "ISO 14289-2 8.14.1");
                    }
                }
            }
            else if (names is not (null or PdfNull)) Incomplete();
            var kids = Resolve(node.Get("Kids"));
            if (kids is PdfArray children)
                foreach (var child in children.Items)
                {
                    Visit(child, depth + 1);
                    if (work > PdfTreeDiagnostics.MaxWork) return;
                }
            else if (kids is not (null or PdfNull)) Incomplete();
        }
    }

    static void Version(ValidationContext c)
    {
        var f = c.F;
        var catalogVersion = f.Resolve(f.Catalog.Get("Version")).AsName();
        if (f.Version != "2.0" && catalogVersion != "2.0")
            c.Add("PDFUA2-VERSION", ValidationSeverity.Error, $"file version is not PDF 2.0 (header '{f.Version}', catalog /Version '{catalogVersion}')", "ISO 14289-2 6.1");
    }

    static void Root(ValidationContext c, StructureTree tree)
    {
        if (tree.Children.Count != 1 || tree.Children[0].StandardType != "Document")
            c.Add("PDFUA2-DOCUMENT-ROOT", ValidationSeverity.Error, "structure tree root must contain a single Document structure element as its only child", "ISO 14289-2 8.2.5.2");
        else if (tree.Children[0].Namespace != Pdf2Namespace)
            c.Add("PDFUA2-DOCUMENT-NS", ValidationSeverity.Error, "the Document structure element must be in the PDF 2.0 namespace (http://iso.org/pdf2/ssn)", "ISO 14289-2 8.2.5.2");
    }

    /// <summary>Where a (type, namespace) pair ends after role mapping: a PDF 1.7 or PDF 2.0 standard type, a circular chain, or nowhere.</summary>
    enum RoleOutcome { Standard, Circular, Unmapped }

    static RoleOutcome Resolve(PdfFile f, StructureTree tree, string type, PdfDictionary? nsDict, string? ns, out string? standardType)
    {
        standardType = null;
        var seen = new HashSet<(string, string?)>();
        for (var depth = 0; depth < MaxRoleDepth; depth++)
        {
            if (((ns is null || ns == Pdf17Namespace) && StructureTree.IsPdf17StandardType(type) && !tree.RoleMap.ContainsKey(type))
                || ns == Pdf2Namespace && type != "Note" && StructureTree.IsStandardType(type))
            {
                standardType = type;
                return RoleOutcome.Standard;
            }
            if (ns == MathMlNamespace) return RoleOutcome.Standard;
            if (!seen.Add((type, ns))) return RoleOutcome.Circular;
            if (nsDict is not null && ns is not null && ns != Pdf17Namespace && ns != Pdf2Namespace)
            {
                if (f.Resolve(nsDict.Get("RoleMapNS")) is not PdfDictionary map || map.Get(type) is not { } entry) return RoleOutcome.Unmapped;
                var target = f.Resolve(entry);
                if (target is PdfName n) { type = n.Value; continue; }
                if (target is PdfArray { Count: >= 1 } a && f.Resolve(a[0]).AsName() is { } tn)
                {
                    type = tn;
                    nsDict = a.Count > 1 ? f.Resolve(a[1]) as PdfDictionary : null;
                    ns = nsDict is not null && f.Resolve(nsDict.Get("NS")) is PdfString s ? s.Text : null;
                    continue;
                }
                return RoleOutcome.Unmapped;
            }
            if (tree.RoleMap.TryGetValue(type, out var next)) { type = next; ns = null; nsDict = null; continue; }
            return RoleOutcome.Unmapped;
        }
        return RoleOutcome.Circular;
    }

    internal static string? StandardType(ValidationContext c, StructureTree tree, StructElement element)
        => StandardType(c, tree, element, out _);

    internal static string? StandardType(ValidationContext c, StructureTree tree, StructElement element, out bool roleResolved)
    {
        roleResolved = Resolve(c.F, tree, element.Type, element.NamespaceDict, element.Namespace, out var type) == RoleOutcome.Standard;
        return type;
    }

    static void Roles(ValidationContext c, StructureTree tree)
    {
        var f = c.F;
        var reported = new HashSet<(string, string?)>();
        var namespaces = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        foreach (var e in tree.Elements())
        {
            if (e.Namespace is { } uri && e.NamespaceDict is { } nd) namespaces.TryAdd(uri, nd);
            if (!reported.Add((e.Type, e.Namespace))) continue;
            switch (Resolve(f, tree, e.Type, e.NamespaceDict, e.Namespace, out _))
            {
                case RoleOutcome.Circular:
                    c.Add("PDFUA2-ROLEMAP-CIRCULAR", ValidationSeverity.Error, $"structure type /{e.Type} has a circular role mapping", "ISO 14289-2 8.2.4", e.PageIndex);
                    break;
                case RoleOutcome.Unmapped:
                    c.Add("PDFUA2-ROLEMAP-MISSING", ValidationSeverity.Error, $"structure type /{e.Type} does not belong to or map to the PDF 1.7 or PDF 2.0 namespace", "ISO 14289-2 8.2.4", e.PageIndex);
                    break;
            }
        }
        foreach (var (uri, nd) in namespaces)
        {
            if (uri is Pdf17Namespace or Pdf2Namespace || f.Resolve(nd.Get("RoleMapNS")) is not PdfDictionary map) continue;
            foreach (var (name, raw) in map.Entries.Take(10_000))
            {
                var target = f.Resolve(raw);
                var sameNs = target is PdfName
                    || target is PdfArray { Count: >= 2 } a && f.Resolve(a[1]) is PdfDictionary td && f.Resolve(td.Get("NS")) is PdfString ts && ts.Text == uri;
                if (sameNs)
                    c.Add("PDFUA2-ROLEMAP-SAME-NS", ValidationSeverity.Error, $"/{name} is role mapped to another type in its own namespace '{uri}'", "ISO 14289-2 8.2.4");
            }
        }
    }

    internal static void WidgetAdditionalActions(ValidationContext c, StructureTree tree, PdfDictionary widget, StructElement parent, int page)
    {
        var f = c.F;
        if (f.Resolve(widget.Get("AA")) is not PdfDictionary
            || f.Resolve(widget.Get("Contents")) is PdfString
            || !(f.Resolve(widget.Get("FT")) is PdfName || f.Resolve(widget.Get("T")) is PdfString
                || f.Resolve(widget.Get("Parent")) is PdfDictionary)) return;
        if (InsideArtifact(c, tree, parent)) return;
        c.Add("PDFUA2-WIDGET-AA-CONTENTS", ValidationSeverity.Error,
            "a tagged field widget with additional actions requires its own /Contents description",
            "ISO 14289-2 8.10.2.3", page);
    }

    internal static bool InsideArtifact(ValidationContext c, StructureTree tree, StructElement parent)
    {
        for (var element = parent; element is not null; element = element.Parent)
            if (StandardType(c, tree, element) == "Artifact") return true;
        return false;
    }

    static void Elements(ValidationContext c, StructureTree tree)
    {
        var f = c.F;
        foreach (var e in tree.Elements())
        {
            var standardType = StandardType(c, tree, e);
            if (e.Type == "Math" && e.Parent?.StandardType != "Formula")
                c.Add("PDFUA2-MATH-PARENT", ValidationSeverity.Error, "a Math element may only occur as a child of Formula", "ISO 14289-2 8.2.5.29", e.PageIndex);
            if (standardType == "H")
                c.Add("PDFUA2-HEADING-H", ValidationSeverity.Error, "the H structure type must not be used; use numbered headings", "ISO 14289-2 8.2.5.12", e.PageIndex);
            if (standardType == "Note")
                c.Add("PDFUA2-NOTE", ValidationSeverity.Error, "the Note structure type is only permitted when role mapped to a PDF 2.0 namespace element", "ISO 14289-2 8.2.5.14", e.PageIndex);
            if (standardType == "Form"
                && e.Children.OfType<StructObjectRef>().Count(o => o.Object is PdfDictionary d && f.Resolve(d.Get("Subtype")).AsName() == "Widget") > 1)
                c.Add("PDFUA2-FORM-WIDGETS", ValidationSeverity.Error, "a Form structure element must contain at most one widget annotation", "ISO 14289-2 8.10.1", e.PageIndex);
            switch (standardType)
            {
                case "L":
                    if (e.ChildElements.Any(li => StandardType(c, tree, li) == "LI"
                        && li.ChildElements.Any(l => StandardType(c, tree, l) == "Lbl"))
                        && (e.Attribute(f, "ListNumbering").AsName() is null or "None"))
                        c.Add("PDFUA2-LIST-NUMBERING", ValidationSeverity.Error, "L contains Lbl elements but has no ListNumbering attribute other than None", "ISO 14289-2 8.2.5.25", e.PageIndex);
                    break;
                case "LI":
                    if (e.MarkedContent.Any())
                        c.Add("PDFUA2-LI-CONTENT", ValidationSeverity.Error, "content directly inside LI must be enclosed in Lbl or LBody", "ISO 14289-2 8.2.5.25", e.PageIndex);
                    break;
                case "FENote":
                    FenoteType(c, tree, e);
                    break;
            }
            var kids = e.ChildElements.ToList();
            for (var i = 1; i < kids.Count - 1; i++)
                if (StandardType(c, tree, kids[i]) == "Caption")
                { c.Add("PDFUA2-CAPTION-POSITION", ValidationSeverity.Error, $"Caption must be the first or last child of /{e.Type}", "ISO 14289-2 8.2.5.27", e.PageIndex); break; }
        }
    }

    static void FenoteType(ValidationContext c, StructureTree tree, StructElement element)
    {
        bool incomplete = false;
        int work = 0;
        PdfObject? Resolve(PdfObject? value) => ResolveBounded(c.F, value, Incomplete);
        void Incomplete()
        {
            if (incomplete) return;
            incomplete = true;
            c.Add("PDFUA2-FENOTE-INCOMPLETE", ValidationSeverity.Error,
                "FENote attribute inspection is malformed or exceeds its reference/work bound",
                "ISO 14289-2 8.2.5.14", element.PageIndex);
        }

        PdfObject? Find(PdfDictionary dictionary)
        {
            var owner = Resolve(dictionary.Get("O"));
            if (incomplete || owner.AsName() != "FENote") return null;
            return Resolve(dictionary.Get("NoteType"));
        }

        PdfObject? FindAttributes(PdfObject? raw)
        {
            var attributes = Resolve(raw);
            if (incomplete) return null;
            if (attributes is PdfDictionary dictionary) return Find(dictionary);
            if (attributes is PdfArray array)
            {
                foreach (var item in array.Items)
                {
                    if (++work > PdfTreeDiagnostics.MaxWork) { Incomplete(); return null; }
                    var value = Resolve(item);
                    if (incomplete) return null;
                    if (value is PdfDictionary attribute)
                    {
                        var found = Find(attribute);
                        if (incomplete || found is not (null or PdfNull)) return found;
                    }
                    else if (value is not (null or PdfNull or PdfInteger)) Incomplete();
                }
            }
            else if (attributes is not (null or PdfNull)) Incomplete();
            return null;
        }

        var noteType = FindAttributes(element.Dict.Get("A"));
        if (!incomplete && noteType is (null or PdfNull))
        {
            var classes = Resolve(element.Dict.Get("C"));
            if (!incomplete && classes is not (null or PdfNull))
            {
                var rawMap = Resolve(tree.Root.Get("ClassMap"));
                if (!incomplete && rawMap is PdfDictionary classMap)
                {
                    PdfObject? FromClass(PdfObject? raw)
                    {
                        if (++work > PdfTreeDiagnostics.MaxWork) { Incomplete(); return null; }
                        var value = Resolve(raw);
                        if (value is PdfName name) return FindAttributes(classMap.Get(name.Value));
                        if (value is not (null or PdfNull or PdfInteger)) Incomplete();
                        return null;
                    }
                    if (classes is PdfName) noteType = FromClass(classes);
                    else if (classes is PdfArray array)
                        foreach (var item in array.Items)
                        {
                            noteType = FromClass(item);
                            if (incomplete || noteType is not (null or PdfNull)) break;
                        }
                    else Incomplete();
                }
                else if (rawMap is not (null or PdfNull)) Incomplete();
            }
        }
        if (!incomplete && noteType is PdfName name && name.Value is not ("Footnote" or "Endnote" or "None"))
            c.Add("PDFUA2-FENOTE-TYPE", ValidationSeverity.Error,
                $"FENote NoteType name /{name.Value} is not Footnote, Endnote or None",
                "ISO 14289-2 8.2.5.14", element.PageIndex);
    }

    static void LinkTargets(ValidationContext c, StructureTree tree)
    {
        var f = c.F;
        foreach (var element in tree.Elements())
        {
            if (StandardType(c, tree, element) is not ("Link" or "Reference")) continue;
            PdfDictionary? firstTarget = null;
            foreach (var reference in element.Children.OfType<StructObjectRef>())
            {
                if (reference.Object is not PdfDictionary annotation || f.Resolve(annotation.Get("Subtype")).AsName() != "Link") continue;
                PdfObject? destination;
                if (annotation.Get("A") is { } rawAction)
                {
                    if (ResolveBounded(f, rawAction, Incomplete) is not PdfDictionary action || f.Resolve(action.Get("S")).AsName() != "GoTo") continue;
                    destination = action.Get("SD") ?? action.Get("D");
                }
                else destination = annotation.Get("Dest");
                var target = DestinationTarget(c, destination, Incomplete);
                if (target is null || tree.ElementFor(target) is null) continue;
                firstTarget ??= target;
                if (!ReferenceEquals(firstTarget, target))
                {
                    c.Add("PDFUA2-LINK-TARGETS", ValidationSeverity.Error, $"a /{element.Type} structure element contains Link annotations targeting different structure elements", "ISO 14289-2 8.2.5.20", element.PageIndex);
                    break;
                }
            }
        }

        void Incomplete() => c.Add("PDFUA2-LINK-TARGET-UNRESOLVED", ValidationSeverity.Error,
            "a reference, destination or name-tree traversal boundary prevented comparison of Link annotation targets", "ISO 14289-2 8.2.5.20");
    }

    internal static PdfObject? ResolveBounded(PdfFile file, PdfObject? value, Action incomplete)
    {
        var seen = new HashSet<int>();
        while (value is PdfRef reference)
        {
            if (seen.Count >= MaxRoleDepth || !seen.Add(reference.Number)) { incomplete(); return null; }
            value = file.GetObject(reference.Number);
        }
        return value;
    }

    static PdfDictionary? DestinationTarget(ValidationContext c, PdfObject? destination, Action incomplete)
    {
        var seen = new HashSet<PdfObject>(ReferenceEqualityComparer.Instance);
        for (var depth = 0; destination is not null && depth < MaxRoleDepth; depth++)
        {
            var value = ResolveBounded(c.F, destination, incomplete);
            if (value is null or PdfNull) return null;
            if (!seen.Add(value)) { incomplete(); return null; }
            switch (value)
            {
                case PdfName name:
                    destination = DocumentFeatures.LookupNamedDest(c.F, name.Value, incomplete);
                    break;
                case PdfString name:
                    destination = DocumentFeatures.LookupNamedDest(c.F, name.Text, incomplete);
                    break;
                case PdfDictionary dictionary:
                    if (c.Doc.Structure?.ElementFor(dictionary) is not null) return dictionary;
                    destination = dictionary.Get("SD") ?? dictionary.Get("D");
                    break;
                case PdfArray { Count: > 0 } array:
                    return ResolveBounded(c.F, array[0], incomplete) as PdfDictionary;
                default:
                    return null;
            }
        }
        if (destination is not null) incomplete();
        return null;
    }

    /// <summary>8.8: detect local page destinations, including named destinations and inert action triggers.</summary>
    static void Destinations(ValidationContext c)
    {
        var f = c.F;
        bool incomplete = false;
        PdfObject? Resolve(PdfObject? value) => ResolveBounded(f, value, Incomplete);
        bool PageTarget(PdfObject? destination) =>
            DestinationTarget(c, destination, Incomplete) is { } target && Resolve(target.Get("Type")).AsName() == "Page";

        void Incomplete()
        {
            if (incomplete) return;
            incomplete = true;
            c.Add("PDFUA2-DEST-INCOMPLETE", ValidationSeverity.Error,
                "local destination validation could not complete because an action or destination boundary was reached", "ISO 14289-2 8.8");
        }

        var actions = c.Doc.GetActionReport();
        if (actions.Truncated) Incomplete();
        foreach (var action in actions.Actions)
            if (action.ActionType == "GoTo" && PageTarget(action.Dictionary.Get("SD") ?? action.Dictionary.Get("D")))
                c.Add("PDFUA2-DEST-NOT-STRUCTURE", ValidationSeverity.Error,
                    "a GoTo action destination targets a page instead of a structure element (veraPDF PDFUA-2 8.8-2)", "ISO 14289-2 8.8", action.PageIndex);
        var openAction = Resolve(f.Catalog.Get("OpenAction"));
        if (openAction is not PdfDictionary && PageTarget(openAction))
            c.Add("PDFUA2-DEST-NOT-STRUCTURE", ValidationSeverity.Error,
                "catalog OpenAction targets a page instead of a structure element (veraPDF PDFUA-2 8.8-1)", "ISO 14289-2 8.8");

        for (var i = 0; i < c.Doc.Pages.Count; i++)
        {
            if (f.Resolve(c.Doc.Pages[i].Dict.Get("Annots")) is not PdfArray annots) continue;
            foreach (var a in annots.Items.Take(10_000))
                if (f.Resolve(a) is PdfDictionary ad && PageTarget(ad.Get("Dest")))
                {
                    c.Add("PDFUA2-DEST-NOT-STRUCTURE", ValidationSeverity.Error, "an annotation destination targets a page instead of a structure element (veraPDF PDFUA-2 8.8-1/2)", "ISO 14289-2 8.8", i);
                    return;
                }
        }
        if (f.Resolve(f.Catalog.Get("Outlines")) is not PdfDictionary root) return;
        var stack = new Stack<PdfDictionary>();
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        if (f.Resolve(root.Get("First")) is PdfDictionary first) stack.Push(first);
        while (stack.Count > 0 && seen.Count < 10_000)
        {
            var item = stack.Pop();
            if (!seen.Add(item)) continue;
            if (PageTarget(item.Get("Dest")))
            {
                c.Add("PDFUA2-DEST-NOT-STRUCTURE", ValidationSeverity.Error, "an outline destination targets a page instead of a structure element (veraPDF PDFUA-2 8.8-1/2)", "ISO 14289-2 8.8");
                return;
            }
            if (f.Resolve(item.Get("Next")) is PdfDictionary next) stack.Push(next);
            if (f.Resolve(item.Get("First")) is PdfDictionary child) stack.Push(child);
        }
    }
}
