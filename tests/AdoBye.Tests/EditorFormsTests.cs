using System.Text;
using System.Text.Json;
using AdoBye.EditorWeb;

public class EditorFormsTests
{
    static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    static EditorSession.Reply Call(EditorSession session, string method, string path, object? body = null, params (string, string)[] query)
        => session.Handle(method, path, query.ToDictionary(x => x.Item1, x => x.Item2), body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body, Web));

    static JsonElement Json(EditorSession.Reply r) => JsonDocument.Parse(r.Data).RootElement.Clone();
    static JsonElement Forms(EditorSession s, int page = -1) => Json(Call(s, "GET", "/api/forms", query: [("page", page.ToString())]));
    static byte[] Png(EditorSession s) => Call(s, "GET", "/api/page/0.png", query: [("dpi", "72")]).Data;

    static EditorSession NewSession()
    {
        var s = new EditorSession(new FontCatalog());
        Call(s, "POST", "/api/new");
        return s;
    }

    static FormFieldDto Text(string name, double x = 40, double y = 40, string? value = null) =>
        new("text", name, 0, x, y, 160, 24, Value: value, BorderColor: "#000000", BackgroundColor: "#eeeeee");

    [Fact]
    public void Add_update_remove_and_history_work_through_the_session()
    {
        var s = NewSession();
        var blank = Png(s);
        var added = Json(Call(s, "POST", "/api/form/add", Text("", value: "Hello")));
        Assert.Equal("Text1", added.GetProperty("name").GetString());
        Assert.NotEqual(blank, Png(s));

        var list = Forms(s, 0);
        Assert.Equal(1, list.GetArrayLength());
        var f = list[0];
        Assert.Equal("text", f.GetProperty("kind").GetString());
        Assert.Equal(40, f.GetProperty("x").GetDouble(), 3);
        Assert.Equal(40, f.GetProperty("y").GetDouble(), 3);
        Assert.Equal("#eeeeee", f.GetProperty("backgroundColor").GetString());

        Call(s, "POST", "/api/form/update", Text("Renamed", value: "Changed") with { Tooltip = "tip", Required = true }, ("n", "Text1"), ("i", "0"));
        Assert.Equal("Renamed", Forms(s)[0].GetProperty("name").GetString());
        Assert.True(Forms(s)[0].GetProperty("required").GetBoolean());

        Call(s, "POST", "/api/form/move", new[] { new FormMoveDto("Renamed", 0, 100, 120, 160, 24) });
        Assert.Equal(120, Forms(s)[0].GetProperty("y").GetDouble(), 3);

        Call(s, "POST", "/api/form/remove", new FormNamesDto(["Renamed"]));
        Assert.Equal(0, Forms(s).GetArrayLength());
        Assert.Equal(blank, Png(s));

        for (var i = 0; i < 4; i++) Call(s, "POST", "/api/page-undo");
        Assert.Equal(0, Forms(s).GetArrayLength());
        Assert.Equal(blank, Png(s));
        Call(s, "POST", "/api/page-redo");
        Assert.Equal(1, Forms(s).GetArrayLength());
        for (var i = 0; i < 3; i++) Call(s, "POST", "/api/page-redo");
        Assert.Equal(0, Forms(s).GetArrayLength());
    }

    [Fact]
    public void Duplicate_names_are_made_unique_and_failures_roll_back()
    {
        var s = NewSession();
        Call(s, "POST", "/api/form/add", Text("a"));
        Call(s, "POST", "/api/form/add", Text("b", 40, 100));
        var undo = Json(Call(s, "GET", "/api/state")).GetProperty("pageUndo").GetInt32();
        Assert.ThrowsAny<ArgumentException>(() => Call(s, "POST", "/api/form/move", new[] { new FormMoveDto("a", 0, 10, 10, 50, 20), new FormMoveDto("missing", 0, 1, 1, 5, 5) }));
        Assert.Equal(40, Forms(s).EnumerateArray().First(f => f.GetProperty("name").GetString() == "a").GetProperty("x").GetDouble(), 3);
        Assert.Equal(undo, Json(Call(s, "GET", "/api/state")).GetProperty("pageUndo").GetInt32());

        var paste = Json(Call(s, "POST", "/api/form/paste", new FormPasteDto(0, 20, 20, [Text("a"), Text("b", 40, 100)])));
        Assert.Equal(["a2", "b2"], paste.GetProperty("created").EnumerateArray().Select(c => c.GetProperty("name").GetString()!).ToArray());
        Assert.Equal(4, Forms(s).GetArrayLength());
        Assert.ThrowsAny<ArgumentException>(() => Call(s, "POST", "/api/form/update", Text("b"), ("n", "a"), ("i", "0")));
    }

    [Fact]
    public void Flatten_export_import_and_recovery_roundtrip()
    {
        var s = NewSession();
        Call(s, "POST", "/api/form/add", Text("name", value: "Ada"));
        Call(s, "POST", "/api/form/add", new FormFieldDto("checkbox", "ok", 0, 40, 100, 20, 20, Value: "Yes", BorderColor: "#000000"));
        var withFields = Png(s);

        var xfdf = Call(s, "GET", "/api/form/export", query: [("format", "xfdf")]);
        Assert.Equal("form-data.xfdf", xfdf.FileName);
        Assert.Contains("<value>Ada</value>", Encoding.UTF8.GetString(xfdf.Data));

        var recovery = Call(s, "GET", "/api/recovery");
        var restored = new EditorSession(new FontCatalog());
        Call(restored, "POST", "/api/recovery", Json(recovery));
        Assert.Equal(2, Forms(restored).GetArrayLength());
        Assert.Equal(withFields, Png(restored));

        var target = NewSession();
        Call(target, "POST", "/api/form/add", Text("name"));
        var result = Json(target.Handle("POST", "/api/form/import", new Dictionary<string, string>(), xfdf.Data));
        Assert.Equal(1, result.GetProperty("applied").GetInt32());
        Assert.Equal(1, result.GetProperty("skipped").GetArrayLength());
        Assert.Equal("Ada", Forms(target)[0].GetProperty("value").GetString());

        var flat = Json(Call(s, "POST", "/api/form/flatten", query: [("scope", "all")]));
        Assert.Equal(2, flat.GetProperty("flattened").GetInt32());
        Assert.Equal(0, Forms(s).GetArrayLength());
        Assert.Equal(withFields, Png(s));
        Call(s, "POST", "/api/page-undo");
        Assert.Equal(2, Forms(s).GetArrayLength());
    }

    [Fact]
    public void Form_edits_keep_text_items_and_page_images_consistent()
    {
        var s = NewSession();
        var item = new Item("t", "text", 20, 200, 0, 0, "Hello", "Helvetica", 18, false, false, "#000000", null, null, 0, 1, null, 0, 0);
        Call(s, "PUT", "/api/page/0/items", new ItemUpdate([item], []));
        var before = Png(s);
        Call(s, "POST", "/api/form/add", Text("a"));
        Assert.NotEqual(before, Png(s));
        Assert.Equal(1, Json(Call(s, "GET", "/api/page/0/items")).GetArrayLength());
        Call(s, "POST", "/api/form/flatten");
        Assert.Equal(1, Json(Call(s, "GET", "/api/page/0/items")).GetArrayLength());
    }
}
