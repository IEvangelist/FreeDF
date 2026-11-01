using System.Text.Json;
using FreeDF.EditorWeb;
using PortableDocument.Document;

public class EditorJsonTests
{
    [Fact]
    public void GeneratedMetadataPreservesWebInputDefaults()
    {
        var edit = JsonSerializer.Deserialize("""{"SX":"2.5","TY":"10","DELETE":true}""", EditorSession.JsonType<ObjEdit>())!;
        Assert.Equal(2.5, edit.Sx);
        Assert.Equal(10, edit.Ty);
        Assert.True(edit.Delete);
        Assert.Equal(1, edit.Sy);

        var field = JsonSerializer.Deserialize("""{"NAME":"field","X":"12.5"}""", EditorSession.JsonType<FormFieldDto>())!;
        Assert.Equal("field", field.Name);
        Assert.Equal(12.5, field.X);
        Assert.Equal("text", field.Kind);
        Assert.Equal("Helv", field.FontName);
        Assert.Equal(1, field.BorderWidth);
    }

    [Fact]
    public void GeneratedMetadataRoundTripsNestedAnnotationPayloads()
    {
        var spec = new PdfAnnotationSpec
        {
            Subtype = "Stamp", Rect = [1, 2, 30, 40], InkList = [[1, 2, 3, 4]],
            Image = new(1, 1, [10, 20, 30], [255]),
            Attachment = new("note.txt", [65, 66], "text/plain"),
            Created = DateTimeOffset.Parse("2026-01-02T03:04:05+00:00", System.Globalization.CultureInfo.InvariantCulture),
        };
        var type = EditorSession.JsonType<PdfAnnotationSpec>();
        var json = JsonSerializer.SerializeToUtf8Bytes(spec, type);
        var restored = JsonSerializer.Deserialize(json, type)!;
        Assert.Equal(spec.Rect, restored.Rect);
        Assert.Equal(spec.InkList![0], restored.InkList![0]);
        Assert.Equal(spec.Image.Rgb, restored.Image!.Rgb);
        Assert.Equal(spec.Image.Alpha, restored.Image.Alpha);
        Assert.Equal(spec.Attachment.Data, restored.Attachment!.Data);
        Assert.Equal(spec.Created, restored.Created);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("author").ValueKind);
        Assert.Equal("ChQe", document.RootElement.GetProperty("image").GetProperty("rgb").GetString());
    }

    [Fact]
    public void EveryFormCommandReplyUsesRegisteredRuntimeMetadata()
    {
        object[] replies =
        [
            new EditorForms.WidgetReply("field", 2),
            new EditorForms.RemovedReply(3),
            new EditorForms.MovedReply(4),
            new EditorForms.PasteReply([new("copied", 0)]),
            new EditorForms.EmptyReply(),
            new EditorForms.FlattenReply(5, 6),
            new EditorForms.ImportReply(7, ["missing"], true),
        ];
        string[] expected =
        [
            """{"name":"field","widget":2}""",
            """{"removed":3}""",
            """{"moved":4}""",
            """{"created":[{"name":"copied","widget":0}]}""",
            "{}",
            """{"flattened":5,"kept":6}""",
            """{"applied":7,"skipped":["missing"],"truncated":true}""",
        ];
        var type = EditorSession.JsonType<object>();
        for (var i = 0; i < replies.Length; i++)
            Assert.Equal(expected[i], JsonSerializer.Serialize(replies[i], type));
    }

    sealed record UnregisteredPayload(string Value);

    [Fact]
    public void UnregisteredPayloadsCannotFallBackToReflection()
    {
        Assert.Throws<NotSupportedException>(() => EditorSession.JsonType<UnregisteredPayload>());
        Assert.Throws<NotSupportedException>(() =>
            JsonSerializer.Serialize<object>(new UnregisteredPayload("unknown"), EditorSession.JsonType<object>()));
    }
}
