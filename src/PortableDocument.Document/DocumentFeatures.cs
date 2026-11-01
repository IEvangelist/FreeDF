using System.Text;
using PortableDocument.Core;

namespace PortableDocument.Document;

public sealed record PdfOutlineItem(string Title, int? PageIndex, string? ActionUri, bool Open, IReadOnlyList<PdfOutlineItem> Children);

public sealed record PdfAnnotation(string Subtype, PdfRect Rect, string? Contents, string? Uri, int? DestPage, int Flags, PdfDictionary Dict);

public sealed record PdfFormField(string FullName, string? FieldType, string? Value, int Flags, PdfDictionary Dict);

public sealed record PdfEmbeddedFile(string Name, string? MimeType, long? Size, byte[] Data)
{
    public string? CreationDate { get; init; }
    public string? ModificationDate { get; init; }
    public byte[]? Checksum { get; init; }
    public PdfDictionary? Parameters { get; init; }
    public PdfStream? Stream { get; init; }

    /// <summary>Whether Params /Size equals the decoded byte count (ISO 32000-2 7.11.4.1, Table 45); null when /Size is absent or not a number.</summary>
    public bool? SizeMatches { get; init; }

    /// <summary>Whether Params /CheckSum equals the MD5 digest of the decoded bytes (Table 45; RFC 1321, a checksum, not a security control).
    /// False for a /CheckSum that is not a 16-byte string; null when absent.</summary>
    public bool? ChecksumMatches { get; init; }
}

public sealed record PdfFileSpecification(
    string Name,
    string? FileName,
    string? UnicodeFileName,
    string? DosFileName,
    string? MacFileName,
    string? UnixFileName,
    string? Description,
    IReadOnlyList<string> Id,
    bool? IsVolatile,
    string? AFRelationship,
    IReadOnlyDictionary<string, PdfEmbeddedFile> EmbeddedFiles,
    PdfDictionary Dictionary,
    PdfDictionary? CollectionItem,
    IReadOnlyList<PdfRelatedFile> RelatedFiles)
{
    /// <summary>The /FS file-system name (ISO 32000-2, Table 43), without interpreting or opening the file name.</summary>
    public string? FileSystem { get; init; }

    /// <summary>The /Thumb image stream (ISO 32000-2, Table 43), retained without decoding or rendering.</summary>
    public PdfStream? Thumbnail { get; init; }

    /// <summary>The /EP encrypted-payload dictionary (ISO 32000-2, Table 43), retained without decrypting the payload.</summary>
    public PdfDictionary? EncryptedPayload { get; init; }
}

public sealed record PdfRelatedFile(string Platform, string? FileName, PdfDictionary Dictionary)
{
    public IReadOnlyList<PdfRelatedFile> RelatedFiles { get; init; } = [];
}

public sealed record PdfDocumentCollection(
    string? View,
    PdfDictionary? Schema,
    PdfDictionary? Sort,
    PdfDictionary Dictionary)
{
    public IReadOnlyList<PdfCollectionField> SchemaFields { get; init; } = [];
    public PdfCollectionSort? SortDefinition { get; init; }
    public PdfCollectionView InitialView { get; init; }
    public IReadOnlyList<PdfCollectionField> VisibleFields { get; init; } = [];

    /// <summary>The /Navigator dictionary (Table 160, PDF 2.0), retained raw; present when the collection is presented by a portable-collection navigator.</summary>
    public PdfCollectionNavigator? Navigator { get; init; }

    /// <summary>Folders reached from /Folders (Table 159, PDF 2.0) in depth-first order, following /Child and /Next with cycle, depth and total-work bounds.</summary>
    public IReadOnlyList<PdfCollectionFolder> Folders { get; init; } = [];

    /// <summary>True when folder traversal stopped at the node or depth bound.</summary>
    public bool FoldersTruncated { get; init; }

    /// <summary>Non-fatal folder-structure problems: DuplicateFolderId, DuplicateSiblingName, ParentMismatch, MissingFolderId, Cycle.</summary>
    public IReadOnlyList<string> FolderIssues { get; init; } = [];
}

/// <summary>A /Navigator dictionary (ISO 32000-2 12.3.5, Table 160). Layout names are preserved in order, including unknown ones.</summary>
public sealed record PdfCollectionNavigator(IReadOnlyList<string> Layouts, PdfDictionary Dictionary);

/// <summary>A collection folder dictionary (ISO 32000-2 12.3.5, Table 159). The id appears in the EmbeddedFiles name-tree keys of the folder's files.</summary>
public sealed record PdfCollectionFolder(
    long? Id,
    string? Name,
    int Depth,
    long? ParentId,
    string? Description,
    string? CreationDate,
    string? ModificationDate,
    PdfDictionary? CollectionItem,
    PdfStream? Thumbnail,
    PdfDictionary Dictionary);

/// <summary>One typed entry of a collection item dictionary (Tables 46 and 47) interpreted against the collection schema (Table 155).</summary>
public sealed record PdfCollectionItemValue(
    string Key,
    string? FieldSubtype,
    string? Text,
    double? Number,
    string? Date,
    string? Prefix,
    bool? MatchesSchema,
    PdfObject Raw);

public enum PdfCollectionView
{
    Details,
    Tile,
    Hidden,
}

public sealed record PdfCollectionField(
    string Key,
    string? Subtype,
    string? Name,
    double? Order,
    bool? Visible,
    bool? Editable,
    PdfDictionary Dictionary);

public sealed record PdfCollectionSort(
    IReadOnlyList<string> Fields,
    IReadOnlyList<bool?> Ascending,
    PdfDictionary Dictionary);

public enum PdfAssociatedFileScope
{
    Catalog,
    Page,
    Annotation,
    AcroForm,
    FormField,
    StructureElement,
    XObject,
}

public sealed record PdfAssociatedFile(
    PdfAssociatedFileScope Scope,
    PdfFileSpecification FileSpecification,
    int? PageIndex = null,
    int? AnnotationIndex = null,
    string? FieldName = null,
    string? StructureRole = null)
{
    public string? ResourceName { get; init; }
}

public sealed record PdfRichMediaAsset(
    int PageIndex,
    int AnnotationIndex,
    string Name,
    PdfFileSpecification FileSpecification);

public sealed record PdfRichMediaInstance(
    string? Subtype,
    PdfObject? Asset,
    PdfDictionary? Parameters,
    PdfDictionary Dictionary);

public sealed record PdfRichMediaConfiguration(
    int PageIndex,
    int AnnotationIndex,
    string? Name,
    string? Subtype,
    IReadOnlyList<PdfRichMediaInstance> Instances,
    PdfDictionary Dictionary);

public sealed record PdfRichMediaSettings(
    int PageIndex,
    int AnnotationIndex,
    PdfDictionary? Activation,
    PdfDictionary? Deactivation,
    PdfDictionary Dictionary)
{
    public PdfRichMediaActivation? ActivationDetails { get; init; }
    public PdfRichMediaDeactivation? DeactivationDetails { get; init; }
}

public sealed record PdfRichMediaActivation(
    string Condition,
    PdfRichMediaAnimation? Animation,
    PdfObject? View,
    PdfDictionary? Configuration,
    PdfRichMediaPresentation? Presentation,
    PdfDictionary Dictionary);

public sealed record PdfRichMediaDeactivation(string Condition, PdfDictionary Dictionary);

public sealed record PdfRichMediaAnimation(
    string Subtype,
    long PlayCount,
    double Speed,
    PdfDictionary Dictionary);

public sealed record PdfRichMediaPresentation(
    string Style,
    bool Transparent,
    bool NavigationPane,
    bool? Toolbar,
    bool PassContextClick,
    PdfDictionary? Window,
    PdfDictionary Dictionary)
{
    public PdfRichMediaWindow? WindowDetails { get; init; }
}

public sealed record PdfRichMediaWindow(
    PdfRichMediaDimension Width,
    PdfRichMediaDimension Height,
    PdfRichMediaPosition? Position,
    PdfDictionary Dictionary);

