using Microsoft.Playwright;
using PortableDocument.Document;
using PortableDocument.Render;
using Pdf = PortableDocument.Render.PortableDocument;

static class EditorImageTools
{
    const string Settled = "!document.getElementById('view').inert && document.getElementById('base')?.naturalWidth > 0 && document.getElementById('base').dataset.loadedUrl === document.getElementById('base').dataset.targetUrl";
    static void Check(bool ok, string message) { if (!ok) throw new Exception("Image tools: " + message); }
    static Task Saved(IPage page, string predicate) => Until(page, "async () => { const items = await (await pdfHost.request('/api/page/0/items')).json(); return items.length === 1 && (" + predicate + "); }");
    static async Task Until(IPage page, string predicate)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!await page.EvaluateAsync<bool>(predicate))
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(60))
            {
                var geometry = await page.Locator("#layer > .it").EvaluateAllAsync<string>("els => JSON.stringify(els.map(el => ({ x: el._it.x, y: el._it.y, w: el._it.w, h: el._it.h, crop: el._it.crop })))");
                throw new TimeoutException("Image geometry was not persisted: " + predicate + "; visible geometry: " + geometry);
            }
            await Task.Delay(50);
        }
    }
    static Task<string> Raster(IPage page) => page.EvaluateAsync<string>("async () => { const r = await pdfHost.request('/api/page/0.png?dpi=72'); return btoa(String.fromCharCode(...new Uint8Array(await r.arrayBuffer()))); }");

    static async Task CropDrag(IPage page, string edge, float dx, float dy)
    {
        var handle = (await page.Locator($"[data-crop-handle={edge}]").BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(handle.X + handle.Width / 2, handle.Y + handle.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(handle.X + handle.Width / 2 + dx, handle.Y + handle.Height / 2 + dy, new() { Steps = 4 });
        await page.Mouse.UpAsync();
    }

    static async Task CropCancellation(IPage page)
    {
        await page.Locator("[data-image=crop]").ClickAsync();
        var original = (await page.Locator(".cropping").BoundingBoxAsync())!;
        await CropDrag(page, "e", -original.Width / 4, 0);
        var cropped = (await page.Locator(".cropping").BoundingBoxAsync())!;
        Check(cropped.Width < original.Width * .8f && Math.Abs(cropped.Height - original.Height) < .02,
            "a rotated/flipped image crops at the dragged visual edge without changing the other axis");
        await CropDrag(page, "e", original.Width / 4, 0);
        Check(Math.Abs((await page.Locator(".cropping").BoundingBoxAsync())!.Width - original.Width) < .05,
            "dragging outward restores hidden pixels without scaling");
        await page.Keyboard.PressAsync("Escape");
        Check(await page.Locator(".cropping").CountAsync() == 0, "Escape cancels the draft");
        await page.Locator("[data-image=crop]").ClickAsync();
        await page.Locator("[data-crop-handle=nw]").HoverAsync();
        var handle = (await page.Locator("[data-crop-handle=nw]").BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(handle.X + handle.Width / 2, handle.Y + handle.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(handle.X + handle.Width / 2 + 15, handle.Y + handle.Height / 2 + 15);
        await page.Locator("[data-crop-handle=nw]").DispatchEventAsync("pointercancel", new { pointerId = 1 });
        await page.Mouse.UpAsync();
        Check(Math.Abs((await page.Locator(".cropping").BoundingBoxAsync())!.Width - original.Width) < .05, "pointer cancellation rolls back the incomplete gesture");
        await page.Keyboard.PressAsync("Escape");
    }

    static async Task TouchCrop(IPage page, string? artifacts)
    {
        await page.Locator("#layer > .it").DblClickAsync();
        Check(await page.Locator(".cropping").IsVisibleAsync(), "crop is directly accessible on mobile");
        Check(await page.Locator(".crop-toolbar").EvaluateAsync<bool>("el => { const r = el.getBoundingClientRect(); return r.left >= 0 && r.right <= innerWidth && r.top >= 0 && r.bottom <= innerHeight; }"), "mobile crop controls fit the viewport");
        var session = await page.Context.NewCDPSessionAsync(page);
        Task Touch(string type, double x = 0, double y = 0) => session.SendAsync("Input.dispatchTouchEvent",
            new Dictionary<string, object> { ["type"] = type, ["touchPoints"] = type == "touchEnd" ? Array.Empty<object>() : new object[] { new { x, y } } });
        await page.WaitForFunctionAsync(Settled);
        await page.Locator("[data-crop-handle=nw]").HoverAsync();
        var handle = (await page.Locator("[data-crop-handle=nw]").BoundingBoxAsync())!;
        var x = handle.X + handle.Width / 2; var y = handle.Y + handle.Height / 2;
        Check(await page.Locator("[data-crop-handle=nw]").EvaluateAsync<bool>("el => { const r = el.getBoundingClientRect(); return document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2) === el; }"), "the mobile crop handle is not obscured by the toolbar");
        await Touch("touchStart", x, y);
        for (var step = 1; step <= 4; step++)
        {
            await page.EvaluateAsync("() => new Promise(requestAnimationFrame)");
            await Touch("touchMove", x + step * 5, y + step * 5);
        }
        await page.EvaluateAsync("() => new Promise(requestAnimationFrame)");
        await Touch("touchEnd");
        Check(await page.Locator(".cropping").EvaluateAsync<bool>("el => el.offsetWidth < el.querySelector('.crop-original').offsetWidth * .95 && el.offsetHeight < el.querySelector('.crop-original').offsetHeight * .95"), "native touch dragging actually crops both axes rather than only changing zoom or scroll");
        if (artifacts is not null)
            await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, "on-image-crop-mobile.png"), Animations = ScreenshotAnimations.Disabled });
        await page.Locator("[data-crop-action=apply]").TapAsync();
        Check(await page.Locator(".cropping").CountAsync() == 0, "native touch Apply confirms the crop");
        await Saved(page, "items[0].crop?.w < .999 && items[0].crop?.h < .999");
        await session.DetachAsync();
        await page.Keyboard.PressAsync("Control+z");
        await Saved(page, "items[0].crop?.w === 1 && items[0].crop?.h === 1");
    }

    static async Task Chrome(IPage page)
    {
        foreach (var theme in new[] { "light", "dark" })
        {
            await page.EvaluateAsync("theme => document.documentElement.dataset.theme = theme", theme);
            Check(await page.Locator("#tl, #tr, #props [data-image][aria-pressed=true]").EvaluateAllAsync<bool>(
                "buttons => buttons.length === 4 && buttons.every(b => getComputedStyle(b).boxShadow === 'none')"), "flat selected panels and flips in " + theme);
        }
        foreach (var id in new[] { "layout", "lock", "prev" })
        {
            var control = page.Locator("#" + id);
            Check(await control.EvaluateAsync<bool>("el => !!el.querySelector('svg') && !el.textContent.trim() && !!el.getAttribute('aria-label')"), id + " has an accessible icon");
            var title = await control.GetAttributeAsync("title");
            if (id == "layout") await control.EvaluateAsync("el => el.setAttribute('aria-describedby', 'pages')");
            var before = (await control.BoundingBoxAsync())!;
            await control.HoverAsync();
            var label = page.GetByRole(AriaRole.Tooltip);
            await label.WaitForAsync();
            Check(await label.EvaluateAsync<bool>("el => el.classList.contains('reveal')"), id + " uses the shared slide-out label");
            Check(await label.TextContentAsync() == title, id + " reveals its purpose");
            var labelId = await label.GetAttributeAsync("id");
            Check((await control.GetAttributeAsync("aria-describedby"))!.Split(' ').Contains(labelId), id + " is associated with its label");
            Check(await label.EvaluateAsync<bool>("el => { const r = el.getBoundingClientRect(); return r.left >= 0 && r.right <= innerWidth && r.top >= 0 && r.bottom <= innerHeight; }"), id + " label stays in the viewport");
            var after = (await control.BoundingBoxAsync())!;
            Check(before.Width == before.Height && before.X == after.X && before.Y == after.Y && before.Width == after.Width && before.Height == after.Height, "hover does not shift the square " + id + " control");
            await page.Keyboard.PressAsync("Escape");
            await label.WaitForAsync(new() { State = WaitForSelectorState.Detached });
            Check(await control.GetAttributeAsync("aria-describedby") == (id == "layout" ? "pages" : null) && await control.GetAttributeAsync("title") == title, "dismissal restores " + id + " and preserves existing descriptions");
            if (id == "layout") await control.EvaluateAsync("el => el.removeAttribute('aria-describedby')");
        }
        await page.EvaluateAsync("document.documentElement.dataset.motion = 'reduce'");
        await page.EmulateMediaAsync(new() { ForcedColors = ForcedColors.Active });
        var forcedColors = await page.Locator("#tl, #tr, #props [data-image][aria-pressed=true]").EvaluateAllAsync<string>(
            "buttons => JSON.stringify(buttons.map(b => { const s = getComputedStyle(b); return { id: b.id || b.dataset.image, background: s.backgroundColor, outline: s.outlineColor, color: s.color }; }))");
        Check(await page.Locator("#tl, #tr, #props [data-image][aria-pressed=true]").EvaluateAllAsync<bool>(
            "buttons => buttons.every(b => { const s = getComputedStyle(b); return s.backgroundColor === s.outlineColor && s.color !== s.backgroundColor; })"), "high-contrast mode retains system selection colors: " + forcedColors);
        await page.EmulateMediaAsync(new() { ForcedColors = ForcedColors.None });
        await page.Keyboard.PressAsync("Tab");
        await page.Locator("#layout").FocusAsync();
        await page.GetByRole(AriaRole.Tooltip).WaitForAsync();
        Check(await page.GetByRole(AriaRole.Tooltip).EvaluateAsync<bool>("el => getComputedStyle(el).animationName === 'none'"), "keyboard labels respect reduced motion");
        Check(await page.Locator("#layout").EvaluateAsync<bool>("el => el.matches(':focus-visible') && getComputedStyle(el).outlineStyle !== 'none'"), "keyboard focus keeps a visible outline");
        await page.Keyboard.PressAsync("Escape");
        await page.EvaluateAsync("delete document.documentElement.dataset.motion");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await page.Locator("#lock").FocusAsync();
        await page.GetByRole(AriaRole.Tooltip).WaitForAsync();
        Check(await page.GetByRole(AriaRole.Tooltip).EvaluateAsync<bool>("el => getComputedStyle(el).animationName === 'none'"), "labels respect OS reduced motion");
        await page.Keyboard.PressAsync("Escape");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await page.Mouse.MoveAsync(0, 0);
    }

    public static async Task Run(IPage page)
    {
        await page.EvaluateAsync("document.documentElement.dataset.theme = 'dark'");
        await page.Keyboard.PressAsync("Alt+n");
        await page.GetByRole(AriaRole.Dialog, new() { Name = "New document", Exact = true })
            .GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync(Settled);
        var rgba = new byte[40 * 40 * 4];
        for (var y = 0; y < 40; y++) for (var x = 0; x < 40; x++)
        {
            var at = (y * 40 + x) * 4;
            rgba[at] = (byte)(40 + x / 10 * 50); rgba[at + 1] = (byte)(40 + y / 10 * 50); rgba[at + 2] = 20; rgba[at + 3] = 255;
        }
        await page.Locator("#img").SetInputFilesAsync(new FilePayload { Name = "crop-fixture.png", MimeType = "image/png", Buffer = PngWriter.Encode(40, 40, rgba) });
        await page.WaitForSelectorAsync("[data-image=crop]");
        foreach (var (key, value) in new[] { ("w", "160"), ("h", "120"), ("x", "48"), ("y", "72") })
            await page.Locator($"[data-k={key}]").FillAsync(value);
        await Saved(page, "items[0].w === 160 && items[0].h === 120 && items[0].x === 48 && items[0].y === 72");
        var uncroppedRaster = await Raster(page);
        await page.Locator("[data-image=crop]").ClickAsync();
        Check(await page.Locator(".mdl").CountAsync() == 0 && await page.Locator("[data-crop-handle]").CountAsync() == 8, "crop uses on-image edge and corner handles, not a dialog");
        var source = (await page.Locator(".cropping").BoundingBoxAsync())!;
        await CropDrag(page, "nw", source.Width / 4, source.Height / 4);
        var croppedWidth = (await page.Locator(".cropping").BoundingBoxAsync())!.Width;
        Check(croppedWidth < source.Width * .8f, "dragging the crop handle visibly clips the image without scaling it");
        await page.Locator("[data-crop-handle=nw]").EvaluateAsync("el => el.dataset.cropRetention = 'true'");
        await page.Locator("#zi").EvaluateAsync("el => el.click()");
        await page.WaitForFunctionAsync(Settled);
        Check(await page.Locator("[data-crop-retention=true]").CountAsync() == 1 &&
            await page.Locator("[data-crop-retention=true]").EvaluateAsync<bool>("el => document.activeElement === el"),
            "zoom repaint retains the active crop handle and keyboard focus");
        await page.Locator("#zo").EvaluateAsync("el => el.click()");
        await page.WaitForFunctionAsync(Settled);
        Check(Math.Abs((await page.Locator(".cropping").BoundingBoxAsync())!.Width - croppedWidth) < .05, "zoom preserves the crop draft");
        var artifacts = Environment.GetEnvironmentVariable("EDITOR_SMOKE_ARTIFACTS");
        if (artifacts is not null)
        {
            Directory.CreateDirectory(artifacts);
            await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, "on-image-crop-dark.png"), Animations = ScreenshotAnimations.Disabled });
        }
        await Saved(page, "!items[0].crop && items[0].w === 160");
        await page.Locator("[data-crop-action=cancel]").ClickAsync();
        Check(await Raster(page) == uncroppedRaster, "cancel leaves the saved image unchanged");
        await page.Locator("[data-image=crop]").ClickAsync();
        await CropDrag(page, "nw", source.Width / 4, source.Height / 4);
        await CropDrag(page, "se", -source.Width / 4, -source.Height / 4);
        await page.Locator("[data-crop-action=apply]").ClickAsync();
        await Saved(page, "Math.abs(items[0].crop?.w - .5) < .001 && Math.abs(items[0].w - 80) < .05 && Math.abs(items[0].h - 60) < .05");
        Check(await Raster(page) != uncroppedRaster, "Apply actually changes the PDF renderer output");
        await page.Keyboard.PressAsync("Control+z");
        await Saved(page, "!items[0].crop && items[0].w === 160");
        Check(await Raster(page) == uncroppedRaster, "one undo restores the entire applied crop");
        await page.Locator("#layer > .it").ClickAsync();
        await page.Locator("#layer > .it").DblClickAsync();
        await page.Locator("[data-crop-handle=nw]").FocusAsync();
        for (var i = 0; i < 4; i++) await page.Keyboard.PressAsync("Shift+ArrowRight");
        for (var i = 0; i < 3; i++) await page.Keyboard.PressAsync("Shift+ArrowDown");
        await page.Locator("[data-crop-handle=se]").FocusAsync();
        for (var i = 0; i < 4; i++) await page.Keyboard.PressAsync("Shift+ArrowLeft");
        for (var i = 0; i < 3; i++) await page.Keyboard.PressAsync("Shift+ArrowUp");
        var keyboardCrop = (await page.Locator(".cropping").BoundingBoxAsync())!;
        Check(Math.Abs(keyboardCrop.Width / source.Width - .5) < .001 && Math.Abs(keyboardCrop.Height / source.Height - .5) < .001,
            $"keyboard crop changes the visible bounds: {keyboardCrop.Width} x {keyboardCrop.Height}; original {source.Width} x {source.Height}");
        await page.Keyboard.PressAsync("Enter");
        await Saved(page, "Math.abs(items[0].crop?.w - .5) < 1e-8 && Math.abs(items[0].w - 80) < 1e-8 && Math.abs(items[0].h - 60) < 1e-8");
        await page.Locator("[data-image=right]").ClickAsync();
        await page.Locator("[data-image=horizontal]").ClickAsync();
        await page.Locator("[data-image=vertical]").ClickAsync();
        await Saved(page, "items[0].rotation === 90 && items[0].flipX && items[0].flipY && Math.abs(items[0].w - 60) < 1e-8 && Math.abs(items[0].h - 80) < 1e-8");
        Console.WriteLine("Image tools: on-image pointer/keyboard crop, cancel, undo and quarter-turn/flips persisted.");
        await CropCancellation(page);
        await Chrome(page);

        var imageBox = (await page.Locator(".it.on .image-viewport").BoundingBoxAsync())!;
        var editPixels = PngWriter.Decode(await page.ScreenshotAsync(new() { Clip = new() { X = imageBox.X, Y = imageBox.Y, Width = imageBox.Width, Height = imageBox.Height } }));
        await page.Locator("#prev").ClickAsync();
        await page.WaitForFunctionAsync(Settled);
        Check(await page.Locator("#prev").GetAttributeAsync("aria-pressed") == "true", "Preview exposes its selected state");
        Check(await page.Locator("#prev").EvaluateAsync<bool>("el => getComputedStyle(el).boxShadow === 'none'"), "Preview uses the flat selection treatment");
        var previewPixels = PngWriter.Decode(await page.ScreenshotAsync(new() { Clip = new() { X = imageBox.X, Y = imageBox.Y, Width = imageBox.Width, Height = imageBox.Height } }));
        foreach (var fy in new[] { .25, .75 }) foreach (var fx in new[] { .25, .75 })
        {
            var at = ((int)(fy * editPixels.H) * editPixels.W + (int)(fx * editPixels.W)) * 4;
            for (var c = 0; c < 3; c++) Check(Math.Abs(editPixels.Rgba[at + c] - previewPixels.Rgba[at + c]) <= 4, "editing and PDF preview agree on transformed pixels");
        }
        Console.WriteLine("Image tools: browser overlay and PDF preview pixels match.");
        var cleanRaster = await Raster(page);
        await page.Locator("#prev").ClickAsync();
        await page.WaitForFunctionAsync(Settled);
        await page.Locator("#layout").ClickAsync();
        var layout = page.GetByRole(AriaRole.Dialog, new() { Name = "Grid and snapping", Exact = true });
        await layout.Locator("[data-grid=visible]").CheckAsync();
        await layout.Locator("[data-grid=snap]").CheckAsync();
        await layout.Locator("[data-spacing]").FillAsync("12");
        await layout.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ClickAsync();
        await layout.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        Check(await page.Locator("#layout").EvaluateAsync<bool>("el => getComputedStyle(el).boxShadow === 'none'"), "Grid uses the flat selection treatment");
        Check(await page.Locator("#layer").EvaluateAsync<bool>("el => el.classList.contains('page-grid')"), "grid is visible");
        Check(await Raster(page) == cleanRaster, "grid is excluded from rendered output");
        var bounds = (await page.Locator(".it.on").BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(bounds.X + bounds.Width / 2 + 29, bounds.Y + bounds.Height / 2 + 19, new() { Steps = 4 });
        await page.Mouse.UpAsync();
        await Saved(page, "Math.abs(items[0].x / 12 - Math.round(items[0].x / 12)) < .00001 && Math.abs(items[0].y / 12 - Math.round(items[0].y / 12)) < .00001");
        bounds = (await page.Locator(".it.on").BoundingBoxAsync())!;
        var fixedY = await page.Locator("[data-k=y]").InputValueAsync();
        await page.Keyboard.DownAsync("Shift");
        await page.Mouse.MoveAsync(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(bounds.X + bounds.Width / 2 + 29, bounds.Y + bounds.Height / 2 + 3, new() { Steps = 4 });
        await page.Mouse.UpAsync();
        await page.Keyboard.UpAsync("Shift");
        await Saved(page, $"items[0].y === {fixedY}");
        bounds = (await page.Locator(".it.on").BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        await page.Mouse.DownAsync();
        await page.Keyboard.DownAsync("Control");
        await page.Mouse.MoveAsync(bounds.X + bounds.Width / 2 + 7, bounds.Y + bounds.Height / 2 + 5, new() { Steps = 3 });
        await page.Mouse.UpAsync();
        await page.Keyboard.UpAsync("Control");
        await Saved(page, "Math.abs(items[0].x / 12 - Math.round(items[0].x / 12)) > .001");
        var center = await page.Locator(".it.on").EvaluateAsync<double[]>("el => [el._it.x + el._it.w / 2, el._it.y + el._it.h / 2, el._it.w / el._it.h]");
        var corner = (await page.Locator(".it.on .hd").Last.BoundingBoxAsync())!;
        await page.Keyboard.DownAsync("Alt");
        await page.Mouse.MoveAsync(corner.X + corner.Width / 2, corner.Y + corner.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(corner.X + corner.Width / 2 + 23, corner.Y + corner.Height / 2 + 17, new() { Steps = 4 });
        await page.Mouse.UpAsync();
        await page.Keyboard.UpAsync("Alt");
        var resized = await page.Locator(".it.on").EvaluateAsync<double[]>("el => [el._it.x + el._it.w / 2, el._it.y + el._it.h / 2, el._it.w / el._it.h, el._it.w]");
        Check(center.Zip(resized).Take(3).All(pair => Math.Abs(pair.First - pair.Second) < .00001), "Alt resize retains the center and aspect ratio");
        await Saved(page, $"Math.abs(items[0].w - {resized[3].ToString(System.Globalization.CultureInfo.InvariantCulture)}) < .00001");
        var shots = Environment.GetEnvironmentVariable("EDITOR_SMOKE_ARTIFACTS");
        if (shots is not null)
        {
            Directory.CreateDirectory(shots);
            await page.ScreenshotAsync(new() { Path = Path.Combine(shots, "image-tools-dark.png"), Animations = ScreenshotAnimations.Disabled });
            await page.Locator("#layout").HoverAsync();
            await page.GetByRole(AriaRole.Tooltip).WaitForAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine(shots, "toolbar-label-dark.png"), Animations = ScreenshotAnimations.Disabled });
            await page.Mouse.MoveAsync(0, 0);
            await page.EvaluateAsync("document.documentElement.dataset.theme = 'light'");
            await page.ScreenshotAsync(new() { Path = Path.Combine(shots, "image-tools-light.png"), Animations = ScreenshotAnimations.Disabled });
        }
        var before = await Raster(page);
        var pdf = await page.EvaluateAsync<byte[]>("async () => Array.from(new Uint8Array(await (await pdfHost.request('/api/export/pdf')).arrayBuffer()))");
        var savedRaster = Pdf.RenderPage(PdfDocument.Load(pdf).Pages[0], 72);
        var previewRaster = PngWriter.Decode(Convert.FromBase64String(before));
        Check(savedRaster.Width == previewRaster.W && savedRaster.Height == previewRaster.H && savedRaster.Rgba.SequenceEqual(previewRaster.Rgba), "saved PDF retains exact transformed pixels");
        await page.Locator("[data-image=crop]").ClickAsync();
        await page.Locator("[data-crop-action=restore]").ClickAsync();
        await page.Locator("[data-crop-action=apply]").ClickAsync();
        await Saved(page, "items[0].crop?.w === 1 && items[0].crop?.h === 1 && items[0].rotation === 90");
        await page.Locator("#lock").ClickAsync();
        Check(await page.Locator("#lock").EvaluateAsync<bool>("el => getComputedStyle(el).boxShadow === 'none'"), "Lock uses the flat selection treatment");
        Check(await page.Locator("[data-image=crop]").IsDisabledAsync(), "geometry lock disables image edits");
        await page.Locator("#lock").ClickAsync();
        await page.SetViewportSizeAsync(390, 844);
        Check(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"), "new controls do not introduce mobile page overflow");
        await TouchCrop(page, shots);
        await page.SetViewportSizeAsync(1440, 1000);
        await page.WaitForFunctionAsync(Settled);
        await page.Locator("#formBtn").ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Add field", Exact = true }).HoverAsync();
        await page.Locator(".cm.sub .cm-i").Filter(new() { HasTextRegex = new System.Text.RegularExpressions.Regex("^Text field$") }).ClickAsync();
        var layer = (await page.Locator("#layer").BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(layer.X + 31, layer.Y + 270);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(layer.X + 180, layer.Y + 311, new() { Steps = 4 });
        await page.Mouse.UpAsync();
        await page.WaitForSelectorAsync(".ffbox.on");
        await Until(page, "async () => { const f = (await (await pdfHost.request('/api/forms?page=0')).json())[0]; return f && [f.x, f.y, f.w, f.h].every(v => Math.abs(v / 12 - Math.round(v / 12)) < .00001); }");
        var field = (await page.Locator(".ffbox.on").BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(field.X + field.Width / 2, field.Y + field.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(field.X + field.Width / 2 + 29, field.Y + field.Height / 2 + 17, new() { Steps = 4 });
        await page.Mouse.UpAsync();
        await Until(page, "async () => { const f = (await (await pdfHost.request('/api/forms?page=0')).json())[0], x = +document.querySelector('[data-ff=x]').value; return Math.abs(f.x - x) < .01 && [f.x, f.y].every(v => Math.abs(v / 12 - Math.round(v / 12)) < .00001); }");
        corner = (await page.Locator(".ffbox.on .ffh").Last.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(corner.X + corner.Width / 2, corner.Y + corner.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(corner.X + 21, corner.Y + 16, new() { Steps = 4 });
        await page.Mouse.UpAsync();
        await Until(page, "async () => { const f = (await (await pdfHost.request('/api/forms?page=0')).json())[0], w = +document.querySelector('[data-ff=w]').value; return Math.abs(f.w - w) < .01 && [f.w, f.h].every(v => Math.abs(v / 12 - Math.round(v / 12)) < .00001); }");
        Console.WriteLine("PASS: on-image crop handles, pointer/keyboard/apply/cancel/undo/restore, rotation/flips, editor/PDF pixel parity, grid snapping with Shift/Ctrl/Alt, form placement/move/resize, print/export exclusion, geometry lock and responsive controls.");
    }
}
