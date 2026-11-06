using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using FreeDF.EditorWeb;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;

namespace FreeDF.Tests;

public class FormObjectHttpTests
{
    const string Font = "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>";
    const string Shape = "10 10 10 8 re f";
    const string Image = "q 20 0 0 20 25 25 cm /Im Do Q";
    const string FormContent = $"1 0 0 rg {Shape} {Image} 0 1 0 rg 40 10 10 8 re f BT /F1 10 Tf 10 50 Td (Form text) Tj ET";
    const string PageContent = "0 0 1 rg 140 30 10 10 re f BT /F1 12 Tf 100 150 Td (Keep) Tj ET q 2 0 0 2 30 100 cm /Fm Do Q";
    static readonly object Transform = new { sx = 1.5, sy = 1.25, tx = 4, ty = 7 };

    static byte[] Fixture(string variant = "single", string formContent = FormContent, string pageContent = PageContent)
    {
        var content = pageContent + (variant switch
        {
            "repeated" => " /Fm Do",
            "hidden" => " /OC /Hidden BDC /Fm Do EMC",
            _ => "",
        });
        var catalog = variant == "hidden"
            ? "/OCProperties << /OCGs [9 0 R] /D << /BaseState /ON /OFF [9 0 R] >> >>" : "";
        var resources = $"{Font} /XObject << /Fm 5 0 R >> /Properties << /Hidden 9 0 R >>";
        return ObjPdf.Build(
            $"<< /Type /Catalog /Pages 2 0 R {catalog} >>",
            "<< /Type /Pages /Kids [3 0 R 7 0 R] /Count 2 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R /Resources << {resources} >> >>",
            ObjPdf.Stream(content),
            ObjPdf.Stream(formContent, $"/Type /XObject /Subtype /Form /BBox [0 0 100 100] /Resources << {Font} /XObject << /Im 6 0 R >> >>"),
            ObjPdf.Stream("\xff\0\0", "/Type /XObject /Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceRGB"),
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 8 0 R /Resources << {(variant == "shared" ? resources : Font)} >> >>",
            ObjPdf.Stream(variant == "shared" ? "/Fm Do" : "BT /F1 12 Tf 20 40 Td (Second page) Tj ET"),
            "<< /Type /OCG /Name (Hidden invocation) >>");
    }

    [Theory]
    [InlineData("image")]
    [InlineData("path")]
    public async Task Http_child_transform_save_reopen_preserves_siblings_and_matches_expected_render(string kind)
    {
        var bytes = Fixture();
        await using var server = await EditorServer.Start(bytes);
        var objects = await server.Objects();
        var selected = Assert.Single(objects, o => o.GetProperty("inForm").GetBoolean() &&
            o.GetProperty("kind").GetString() == kind && o.GetProperty("x0").GetDouble() < 100);
        var index = selected.GetProperty("index").GetInt32();
        Assert.False((await server.State()).GetProperty("dirty").GetBoolean());
        var before = PdfDocument.Load(bytes);
        var beforeAnalysis = DocumentRenderer.Analyze(before.Pages[0]);

        await server.Put(index, Transform);
        Assert.True((await server.State()).GetProperty("dirty").GetBoolean());
        objects = await server.Objects();
        Assert.Equal(4, Assert.Single(objects, o => o.GetProperty("index").GetInt32() == index)
            .GetProperty("edit").GetProperty("tx").GetDouble());

        var target = kind == "image" ? Image : Shape;
        // Independently wrap the entire target in Form space, not the server's
        // selected operator range/current-CTM conjugation.
        var expected = PdfDocument.Load(Fixture(formContent: FormContent.Replace(target, $"q 1.5 0 0 1.25 9.5 -16 cm {target} Q")));
        var expectedPixels = DocumentRenderer.RenderPage(expected.Pages[0], 72);
        Assert.NotEqual(DocumentRenderer.RenderPage(before.Pages[0], 72).Rgba, expectedPixels.Rgba);
        Assert.Equal(expectedPixels.ToPng(), await server.Client.GetByteArrayAsync("/api/page/0.png?dpi=72&bare=true"));
        using (var response = await server.Client.PostAsync("/api/save", null))
        {
            response.EnsureSuccessStatusCode();
            Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("saved").GetBoolean());
        }