public sealed record PdfRichMediaDimension(
    double Default,
    double Max,
    double Min,
    PdfDictionary? Dictionary);

public sealed record PdfRichMediaPosition(
    string HorizontalAlignment,
    string VerticalAlignment,
    double HorizontalOffset,
    double VerticalOffset,
    PdfDictionary Dictionary);

public sealed record Pdf3DAnnotation(
    int PageIndex,
    int AnnotationIndex,
    PdfObject? Data,
    PdfObject? DefaultView,
    PdfDictionary? Activation,
    PdfDictionary Dictionary)
{
    /// <summary>Typed 3D detail traversal hit a work limit; raw annotation data is preserved.</summary>
    public bool Truncated { get; init; }
    public Pdf3DView? DefaultViewDetails { get; init; }

    /// <summary>Typed 3D stream (or 3D reference target) from /3DD (ISO 32000-1:2008 Tables 298, 300, 303); null when /3DD is not a stream.</summary>
    public Pdf3DStream? StreamDetails { get; init; }

    /// <summary>Typed /3DA activation dictionary (Table 299) with spec defaults; the annotation is never activated.</summary>
    public Pdf3DActivation? ActivationDetails { get; init; }

    /// <summary>/3DI flag; true by default (Table 298).</summary>
    public bool Interactive { get; init; } = true;

    /// <summary>The raw /3DB view box array, if present.</summary>
    public PdfArray? ViewBox { get; init; }

    /// <summary>The /3DV selector (view dictionary, VA index, IN string, or F/L/D name) resolved against the stream's views; null if it cannot be resolved.</summary>
    public Pdf3DView? ResolvedDefaultView { get; init; }
}

public sealed record Pdf3DView(
    string? ExternalName,
    string? InternalName,
    string? MatrixSource,
    PdfArray? CameraToWorld,
    PdfObject? U3DPath,
    double? CenterOfOrbit,
    PdfDictionary? Projection,
    PdfStream? Overlay,
    PdfDictionary? Background,
    PdfDictionary? RenderMode,
    PdfDictionary? LightingScheme,
    PdfArray? Measurements,
    PdfArray? CrossSections,
    PdfArray? Nodes,
    bool? ResetNodes,
    PdfDictionary Dictionary)
{
    /// <summary>Typed detail lists are incomplete because a work limit was reached; raw arrays are preserved.</summary>
    public bool Truncated { get; init; }
    public Pdf3DProjection? ProjectionDetails { get; init; }
    public Pdf3DBackground? BackgroundDetails { get; init; }
    public Pdf3DRenderMode? RenderModeDetails { get; init; }
    public Pdf3DLightingScheme? LightingSchemeDetails { get; init; }
    public IReadOnlyList<Pdf3DMeasure> MeasureDetails { get; init; } = [];
    public IReadOnlyList<Pdf3DCrossSection> CrossSectionDetails { get; init; } = [];
    public IReadOnlyList<Pdf3DNode> NodeDetails { get; init; } = [];
}

public static class DocumentFeatures
{
    const int MaxDepth = 64;

    /// <summary>Collection dictionary (Catalog /Collection), preserving its specification-defined entries.</summary>
    public static PdfDocumentCollection? GetCollection(this PdfDocument doc)
    {
        var f = doc.File;
        if (f.Resolve(f.Catalog.Get("Collection")) is not PdfDictionary collection) return null;
        var schema = f.Resolve(collection.Get("Schema")) as PdfDictionary;
        var sort = f.Resolve(collection.Get("Sort")) as PdfDictionary;
        var schemaFields = ReadCollectionSchemaFields(f, schema);
        var (folders, foldersTruncated, folderIssues) = ReadCollectionFolders(f, collection.Get("Folders"));
        return new PdfDocumentCollection(
            f.Resolve(collection.Get("View")).AsName(),
            schema,
            sort,
            collection)
        {
            SchemaFields = schemaFields,
            SortDefinition = ReadCollectionSort(f, sort),
            InitialView = f.Resolve(collection.Get("View")).AsName() switch
            {
                "T" => PdfCollectionView.Tile,
                "H" => PdfCollectionView.Hidden,
                _ => PdfCollectionView.Details,
            },
            VisibleFields = schemaFields
                .Where(field => field.Visible != false)
                .OrderBy(field => field.Order ?? 0)
                .ToArray(),
            Navigator = f.Resolve(collection.Get("Navigator")) is PdfDictionary navigator
                ? new PdfCollectionNavigator(ReadNavigatorLayouts(f, navigator), navigator)
                : null,
            Folders = folders,
            FoldersTruncated = foldersTruncated,
            FolderIssues = folderIssues,
        };
    }

    static IReadOnlyList<string> ReadNavigatorLayouts(PdfFile f, PdfDictionary navigator) => f.Resolve(navigator.Get("Layout")) switch
    {
        PdfName name => [name.Value],
        PdfArray array => array.Items.Select(item => f.Resolve(item).AsName()).Where(name => name is not null).Select(name => name!).ToArray(),
        _ => [],
    };

    const int MaxCollectionFolders = 10_000;

    // Iterative depth-first walk over /Child and /Next; the visited set breaks cycles and shared nodes so reuse cannot fan out.
    static (IReadOnlyList<PdfCollectionFolder> Folders, bool Truncated, IReadOnlyList<string> Issues) ReadCollectionFolders(PdfFile f, PdfObject? rootValue)
    {
        if (f.Resolve(rootValue) is not PdfDictionary root) return ([], false, []);
        var folders = new List<PdfCollectionFolder>();
        var issues = new List<string>();
        var truncated = false;
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var ids = new HashSet<long>();
        var siblingNames = new HashSet<(long?, string)>();
        var stack = new Stack<(PdfDictionary Folder, int Depth, PdfDictionary? Parent, long? ParentId)>();
        stack.Push((root, 0, null, null));
        while (stack.TryPop(out var item))
        {
            if (!seen.Add(item.Folder)) { issues.Add("Cycle"); continue; }
            if (folders.Count >= MaxCollectionFolders || item.Depth > MaxDepth) { truncated = true; continue; }
            var folder = item.Folder;
            long? id = f.Resolve(folder.Get("ID")) is PdfInteger { Value: >= 0 } folderId ? folderId.Value : null;
            var name = PdfStringValue(f, folder.Get("Name"));
            if (id is null) issues.Add("MissingFolderId");
            else if (!ids.Add(id.Value)) issues.Add($"DuplicateFolderId:{id}");
            if (name is not null && !siblingNames.Add((item.ParentId ?? -1, name.ToUpperInvariant())))
                issues.Add($"DuplicateSiblingName:{name}");
            if (item.Parent is not null && !ReferenceEquals(f.Resolve(folder.Get("Parent")), item.Parent))
                issues.Add($"ParentMismatch:{id}");
            folders.Add(new PdfCollectionFolder(
                id, name, item.Depth, item.ParentId,
                PdfStringValue(f, folder.Get("Desc")),
                PdfStringValue(f, folder.Get("CreationDate")),
                PdfStringValue(f, folder.Get("ModDate")),
                f.Resolve(folder.Get("CI")) as PdfDictionary,
                f.Resolve(folder.Get("Thumb")) as PdfStream,
                folder));
            if (f.Resolve(folder.Get("Next")) is PdfDictionary next) stack.Push((next, item.Depth, item.Parent, item.ParentId));
            if (f.Resolve(folder.Get("Child")) is PdfDictionary child) stack.Push((child, item.Depth + 1, folder, id));
            if (issues.Count > 1000) { truncated = true; break; }
        }
        return (folders, truncated, issues);
    }

