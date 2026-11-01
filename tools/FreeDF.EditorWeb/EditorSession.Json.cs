using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FreeDF.EditorWeb;
using PortableDocument.Document;
using PortableDocument.Render;

public sealed partial class EditorSession
{
    internal static JsonTypeInfo<T> JsonType<T>() =>
        (JsonTypeInfo<T>)(EditorJsonContext.Default.GetTypeInfo(typeof(T))
            ?? throw new NotSupportedException($"No editor JSON metadata for {typeof(T)}."));

    sealed record EmptyReply;
    sealed record PageDetails(int Index, int Id, int Rotation, double X0, double Top, double Width, double Height);
    sealed record ProtectionDetails(bool Owner, string Method, bool PublicKey, bool Print, bool Copy, bool Modify,
        bool Annotate, bool FillForms, bool Assemble);
    sealed record StateReply(int Version, string FileName, int PageCount, IEnumerable<PageDetails> Pages, bool Dirty,
        bool CanSave, bool RecoverySupported, int PageUndo, int PageRedo, ProtectionDetails? Protection);
    sealed record FieldDetails(string Name, string? Type, string Value);
    sealed record SelectionReply(IEnumerable<TextDetails> Spans, IEnumerable<ObjectDetails> Objects);
    sealed record PageHistoryReply(string Label, int Undo, int Redo);
    sealed record RecoveryReply(Dictionary<int, int> OriginalForms);
    sealed record ImportReply(int First, int Count);
    sealed record BatchImportReply(int First, int Count, int Files);
    sealed record InspectReply(int PageCount, bool Encrypted);
    sealed record AssetReply(string Id, int W, int H);
    sealed record SaveUnavailableReply(bool Saved);
    sealed record SaveReply(bool Saved, string Path);
    sealed record MissingFontsReply(IEnumerable<string> Missing);
    sealed record ReplaceFontReply(int Changed, IEnumerable<string> Missing);
    sealed record AnnotationTextReply(List<AnnotationTools.TextRun> Runs, double[] Quads, string Text);
    sealed record AnnotationNumberReply(int Number);
    sealed record AnnotationRemovedReply(int Removed);
    sealed record AnnotationReplaceReply(int StrikeOut, int Caret);
    sealed record AnnotationCreatedReply(int Created);
    sealed record AnnotationFlattenedReply(int Flattened);
    sealed record AnnotationAppliedReply(int Applied);
    sealed record FindHit(int Page, double X, double Y, double W, double Size, double Angle);

    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(object))]
    [JsonSerializable(typeof(EmptyReply))]
    [JsonSerializable(typeof(StateReply))]
    [JsonSerializable(typeof(IEnumerable<FieldDetails>))]
    [JsonSerializable(typeof(IEnumerable<FontFamilyDto>))]
    [JsonSerializable(typeof(IReadOnlyList<FormFieldDto>))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    [JsonSerializable(typeof(List<int>))]
    [JsonSerializable(typeof(int[]))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(string[]))]
    [JsonSerializable(typeof(List<Item>))]
    [JsonSerializable(typeof(ItemUpdate))]
    [JsonSerializable(typeof(ObjEdit))]
    [JsonSerializable(typeof(List<WidgetDetails>))]
    [JsonSerializable(typeof(IEnumerable<TextDetails>))]
    [JsonSerializable(typeof(IEnumerable<ObjectDetails>))]
    [JsonSerializable(typeof(SelectionReply))]
    [JsonSerializable(typeof(RecoveryData))]
    [JsonSerializable(typeof(PageHistoryReply))]
    [JsonSerializable(typeof(RecoveryReply))]
    [JsonSerializable(typeof(ImportReply))]
    [JsonSerializable(typeof(BatchImportReply))]
    [JsonSerializable(typeof(InspectReply))]
    [JsonSerializable(typeof(AssetReply))]
    [JsonSerializable(typeof(SaveUnavailableReply))]
    [JsonSerializable(typeof(SaveReply))]
    [JsonSerializable(typeof(MissingFontsReply))]
    [JsonSerializable(typeof(ReplaceFontReply))]
    [JsonSerializable(typeof(FontRegistration))]
    [JsonSerializable(typeof(RenderMetrics))]
    [JsonSerializable(typeof(IEnumerable<PdfAnnotationInfo>))]
    [JsonSerializable(typeof(PdfAnnotationSpec))]
    [JsonSerializable(typeof(AnnotationTextReply))]
    [JsonSerializable(typeof(AnnotationReplyRequest))]
    [JsonSerializable(typeof(AnnotationReplaceRequest))]
    [JsonSerializable(typeof(AnnotationNumberReply))]
    [JsonSerializable(typeof(AnnotationRemovedReply))]
    [JsonSerializable(typeof(AnnotationReplaceReply))]
    [JsonSerializable(typeof(AnnotationCreatedReply))]
    [JsonSerializable(typeof(AnnotationFlattenedReply))]
    [JsonSerializable(typeof(AnnotationAppliedReply))]
    [JsonSerializable(typeof(List<FindHit>))]
    [JsonSerializable(typeof(FormFieldDto))]
    [JsonSerializable(typeof(FormNamesDto))]
    [JsonSerializable(typeof(List<FormMoveDto>))]
    [JsonSerializable(typeof(FormPasteDto))]
    [JsonSerializable(typeof(EditorForms.WidgetReply))]
    [JsonSerializable(typeof(EditorForms.RemovedReply))]
    [JsonSerializable(typeof(EditorForms.MovedReply))]
    [JsonSerializable(typeof(EditorForms.PasteReply))]
    [JsonSerializable(typeof(EditorForms.EmptyReply), TypeInfoPropertyName = "FormEmptyReply")]
    [JsonSerializable(typeof(EditorForms.FlattenReply))]
    [JsonSerializable(typeof(EditorForms.ImportReply), TypeInfoPropertyName = "FormImportReply")]
    sealed partial class EditorJsonContext : JsonSerializerContext;
}
