using System.Text.Json;
using PortableDocument.Document;
using PortableDocument.Render;
using Pdf = PortableDocument.Render.PortableDocument;

public class EditorImageToolsTests
{
    static EditorSession.Reply Call(EditorSession session, string method, string path, object? body = null, int dpi = 72) =>
        session.Handle(method, path, new Dictionary<string, string> { ["dpi"] = dpi.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    static (EditorSession Session, Item Image, byte[] Source) Create()
    {
        var session = new EditorSession(new FontCatalog());
        Call(session, "POST", "/api/new");
        var rgba = new byte[4 * 4 * 4];
        for (var y = 0; y < 4; y++)
            for (var x = 0; x < 4; x++)
            {
                var at = (y * 4 + x) * 4;
                rgba[at] = (byte)(40 + x * 50); rgba[at + 1] = (byte)(40 + y * 50);
                rgba[at + 2] = 20; rgba[at + 3] = 255;
            }
        var png = PngWriter.Encode(4, 4, rgba);
        using var asset = JsonDocument.Parse(session.Handle("POST", "/api/asset", new Dictionary<string, string>(), png).Data);
        var image = new Item("image", "image", 20, 30, 80, 60, null, null, 0, false, false, null, null, null, 0, 1,
            asset.RootElement.GetProperty("id").GetString(), 0, 0);
        return (session, image, png);
    }

    public static IEnumerable<object[]> Transforms() =>
        from rotation in new[] { 0, 90, 180, 270 }
        from flipX in new[] { false, true }
        from flipY in new[] { false, true }
        select new object[] { rotation, flipX, flipY };

    [Theory]
    [MemberData(nameof(Transforms))]
    public void CropRotationAndFlipsKeepExactPixelsThroughPreviewSaveAndRecovery(int rotation, bool flipX, bool flipY)
    {
        var (session, image, source) = Create();
        image = image with { Crop = new(.25, .25, .5, .5), Rotation = rotation, FlipX = flipX, FlipY = flipY };
        Call(session, "PUT", "/api/page/0/items", new ItemUpdate([image], []));
        var png = Call(session, "GET", "/api/page/0.png").Data;
        var raster = PngWriter.Decode(png);
        for (var y = 0; y < 2; y++)
            for (var x = 0; x < 2; x++)
            {
                var (sx, sy) = rotation switch { 90 => (y, 1 - x), 180 => (1 - x, 1 - y), 270 => (1 - y, x), _ => (x, y) };
                if (flipX) sx = 1 - sx;
                if (flipY) sy = 1 - sy;
                var at = ((45 + y * 30) * raster.W + 40 + x * 40) * 4;
                Assert.Equal(new byte[] { (byte)(90 + sx * 50), (byte)(90 + sy * 50), 20, 255 }, raster.Rgba[at..(at + 4)]);
            }
        foreach (var (x, y) in new[] { (19, 50), (101, 50), (50, 29), (50, 91) })
        {
            var at = (y * raster.W + x) * 4;
            Assert.Equal(new byte[] { 255, 255, 255, 255 }, raster.Rgba[at..(at + 4)]);
        }
        Assert.Equal(source, Call(session, "GET", "/api/asset/" + image.Asset).Data);
        var saved = PdfDocument.Load(Call(session, "GET", "/api/export/pdf").Data);
        foreach (var dpi in new[] { 48, 150 })
            Assert.Equal(Pdf.RenderPage(saved.Pages[0], dpi).ToPng(), Call(session, "GET", "/api/page/0.png", dpi: dpi).Data);

        var recovered = new EditorSession(new FontCatalog());
        recovered.Handle("POST", "/api/recovery", new Dictionary<string, string>(), Call(session, "GET", "/api/recovery").Data);
        Assert.Equal(png, Call(recovered, "GET", "/api/page/0.png").Data);
        Assert.Equal(image, JsonSerializer.Deserialize(Call(recovered, "GET", "/api/page/0/items").Data, EditorSession.JsonType<List<Item>>())!.Single());
        Assert.Equal(source, Call(recovered, "GET", "/api/asset/" + image.Asset).Data);
    }

    [Theory]
    [InlineData(-.1, 0, .5, .5, 0)]
    [InlineData(0, -.1, .5, .5, 0)]
    [InlineData(0, 0, 0, .5, 0)]
    [InlineData(0, 0, .5, -.1, 0)]
    [InlineData(.8, 0, .5, .5, 0)]
    [InlineData(0, .8, .5, .5, 0)]
    [InlineData(0, 0, 1, 1, 45)]
    public void InvalidImageTransformsCannotReplaceValidItemsOrRecovery(double x, double y, double w, double h, int rotation)
    {
        var (session, image, _) = Create();
        Call(session, "PUT", "/api/page/0/items", new[] { image });
        var before = Call(session, "GET", "/api/page/0.png").Data;
        var invalid = image with { Crop = new(x, y, w, h), Rotation = rotation };
        Assert.Throws<ArgumentException>(() => Call(session, "PUT", "/api/page/0/items", new ItemUpdate([invalid], [])));
        Assert.Equal(before, Call(session, "GET", "/api/page/0.png").Data);
        var recovery = System.Text.Json.Nodes.JsonNode.Parse(Call(session, "GET", "/api/recovery").Data)!;
        recovery["pages"]![0]!["items"]![0] = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(invalid, EditorSession.JsonType<Item>()));
        Assert.Throws<ArgumentException>(() => session.Handle("POST", "/api/recovery", new Dictionary<string, string>(), System.Text.Encoding.UTF8.GetBytes(recovery.ToJsonString())));
        Assert.Equal(before, Call(session, "GET", "/api/page/0.png").Data);
    }

    [Fact]
    public void OlderImageJsonRetainsUncroppedUnrotatedDefaults()
    {
        var item = JsonSerializer.Deserialize("""{"id":"old","type":"image","x":0,"y":0,"w":10,"h":10}""", EditorSession.JsonType<Item>())!;
        Assert.Null(item.Crop);
        Assert.Equal(0, item.Rotation);
        Assert.False(item.FlipX);
        Assert.False(item.FlipY);
    }
}