    /// <summary>Interprets a file specification's /CI collection item dictionary against the catalog collection schema (ISO 32000-2 Tables 46, 47, 155).
    /// Entries keep their raw objects; a value whose type disagrees with its schema field is reported through <see cref="PdfCollectionItemValue.MatchesSchema"/> rather than coerced.</summary>
    public static IReadOnlyList<PdfCollectionItemValue> GetCollectionItemValues(this PdfDocument doc, PdfDictionary? item)
    {
        if (item is null) return [];
        var f = doc.File;
        var schema = doc.GetCollection()?.SchemaFields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        var values = new List<PdfCollectionItemValue>();
        foreach (var (key, raw) in item.Entries)
        {
            if (key == "Type") continue;
            var resolved = f.Resolve(raw);
            string? prefix = null;
            if (resolved is PdfDictionary subitem)
            {
                prefix = PdfStringValue(f, subitem.Get("P"));
                resolved = f.Resolve(subitem.Get("D"));
            }
            var subtype = schema is not null && schema.TryGetValue(key, out var field) ? field.Subtype : null;
            string? text = null, date = null;
            double? number = null;
            bool? matches = null;
            switch (subtype)
            {
                case "N" or "Size" or "CompressedSize":
                    number = resolved.AsNumber();
                    matches = number is not null;
                    break;
                case "D" or "ModDate" or "CreationDate":
                    date = PdfStringValue(f, resolved);
                    matches = date is not null;
                    break;
                case "S" or "F" or "Desc":
                    text = PdfStringValue(f, resolved);
                    matches = text is not null;
                    break;
                default:
                    text = PdfStringValue(f, resolved);
                    number = text is null ? resolved.AsNumber() : null;
                    break;
            }
            values.Add(new PdfCollectionItemValue(key, subtype, text, number, date, prefix, matches, raw));
        }
        return values;
    }

    static IReadOnlyList<PdfCollectionField> ReadCollectionSchemaFields(PdfFile f, PdfDictionary? schema)
    {
        if (schema is null) return [];
        var fields = new List<PdfCollectionField>();
        foreach (var (key, value) in schema.Entries)
            if (f.Resolve(value) is PdfDictionary field)
                fields.Add(new PdfCollectionField(
                    key,
                    f.Resolve(field.Get("Subtype")).AsName(),
                    PdfStringValue(f, field.Get("N")),
                    f.Resolve(field.Get("O")).AsNumber(),
                    f.Resolve(field.Get("V")) is PdfBoolean visible ? visible.Value : null,
                    f.Resolve(field.Get("E")) is PdfBoolean editable ? editable.Value : null,
                    field));
        return fields;
    }

    static PdfCollectionSort? ReadCollectionSort(PdfFile f, PdfDictionary? sort)
    {
        if (sort is null) return null;
        var fields = f.Resolve(sort.Get("S")) switch
        {
            PdfArray array => array.Items.Select(value => f.Resolve(value).AsName()).Where(name => name is not null).Select(name => name!).ToArray(),
            PdfName name => [name.Value],
            _ => [],
        };
        var ascending = f.Resolve(sort.Get("A")) switch
        {
            PdfArray array => array.Items.Select(value =>
                f.Resolve(value) is PdfBoolean direction ? (bool?)direction.Value : null).ToArray(),
            PdfBoolean direction => Enumerable.Repeat<bool?>(direction.Value, fields.Length).ToArray(),
            _ => [],
        };
        return new PdfCollectionSort(fields, ascending, sort);
    }

    /// <summary>Collection file specifications, ordered by the collection's declared sort keys.</summary>
    public static IReadOnlyList<PdfFileSpecification> GetCollectionItems(this PdfDocument doc)
    {
        var collection = doc.GetCollection();
        if (collection is null) return [];
        var files = doc.GetFileSpecifications();
        if (collection.SortDefinition is not { Fields.Count: > 0 } sort) return files;

        var fields = collection.SchemaFields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        return files.OrderBy(file => file, Comparer<PdfFileSpecification>.Create((left, right) =>
        {
            for (var index = 0; index < sort.Fields.Count; index++)
            {
                if (!fields.TryGetValue(sort.Fields[index], out var field)) continue;
                var comparison = CompareCollectionValues(doc.File, left, right, field);
                if (comparison == 0) continue;
                return sort.Ascending.ElementAtOrDefault(index) ?? true ? comparison : -comparison;
            }
            return 0;
        })).ToArray();
    }

    static int CompareCollectionValues(PdfFile f, PdfFileSpecification left, PdfFileSpecification right, PdfCollectionField field)
    {
        var leftValue = CollectionSortValue(f, left, field);
        var rightValue = CollectionSortValue(f, right, field);
        if (leftValue is null) return rightValue is null ? 0 : -1;
        if (rightValue is null) return 1;

        return field.Subtype switch
        {
            "S" or "F" or "Desc" => StringComparer.Ordinal.Compare((string)leftValue, (string)rightValue),
            "D" or "ModDate" or "CreationDate" => ((long)leftValue).CompareTo((long)rightValue),
            "N" or "Size" or "CompressedSize" => ((double)leftValue).CompareTo((double)rightValue),
            _ => 0,
        };
    }

    static object? CollectionSortValue(PdfFile f, PdfFileSpecification spec, PdfCollectionField field)
    {
        var item = spec.CollectionItem;
        var value = item?.Get(field.Key);
        return field.Subtype switch
        {
            "S" => PdfStringValue(f, value),
            "D" => CollectionDateValue(f, value),
            "N" => f.Resolve(value).AsNumber(),
            "F" => spec.UnicodeFileName ?? spec.FileName,
            "Desc" => spec.Description,
            "ModDate" => CollectionEmbeddedDate(spec, "ModDate"),
            "CreationDate" => CollectionEmbeddedDate(spec, "CreationDate"),
            "Size" => CollectionEmbeddedNumber(spec, "Size"),
            "CompressedSize" => CollectionCompressedSize(f, spec),
            _ => null,
        };
    }

    static long? CollectionDateValue(PdfFile f, PdfObject? value) =>
        PdfStringValue(f, value) is { } text ? ParseCollectionDate(text) : null;

    static long? CollectionEmbeddedDate(PdfFileSpecification spec, string key)
    {
        var file = PreferredEmbeddedFile(spec);
        var text = key == "ModDate" ? file?.ModificationDate : file?.CreationDate;
        return text is null ? null : ParseCollectionDate(text);
    }

    static double? CollectionEmbeddedNumber(PdfFileSpecification spec, string key) =>
        key == "Size" ? PreferredEmbeddedFile(spec)?.Size : null;

    static double? CollectionCompressedSize(PdfFile f, PdfFileSpecification spec) =>
        f.Resolve(PreferredEmbeddedFile(spec)?.Stream?.Dict.Get("Length")).AsNumber();

    static PdfEmbeddedFile? PreferredEmbeddedFile(PdfFileSpecification spec) =>
        spec.EmbeddedFiles.TryGetValue("UF", out var unicodeFile) ? unicodeFile
            : spec.EmbeddedFiles.TryGetValue("F", out var file) ? file
            : null;