        Assert.False((await server.State()).GetProperty("dirty").GetBoolean());
        var saved = await File.ReadAllBytesAsync(server.InputPath);
        Assert.Equal(saved, await server.Client.GetByteArrayAsync("/api/download"));
        var reopened = PdfDocument.Load(saved);
        var after = DocumentRenderer.Analyze(reopened.Pages[0]);
        var selectedBefore = beforeAnalysis.Objects[index];
        var selectedAfter = after.Objects[index];
        Assert.Equal(1.5 * selectedBefore.X0 + 4, selectedAfter.X0, 4);
        Assert.Equal(1.25 * selectedBefore.Y0 + 7, selectedAfter.Y0, 4);
        Assert.Equal(1.5 * selectedBefore.X1 + 4, selectedAfter.X1, 4);
        Assert.Equal(1.25 * selectedBefore.Y1 + 7, selectedAfter.Y1, 4);
        Assert.Equal(beforeAnalysis.Objects.Where(o => o.Index != index).Select(Bounds),
            after.Objects.Where(o => o.Index != index).Select(Bounds));
        Assert.Equal(beforeAnalysis.Spans.Select(s => (s.Text, s.X, s.Y)),
            after.Spans.Select(s => (s.Text, s.X, s.Y)));
        Assert.Equal(before.Pages[0].GetContents(), reopened.Pages[0].GetContents());
        Assert.Equal(DocumentRenderer.RenderPage(before.Pages[1], 72).Rgba,
            DocumentRenderer.RenderPage(reopened.Pages[1], 72).Rgba);
        Assert.Equal(expectedPixels.Rgba, DocumentRenderer.RenderPage(reopened.Pages[0], 72).Rgba);
        await server.Open(saved);
        Assert.Equal(expectedPixels.ToPng(), await server.Client.GetByteArrayAsync("/api/page/0.png?dpi=72"));
        var reopenedChild = Assert.Single(await server.Objects(), o =>
            o.GetProperty("index").GetInt32() == index);
        Assert.Equal(JsonValueKind.Null, reopenedChild.GetProperty("edit").ValueKind);
        Assert.Equal(selectedAfter.X0, reopenedChild.GetProperty("x0").GetDouble(), 4);
    }

    [Fact]
    public async Task Selection_metadata_identifies_graphic_groups_and_their_children_and_text()
    {
        await using var server = await EditorServer.Start(Fixture());
        var objects = await server.Objects();
        var group = Assert.Single(objects, o => o.GetProperty("kind").GetString() == "form" && !o.GetProperty("inForm").GetBoolean());
        var reference = group.GetProperty("formRef").GetInt32();
        Assert.True(reference > 0);
        Assert.Equal(0, group.GetProperty("parentForm").GetInt32());
        var children = objects.Where(o => o.GetProperty("inForm").GetBoolean()).ToArray();
        Assert.NotEmpty(children);
        Assert.All(children, child => Assert.Equal(reference, child.GetProperty("parentForm").GetInt32()));
        using var text = JsonDocument.Parse(await server.Client.GetByteArrayAsync("/api/page/0/text"));
        var nestedText = Assert.Single(text.RootElement.EnumerateArray(), span => span.GetProperty("text").GetString() == "Form text");
        Assert.Equal(reference, nestedText.GetProperty("index").GetInt64() / 10_000_000);
    }

    static (string Kind, double X0, double Y0, double X1, double Y1) Bounds(ContentInterpreter.PageObject o) =>
        (o.Kind, o.X0, o.Y0, o.X1, o.Y1);

    [Fact]
    public void Nested_form_groups_and_their_children_remain_excluded()
    {
        var source = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R /Resources << /XObject << /Fm 5 0 R >> >> >>",
            ObjPdf.Stream("/Fm Do"),
            ObjPdf.Stream("/Inner Do", "/Type /XObject /Subtype /Form /BBox [0 0 100 100] /Resources << /XObject << /Inner 6 0 R >> >>"),
            ObjPdf.Stream("10 10 20 20 re f", "/Type /XObject /Subtype /Form /BBox [0 0 100 100] /Resources << >>")));
        var objects = DocumentRenderer.Analyze(source.Pages[0]).Objects;
        var ambiguous = FormObjectEditPolicy.FindAmbiguous(objects, FormObjectEditPolicy.FindSharedForms(source));
        Assert.Contains(objects, o => o.Form == 5 && o.Kind == "form");
        Assert.Contains(objects, o => o.Form == 6 && o.Kind == "path");
        Assert.All(objects.Where(o => o.Form != 0), o => Assert.False(FormObjectEditPolicy.IsEditable(o, ambiguous)));
        Assert.True(FormObjectEditPolicy.IsEditable(Assert.Single(objects, o => o.Form == 0), ambiguous));
    }

    [Theory]
    [InlineData("repeated")]
    [InlineData("shared")]
    [InlineData("hidden")]
    public async Task Http_shared_or_hidden_invocations_cannot_expose_or_write_children(string variant)
    {
        var bytes = Fixture(variant);
        var source = PdfDocument.Load(bytes);
        var analysis = DocumentRenderer.Analyze(source.Pages[0]);
        Assert.Contains(analysis.Objects, o => o.Form != 0);
        if (variant == "hidden")
            Assert.Equal(1, analysis.Objects.Count(o => o.Kind == "form" && o.Form == 0));
        await using var server = await EditorServer.Start(bytes);
        Assert.All(await server.Objects(), o => Assert.False(o.GetProperty("inForm").GetBoolean()));
        var childIndex = analysis.Objects.First(o => o.Form != 0).Index;
        var state = await server.State();
        var originalDownload = await server.Client.GetByteArrayAsync("/api/download");
        using (var response = await server.Client.PutAsJsonAsync($"/api/page/0/objects/{childIndex}", Transform))
            await AssertConflict(response, "hidden invocations");
        Assert.Equal(state.GetRawText(), (await server.State()).GetRawText());
        Assert.Equal(originalDownload, await server.Client.GetByteArrayAsync("/api/download"));

        var parent = Assert.Single(await server.Objects(), o => o.GetProperty("kind").GetString() == "form" &&
            o.GetProperty("index").GetInt32() == analysis.Objects.First(o => o.Kind == "form").Index);
        Assert.True(parent.GetProperty("formContentsShared").GetBoolean());
        await server.Put(parent.GetProperty("index").GetInt32(), Transform);
        var changed = PdfDocument.Load(await server.Client.GetByteArrayAsync("/api/download"));
        var after = DocumentRenderer.Analyze(changed.Pages[0]);
        Assert.Equal(1.5 * analysis.Objects.First(o => o.Form != 0).X0 + 4,
            after.Objects.First(o => o.Form != 0).X0, 4);
        Assert.Equal(DocumentRenderer.RenderPage(source.Pages[1], 72).Rgba,
            DocumentRenderer.RenderPage(changed.Pages[1], 72).Rgba);
        var expected = PdfDocument.Load(Fixture(variant, pageContent:
            PageContent.Replace("/Fm Do", "q 1.5 0 0 1.25 9.5 -16 cm /Fm Do Q")));
        Assert.Equal(DocumentRenderer.RenderPage(expected.Pages[0], 72).Rgba,
            DocumentRenderer.RenderPage(changed.Pages[0], 72).Rgba);
        var originalForm = Assert.IsType<PdfStream>(source.File.GetObject(5));
        var changedForm = Assert.IsType<PdfStream>(changed.File.GetObject(after.Objects.First(o => o.Form != 0).Form));
        Assert.Equal(source.File.DecodeStream(originalForm), changed.File.DecodeStream(changedForm));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Http_parent_child_combinations_are_rejected_without_mutating_the_document(bool childFirst)
    {
        await using var server = await EditorServer.Start(Fixture());
        var objects = await server.Objects();
        var child = objects.First(o => o.GetProperty("inForm").GetBoolean()).GetProperty("index").GetInt32();
        var parent = objects.First(o => o.GetProperty("kind").GetString() == "form").GetProperty("index").GetInt32();
        var first = childFirst ? child : parent;
        var second = childFirst ? parent : child;
        await server.Put(first, Transform);
        var state = await server.State();
        var download = await server.Client.GetByteArrayAsync("/api/download");
        using (var response = await server.Client.PutAsJsonAsync($"/api/page/0/objects/{second}", Transform))
            await AssertConflict(response, "save and reopen");
        Assert.Equal(state.GetRawText(), (await server.State()).GetRawText());
        Assert.Equal(download, await server.Client.GetByteArrayAsync("/api/download"));
        await server.Put(first, null);
        await server.Put(second, Transform);
    }

    [Fact]
    public async Task Http_page_identity_mutations_are_rejected_until_child_edits_are_reset()
    {
        var bytes = Fixture();
        await using var server = await EditorServer.Start(bytes);
        var child = (await server.Objects()).First(o => o.GetProperty("inForm").GetBoolean()).GetProperty("index").GetInt32();
        await server.Put(child, Transform);
        var state = await server.State();
        var download = await server.Client.GetByteArrayAsync("/api/download");
        foreach (var route in new[] { "/api/delete/0", "/api/move/0/1", "/api/rotate/0/90", "/api/blank/1", "/api/duplicate/0" })
        {
            using var response = await server.Client.PostAsync(route, null);
            await AssertConflict(response, "before changing pages");
            Assert.Equal(state.GetRawText(), (await server.State()).GetRawText());
            Assert.Equal(download, await server.Client.GetByteArrayAsync("/api/download"));
        }
        using (var upload = Upload(bytes))
        using (var response = await server.Client.PostAsync("/api/import", upload))
            await AssertConflict(response, "before changing pages");
        Assert.Equal(state.GetRawText(), (await server.State()).GetRawText());
        Assert.Equal(download, await server.Client.GetByteArrayAsync("/api/download"));
        await server.Put(child, null);
        using var duplicate = await server.Client.PostAsync("/api/duplicate/0", null);
        duplicate.EnsureSuccessStatusCode();
        Assert.Equal(3, (await server.State()).GetProperty("pageCount").GetInt32());
    }

    static async Task AssertConflict(HttpResponseMessage response, string reason)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(reason, await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    static MultipartFormDataContent Upload(byte[] bytes)
    {
        var upload = new MultipartFormDataContent();
        upload.Add(new ByteArrayContent(bytes), "file", "fixture.pdf");
        return upload;
    }

    sealed class EditorServer : IAsyncDisposable
    {
        readonly Process process;
        readonly string directory;
        readonly Task<string> output, errors;
        public HttpClient Client { get; }
        public string InputPath { get; }

        EditorServer(Process process, string directory, string inputPath, int port)
        {
            this.process = process; this.directory = directory; InputPath = inputPath;
            output = process.StandardOutput.ReadToEndAsync();
            errors = process.StandardError.ReadToEndAsync();
            Client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(10) };
        }

        public static async Task<EditorServer> Start(byte[] bytes)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "PortableDocument.slnx"))) root = root.Parent;
            Assert.NotNull(root);
            var project = Path.Combine(root.FullName, "tools", "FreeDF.EditorWeb");
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            var assembly = Path.Combine(project, "bin", configuration, "net10.0", "FreeDF.EditorWeb.dll");
            Assert.True(File.Exists(assembly), $"Editor build output missing: {assembly}");
            var directory = Path.Combine(Path.GetTempPath(), $"pdf-form-http-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var inputPath = Path.Combine(directory, "input.pdf");
            await File.WriteAllBytesAsync(inputPath, bytes);
            int port;
            using (var listener = new TcpListener(IPAddress.Loopback, 0))
            {
                listener.Start();
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = project, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var arg in new[] { assembly, inputPath, "--port", port.ToString() }) start.ArgumentList.Add(arg);
            var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start editor.");
            var server = new EditorServer(process, directory, inputPath, port);
            try
            {
                using var ready = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                while (true)
                {
                    if (process.HasExited)
                        throw new InvalidOperationException($"Editor exited: {await server.output}\n{await server.errors}");
                    try
                    {
                        using var response = await server.Client.GetAsync("/api/state", ready.Token);
                        response.EnsureSuccessStatusCode();
                        return server;
                    }
                    catch (HttpRequestException) when (!process.HasExited && !ready.IsCancellationRequested)
                    {
                        await Task.Delay(50, ready.Token);
                    }
                }
            }
            catch
            {
                await server.DisposeAsync();
                throw;
            }
        }

        public async Task<JsonElement[]> Objects() =>
            await Client.GetFromJsonAsync<JsonElement[]>("/api/page/0/objects") ?? throw new InvalidOperationException("No objects response.");
        public Task<JsonElement> State() => Client.GetFromJsonAsync<JsonElement>("/api/state");
        public async Task Put(int index, object? edit)
        {
            using var response = await Client.PutAsJsonAsync($"/api/page/0/objects/{index}", edit);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
        public async Task Open(byte[] bytes)
        {
            using var upload = Upload(bytes);
            using var response = await Client.PostAsync("/api/open", upload);
            response.EnsureSuccessStatusCode();
        }
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(output, errors);
            process.Dispose();
            File.Delete(InputPath);
            Directory.Delete(directory);
        }
    }
}
