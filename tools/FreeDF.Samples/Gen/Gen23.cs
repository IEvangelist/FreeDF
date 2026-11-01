using FreeDF.Samples.Kit;
using static FreeDF.Samples.Kit.Helpers;
using static FreeDF.Samples.Kit.Theme;

namespace FreeDF.Samples.Gen;

static class Gen23
{
    public const string Out = "23-resume.pdf";

    public static void Build(string path)
    {
        var d = new Doc("Résumé: Ada Marlowe", author: "Ada Marlowe", subject: "Two-column résumé: sidebar, skill meters, timeline", keywords: "resume, CV, engineer", lang: "en-US");
        var t = new Faces(d);
        var pg = d.Page(A4.W, A4.H);
        Background(pg, P["white"]);
        const double SW = 196;
        Box(pg, 0, 0, SW, pg.H, fill: P["ink"]);
        pg.Save(); pg.Rect(0, 0, SW, pg.H); pg.Clip(); pg.Gs(("ca", 0.35));
        pg.Sh(Radial(0, pg.H, 0, 0, pg.H, 340, [(0, P["indigo"]), (1, P["ink"])]));
        pg.Restore();
        pg.Save(); pg.Fill(P["white"]); pg.Circle(SW / 2, pg.H - 92, 40); pg.FillPath(); pg.Restore();
        pg.Save(); pg.Fill(P["indigo"]); pg.Circle(SW / 2, pg.H - 92, 36); pg.FillPath(); pg.Restore();
        pg.Text(t.SerifB, 30, SW / 2 - t.SerifB.Width("AM", 30) / 2, pg.H - 102, "AM", color: "#FFFFFF");

        const double sx = 24, sw = SW - 48;

        void SideHead(double y, string s)
        {
            pg.Text(t.SansB, 8, sx, y, s.ToUpperInvariant(), color: "#A5B4FC", tracking: 1.8, kern: false);
            pg.Save(); pg.Stroke(P["indigo"]); pg.Width(1.5); pg.Move(sx, y - 6); pg.Line(sx + 18, y - 6); pg.StrokePath(); pg.Restore();
        }

        var y = pg.H - 170;
        SideHead(y, "Contact");
        var contact = new[] { ("Email", "ada@marlowe.example"), ("Phone", "+44 20 7946 0123"), ("Web", "marlowe.example"), ("Based in", "Bristol, UK") };
        for (var j = 0; j < contact.Length; j++)
        {
            pg.Text(t.Sans, 7.5, sx, y - 24 - j * 28, contact[j].Item1.ToUpperInvariant(), color: "#94A3B8", tracking: 1, kern: false);
            pg.Text(t.Sans, 9.5, sx, y - 36 - j * 28, contact[j].Item2, color: "#F1F5F9", kern: false);
        }
        y -= 150;
        SideHead(y, "Skills");
        var skills = new[] { ("PDF & graphics", 0.95), ("C# / .NET", 0.9), ("Python", 0.82), ("Typography", 0.78), ("Accessibility", 0.7), ("Rust", 0.55) };
        for (var j = 0; j < skills.Length; j++)
        {
            var (k, v) = skills[j];
            var yy = y - 26 - j * 30;
            pg.Text(t.Sans, 9.5, sx, yy, k, color: "#E2E8F0");
            Box(pg, sx, yy - 11, sw, 4, fill: "#334155", r: 2);
            pg.Save(); pg.RRect(sx, yy - 11, sw * v, 4, 2); pg.Clip();
            pg.Sh(Axial(sx, 0, sx + sw, 0, [(0, P["indigo"]), (1, P["sky"])]));
            pg.Restore();
        }
        y -= 230;
        SideHead(y, "Languages");
        var langs = new[] { ("English", "Native"), ("French", "Fluent"), ("German", "Conversational") };
        for (var j = 0; j < langs.Length; j++)
        {
            pg.Text(t.SansB, 9.5, sx, y - 24 - j * 18, langs[j].Item1, color: "#F1F5F9");
            pg.Text(t.Sans, 9, sx + 70, y - 24 - j * 18, langs[j].Item2, color: "#94A3B8");
        }
        y -= 110;
        SideHead(y, "Interests");
        Paragraph(pg, t.Sans, 9, sx, y - 24, sw, "Letterpress, ultramarathons, hand-drawn maps and the odd 1990s demoscene remake.", color: "#CBD5E1", leading: 13.5);

        const double mx = SW + 40;
        var mw = pg.W - mx - 40;
        pg.Text(t.SerifB, 40, mx, pg.H - 82, "Ada Marlowe", color: P["ink"]);
        pg.Text(t.SansB, 11, mx, pg.H - 106, "SENIOR GRAPHICS ENGINEER", color: P["indigo"], tracking: 2, kern: false);
        Paragraph(pg, t.Serif, 11, mx, pg.H - 138, mw,
            "Engineer who turns specifications into pixels. Twelve years building document renderers, font pipelines and print tooling; happiest where PDF operators meet careful typography.",
            color: P["slate"], leading: 16.5);

        void MainHead(double yy, string s)
        {
            pg.Text(t.SansB, 9, mx, yy, s.ToUpperInvariant(), color: P["ink"], tracking: 1.8, kern: false);
            Rule(pg, mx + t.SansB.Width(s.ToUpperInvariant(), 9, tracking: 1.8) + 10, yy + 3, mx + mw);
        }

        y = pg.H - 222;
        MainHead(y, "Experience");
        (string When, string Role, string Desc, string C)[] jobs =
        [
            ("2021 – now", "Principal Engineer, Graphite Docs", "Led the rendering core rewrite: 4× faster page rasterisation, full transparency model, CFF and TrueType font stacks. Mentored a team of six.", P["indigo"]),
            ("2017 – 2021", "Staff Engineer, Quill & Co.", "Designed the colour-managed PDF export path (ICC, DeviceN, soft masks) used for 40 M documents a year.", P["teal"]),
            ("2013 – 2017", "Software Engineer, Foundry Print", "Built imposition and trapping tools; shipped a PostScript-to-PDF distiller and an accessibility checker.", P["violet"]),
            ("2011 – 2013", "Junior Developer, Pixelbox", "Maintained a Flash-to-PDF converter, then migrated it to a cross-platform C++ library.", P["amber"]),
        ];
        y -= 12;
        pg.Save(); pg.Stroke(P["mist"]); pg.Width(1.5); pg.Move(mx + 4, y - 18); pg.Line(mx + 4, y - 4 * 92 + 62); pg.StrokePath(); pg.Restore();
        foreach (var (when, role, desc, c) in jobs)
        {
            pg.Save(); pg.Fill("#FFFFFF"); pg.Stroke(c); pg.Width(2.2); pg.Circle(mx + 4, y - 12, 4.2); pg.FillStroke(); pg.Restore();
            pg.Text(t.SansB, 8, mx + 20, y - 8, when.ToUpperInvariant(), color: c, tracking: 1.2, kern: false);
            pg.Text(t.SerifB, 13, mx + 20, y - 26, role, color: P["ink"]);
            Paragraph(pg, t.Sans, 9.5, mx + 20, y - 43, mw - 20, desc, color: P["slate"], leading: 13.5);
            y -= 92;
        }
        y -= 6;
        MainHead(y, "Education");
        var edu = new[] { ("2008 – 2011", "BSc Computer Science (First)", "University of Bath"), ("2007", "A-levels: Maths, Further Maths, Art", "Redland High School") };
        for (var j = 0; j < edu.Length; j++)
        {
            var yy = y - 26 - j * 36;
            pg.Text(t.SansB, 8, mx, yy, edu[j].Item1.ToUpperInvariant(), color: P["muted"], tracking: 1.2, kern: false);
            pg.Text(t.SansB, 10.5, mx + 84, yy, edu[j].Item2, color: P["ink"]);
            pg.Text(t.Sans, 9.5, mx + 84, yy - 14, edu[j].Item3, color: P["slate"]);
        }
        y -= 120;
        MainHead(y, "Selected projects");
        var projects = new[] { ("csharp-pdf", "A from-scratch PDF parser, renderer and editor in C#."), ("inkwell", "Font subsetter and CFF-to-glyf converter."), ("loupe", "Pixel-diff harness for cross-renderer comparison.") };
        for (var j = 0; j < projects.Length; j++)
        {
            var yy = y - 26 - j * 20;
            pg.Text(t.Mono, 9.5, mx, yy, projects[j].Item1, color: P["indigo"], kern: false);
            pg.Text(t.Sans, 9.5, mx + 84, yy, projects[j].Item2, color: P["slate"]);
        }
        d.Save(path);
    }
}