    static long? ParseCollectionDate(string value)
    {
        if (!value.StartsWith("D:", StringComparison.Ordinal)) return null;
        var index = 2;
        if (!ReadDateComponent(value, ref index, 4, out var year)) return null;
        var month = 1;
        var day = 1;
        var hour = 0;
        var minute = 0;
        var second = 0;
        if (ReadDateComponent(value, ref index, 2, out var dateMonth, optional: true)) month = dateMonth;
        if (ReadDateComponent(value, ref index, 2, out var dateDay, optional: true)) day = dateDay;
        if (ReadDateComponent(value, ref index, 2, out var dateHour, optional: true)) hour = dateHour;
        if (ReadDateComponent(value, ref index, 2, out var dateMinute, optional: true)) minute = dateMinute;
        if (ReadDateComponent(value, ref index, 2, out var dateSecond, optional: true)) second = dateSecond;

        var offsetMinutes = 0;
        if (index < value.Length && value[index] == 'Z')
        {
            index++;
        }
        else if (index < value.Length && value[index] is '+' or '-')
        {
            var sign = value[index++] == '+' ? 1 : -1;
            if (!ReadDateComponent(value, ref index, 2, out var offsetHours)) return null;
            if (index < value.Length && value[index] == '\'') index++;
            var offsetMinutePart = 0;
            if (index < value.Length && char.IsDigit(value[index]))
            {
                if (!ReadDateComponent(value, ref index, 2, out offsetMinutePart)) return null;
                if (index < value.Length && value[index] == '\'') index++;
            }
            if (offsetHours > 23 || offsetMinutePart > 59) return null;
            offsetMinutes = sign * (offsetHours * 60 + offsetMinutePart);
        }

        if (index != value.Length) return null;
        try
        {
            return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc).Ticks
                - TimeSpan.FromMinutes(offsetMinutes).Ticks;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    static bool ReadDateComponent(string value, ref int index, int length, out int component, bool optional = false)
    {
        component = 0;
        var start = index;
        while (index < value.Length && index - start < length && char.IsDigit(value[index])) index++;
        if (index - start == length)
        {
            component = int.Parse(value.AsSpan(start, length), System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        if (optional && start == index) return false;
        index = start;
        return false;
    }

    /// <summary>XMP metadata packet (Catalog /Metadata), decoded as UTF-8.</summary>
    public static string? GetXmpMetadata(this PdfDocument doc)
    {
        var f = doc.File;
        return f.Resolve(f.Catalog.Get("Metadata")) is PdfStream s ? Encoding.UTF8.GetString(f.DecodeStream(s)) : null;
    }

    public static string? GetPageMode(this PdfDocument doc) => doc.File.Resolve(doc.File.Catalog.Get("PageMode")).AsName();
    public static string? GetLanguage(this PdfDocument doc) => doc.File.Resolve(doc.File.Catalog.Get("Lang")) is PdfString s ? s.Text : null;

    /// <summary>True when the catalog carries a StructTreeRoot and MarkInfo/Marked is true (tagged PDF).</summary>
    public static bool IsTagged(this PdfDocument doc)
    {
        var f = doc.File;
        return f.Resolve(f.Catalog.Get("StructTreeRoot")) is PdfDictionary
            && f.Resolve(f.Catalog.Get("MarkInfo")) is PdfDictionary mi && f.Resolve(mi.Get("Marked")) is PdfBoolean { Value: true };
    }

    public static IReadOnlyList<PdfOutlineItem> GetOutlines(this PdfDocument doc)
    {
        var f = doc.File;
        if (f.Resolve(f.Catalog.Get("Outlines")) is not PdfDictionary root) return [];
        var pageIndex = PageIndexMap(doc);
        return Siblings(doc, pageIndex, root.Get("First"), 0, new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance));
    }

    static List<PdfOutlineItem> Siblings(PdfDocument doc, Dictionary<PdfDictionary, int> pages, PdfObject? first, int depth, HashSet<PdfDictionary> seen)
    {
        var f = doc.File;
        var list = new List<PdfOutlineItem>();
        var cur = f.Resolve(first) as PdfDictionary;
        while (cur is not null && depth < MaxDepth && seen.Add(cur))
        {
            var title = f.Resolve(cur.Get("Title")) is PdfString t ? t.Text : "";
            var (page, uri) = ResolveTarget(doc, pages, cur.Get("Dest"), cur.Get("A"));
            var open = (f.Resolve(cur.Get("Count")).AsNumber() ?? 0) > 0;
            list.Add(new PdfOutlineItem(title, page, uri, open, Siblings(doc, pages, cur.Get("First"), depth + 1, seen)));
            cur = f.Resolve(cur.Get("Next")) as PdfDictionary;
        }
        return list;
    }

    static Dictionary<PdfDictionary, int> PageIndexMap(PdfDocument doc)
    {
        var m = new Dictionary<PdfDictionary, int>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < doc.Pages.Count; i++) m[doc.Pages[i].Dict] = i;
        return m;
    }

    static (int? Page, string? Uri) ResolveTarget(PdfDocument doc, Dictionary<PdfDictionary, int> pages, PdfObject? dest, PdfObject? action)
    {
        var f = doc.File;
        string? uri = null;
        if (f.Resolve(action) is PdfDictionary a)
        {
            var s = f.Resolve(a.Get("S")).AsName();
            if (s == "URI" && f.Resolve(a.Get("URI")) is PdfString us) uri = us.Text;
            else if (s == "GoTo") dest = a.Get("D");
        }
        return (DestToPage(doc, pages, dest, 0), uri);
    }

    static int? DestToPage(PdfDocument doc, Dictionary<PdfDictionary, int> pages, PdfObject? dest, int depth)
    {
        var f = doc.File;
        if (depth > 4) return null;
        var d = f.Resolve(dest);
        if (d is PdfString or PdfName)
        {
            var key = d is PdfName n ? n.Value : ((PdfString)d).Text;
            return DestToPage(doc, pages, LookupNamedDest(f, key), depth + 1);
        }
        if (d is PdfDictionary dd) return DestToPage(doc, pages, dd.Get("D"), depth + 1);
        if (d is PdfArray arr && arr.Count > 0)
        {
            if (f.Resolve(arr[0]) is PdfDictionary pd && pages.TryGetValue(pd, out var idx)) return idx;
            if (arr[0] is PdfInteger pi && pi.Value >= 0 && pi.Value < doc.Pages.Count) return (int)pi.Value; // remote-style page index
        }
        return null;
    }

    internal static PdfObject? LookupNamedDest(PdfFile f, string key)
    {
        if (f.Resolve(f.Catalog.Get("Dests")) is PdfDictionary old && old.Get(key) is { } v) return v;
        if (f.Resolve(f.Catalog.Get("Names")) is PdfDictionary names && f.Resolve(names.Get("Dests")) is PdfDictionary tree)
            return SearchNameTree(f, tree, key, 0, new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance));
        return null;
    }

    static PdfObject? SearchNameTree(PdfFile f, PdfDictionary node, string key, int depth, HashSet<PdfDictionary> seen)
    {
        if (depth > MaxDepth || !seen.Add(node)) return null;
        if (f.Resolve(node.Get("Names")) is PdfArray pairs)
        {
            for (var i = 0; i + 1 < pairs.Count; i += 2)
                if (f.Resolve(pairs[i]) is PdfString s && s.Text == key) return pairs[i + 1];
        }
        if (f.Resolve(node.Get("Kids")) is PdfArray kids)
            foreach (var k in kids.Items)
                if (f.Resolve(k) is PdfDictionary kd && SearchNameTree(f, kd, key, depth + 1, seen) is { } r) return r;
        return null;
    }

    public static IReadOnlyList<PdfAnnotation> GetAnnotations(this PdfPage page)
    {
        var doc = page.Document; var f = doc.File;
        if (f.Resolve(page.Dict.Get("Annots")) is not PdfArray arr) return [];
        var pages = PageIndexMap(doc);
        var list = new List<PdfAnnotation>(arr.Count);
        foreach (var o in arr.Items)
        {
            if (f.Resolve(o) is not PdfDictionary d) continue;
            var rect = default(PdfRect);
            if (f.Resolve(d.Get("Rect")) is PdfArray r && r.Count >= 4)
            {
                var v = new double[4];
                for (var i = 0; i < 4; i++) v[i] = f.Resolve(r[i]).AsNumber() ?? 0;
                rect = new PdfRect(Math.Min(v[0], v[2]), Math.Min(v[1], v[3]), Math.Max(v[0], v[2]), Math.Max(v[1], v[3]));
            }
            var (pg, uri) = ResolveTarget(doc, pages, d.Get("Dest"), d.Get("A"));
            list.Add(new PdfAnnotation(
                f.Resolve(d.Get("Subtype")).AsName() ?? "",
                rect,
                f.Resolve(d.Get("Contents")) is PdfString c ? c.Text : null,
                uri, pg, (int)(f.Resolve(d.Get("F")).AsNumber() ?? 0), d));
        }
        return list;
    }

    /// <summary>Flattened AcroForm fields with inherited FT/V/Ff and fully qualified names.</summary>
    public static IReadOnlyList<PdfFormField> GetFormFields(this PdfDocument doc)
    {
        var f = doc.File;
        if (f.Resolve(f.Catalog.Get("AcroForm")) is not PdfDictionary form || f.Resolve(form.Get("Fields")) is not PdfArray fields) return [];
        var list = new List<PdfFormField>();
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        foreach (var o in fields.Items) Field(f, o, "", null, null, 0, list, seen, 0);
        return list;
    }

