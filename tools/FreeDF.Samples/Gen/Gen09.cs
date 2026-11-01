using FreeDF.Samples.Kit;
using static FreeDF.Samples.Kit.Theme;

namespace FreeDF.Samples.Gen;

static class Gen09
{
    public const string Out = "09-images.pdf";

    public static void Build(string path)
    {
        var d = new Doc("Images", subject: "DCT, Flate+predictor, grayscale, indexed, image masks, colour-key, SMask, Decode, inline images, CMYK JPEG", lang: "en-US");
        var t = new Faces(d);
        var pg = d.Page(A4.W, A4.H);
        Background(pg, P["paper"]);
        PageHeader(pg, t, "Image gallery", "Images  /  01", 1, 1);
        pg.Text(t.Sans, 11, M, pg.H - 106, "Twelve image XObjects (plus one inline image) exercising filters, colour spaces and masking.", color: P["slate"]);

        int Image(int w, int h, string cs, byte[] data, string extra = "", int bpc = 8, string filt = "/FlateDecode") =>
            d.Stream($"/Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace {cs} /BitsPerComponent {bpc} {extra} /Filter {filt}", data, compress: false);

        int FlateRgb(Rgb8 im, string extra = "") => Image(im.W, im.H, "/DeviceRGB", Raster.Zlib(im.Pixels), extra);

        int Stencil(int w, int h, byte[] bits) =>
            d.Stream($"/Type /XObject /Subtype /Image /Width {w} /Height {h} /ImageMask true /BitsPerComponent 1 /Filter /FlateDecode", Raster.Zlib(bits), compress: false);

        const int W = 300, H = 200;
        var land = Rgb8.Landscape(360, 240);
        var plas = Rgb8.Plasma(W, H, 7);
        var objs = new List<(string Title, string Sub, int Ref)>
        {
            ("DCTDecode", "baseline JPEG, passed through", Image(360, 240, "/DeviceRGB", Jpeg.Encode(land, 80), filt: "/DCTDecode")),
            ("FlateDecode RGB", "8-bit RGB, zlib", FlateRgb(plas)),
            ("Flate + PNG predictor", "/Predictor 15 (Up filter)",
                Image(W, H, "/DeviceRGB", Raster.Zlib(Raster.PngPredictUp(plas.Pixels, W, H, 3)), $"/DecodeParms << /Predictor 15 /Colors 3 /BitsPerComponent 8 /Columns {W} >>")),
            ("DeviceGray", "8-bit, 1 component", Image(W, H, "/DeviceGray", Raster.Zlib(plas.ToGray()))),
        };

        var (indices, palette) = Raster.Quantize(plas, 16);
        objs.Add(("Indexed, 4 bpc", "16-colour palette, packed nibbles",
            Image(W, H, $"[/Indexed /DeviceRGB 15 <{Convert.ToHexString(Raster.PaletteBytes(palette, 16)).ToLowerInvariant()}>]", Raster.Zlib(Raster.PackNibbles(indices, W, H)), bpc: 4)));

        var sm = new BitCanvas(200, 200);
        sm.FillEllipse(10, 10, 190, 190, false);
        sm.FillEllipse(60, 60, 140, 140, true);
        for (var k = 0; k < 8; k++)
        {
            var a = k * Math.PI / 4;
            sm.Line(100 + 30 * Math.Cos(a), 100 + 30 * Math.Sin(a), 100 + 70 * Math.Cos(a), 100 + 70 * Math.Sin(a), 7, true);
        }
        objs.Add(("Stencil ImageMask", "1-bit, painted in the fill colour", Stencil(200, 200, sm.PackBits())));

        var cm = new BitCanvas(W, H);
        cm.FillRoundedRect(20, 20, W - 20, H - 20, 60, false);
        var emask = Stencil(W, H, cm.PackBits(invert: true));
        objs.Add(("Explicit /Mask", "1-bit stencil clips a photograph", FlateRgb(plas, $"/Mask {emask} 0 R")));

        var ck = new Rgb8(W, H, (0, 255, 0));
        (int, int, int)[] ckCols = [(79, 70, 229), (14, 165, 233), (245, 158, 11), (225, 29, 72), (16, 185, 129), (124, 58, 237)];
        for (var i = 0; i < 6; i++) ck.FillEllipse(20 + i * 40, 40 + i % 2 * 40, 100 + i * 40, 120 + i % 2 * 40, ckCols[i]);
        objs.Add(("Colour-key mask", "/Mask [0 0 255 255 0 0] removes pure green", FlateRgb(ck, "/Mask [0 0 255 255 0 0]")));

        var al = new byte[W * H];
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var v = Math.Pow(Math.Clamp(1 - Math.Sqrt(Math.Pow((x - W / 2.0) / (W / 2.0), 2) + Math.Pow((y - H / 2.0) / (H / 2.0), 2)), 0, 1), 0.7);
                al[y * W + x] = (byte)(int)(v * 255);
            }
        var smask = Image(W, H, "/DeviceGray", Raster.Zlib(al));
        objs.Add(("SMask", "8-bit soft alpha channel", FlateRgb(plas, $"/SMask {smask} 0 R")));

