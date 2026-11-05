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
        c.Guard("PDF/UA-2 page structure parent keys", () => PageStructureParents(c));
        c.Guard("PDF/UA-2 annotation structure parent keys", () => AnnotationStructureParents(c));
        c.Guard("PDF/UA-2 appearance structure parent keys", () => AppearanceStructureParents(c));
        c.Guard("PDF/UA-2 XObject structure parent keys", () => XObjectStructureParents(c));
        c.Guard("PDF/UA-2 role map shape", () => RoleMapShape(c, tree));
        c.Guard("PDF/UA-2 roles", () => Roles(c, tree));
        c.Guard("PDF/UA-2 containment", () => PdfUA2NestingChecks.Run(c, tree));
        c.Guard("PDF/UA-2 namespace registration", () => NamespaceRegistration(c, tree));
        c.Guard("PDF/UA-2 element rules", () => Elements(c, tree));
        c.Guard("PDF/UA-2 link targets", () => LinkTargets(c, tree));
        c.Guard("PDF/UA-2 destinations", () => Destinations(c));
        c.Guard("PDF/UA-2 file specifications", () => Files(c));
        c.Guard("PDF/UA-2 text field values", () => TextFieldValues(c));
    }

    static void TextFieldValues(ValidationContext c)
    {
        var fields = c.Doc.GetFormFieldDetails();
        bool incomplete = false, limitIncomplete = false;
        if (fields.Truncated) { Incomplete(); LimitIncomplete(); }
        foreach (var field in fields.Fields)
        {
            if (field.FieldType?.Value != "Tx") continue;
            bool limitComplete = true;
            var limit = InheritedFieldValue(c, field.Dictionary, "MaxLen", () =>
            {
                limitComplete = false;
                LimitIncomplete();
            });
            if (limitComplete && limit is not (null or PdfNull or PdfInteger { Value: >= 0 }))
                c.Add("PDFUA2-FORM-MAXLEN-TYPE", ValidationSeverity.Error,
                    "a text field MaxLen must be a non-negative integer",
                    "ISO 32000-2 12.7.5.3 Table 232");
            bool complete = true;
            void Unresolved() { complete = false; Incomplete(); }
            var value = InheritedFieldValue(c, field.Dictionary, "V", Unresolved);
            if (!complete) continue;
            if (value is not (null or PdfNull or PdfString or PdfStream))
                c.Add("PDFUA2-FORM-TEXT-V-TYPE", ValidationSeverity.Error,
                    "a non-null text field V value must be a text string or stream",
                    "ISO 32000-2 12.7.5.3");
            var rich = ResolveBounded(c.F, field.Dictionary.Get("RV"), Unresolved);
            if (!complete || rich is null or PdfNull) continue;
            if (rich is not (PdfString or PdfStream)) { Incomplete(); continue; }
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

        void Incomplete()
        {
            if (incomplete) return;
            incomplete = true;
            c.Add("PDFUA2-FORM-VALUE-INCOMPLETE", ValidationSeverity.Error,
                "text-field value inspection is truncated, cyclic, malformed or bounded",
                "ISO 14289-2 8.10.3.3");
        }

        void LimitIncomplete()
        {
            if (limitIncomplete) return;
            limitIncomplete = true;
            c.Add("PDFUA2-FORM-MAXLEN-INCOMPLETE", ValidationSeverity.Error,
                "text-field MaxLen inspection is truncated, cyclic, unsupported or bounded",
                "ISO 32000-2 12.7.5.3 Table 232");
        }
    }

    static PdfObject? InheritedFieldValue(ValidationContext c, PdfDictionary dictionary, string key, Action incompleteInspection)
    {
        bool complete = true;
        void Incomplete() { complete = false; incompleteInspection(); }
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        PdfDictionary? current = dictionary;
        while (current is not null)
        {
            if (seen.Count > FormFieldDetailFeatures.MaxDepth || !seen.Add(current)) { Incomplete(); return null; }
            var value = ResolveBounded(c.F, current.Get(key), Incomplete);
            if (!complete || value is not (null or PdfNull)) return value;
            var parent = ResolveBounded(c.F, current.Get("Parent"), Incomplete);
            if (!complete) return null;
            if (parent is not (null or PdfNull or PdfDictionary)) { Incomplete(); return null; }
            current = parent as PdfDictionary;
        }
        return null;
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
                throw new InvalidDataException("undefined PDFDocEncoding byte in rich-field text value");
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
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = XfaReader.MaxBytes,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            CloseInput = true,
        };
        using var reader = rich is PdfString
            ? XmlReader.Create(new StringReader(RichFieldPlainText(bytes)), settings)
            : XmlReader.Create(new MemoryStream(bytes), settings);
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
        => Resolve(f, tree, type, nsDict, ns, out standardType, out _);

    static RoleOutcome Resolve(PdfFile f, StructureTree tree, string type, PdfDictionary? nsDict, string? ns,
        out string? standardType, out string? resolvedNamespace)
    {
        standardType = null;
        resolvedNamespace = null;
        var seen = new HashSet<(string, string?)>();
        for (var depth = 0; depth < MaxRoleDepth; depth++)
        {
            if (((ns is null || ns == Pdf17Namespace) && StructureTree.IsPdf17StandardType(type) && !tree.RoleMap.ContainsKey(type))
                || ns == Pdf2Namespace && type != "Note" && StructureTree.IsStandardType(type))
            {
                standardType = type;
                resolvedNamespace = ns ?? Pdf17Namespace;
                return RoleOutcome.Standard;
            }
            if (ns == MathMlNamespace) { resolvedNamespace = ns; return RoleOutcome.Standard; }
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

    static void PageStructureParents(ValidationContext c)
    {
        for (int page = 0; page < c.Doc.Pages.Count; page++)
        {
            var dictionary = c.Doc.Pages[page].Dict;
            if (InspectStructureParentKeys(c, dictionary, "PDFUA2-PAGE-STRUCT-PARENT-TYPE", page, () =>
                c.Add("PDFUA2-PAGE-STRUCT-PARENT-INCOMPLETE", ValidationSeverity.Error,
                    "page structure parent key inspection exceeds its reference bound", StructureParentClause, page)))
                c.Add("PDFUA2-PAGE-STRUCT-PARENT-CONFLICT", ValidationSeverity.Error,
                    "page must not contain both StructParent and StructParents", StructureParentClause, page);
        }
    }

    const string StructureParentClause = "ISO 32000-2 14.7.5.4 Table 359";

    static bool InspectStructureParentKeys(ValidationContext c, PdfDictionary dictionary, string typeRule, int page,
        Action incompleteInspection)
    {
        bool incomplete = false;
        void Incomplete() => incomplete = true;
        var single = ResolveBounded(c.F, dictionary.Get("StructParent"), Incomplete);
        var multiple = ResolveBounded(c.F, dictionary.Get("StructParents"), Incomplete);
        if (single is not (null or PdfNull or PdfInteger) || multiple is not (null or PdfNull or PdfInteger))
            c.Add(typeRule, ValidationSeverity.Error,
                "StructParent and StructParents values must be integers when present", StructureParentClause, page);
        if (incomplete) { incompleteInspection(); return false; }
        return single is not (null or PdfNull) && multiple is not (null or PdfNull);
    }

    static void AnnotationStructureParents(ValidationContext c)
    {
        for (int page = 0; page < c.Doc.Pages.Count; page++)
        {
            bool incomplete = false;
            void Incomplete()
            {
                if (incomplete) return;
                incomplete = true;
                c.Add("PDFUA2-ANNOT-STRUCT-PARENT-INCOMPLETE", ValidationSeverity.Error,
                    "annotation structure parent inspection is unsupported or exceeds its reference/work bound",
                    StructureParentClause, page);
            }
            int work = 0;
            VisitPageAnnotations(c, page, annotation =>
            {
                if (InspectStructureParentKeys(c, annotation, "PDFUA2-ANNOT-STRUCT-PARENT-TYPE", page, Incomplete))
                    c.Add("PDFUA2-ANNOT-STRUCT-PARENT-CONFLICT", ValidationSeverity.Error,
                        "annotation must not contain both StructParent and StructParents", StructureParentClause, page);
                return !incomplete;
            }, Incomplete, ref work);
        }
    }

    static void VisitPageAnnotations(ValidationContext c, int page, Func<PdfDictionary, bool> inspect,
        Action incompleteInspection, ref int work)
    {
        bool incomplete = false;
        void Incomplete() { incomplete = true; incompleteInspection(); }
        var raw = ResolveBounded(c.F, c.Doc.Pages[page].Dict.Get("Annots"), Incomplete);
        if (incomplete || raw is null or PdfNull) return;
        if (raw is not PdfArray array) { Incomplete(); return; }
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        foreach (var item in array.Items)
        {
            if (++work > PdfTreeDiagnostics.MaxWork) { Incomplete(); return; }
            var value = ResolveBounded(c.F, item, Incomplete);
            if (incomplete) return;
            if (value is null or PdfNull) continue;
            if (value is not PdfDictionary annotation) { Incomplete(); return; }
            if (seen.Add(annotation) && !inspect(annotation)) return;
        }
    }

    static void AppearanceStructureParents(ValidationContext c)
    {
        for (int page = 0; page < c.Doc.Pages.Count; page++)
        {
            bool incomplete = false;
            int work = 0;
            var seen = new HashSet<PdfStream>(ReferenceEqualityComparer.Instance);
            var seenResources = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            void Incomplete()
            {
                if (incomplete) return;
                incomplete = true;
                c.Add("PDFUA2-AP-STRUCT-PARENT-INCOMPLETE", ValidationSeverity.Error,
                    "appearance structure parent inspection is unsupported or exceeds its reference/work bound",
                    StructureParentClause, page);
            }
            PdfObject? Resolve(PdfObject? raw) => ResolveBounded(c.F, raw, Incomplete);
            bool InspectStream(PdfObject? raw)
            {
                var value = Resolve(raw);
                if (incomplete) return false;
                if (value is null or PdfNull) return true;
                if (value is not PdfStream stream) { Incomplete(); return false; }
                if (!seen.Add(stream)) return true;
                if (InspectStructureParentKeys(c, stream.Dict, "PDFUA2-AP-STRUCT-PARENT-TYPE", page, Incomplete))
                    c.Add("PDFUA2-AP-STRUCT-PARENT-CONFLICT", ValidationSeverity.Error,
                        "appearance stream must not contain both StructParent and StructParents", StructureParentClause, page);
                if (incomplete) return false;
                var resources = Resolve(stream.Dict.Get("Resources"));
                if (incomplete) return false;
                if (resources is PdfDictionary dictionary)
                    VisitXObjectResources(c, dictionary, child =>
                    {
                        if (InspectStructureParentKeys(c, child.Dict, "PDFUA2-AP-STRUCT-PARENT-TYPE", page, Incomplete))
                            c.Add("PDFUA2-AP-STRUCT-PARENT-CONFLICT", ValidationSeverity.Error,
                                "appearance resource XObject must not contain both StructParent and StructParents",
                                StructureParentClause, page);
                        return !incomplete;
                    }, Incomplete, seenResources, seen, ref work);
                else if (resources is not (null or PdfNull)) Incomplete();
                return !incomplete;
            }
            VisitPageAnnotations(c, page, annotation =>
            {
                var raw = Resolve(annotation.Get("AP"));
                if (incomplete) return false;
                if (raw is null or PdfNull) return true;
                if (raw is not PdfDictionary appearances) { Incomplete(); return false; }
                foreach (var key in new[] { "N", "R", "D" })
                {
                    var entry = Resolve(appearances.Get(key));
                    if (incomplete) return false;
                    if (entry is null or PdfNull) continue;
                    if (++work > PdfTreeDiagnostics.MaxWork) { Incomplete(); return false; }
                    if (entry is PdfDictionary states)
                    {
                        foreach (var state in states.Entries.Values)
                        {
                            if (++work > PdfTreeDiagnostics.MaxWork) { Incomplete(); return false; }
                            if (!InspectStream(state)) return false;
                        }
                    }
                    else if (!InspectStream(entry)) return false;
                }
                return true;
            }, Incomplete, ref work);
        }
    }

    static void XObjectStructureParents(ValidationContext c)
    {
        for (int page = 0; page < c.Doc.Pages.Count; page++)
        {
            bool incomplete = false;
            void Incomplete()
            {
                if (incomplete) return;
                incomplete = true;
                c.Add("PDFUA2-XOBJECT-STRUCT-PARENT-INCOMPLETE", ValidationSeverity.Error,
                    "XObject structure parent inspection is unsupported or exceeds its reference/work bound",
                    StructureParentClause, page);
            }
            PdfObject? Resolve(PdfObject? raw) => ResolveBounded(c.F, raw, Incomplete);
            var ancestors = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            PdfDictionary? ancestor = c.Doc.Pages[page].Dict;
            PdfObject? root = null;
            while (ancestor is not null)
            {
                if (ancestors.Count >= 32 || !ancestors.Add(ancestor)) { Incomplete(); break; }
                root = Resolve(ancestor.Get("Resources"));
                if (incomplete || root is not (null or PdfNull)) break;
                var parent = Resolve(ancestor.Get("Parent"));
                if (incomplete) break;
                if (parent is not (null or PdfNull or PdfDictionary)) { Incomplete(); break; }
                ancestor = parent as PdfDictionary;
            }
            if (incomplete || root is null or PdfNull) continue;
            if (root is not PdfDictionary resources) { Incomplete(); continue; }
            var seenResources = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            var seenObjects = new HashSet<PdfStream>(ReferenceEqualityComparer.Instance);
            int work = 0;
            VisitXObjectResources(c, resources, stream =>
            {
                if (InspectStructureParentKeys(c, stream.Dict, "PDFUA2-XOBJECT-STRUCT-PARENT-TYPE", page, Incomplete))
                    c.Add("PDFUA2-XOBJECT-STRUCT-PARENT-CONFLICT", ValidationSeverity.Error,
                        "XObject must not contain both StructParent and StructParents", StructureParentClause, page);
                return !incomplete;
            }, Incomplete, seenResources, seenObjects, ref work);
        }
    }

    static void VisitXObjectResources(ValidationContext c, PdfDictionary resources, Func<PdfStream, bool> inspect,
        Action incompleteInspection, HashSet<PdfDictionary> seenResources, HashSet<PdfStream> seenObjects, ref int work)
    {
        bool incomplete = false;
        void Incomplete() { incomplete = true; incompleteInspection(); }
        PdfObject? Resolve(PdfObject? raw) => ResolveBounded(c.F, raw, Incomplete);
        var pending = new Stack<PdfDictionary>();
        pending.Push(resources);
        while (pending.TryPop(out var current))
        {
            if (!seenResources.Add(current)) continue;
            var rawMap = Resolve(current.Get("XObject"));
            if (incomplete) return;
            if (rawMap is null or PdfNull) continue;
            if (rawMap is not PdfDictionary map) { Incomplete(); return; }
            foreach (var raw in map.Entries.Values)
            {
                if (++work > PdfTreeDiagnostics.MaxWork) { Incomplete(); return; }
                var value = Resolve(raw);
                if (incomplete) return;
                if (value is null or PdfNull) continue;
                if (value is not PdfStream stream) { Incomplete(); return; }
                if (seenObjects.Contains(stream)) continue;
                var subtype = Resolve(stream.Dict.Get("Subtype")).AsName();
                if (incomplete) return;
                if (subtype is not ("Form" or "Image")) continue;
                seenObjects.Add(stream);
                if (!inspect(stream)) return;
                if (subtype != "Form") continue;
                var nested = Resolve(stream.Dict.Get("Resources"));
                if (incomplete) return;
                if (nested is PdfDictionary dictionary) pending.Push(dictionary);
                else if (nested is not (null or PdfNull)) { Incomplete(); return; }
            }
        }
    }

    static void RoleMapShape(ValidationContext c, StructureTree tree)
    {
        const string clause = "ISO 32000-2 14.7.3";
        bool incomplete = false;
        void Incomplete()
        {
            if (incomplete) return;
            incomplete = true;
            c.Add("PDFUA2-ROLEMAP-SHAPE-INCOMPLETE", ValidationSeverity.Error,
                "RoleMap shape inspection exceeds its reference/work bound", clause);
        }
        var map = ResolveBounded(c.F, tree.Root.Get("RoleMap"), Incomplete);
        if (incomplete || map is null or PdfNull) return;
        bool invalid = map is not PdfDictionary;
        if (map is PdfDictionary dictionary)
        {
            int work = 0;
            foreach (var raw in dictionary.Entries.Values)
            {
                if (++work > PdfTreeDiagnostics.MaxWork) { Incomplete(); break; }
                var value = ResolveBounded(c.F, raw, Incomplete);
                if (incomplete) break;
                invalid |= value is not (null or PdfNull or PdfName);
            }
        }
        if (invalid)
            c.Add("PDFUA2-ROLEMAP-SHAPE", ValidationSeverity.Error,
                "RoleMap must be a dictionary with name-valued defined mappings", clause);
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

    static void NamespaceRegistration(ValidationContext c, StructureTree tree)
    {
        const string clause = "ISO 32000-2 14.7.2 Tables 354/355";
        bool incomplete = false;
        void Incomplete()
        {
            if (incomplete) return;
            incomplete = true;
            c.Add("PDFUA2-NS-REGISTRY-INCOMPLETE", ValidationSeverity.Error,
                "namespace registration inspection is malformed or exceeds its reference/work bound", clause);
        }
        var registered = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var value = ResolveBounded(c.F, tree.Root.Get("Namespaces"), Incomplete);
        if (value is PdfArray array)
        {
            int work = 0;
            foreach (var raw in array.Items)
            {
                if (++work > PdfTreeDiagnostics.MaxWork) { Incomplete(); break; }
                var item = ResolveBounded(c.F, raw, Incomplete);
                if (incomplete) break;
                if (item is PdfDictionary dictionary) registered.Add(dictionary);
                else if (item is not (null or PdfNull)) { Incomplete(); break; }
            }
        }
        else if (value is not (null or PdfNull)) Incomplete();
        if (incomplete) return;
        foreach (var element in tree.Elements())
        {
            var ns = ResolveBounded(c.F, element.Dict.Get("NS"), Incomplete);
            if (incomplete) break;
            if (ns is PdfDictionary dictionary && !registered.Contains(dictionary))
                c.Add("PDFUA2-NS-UNREGISTERED", ValidationSeverity.Error,
                    "structure element NS dictionary is not an element of the root Namespaces array",
                    clause, element.PageIndex);
        }
    }

    static void Elements(ValidationContext c, StructureTree tree)
    {
        var f = c.F;
        StructureRootKids(c, tree.Root);
        foreach (var e in tree.Elements())
        {
            if (e.Dict.Get("NS") is PdfDictionary)
                c.Add("PDFUA2-NS-INDIRECT", ValidationSeverity.Error,
                    "structure element NS namespace dictionary must be an indirect reference",
                    "ISO 32000-2 14.7.2 Table 355", e.PageIndex);
            StructureArrayShape(c, e.Dict, "K", "ISO 32000-2 14.7.2 Table 355", e.PageIndex);
            StructureArrayShape(c, e.Dict, "A", "ISO 32000-2 14.7.2 Table 355", e.PageIndex);
            StructureArrayShape(c, e.Dict, "C", "ISO 32000-2 14.7.2 Table 355", e.PageIndex);
            AttributeOwners(c, tree, e);
            StructureReferenceShape(c, e);
            StructureRevision(c, e);
            var outcome = Resolve(f, tree, e.Type, e.NamespaceDict, e.Namespace, out var standardType, out var resolvedNamespace);
            if (outcome == RoleOutcome.Standard && resolvedNamespace == MathMlNamespace)
            {
                var parent = e.Parent;
                if (parent is null
                    || Resolve(f, tree, parent.Type, parent.NamespaceDict, parent.Namespace, out var parentType, out var parentNamespace) == RoleOutcome.Standard
                    && parentType != "Formula" && parentNamespace != MathMlNamespace)
                    c.Add("PDFUA2-MATH-PARENT", ValidationSeverity.Error,
                        "a MathML structure element must have a Formula or MathML parent",
                        "ISO 14289-2 8.2.5.29", e.PageIndex);
            }
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
                        && li.ChildElements.Any(l => StandardType(c, tree, l) == "Lbl")))
                    {
                        bool complete = true;
                        var numbering = OwnedAttribute(c, tree, e, "List", "ListNumbering", () =>
                        {
                            if (!complete) return;
                            complete = false;
                            c.Add("PDFUA2-LIST-ATTRIBUTE-INCOMPLETE", ValidationSeverity.Error,
                                "ListNumbering attribute inspection is malformed or exceeds its reference/work bound",
                                "ISO 14289-2 8.2.5.25", e.PageIndex);
                        });
                        if (complete && numbering.AsName() is null or "None")
                            c.Add("PDFUA2-LIST-NUMBERING", ValidationSeverity.Error, "L contains Lbl elements but has no ListNumbering attribute other than None", "ISO 14289-2 8.2.5.25", e.PageIndex);
                    }
                    break;
                case "LI":
                    if (e.MarkedContent.Any())
                        c.Add("PDFUA2-LI-CONTENT", ValidationSeverity.Error, "content directly inside LI must be enclosed in Lbl or LBody", "ISO 14289-2 8.2.5.25", e.PageIndex);
                    break;
                case "FENote":
                    FenoteType(c, tree, e);
                    break;
                case "TOCI":
                    TociReference(c, e);
                    break;
            }
            var kids = e.ChildElements.ToList();
            for (var i = 1; i < kids.Count - 1; i++)
                if (StandardType(c, tree, kids[i]) == "Caption")
                { c.Add("PDFUA2-CAPTION-POSITION", ValidationSeverity.Error, $"Caption must be the first or last child of /{e.Type}", "ISO 14289-2 8.2.5.27", e.PageIndex); break; }
        }
    }

    static void TociReference(ValidationContext c, StructElement element)
    {
        bool incomplete = false;
        void Incomplete()
        {
            if (incomplete) return;
            incomplete = true;
            c.Add("PDFUA2-TOCI-REF-INCOMPLETE", ValidationSeverity.Error,
                "TOCI reference-presence inspection is cyclic or exceeds its reference/work bound",
                "ISO 14289-2 8.2.5.8", element.PageIndex);
        }
        var pending = new Stack<StructElement>();
        var seen = new HashSet<StructElement>(ReferenceEqualityComparer.Instance);
        int work = 1;
        pending.Push(element);
        while (pending.TryPop(out var current))
        {
            if (seen.Count >= PdfTreeDiagnostics.MaxWork || !seen.Add(current))
            { Incomplete(); return; }
            var reference = ResolveBounded(c.F, current.Dict.Get("Ref"), Incomplete);
            if (incomplete) return;
            if (reference is not (null or PdfNull)) return;
            foreach (var child in current.ChildElements)
            {
                if (++work > PdfTreeDiagnostics.MaxWork) { Incomplete(); return; }
                pending.Push(child);
            }
        }
        c.Add("PDFUA2-TOCI-REF", ValidationSeverity.Error,
            "TOCI and its descendants have no non-null /Ref entry; target shape and identity are not evaluated",
            "ISO 14289-2 8.2.5.8", element.PageIndex);
    }

    static void StructureReferenceShape(ValidationContext c, StructElement element)
    {
        bool incomplete = false;
        void Incomplete()
        {
            if (incomplete) return;
            incomplete = true;
            c.Add("PDFUA2-STRUCT-REF-INCOMPLETE", ValidationSeverity.Error,
                "structure Ref inspection exceeds its reference/work bound",
                "ISO 32000-2 14.7.2 Table 355", element.PageIndex);
        }
        var value = ResolveBounded(c.F, element.Dict.Get("Ref"), Incomplete);
        if (incomplete || value is null or PdfNull) return;
        if (value is not PdfArray references)
        {
            c.Add("PDFUA2-STRUCT-REF-TYPE", ValidationSeverity.Error,
                "structure Ref must be an array", "ISO 32000-2 14.7.2 Table 355", element.PageIndex);
            return;
        }
        if (ArrayContainsNull(c, references, Incomplete))
            c.Add("PDFUA2-STRUCT-REF-NULL", ValidationSeverity.Error,
                "structure Ref array contains a null element", "ISO 32000-2 14.7.2 Table 355", element.PageIndex);
    }

    static void StructureArrayShape(ValidationContext c, PdfDictionary dictionary, string key, string clause, int? page)
    {
        bool incomplete = false;
        void Incomplete()
        {
            if (incomplete) return;
            incomplete = true;
            c.Add($"PDFUA2-STRUCT-{key}-INCOMPLETE", ValidationSeverity.Error,
                $"structure {key} null-item inspection exceeds its reference/work bound", clause, page);
        }
        var value = ResolveBounded(c.F, dictionary.Get(key), Incomplete);
        if (incomplete) return;
        if (key == "C" && value is not (null or PdfNull or PdfName or PdfArray))
            c.Add("PDFUA2-STRUCT-C-TYPE", ValidationSeverity.Error,
                "structure C must be a name or array", clause, page);
        if (value is not PdfArray array) return;
        if (key == "K" && array.Count == 0)
            c.Add("PDFUA2-STRUCT-K-EMPTY", ValidationSeverity.Error,
                "structure element K array must contain one or more children", clause, page);
        bool invalidClassItem = false;
        if (ArrayContainsNull(c, array, Incomplete,
            item => invalidClassItem |= key == "C" && item is not (null or PdfNull or PdfName or PdfInteger or PdfReal)))
            c.Add($"PDFUA2-STRUCT-{key}-NULL", ValidationSeverity.Error,
                $"structure {key} array contains a null item", clause, page);
        if (invalidClassItem)
            c.Add("PDFUA2-STRUCT-C-ITEM-TYPE", ValidationSeverity.Error,
                "structure C array items must be class names or numeric revision markers",
                "ISO 32000-2 14.7.6.2", page);
    }

    static void StructureRootKids(ValidationContext c, PdfDictionary root)
    {
        const string clause = "ISO 32000-2 14.7.2 Table 354";
        bool incomplete = false;
        void Incomplete()
        {
            if (incomplete) return;
            incomplete = true;
            c.Add("PDFUA2-STRUCT-K-INCOMPLETE", ValidationSeverity.Error,
                "structure root K inspection exceeds its reference/work bound", clause);
        }
        var value = ResolveBounded(c.F, root.Get("K"), Incomplete);
        if (incomplete || value is null or PdfNull) return;
        bool invalidType = value is not (PdfDictionary or PdfArray);
        if (value is PdfArray array && ArrayContainsNull(c, array, Incomplete,
            item => invalidType |= item is not (null or PdfNull or PdfDictionary)))
            c.Add("PDFUA2-STRUCT-K-NULL", ValidationSeverity.Error,
                "structure K array contains a null item", clause);
        if (invalidType)
            c.Add("PDFUA2-STRUCT-ROOT-K-TYPE", ValidationSeverity.Error,
                "structure root K must be a dictionary or a flat array of dictionaries", clause);
    }

    static bool ArrayContainsNull(ValidationContext c, PdfArray array, Action incompleteInspection,
        Action<PdfObject?>? inspectItem = null)
    {
        bool incomplete = false;
        void Incomplete()
        {
            incomplete = true;
            incompleteInspection();
        }
        int work = 0;
        bool foundNull = false;
        foreach (var raw in array.Items)
        {
            if (++work > PdfTreeDiagnostics.MaxWork) { Incomplete(); break; }
            var item = ResolveBounded(c.F, raw, Incomplete);
            if (incomplete) break;
            foundNull |= item is null or PdfNull;
            inspectItem?.Invoke(item);
        }
        return foundNull;
    }

    static void StructureRevision(ValidationContext c, StructElement element)
    {
        bool incomplete = false;
        var value = ResolveBounded(c.F, element.Dict.Get("R"), () =>
        {
            incomplete = true;
            c.Add("PDFUA2-STRUCT-R-INCOMPLETE", ValidationSeverity.Error,
                "structure R revision inspection exceeds its reference bound",
                "ISO 32000-2 14.7.2 Table 355", element.PageIndex);
        });
        if (!incomplete && value is not (null or PdfNull or PdfInteger { Value: >= 0 }))
            c.Add("PDFUA2-STRUCT-R-TYPE", ValidationSeverity.Error,
                "structure R revision must be a non-negative integer",
                "ISO 32000-2 14.7.2 Table 355", element.PageIndex);
    }

    static void FenoteType(ValidationContext c, StructureTree tree, StructElement element)
    {
        bool incomplete = false;
        void Incomplete()
        {
            if (incomplete) return;
            incomplete = true;
            c.Add("PDFUA2-FENOTE-INCOMPLETE", ValidationSeverity.Error,
                "FENote attribute inspection is malformed or exceeds its reference/work bound",
                "ISO 14289-2 8.2.5.14", element.PageIndex);
        }

        var noteType = OwnedAttribute(c, tree, element, "FENote", "NoteType", Incomplete);
        if (!incomplete && noteType is PdfName name && name.Value is not ("Footnote" or "Endnote" or "None"))
            c.Add("PDFUA2-FENOTE-TYPE", ValidationSeverity.Error,
                $"FENote NoteType name /{name.Value} is not Footnote, Endnote or None",
                "ISO 14289-2 8.2.5.14", element.PageIndex);
    }

    static bool VisitAttributeObjects(ValidationContext c, PdfObject? raw, Func<PdfDictionary, bool> inspect,
        Action incompleteInspection, ref int work, bool includeStreams = false)
    {
        bool complete = true;
        void Incomplete() { complete = false; incompleteInspection(); }
        bool Visit(PdfObject? value, bool arrayItem)
        {
            if (value is PdfDictionary dictionary) return inspect(dictionary);
            if (includeStreams && value is PdfStream stream) return inspect(stream.Dict);
            if (value is null or PdfNull || arrayItem && value is PdfInteger) return true;
            Incomplete();
            return false;
        }
        var attributes = ResolveBounded(c.F, raw, Incomplete);
        if (!complete) return false;
        if (attributes is not PdfArray array) return Visit(attributes, false);
        foreach (var rawItem in array.Items)
        {
            if (++work > PdfTreeDiagnostics.MaxWork) { Incomplete(); return false; }
            var item = ResolveBounded(c.F, rawItem, Incomplete);
            if (!complete || !Visit(item, true)) return false;
        }
        return true;
    }

    static bool VisitClassAttributes(ValidationContext c, StructureTree tree, StructElement element,
        Func<PdfDictionary, bool> inspect, Action incompleteInspection, ref int work, bool includeStreams = false)
    {
        bool complete = true;
        void Incomplete() { complete = false; incompleteInspection(); }
        var classes = ResolveBounded(c.F, element.Dict.Get("C"), Incomplete);
        if (!complete) return false;
        if (classes is null or PdfNull) return true;
        var rawMap = ResolveBounded(c.F, tree.Root.Get("ClassMap"), Incomplete);
        if (!complete) return false;
        if (rawMap is null or PdfNull) return true;
        if (rawMap is not PdfDictionary classMap) { Incomplete(); return false; }
        IEnumerable<PdfObject>? names = classes is PdfName name ? [name] : classes is PdfArray array ? array.Items : null;
        if (names is null) { Incomplete(); return false; }
        foreach (var rawName in names)
        {
            if (++work > PdfTreeDiagnostics.MaxWork) { Incomplete(); return false; }
            var value = ResolveBounded(c.F, rawName, Incomplete);
            if (!complete) return false;
            if (value is PdfName className)
            {
                if (!VisitAttributeObjects(c, classMap.Get(className.Value), inspect, Incomplete, ref work, includeStreams))
                    return false;
            }
            else if (value is not (null or PdfNull or PdfInteger)) { Incomplete(); return false; }
        }
        return true;
    }

    static void AttributeOwners(ValidationContext c, StructureTree tree, StructElement element)
    {
        const string clause = "ISO 32000-2 14.7.6.1 Table 360";
        bool incomplete = false, invalid = false, missingNamespace = false;
        int work = 0;
        void Incomplete()
        {
            if (incomplete) return;
            incomplete = true;
            c.Add("PDFUA2-ATTR-OWNER-INCOMPLETE", ValidationSeverity.Error,
                "attribute owner inspection is unsupported or exceeds its reference/work bound", clause, element.PageIndex);
        }
        bool Inspect(PdfDictionary dictionary)
        {
            bool complete = true;
            var owner = ResolveBounded(c.F, dictionary.Get("O"), () => { complete = false; Incomplete(); });
            if (complete)
            {
                invalid |= owner is not PdfName;
                if (owner.AsName() == "NSO")
                {
                    var ns = ResolveBounded(c.F, dictionary.Get("NS"), () => { complete = false; Incomplete(); });
                    if (complete) missingNamespace |= ns is null or PdfNull;
                }
            }
            return complete;
        }
        if (VisitAttributeObjects(c, element.Dict.Get("A"), Inspect, Incomplete, ref work, includeStreams: true))
            VisitClassAttributes(c, tree, element, Inspect, Incomplete, ref work, includeStreams: true);
        if (invalid)
            c.Add("PDFUA2-ATTR-OWNER", ValidationSeverity.Error,
                "attached attribute object O owner must be a name", clause, element.PageIndex);
        if (missingNamespace)
            c.Add("PDFUA2-ATTR-NSO-NS", ValidationSeverity.Error,
                "NSO-owned attribute object NS must be present and non-null", clause, element.PageIndex);
    }

    internal static PdfObject? OwnedAttribute(ValidationContext c, StructureTree tree, StructElement element,
        string ownerName, string key, Action incompleteInspection)
    {
        bool incomplete = false;
        int work = 0;
        PdfObject? result = null;
        void Incomplete()
        {
            if (incomplete) return;
            incomplete = true;
            incompleteInspection();
        }
        bool Inspect(PdfDictionary dictionary)
        {
            var owner = ResolveBounded(c.F, dictionary.Get("O"), Incomplete);
            if (incomplete) return false;
            if (owner.AsName() != ownerName) return true;
            var value = ResolveBounded(c.F, dictionary.Get(key), Incomplete);
            if (incomplete) return false;
            if (value is not (null or PdfNull)) result = value;
            return true;
        }
        if (!VisitAttributeObjects(c, element.Dict.Get("A"), Inspect, Incomplete, ref work)) return null;
        if (result is null && !VisitClassAttributes(c, tree, element, Inspect, Incomplete, ref work)) return null;
        return result;
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