    static void Field(PdfFile f, PdfObject? o, string prefix, string? ft, PdfObject? val, int ff, List<PdfFormField> acc, HashSet<PdfDictionary> seen, int depth)
    {
        if (depth > MaxDepth || f.Resolve(o) is not PdfDictionary d || !seen.Add(d)) return;
        var partial = f.Resolve(d.Get("T")) is PdfString t ? t.Text : null;
        var name = partial is null ? prefix : prefix.Length == 0 ? partial : prefix + "." + partial;
        ft = f.Resolve(d.Get("FT")).AsName() ?? ft;
        val = d.Get("V") ?? val;
        ff = d.Get("Ff") is { } fo ? (int)(f.Resolve(fo).AsNumber() ?? 0) : ff;
        if (f.Resolve(d.Get("Kids")) is PdfArray kids && kids.Items.Any(k => f.Resolve(k) is PdfDictionary kd && kd.Get("T") is not null))
        {
            foreach (var k in kids.Items) Field(f, k, name, ft, val, ff, acc, seen, depth + 1);
            return;
        }
        var rv = f.Resolve(val);
        acc.Add(new PdfFormField(name, ft, rv is PdfString s ? s.Text : rv is PdfName n ? n.Value : null, ff, d));
    }

    /// <summary>File specifications from the Names/EmbeddedFiles name tree.</summary>
    public static IReadOnlyList<PdfFileSpecification> GetFileSpecifications(this PdfDocument doc)
    {
        var f = doc.File;
        var list = new List<PdfFileSpecification>();
        if (f.Resolve(f.Catalog.Get("Names")) is PdfDictionary names && f.Resolve(names.Get("EmbeddedFiles")) is PdfDictionary tree)
            CollectFileSpecifications(f, tree, list, 0, new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance));
        return list;
    }

    /// <summary>Audits the Names/EmbeddedFiles name tree for ordering, duplicate keys, wrong or missing /Limits, cycles and malformed pairs
    /// (ISO 32000-2 7.9.6, Table 36). Lookups and <see cref="GetFileSpecifications"/> keep their tolerant behavior; null when there is no tree.</summary>
    public static PdfTreeDiagnostics? GetEmbeddedFilesNameTreeDiagnostics(this PdfDocument doc)
    {
        var f = doc.File;
        return f.Resolve(f.Catalog.Get("Names")) is PdfDictionary names && f.Resolve(names.Get("EmbeddedFiles")) is PdfDictionary tree
            ? PdfTreeDiagnostics.Audit(f, tree, numberTree: false)
            : null;
    }

    /// <summary>Associated file specifications on the catalog, pages, and annotations.</summary>
    public static IReadOnlyList<PdfAssociatedFile> GetAssociatedFiles(this PdfDocument doc)
    {
        var f = doc.File;
        var list = new List<PdfAssociatedFile>();
        AddAssociatedFiles(f, f.Catalog.Get("AF"), PdfAssociatedFileScope.Catalog, null, null, list);
        if (f.Resolve(f.Catalog.Get("AcroForm")) is PdfDictionary form)
        {
            AddAssociatedFiles(f, form.Get("AF"), PdfAssociatedFileScope.AcroForm, null, null, list);
            if (f.Resolve(form.Get("Fields")) is PdfArray fields)
            {
                var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
                foreach (var field in fields.Items)
                    CollectFieldAssociatedFiles(f, field, "", 0, seen, list);
            }
        }
        if (f.Resolve(f.Catalog.Get("StructTreeRoot")) is PdfDictionary structureRoot)
        {
            var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            var visited = 0;
            CollectStructureAssociatedFiles(f, structureRoot.Get("K"), 0, seen, ref visited, list);
        }
        for (var pageIndex = 0; pageIndex < doc.Pages.Count; pageIndex++)
        {
            var page = doc.Pages[pageIndex];
            AddAssociatedFiles(f, page.Dict.Get("AF"), PdfAssociatedFileScope.Page, pageIndex, null, list);
            var seenXObjects = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            var visitedXObjects = 0;
            CollectXObjectAssociatedFiles(f, page.Resources.Get("XObject"), pageIndex, "", 0,
                seenXObjects, ref visitedXObjects, list);
            if (f.Resolve(page.Dict.Get("Annots")) is not PdfArray annotations) continue;
            for (var annotationIndex = 0; annotationIndex < annotations.Count; annotationIndex++)
                if (f.Resolve(annotations[annotationIndex]) is PdfDictionary annotation)
                    AddAssociatedFiles(f, annotation.Get("AF"), PdfAssociatedFileScope.Annotation, pageIndex, annotationIndex, list);
        }
        return list;
    }

    /// <summary>Returns RichMedia asset file specifications without activating or executing the media.</summary>
    public static IReadOnlyList<PdfRichMediaAsset> GetRichMediaAssets(this PdfDocument doc)
    {
        var f = doc.File;
        var assets = new List<PdfRichMediaAsset>();
        for (var pageIndex = 0; pageIndex < doc.Pages.Count; pageIndex++)
        {
            if (f.Resolve(doc.Pages[pageIndex].Dict.Get("Annots")) is not PdfArray annotations) continue;
            for (var annotationIndex = 0; annotationIndex < annotations.Count; annotationIndex++)
            {
                if (f.Resolve(annotations[annotationIndex]) is not PdfDictionary annotation
                    || f.Resolve(annotation.Get("Subtype")).AsName() != "RichMedia"
                    || f.Resolve(annotation.Get("RichMediaContent")) is not PdfDictionary content
                    || f.Resolve(content.Get("Assets")) is not PdfDictionary tree)
                    continue;
                CollectRichMediaAssets(f, tree, pageIndex, annotationIndex, 0,
                    new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance), assets);
            }
        }
        return assets;
    }

    /// <summary>Returns RichMedia configurations and instances as inert PDF data; nothing is activated.</summary>
    public static IReadOnlyList<PdfRichMediaConfiguration> GetRichMediaConfigurations(this PdfDocument doc)
    {
        var f = doc.File;
        var configurations = new List<PdfRichMediaConfiguration>();
        for (var pageIndex = 0; pageIndex < doc.Pages.Count; pageIndex++)
        {
            if (f.Resolve(doc.Pages[pageIndex].Dict.Get("Annots")) is not PdfArray annotations) continue;
            for (var annotationIndex = 0; annotationIndex < annotations.Count; annotationIndex++)
            {
                if (f.Resolve(annotations[annotationIndex]) is not PdfDictionary annotation
                    || f.Resolve(annotation.Get("Subtype")).AsName() != "RichMedia"
                    || f.Resolve(annotation.Get("RichMediaContent")) is not PdfDictionary content
                    || f.Resolve(content.Get("Configurations")) is not PdfArray configs)
                    continue;
                foreach (var value in configs.Items)
                {
                    if (f.Resolve(value) is not PdfDictionary config) continue;
                    var instances = new List<PdfRichMediaInstance>();
                    if (f.Resolve(config.Get("Instances")) is PdfArray instanceArray)
                        foreach (var instanceValue in instanceArray.Items)
                            if (f.Resolve(instanceValue) is PdfDictionary instance)
                                instances.Add(new PdfRichMediaInstance(
                                    f.Resolve(instance.Get("Subtype")).AsName(),
                                    f.Resolve(instance.Get("Asset")),
                                    f.Resolve(instance.Get("Params")) as PdfDictionary,
                                    instance));
                    configurations.Add(new PdfRichMediaConfiguration(
                        pageIndex,
                        annotationIndex,
                        PdfStringValue(f, config.Get("Name")),
                        f.Resolve(config.Get("Subtype")).AsName(),
                        instances,
                        config));
                }
            }
        }
        return configurations;
    }

    /// <summary>Returns inert RichMedia activation settings; conditions are not acted upon.</summary>
    public static IReadOnlyList<PdfRichMediaSettings> GetRichMediaSettings(this PdfDocument doc)
    {
        var f = doc.File;
        var settings = new List<PdfRichMediaSettings>();
        for (var pageIndex = 0; pageIndex < doc.Pages.Count; pageIndex++)
        {
            if (f.Resolve(doc.Pages[pageIndex].Dict.Get("Annots")) is not PdfArray annotations) continue;
            for (var annotationIndex = 0; annotationIndex < annotations.Count; annotationIndex++)
            {
                if (f.Resolve(annotations[annotationIndex]) is not PdfDictionary annotation
                    || f.Resolve(annotation.Get("Subtype")).AsName() != "RichMedia"
                    || f.Resolve(annotation.Get("RichMediaSettings")) is not PdfDictionary dictionary)
                    continue;
                settings.Add(new PdfRichMediaSettings(
                    pageIndex,
                    annotationIndex,
                    f.Resolve(dictionary.Get("Activation")) as PdfDictionary,
                    f.Resolve(dictionary.Get("Deactivation")) as PdfDictionary,
                    dictionary)
                {
                    ActivationDetails = ReadRichMediaActivation(f, f.Resolve(dictionary.Get("Activation")) as PdfDictionary),
                    DeactivationDetails = ReadRichMediaDeactivation(f, f.Resolve(dictionary.Get("Deactivation")) as PdfDictionary),
                });
            }
        }
        return settings;
    }

    static PdfRichMediaActivation? ReadRichMediaActivation(PdfFile f, PdfDictionary? activation)
    {
        if (activation is null) return null;
        var animation = f.Resolve(activation.Get("Animation")) as PdfDictionary;
        var presentation = f.Resolve(activation.Get("Presentation")) as PdfDictionary;
        var condition = f.Resolve(activation.Get("Condition")).AsName();
        return new PdfRichMediaActivation(
            condition is "XA" or "PO" or "PV" ? condition : "XA",
            ReadRichMediaAnimation(f, animation),
            f.Resolve(activation.Get("View")),
            f.Resolve(activation.Get("Configuration")) as PdfDictionary,
            ReadRichMediaPresentation(f, presentation),
            activation);
    }

    static PdfRichMediaDeactivation? ReadRichMediaDeactivation(PdfFile f, PdfDictionary? deactivation)
    {
        if (deactivation is null) return null;
        var condition = f.Resolve(deactivation.Get("Condition")).AsName();
        return new PdfRichMediaDeactivation(
            condition is "XD" or "PC" or "PI" ? condition : "XD",
            deactivation);
    }

    static PdfRichMediaAnimation? ReadRichMediaAnimation(PdfFile f, PdfDictionary? animation)
    {
        if (animation is null) return null;
        var subtype = f.Resolve(animation.Get("Subtype")).AsName();
        var playCount = f.Resolve(animation.Get("PlayCount")) switch
        {
            PdfInteger integer => integer.Value,
            _ => -1,
        };
        var speed = f.Resolve(animation.Get("Speed")).AsNumber();
        return new PdfRichMediaAnimation(
            subtype is "None" or "Linear" or "Oscillating" ? subtype : "None",
            playCount,
            speed is > 0 ? speed.Value : 1,
            animation);
    }

    static PdfRichMediaPresentation? ReadRichMediaPresentation(PdfFile f, PdfDictionary? presentation)
    {
        if (presentation is null) return null;
        var style = f.Resolve(presentation.Get("Style")).AsName();
        var window = f.Resolve(presentation.Get("Window")) as PdfDictionary;
        return new PdfRichMediaPresentation(
            style is "Embedded" or "Windowed" ? style : "Embedded",
            f.Resolve(presentation.Get("Transparent")) is PdfBoolean transparent && transparent.Value,
            f.Resolve(presentation.Get("NavigationPane")) is PdfBoolean navigationPane && navigationPane.Value,
            f.Resolve(presentation.Get("Toolbar")) is PdfBoolean toolbar ? toolbar.Value : null,
            f.Resolve(presentation.Get("PassContextClick")) is PdfBoolean passContextClick && passContextClick.Value,
            window,
            presentation)
        {
            WindowDetails = ReadRichMediaWindow(f, window),
        };
    }

    static PdfRichMediaWindow? ReadRichMediaWindow(PdfFile f, PdfDictionary? window)
    {
        if (window is null) return null;
        var position = f.Resolve(window.Get("Position")) as PdfDictionary;
        return new PdfRichMediaWindow(
            ReadRichMediaDimension(f, window.Get("Width"), 288, 576, 72),
            ReadRichMediaDimension(f, window.Get("Height"), 216, 432, 72),
            position is null ? null : ReadRichMediaPosition(f, position),
            window);
    }

    static PdfRichMediaPosition ReadRichMediaPosition(PdfFile f, PdfDictionary position)
    {
        var horizontal = f.Resolve(position.Get("HAlign")).AsName();
        var vertical = f.Resolve(position.Get("VAlign")).AsName();
        return new PdfRichMediaPosition(
            horizontal is "Near" or "Center" or "Far" ? horizontal : "Far",
            vertical is "Near" or "Center" or "Far" ? vertical : "Near",
            f.Resolve(position.Get("HOffset")).AsNumber() ?? 18,
            f.Resolve(position.Get("VOffset")).AsNumber() ?? 18,
            position);
    }

    static PdfRichMediaDimension ReadRichMediaDimension(
        PdfFile f,
        PdfObject? value,
        double defaultValue,
        double max,
        double min)
    {
        var dictionary = f.Resolve(value) as PdfDictionary;
        return new PdfRichMediaDimension(
            f.Resolve(dictionary?.Get("Default")).AsNumber() ?? defaultValue,
            f.Resolve(dictionary?.Get("Max")).AsNumber() ?? max,
            f.Resolve(dictionary?.Get("Min")).AsNumber() ?? min,
            dictionary);
    }

    /// <summary>Returns 3D annotation references as inert PDF objects; model data is never decoded or activated.</summary>
    public static IReadOnlyList<Pdf3DAnnotation> Get3DAnnotations(this PdfDocument doc)
    {
        var f = doc.File;
        var annotations3D = new List<Pdf3DAnnotation>();
        var context = new ThreeDFeatures.ReadContext();
        for (var pageIndex = 0; pageIndex < doc.Pages.Count; pageIndex++)
        {
            if (f.Resolve(doc.Pages[pageIndex].Dict.Get("Annots")) is not PdfArray annotations) continue;
            for (var annotationIndex = 0; annotationIndex < annotations.Count; annotationIndex++)
            {
                if (f.Resolve(annotations[annotationIndex]) is not PdfDictionary annotation
                    || f.Resolve(annotation.Get("Subtype")).AsName() != "3D")
                    continue;
                var defaultView = f.Resolve(annotation.Get("3DV"));
                var stream = ThreeDFeatures.ReadStream(f, annotation.Get("3DD"), context);
                var details = Read3DView(f, defaultView, context);
                var selected = ThreeDFeatures.SelectView(f, defaultView ?? stream?.DefaultView ?? new PdfInteger(0), stream, context);
                annotations3D.Add(new Pdf3DAnnotation(
                    pageIndex,
                    annotationIndex,
                    f.Resolve(annotation.Get("3DD")),
                    defaultView,
                    f.Resolve(annotation.Get("3DA")) as PdfDictionary,
                    annotation)
                {
                    DefaultViewDetails = details,
                    StreamDetails = stream,
                    ActivationDetails = ThreeDFeatures.ReadActivation(f, annotation.Get("3DA")),
                    Interactive = f.Resolve(annotation.Get("3DI")) is not PdfBoolean { Value: false },
                    ViewBox = f.Resolve(annotation.Get("3DB")) as PdfArray,
                    ResolvedDefaultView = selected,
                    Truncated = context.Truncated || stream?.Truncated == true,
                });
            }
        }
        return annotations3D;
    }

    internal static Pdf3DView? Read3DView(PdfFile f, PdfObject? value, ThreeDFeatures.ReadContext context)
    {
        if (f.Resolve(value) is not PdfDictionary view) return null;
        if (context.Views.TryGetValue(view, out var cached)) return cached;
        if (!context.Take()) return null;
        var externalName = PdfStringValue(f, view.Get("XN"));
        var result = new Pdf3DView(
            externalName,
            PdfStringValue(f, view.Get("IN")) ?? externalName,
            f.Resolve(view.Get("MS")).AsName(),
            f.Resolve(view.Get("C2W")) as PdfArray,
            f.Resolve(view.Get("U3DPath")),
            f.Resolve(view.Get("CO")).AsNumber(),
            f.Resolve(view.Get("P")) as PdfDictionary,
            f.Resolve(view.Get("O")) as PdfStream,
            f.Resolve(view.Get("BG")) as PdfDictionary,
            f.Resolve(view.Get("RM")) as PdfDictionary,
            f.Resolve(view.Get("LS")) as PdfDictionary,
            f.Resolve(view.Get("MA")) as PdfArray,
            f.Resolve(view.Get("SA")) as PdfArray,
            f.Resolve(view.Get("NA")) as PdfArray,
            f.Resolve(view.Get("NR")) is PdfBoolean resetNodes ? resetNodes.Value : null,
            view)
        {
            ProjectionDetails = ThreeDFeatures.ReadProjection(f, view.Get("P")),
            BackgroundDetails = ThreeDFeatures.ReadBackground(f, view.Get("BG")),
            RenderModeDetails = ThreeDFeatures.ReadRenderMode(f, view.Get("RM")),
            LightingSchemeDetails = ThreeDFeatures.ReadLightingScheme(f, view.Get("LS")),
            MeasureDetails = ThreeDFeatures.ReadMeasures(f, view.Get("MA"), context),
            CrossSectionDetails = ThreeDFeatures.ReadCrossSections(f, view.Get("SA"), context),
            NodeDetails = ThreeDFeatures.ReadNodes(f, view.Get("NA"), context),
            Truncated = context.Truncated,
        };
        context.Views[view] = result;
        return result;
    }

    static void CollectRichMediaAssets(
        PdfFile f,
        PdfDictionary node,
        int pageIndex,
        int annotationIndex,
        int depth,
        HashSet<PdfDictionary> seen,
        List<PdfRichMediaAsset> assets)
    {
        if (depth > MaxDepth || !seen.Add(node)) return;
        if (f.Resolve(node.Get("Names")) is PdfArray pairs)
            for (var i = 0; i + 1 < pairs.Count; i += 2)
                if (f.Resolve(pairs[i]) is PdfString name && f.Resolve(pairs[i + 1]) is PdfDictionary spec)
                    assets.Add(new PdfRichMediaAsset(
                        pageIndex,
                        annotationIndex,
                        name.Text,
                        ReadFileSpecification(f, spec, name.Text)));
        if (f.Resolve(node.Get("Kids")) is PdfArray kids)
            foreach (var kid in kids.Items)
                if (f.Resolve(kid) is PdfDictionary child)
                    CollectRichMediaAssets(f, child, pageIndex, annotationIndex, depth + 1, seen, assets);
    }

    static void CollectXObjectAssociatedFiles(
        PdfFile f,
        PdfObject? xObjectResources,
        int pageIndex,
        string prefix,
        int depth,
        HashSet<PdfDictionary> seen,
        ref int visited,
        List<PdfAssociatedFile> acc)
    {
        if (depth > MaxDepth || f.Resolve(xObjectResources) is not PdfDictionary xObjects) return;
        foreach (var (name, value) in xObjects.Entries)
        {
            if (++visited > 10_000) return;
            var resolved = f.Resolve(value);
            var dictionary = resolved switch
            {
                PdfStream stream => stream.Dict,
                PdfDictionary direct => direct,
                _ => null,
            };
            if (dictionary is null || !seen.Add(dictionary)) continue;
            var resourceName = prefix.Length == 0 ? name : prefix + "/" + name;
            if (f.Resolve(dictionary.Get("AF")) is PdfArray files)
                foreach (var file in files.Items)
                    if (f.Resolve(file) is PdfDictionary spec)
                        acc.Add(new PdfAssociatedFile(
                            PdfAssociatedFileScope.XObject,
                            ReadFileSpecification(f, spec, FileName(f, spec) ?? ""),
                            pageIndex)
                        {
                            ResourceName = resourceName,
                        });
            if (resolved is PdfStream form
                && f.Resolve(form.Dict.Get("Subtype")).AsName() == "Form")
            {
                var resources = f.Resolve(form.Dict.Get("Resources")) as PdfDictionary;
                CollectXObjectAssociatedFiles(f, resources?.Get("XObject"), pageIndex, resourceName, depth + 1,
                    seen, ref visited, acc);
            }
        }
    }

    static void CollectStructureAssociatedFiles(
        PdfFile f,
        PdfObject? value,
        int depth,
        HashSet<PdfDictionary> seen,
        ref int visited,
        List<PdfAssociatedFile> acc)
    {
        if (depth > MaxDepth || ++visited > 10_000) return;
        var resolved = f.Resolve(value);
        if (resolved is PdfArray array)
        {
            foreach (var child in array.Items)
                CollectStructureAssociatedFiles(f, child, depth + 1, seen, ref visited, acc);
            return;
        }
        if (resolved is not PdfDictionary element || !seen.Add(element)) return;
        var type = f.Resolve(element.Get("Type")).AsName();
        var role = f.Resolve(element.Get("S")).AsName();
        if (type != "StructElem" && role is null) return;
        if (element.Get("AF") is { } associatedFiles && f.Resolve(associatedFiles) is PdfArray files)
        {
            foreach (var file in files.Items)
                if (f.Resolve(file) is PdfDictionary spec)
                    acc.Add(new PdfAssociatedFile(
                        PdfAssociatedFileScope.StructureElement,
                        ReadFileSpecification(f, spec, FileName(f, spec) ?? ""),
                        StructureRole: role));
        }
        if (element.Get("K") is { } kids)
            CollectStructureAssociatedFiles(f, kids, depth + 1, seen, ref visited, acc);
    }

    static void CollectFieldAssociatedFiles(
        PdfFile f,
        PdfObject? field,
        string prefix,
        int depth,
        HashSet<PdfDictionary> seen,
        List<PdfAssociatedFile> acc)
    {
        if (depth > MaxDepth || f.Resolve(field) is not PdfDictionary dict || !seen.Add(dict)) return;
        var partialName = PdfStringValue(f, dict.Get("T"));
        var name = partialName is null ? prefix : prefix.Length == 0 ? partialName : prefix + "." + partialName;
        if (dict.Get("AF") is { } associatedFiles && f.Resolve(associatedFiles) is PdfArray files)
        {
            foreach (var file in files.Items)
                if (f.Resolve(file) is PdfDictionary spec)
                    acc.Add(new PdfAssociatedFile(
                        PdfAssociatedFileScope.FormField,
                        ReadFileSpecification(f, spec, FileName(f, spec) ?? ""),
                        FieldName: name));
        }
        if (f.Resolve(dict.Get("Kids")) is PdfArray kids)
            foreach (var child in kids.Items)
                CollectFieldAssociatedFiles(f, child, name, depth + 1, seen, acc);
    }

    /// <summary>Embedded file streams from the Names/EmbeddedFiles tree, preferring /UF over /F.</summary>
    public static IReadOnlyList<PdfEmbeddedFile> GetEmbeddedFiles(this PdfDocument doc)
    {
        var list = new List<PdfEmbeddedFile>();
        foreach (var spec in doc.GetFileSpecifications())
            if (spec.EmbeddedFiles.TryGetValue("UF", out var embedded) || spec.EmbeddedFiles.TryGetValue("F", out embedded))
                list.Add(embedded with { Name = spec.Name });
        return list;
    }

    static void CollectFileSpecifications(PdfFile f, PdfDictionary node, List<PdfFileSpecification> acc, int depth, HashSet<PdfDictionary> seen)
    {
        if (depth > MaxDepth || !seen.Add(node)) return;
        if (f.Resolve(node.Get("Names")) is PdfArray pairs)
            for (var i = 0; i + 1 < pairs.Count; i += 2)
                if (f.Resolve(pairs[i]) is PdfString nm && f.Resolve(pairs[i + 1]) is PdfDictionary spec)
                    acc.Add(ReadFileSpecification(f, spec, nm.Text));
        if (f.Resolve(node.Get("Kids")) is PdfArray kids)
            foreach (var k in kids.Items)
                if (f.Resolve(k) is PdfDictionary kd)
                    CollectFileSpecifications(f, kd, acc, depth + 1, seen);
    }

    static void AddAssociatedFiles(
        PdfFile f,
        PdfObject? associatedFiles,
        PdfAssociatedFileScope scope,
        int? pageIndex,
        int? annotationIndex,
        List<PdfAssociatedFile> acc)
    {
        if (f.Resolve(associatedFiles) is not PdfArray files) return;
        foreach (var file in files.Items)
            if (f.Resolve(file) is PdfDictionary spec)
                acc.Add(new PdfAssociatedFile(scope, ReadFileSpecification(f, spec, FileName(f, spec) ?? ""), pageIndex, annotationIndex));
    }

    static PdfFileSpecification ReadFileSpecification(PdfFile f, PdfDictionary spec, string name)
    {
        var embedded = new Dictionary<string, PdfEmbeddedFile>(StringComparer.Ordinal);
        if (f.Resolve(spec.Get("EF")) is PdfDictionary ef)
        {
            var byStream = new Dictionary<PdfStream, PdfEmbeddedFile>(ReferenceEqualityComparer.Instance);
            foreach (var (platform, value) in ef.Entries)
            {
                if (f.Resolve(value) is not PdfStream stream) continue;
                if (!byStream.TryGetValue(stream, out var file))
                {
                    var parameters = f.Resolve(stream.Dict.Get("Params")) as PdfDictionary;
                    var data = f.DecodeStream(stream);
                    var declaredSize = parameters is null ? null : f.Resolve(parameters.Get("Size")).AsNumber();
                    var declaredChecksum = f.Resolve(parameters?.Get("CheckSum")) as PdfString;
                    file = new PdfEmbeddedFile(
                        name,
                        f.Resolve(stream.Dict.Get("Subtype")).AsName(),
                        declaredSize is null ? null : (long?)declaredSize,
                        data)
                    {
                        CreationDate = PdfStringValue(f, parameters?.Get("CreationDate")),
                        ModificationDate = PdfStringValue(f, parameters?.Get("ModDate")),
                        Checksum = declaredChecksum?.Bytes,
                        Parameters = parameters,
                        Stream = stream,
                        SizeMatches = declaredSize is null ? null : declaredSize == data.Length,
                        ChecksumMatches = declaredChecksum is null ? null
                            : declaredChecksum.Bytes.Length == 16 && System.Security.Cryptography.MD5.HashData(data).AsSpan().SequenceEqual(declaredChecksum.Bytes),
                    };
                    byStream.Add(stream, file);
                }
                embedded[platform] = file;
            }
        }

        var ids = f.Resolve(spec.Get("ID")) is PdfArray idArray
            ? idArray.Items.Select(value => PdfStringValue(f, value)).Where(value => value is not null).Select(value => value!).ToArray()
            : [];
        return new PdfFileSpecification(
            name,
            PdfStringValue(f, spec.Get("F")),
            PdfStringValue(f, spec.Get("UF")),
            PdfStringValue(f, spec.Get("DOS")),
            PdfStringValue(f, spec.Get("Mac")),
            PdfStringValue(f, spec.Get("Unix")),
            PdfStringValue(f, spec.Get("Desc")),
            ids,
            f.Resolve(spec.Get("V")) is PdfBoolean isVolatile ? isVolatile.Value : null,
            f.Resolve(spec.Get("AFRelationship")).AsName(),
            embedded,
            spec,
            f.Resolve(spec.Get("CI")) as PdfDictionary,
            ReadRelatedFiles(f, spec.Get("RF"), new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance) { spec }, 0))
        {
            FileSystem = f.Resolve(spec.Get("FS")).AsName(),
            Thumbnail = f.Resolve(spec.Get("Thumb")) as PdfStream,
            EncryptedPayload = f.Resolve(spec.Get("EP")) as PdfDictionary,
        };
    }

    static IReadOnlyList<PdfRelatedFile> ReadRelatedFiles(
        PdfFile f,
        PdfObject? value,
        HashSet<PdfDictionary> path,
        int depth)
    {
        if (depth >= MaxDepth || f.Resolve(value) is not PdfDictionary related) return [];
        var files = new List<PdfRelatedFile>();
        foreach (var (platform, relatedFiles) in related.Entries)
            if (f.Resolve(relatedFiles) is PdfArray array)
                foreach (var file in array.Items)
                    if (f.Resolve(file) is PdfDictionary spec)
                    {
                        var isNew = path.Add(spec);
                        var nested = isNew
                            ? ReadRelatedFiles(f, spec.Get("RF"), path, depth + 1)
                            : [];
                        if (isNew) path.Remove(spec);
                        files.Add(new PdfRelatedFile(platform, FileName(f, spec) ?? "", spec)
                        {
                            RelatedFiles = nested,
                        });
                    }
        return files;
    }

    static string? FileName(PdfFile f, PdfDictionary spec) =>
        PdfStringValue(f, spec.Get("UF")) ?? PdfStringValue(f, spec.Get("F"));

    static string? PdfStringValue(PdfFile f, PdfObject? value) =>
        f.Resolve(value) is PdfString text ? text.Text : null;

    /// <summary>Page label for a zero-based page index per the Catalog /PageLabels number tree.</summary>
    public static string GetPageLabel(this PdfDocument doc, int index)
    {
        var f = doc.File;
        var ranges = new SortedList<int, PdfDictionary>();
        if (f.Resolve(f.Catalog.Get("PageLabels")) is PdfDictionary tree) CollectNums(f, tree, ranges, 0);
        if (ranges.Count == 0) return (index + 1).ToString();
        var start = ranges.Keys.Where(k => k <= index).DefaultIfEmpty(-1).Max();
        if (start < 0) return (index + 1).ToString();
        var d = ranges[start];
        var style = f.Resolve(d.Get("S")).AsName();
        var prefix = f.Resolve(d.Get("P")) is PdfString ps ? ps.Text : "";
        var n = (int)(f.Resolve(d.Get("St")).AsNumber() ?? 1) + (index - start);
        return prefix + style switch
        {
            "D" => n.ToString(),
            "R" => Roman(n),
            "r" => Roman(n).ToLowerInvariant(),
            "A" => Alpha(n, 'A'),
            "a" => Alpha(n, 'a'),
            _ => "",
        };
    }

    static void CollectNums(PdfFile f, PdfDictionary node, SortedList<int, PdfDictionary> acc, int depth)
    {
        if (depth > MaxDepth) return;
        if (f.Resolve(node.Get("Nums")) is PdfArray nums)
            for (var i = 0; i + 1 < nums.Count; i += 2)
                if (f.Resolve(nums[i]).AsNumber() is { } k && f.Resolve(nums[i + 1]) is PdfDictionary v) acc[(int)k] = v;
        if (f.Resolve(node.Get("Kids")) is PdfArray kids)
            foreach (var k in kids.Items) if (f.Resolve(k) is PdfDictionary kd) CollectNums(f, kd, acc, depth + 1);
    }

    static string Roman(int n)
    {
        if (n <= 0 || n >= 4000) return n.ToString();
        ReadOnlySpan<int> v = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] s = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
        var sb = new StringBuilder();
        for (var i = 0; i < v.Length; i++) while (n >= v[i]) { sb.Append(s[i]); n -= v[i]; }
        return sb.ToString();
    }

    static string Alpha(int n, char a)
    {
        if (n <= 0) return n.ToString();
        var letter = (char)(a + (n - 1) % 26);
        return new string(letter, (n - 1) / 26 + 1);
    }
}