        objs.Add(("Decode [1 0 1 0 1 0]", "inverts every component", FlateRgb(land.Resize(W, H), "/Decode [1 0 1 0 1 0]")));

        var px = new byte[16 * 16 * 3];
        for (var j = 0; j < 16; j++)
            for (var i = 0; i < 16; i++)
            {
                int[] c = [(int)(79 + i / 15.0 * 150), (int)(70 + j / 15.0 * 120), 229 - (int)((i + j) / 30.0 * 200)];
                var alt = (i / 4 + j / 4) % 2 != 0;
                for (var k = 0; k < 3; k++) px[(j * 16 + i) * 3 + k] = (byte)(alt ? c[k] / 2 + 100 : c[k]);
            }
        var small = new Rgb8(16, 16, px);
        objs.Add(("Interpolate true", "16 x 16 px, smoothed on upscale", FlateRgb(small, "/Interpolate true")));

        objs.Add(("CMYK JPEG", "DeviceCMYK + /Decode for Adobe inversion",
            Image(W, H, "/DeviceCMYK", Jpeg.EncodeCmyk(plas.ToCmyk(), W, H, 85), "/Decode [1 0 1 0 1 0 1 0]", filt: "/DCTDecode")));

        const int cols = 3;
        const double gap = 16, ih = 100;
        var tw = (pg.W - 2 * M - 2 * gap) / 3;
        var top = pg.H - 132;
        for (var i = 0; i < objs.Count; i++)
        {
            var (title, sub, rf) = objs[i];
            int c = i % cols, r = i / cols;
            double x = M + c * (tw + gap), y = top - ih - r * (ih + 44);
            Box(pg, x, y, tw, ih, fill: P["white"], stroke: P["mist"], r: 6);
            pg.Save();
            if (title == "Stencil ImageMask")
            {
                pg.Fill(P["rose"]); pg.Cm(ih - 12, 0, 0, ih - 12, x + (tw - ih + 12) / 2, y + 6); pg.XObj(rf);
            }
            else
            {
                const double pad = 6;
                pg.Cm(tw - 2 * pad, 0, 0, ih - 2 * pad, x + pad, y + pad); pg.XObj(rf);
            }
            pg.Restore();
            pg.Text(t.SansB, 8.5, x, y - 13, title, color: P["ink"]);
            pg.Text(t.Sans, 7.5, x, y - 23, sub, color: P["muted"], kern: false);
        }

        var iy = top - 4 * (ih + 44) - 6;
        pg.Text(t.SansB, 8.5, M, iy + 14, "Inline image (BI / ID / EI)", color: P["ink"]);
        var hex = Convert.ToHexString(px);
        pg.Save(); pg.Cm(64, 0, 0, 64, M, iy - 54);
        pg.Raw($"BI /W 16 /H 16 /CS /RGB /BPC 8 /F /AHx ID\n{hex}>\nEI");
        pg.Restore();
        pg.Save(); pg.Cm(64, 0, 0, 64, M + 80, iy - 54);
        pg.Raw($"BI /W 16 /H 16 /CS /RGB /BPC 8 /I true /F /AHx ID\n{hex}>\nEI");
        pg.Restore();
        pg.Text(t.Sans, 7.5, M, iy - 66, "nearest-neighbour", color: P["muted"], kern: false);
        pg.Text(t.Sans, 7.5, M + 80, iy - 66, "/I true", color: P["muted"], kern: false);
        d.Save(path);
    }
}
