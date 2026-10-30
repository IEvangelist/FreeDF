using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AdoBye.Samples.Kit;
using static AdoBye.Samples.Kit.Helpers;
using static AdoBye.Samples.Kit.Theme;

namespace AdoBye.Samples.Gen;

static class Gen14
{
    public const string Out = "14-embedded-files.pdf";

    static readonly (string Name, string Mime, string Desc, string Rel, string Text)[] Files =
    [
        ("readings.csv", "text/csv", "Sensor readings, five columns", "Data",
            "time,temp_c,humidity,pressure,status\n" + string.Concat(Enumerable.Range(0, 24).Select(i =>
                string.Create(CultureInfo.InvariantCulture, $"{i * 15:D2}:00,{(18 + i * 7 % 9 + i * 0.3):F1},{40 + i * 11 % 30},{1008 + i * 5 % 14},{(i % 5 != 0 ? "ok" : "warn")}\n")))),
        ("notes.md", "text/markdown", "Authoring notes in Markdown", "Supplement",
            "# Notes\n\nThis attachment travels inside the PDF.\n\n- Stored in /EmbeddedFiles\n- Referenced from /AF with /AFRelationship\n- Compressed with FlateDecode\n"),
        ("mark.svg", "image/svg+xml", "Vector logo mark", "Source",
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\"><rect x=\"10\" y=\"10\" width=\"60\" height=\"60\" rx=\"14\" fill=\"#4F46E5\"/><rect x=\"30\" y=\"30\" width=\"60\" height=\"60\" rx=\"14\" fill=\"#7C3AED\" fill-opacity=\".85\"/></svg>\n"),
    ];

    public static void Build(string path)
    {
        var d = new Doc("Embedded files", subject: "EmbeddedFiles name tree, associated files (/AF), FileAttachment annotations, file specification parameters", lang: "en-US");
        var t = new Faces(d);
        var pg = d.Page(A4.W, A4.H);
        Background(pg, P["paper"]);
        PageHeader(pg, t, "Embedded files", "Packaging  /  Attachments", 1, 1);
        pg.Text(t.Sans, 11, M, pg.H - 106, "Three files ride inside this PDF. Open the attachments panel, or click a paperclip.", color: P["slate"]);

        var specs = new List<int>();
        var names = new List<(string Name, string Entry)>();
        foreach (var (name, mime, desc, rel, text) in Files)
        {
            var data = Encoding.UTF8.GetBytes(text);
            const string mod = "D:20260115093000Z";
            var ef = d.Stream($"/Type /EmbeddedFile /Subtype /{mime.Replace("/", "#2F")} /Params << /Size {data.Length} /CheckSum <{Convert.ToHexStringLower(MD5.HashData(data))}> /ModDate ({mod}) /CreationDate ({mod}) >>", data);
            var fs = d.Add($"<< /Type /Filespec /F {K.PStr(name)} /UF {K.Utf16(name)} /Desc {K.PStr(desc)} /AFRelationship /{rel} /EF << /F {ef} 0 R /UF {ef} 0 R >> >>");
            specs.Add(fs);
            names.Add((name, $"{K.PStr(name)} {fs} 0 R"));
        }
        var sorted = names.OrderBy(n => n.Entry, StringComparer.Ordinal).Select(n => n.Entry);
        d.Cat.Add($"/Names << /EmbeddedFiles << /Names [{string.Join(" ", sorted)}] >> >> /AF [{string.Join(" ", specs.Select(s => $"{s} 0 R"))}]");
        d.Cat.Add("/PageMode /UseAttachments");

        var y = pg.H - 170;
        string[] icons = ["Paperclip", "PushPin", "Graph"];
        for (var i = 0; i < Files.Length; i++)
        {
            var (name, mime, desc, rel, text) = Files[i];
            const double h = 92;
            Box(pg, M, y - h, pg.W - 2 * M, h, fill: P["white"], stroke: P["mist"], r: 10);
            pg.Text(t.SerifB, 17, M + 76, y - 32, name, color: P["ink"]);
            pg.Text(t.Sans, 10.5, M + 76, y - 50, desc, color: P["slate"]);
            pg.Text(t.Mono, 8.5, M + 76, y - 70, $"{mime}  ·  {Encoding.UTF8.GetByteCount(text)} bytes  ·  /AFRelationship /{rel}", color: P["muted"], kern: false);
            double ix = M + 22, iy = y - 62;
            double[] r = [ix, iy, ix + 32, iy + 36];

            var xf = new XForm(d, r);
            xf.Fill(P["indigo"]); xf.Move(ix + 4, iy + 2); xf.Line(ix + 20, iy + 2); xf.Line(ix + 28, iy + 10); xf.Line(ix + 28, iy + 34); xf.Line(ix + 4, iy + 34); xf.Close(); xf.FillPath();
            xf.Fill(Lighten(P["indigo"], 0.55)); xf.Move(ix + 20, iy + 2); xf.Line(ix + 28, iy + 10); xf.Line(ix + 20, iy + 10); xf.Close(); xf.FillPath();
            xf.Stroke("#FFFFFF"); xf.Width(1.6); xf.Cap(1);
            for (var k = 0; k < 3; k++) { xf.Move(ix + 9, iy + 17 + k * 6); xf.Line(ix + 23, iy + 17 + k * 6); xf.StrokePath(); }
            var a = xf.Build();
            pg.AddAnnot($"<< /Type /Annot /Subtype /FileAttachment /Rect [{K.Join(r)}] /FS {specs[i]} 0 R /Name /{icons[i]} /Contents {K.PStr(desc)} /T (Embedded) /C [0.31 0.27 0.9] /F 4 /AP << /N {a} 0 R >> >>");
            y -= h + 16;
        }
        Paragraph(pg, t.Sans, 10, M, y - 12, pg.W - 2 * M,
            "Each file specification carries /EF with a Flate-compressed stream plus /Params (Size, MD5 CheckSum, dates). /AF at the catalog level marks them as associated files, as PDF/A-3 and PDF 2.0 require.",
            color: P["slate"], leading: 15);
        d.Save(path, version: "2.0");
    }
}
