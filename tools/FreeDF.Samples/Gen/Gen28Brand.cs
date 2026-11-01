using FreeDF.Samples.Kit;
using static FreeDF.Samples.Kit.Helpers;
using static FreeDF.Samples.Kit.Theme;

namespace FreeDF.Samples.Gen;

sealed partial class Gen28
{
    static readonly Color Violet2 = "#6D28D9";

    // ---- marks -------------------------------------------------------------------------------------------------------------------

    void Mark(int k, double x, double y, double s)
    {
        switch (k)
        {
            case 0: // two stacked sheets
                LogoMark(_pg, x, y, s, Indigo, Violet);
                break;
            case 1: // dog-eared page
            {
                double pw = 0.74 * s, px = x + 0.13 * s, f = 0.30 * s;
                _pg.Save();
                _pg.Move(px, y); _pg.Line(px + pw, y); _pg.Line(px + pw, y + s - f); _pg.Line(px + pw - f, y + s); _pg.Line(px, y + s); _pg.Close();
                _pg.Clip();
                _pg.Sh(Axial(px, y + s, px + pw, y, [(0, Indigo), (1, Violet2)]));
                _pg.Restore();
                _pg.Save(); _pg.Fill("#C7D2FE");
                _pg.Move(px + pw - f, y + s); _pg.Line(px + pw, y + s - f); _pg.Line(px + pw - f, y + s - f); _pg.Close(); _pg.FillPath(); _pg.Restore();
                double[] widths = [0.40, 0.48, 0.30];
                for (var i = 0; i < 3; i++) Box(_pg, px + 0.13 * s, y + 0.20 * s + i * 0.15 * s, widths[i] * s, 0.055 * s, fill: White, r: 0.027 * s);
                break;
            }
            case 2: // FreeDF's editor and app icon
                GradRect(x, y, s, s, s * 0.22, "#BE185D", "#9D174D");
                Tx(_t.SansB, s * 0.66, x + s / 2, y + s * 0.27, "F", White, 'c');
                break;
            case 3: // lines of text with a caret
                Box(_pg, x, y, s, s, fill: Ink, r: s * 0.22);
                Box(_pg, x + 0.2 * s, y + 0.64 * s, 0.6 * s, 0.1 * s, fill: White, r: 0.05 * s);
                Box(_pg, x + 0.2 * s, y + 0.45 * s, 0.42 * s, 0.1 * s, fill: White, r: 0.05 * s);
                Box(_pg, x + 0.2 * s, y + 0.26 * s, 0.28 * s, 0.1 * s, fill: White, r: 0.05 * s);
                Box(_pg, x + 0.54 * s, y + 0.21 * s, 0.07 * s, 0.2 * s, fill: AmberL, r: 0.02 * s);
                break;
            case 4: // object graph
            {
                GradRect(x, y, s, s, s * 0.22, "#115E59", "#0F172A");
                (double X, double Y)[] n = [(0.27, 0.72), (0.73, 0.76), (0.5, 0.5), (0.25, 0.27), (0.75, 0.25)];
                int[][] e = [[0, 2], [1, 2], [2, 3], [2, 4], [0, 1]];
                foreach (var ed in e) Line(x + n[ed[0]].X * s, y + n[ed[0]].Y * s, x + n[ed[1]].X * s, y + n[ed[1]].Y * s, "#99F6E4", Math.Max(0.8, 0.03 * s));
                for (var i = 0; i < n.Length; i++) Dot(x + n[i].X * s, y + n[i].Y * s, (i == 2 ? 0.095 : 0.07) * s, i == 2 ? AmberL : White);
                break;
            }
            default: // a sharp on a page
                Box(_pg, x, y, s, s, fill: White, stroke: Indigo, r: s * 0.22, lw: Math.Max(1, s * 0.06));
                _pg.Save(); _pg.Stroke(Indigo); _pg.Width(Math.Max(1.2, s * 0.075)); _pg.Cap(1);
                _pg.Move(x + 0.38 * s, y + 0.2 * s); _pg.Line(x + 0.45 * s, y + 0.8 * s); _pg.StrokePath();
                _pg.Move(x + 0.58 * s, y + 0.2 * s); _pg.Line(x + 0.65 * s, y + 0.8 * s); _pg.StrokePath();
                _pg.Move(x + 0.25 * s, y + 0.42 * s); _pg.Line(x + 0.78 * s, y + 0.46 * s); _pg.StrokePath();
                _pg.Move(x + 0.22 * s, y + 0.6 * s); _pg.Line(x + 0.75 * s, y + 0.64 * s); _pg.StrokePath();
                _pg.Restore();
                break;
        }
    }

    static readonly (string Name, string Status, string Why, string Alt)[] Marks =
    [
        ("Quire", "Idea", "A quire is a gathered set of sheets. The overlapping squares already appear on the sample covers.", "Two overlapping rounded squares in indigo and violet, like two stacked sheets."),
        ("Folio", "Idea", "A folded sheet with text lines. Reads as \u201Cdocument\u201D at 16 px and carries a gradient well.", "A page with a folded top-right corner, filled with an indigo to violet gradient and three white text lines."),
        ("FreeDF", "Official", "The selected public identity: a white F on the PinkInk-inspired pink primary. Used by the editor and installed app.", "A rounded pink square with a white F."),
        ("Caret", "Idea", "Text lines and an amber insertion caret: the editor's core gesture of changing existing text.", "A dark rounded square with three white text lines and an amber text caret."),
        ("Xref", "Idea", "Five linked nodes, the object graph and cross-reference table underneath every PDF.", "A dark teal rounded square showing five linked nodes, the central one in amber."),
        ("csharp-pdf", "In use", "A sharp sign on a page. Literal and nerdy, and tied to the current repository name.", "A white rounded square with an indigo outline and an indigo sharp sign, tilted like italic type."),
    ];

    // ---- 10: names and marks ----------------------------------------------------------------------------------------------------

    void BrandNamesPage()
    {
        var y = ContentPage("Part 2  /  FreeDF brand", Violet2, "FreeDF: the official name and mark",
            "FreeDF names the editor and its tools. The repository path and PortableDocument APIs stay stable. Other directions remain archived design studies, not pending renames.",
            "Brand: names and marks", "Part 2: FreeDF brand");
        y -= 12;
        SubHeading(y, "Names already in the repository", Violet2);
        string[][] rows =
        [
            ["csharp-pdf", "Repository IEvangelist/csharp-pdf and the Pages path /csharp-pdf/", "Repository"],
            ["PortableDocument", "Assemblies and namespaces PortableDocument.*, and the static render API", "In the API"],
            ["FreeDF", "Editor, PWA and FreeDF.* project and tool names", "Official"],
            ["editPDF", "Considered as a rename, then cancelled: editpdf.io already exists", "Cancelled"],
        ];
        var yb = DataTable(M, y - 8, [new("Name", 120, true), new("Where it appears today", W - 200), new("Status", 80, false, true)], rows,
            "Project names in use or considered, where each appears, and its status.", 25);
        y = yb - 26;
        SubHeading(y, "Six mark directions", Violet2);
        const double cw = 153, ch = 166;
        var gap = (W - 3 * cw) / 2;
        for (var i = 0; i < Marks.Length; i++)
        {
            var col = i % 3;
            var row = i / 3;
            var x = M + col * (cw + gap);
            var top = y - 12 - row * (ch + 14);
            var m = Marks[i];
            Art(() => { Shadow(x, top - ch, cw, ch, 12, 0.07, 2, 4); Card(x, top - ch, cw, ch, White, 12); });
            Fig(m.Alt, () =>
            {
                Box(_pg, x + 8, top - 88, cw - 16, 80, fill: i is 3 or 4 ? "#E2E8F0" : "#EEF2FF", r: 8);
                Mark(i, x + cw / 2 - 28, top - 80, 56);
            });
            T("H3", () => Tx(_t.SerifB, 13.5, x + 12, top - 106, m.Name, Ink));
            T("P", () =>
            {
                var (bg, fg) = m.Status is "In use" or "Official" ? ((Color)"#D1FAE5", (Color)"#064E3B") : ((Color)"#E0E7FF", (Color)"#312E81");
                Pill(x + cw - 12 - (_t.SansB.Width(m.Status, 7.6) + 14), top - 112, _t.SansB.Width(m.Status, 7.6) + 14, 14, m.Status, bg, fg, 7.6);
                Para(_t.Sans, 8.3, x + 12, top - 121, cw - 24, m.Why, Slate, 11.4);
            });
        }
        var by = y - 12 - 2 * (ch + 14) - 4;
        var note = "FreeDF is the selected product name, not a claim of trademark, domain or app-store clearance. Other names and marks remain unvetted, archived explorations.";
        var nh = ParaH(_t.Sans, 9, W - 40, note, 12.6) + 22;
        Art(() => { Box(_pg, M, by - nh, W, nh, fill: "#F5F3FF", stroke: "#DDD6FE", r: 9); Box(_pg, M, by - nh + 6, 3, nh - 12, fill: Violet2, r: 1.5); });
        T("P", () =>
        {
            Tx(_t.SansB, 8, M + 16, by - 14, "IDENTITY AND CLEARANCE ARE DIFFERENT", Violet2, tracking: 1.3);
            Para(_t.Sans, 9, M + 16, by - 27, W - 40, note, Ink, 12.6);
        });
    }

    // ---- Caret due diligence ---------------------------------------------------------------------------------------------------

    void BrandCaretPage()
    {
        var y = ContentPage("Part 2  /  FreeDF brand", Violet2, "Caret: archived name study",
            "An archived check from 2026-10-31, before FreeDF was selected. Not legal advice or a clearance; this is not a proposal to rename the product.",
            "Brand: Caret due diligence");
        y -= 10;
        Fig(Marks[3].Alt, () =>
        {
            Box(_pg, M, y - 74, 74, 74, fill: "#E2E8F0", r: 10);
            Mark(3, M + 9, y - 65, 56);
        });
        T("P", () => Para(_t.Sans, 9.4, M + 92, y - 14, W - 92, "Caret is the editor's core gesture: the insertion point where text changes. It is also the PDF annotation subtype /Subtype /Caret, so the name ties to the format itself. Both facts cut two ways, as below.", Ink, 13.2));
        y -= 92;
        SubHeading(y, "Name due diligence (2026-10-31, not legal advice)", Violet2);
        string[][] rows =
        [
            ["NuGet", "Ids Caret and Caret.Pdf returned 404. Nearby ids such as WpfCaret exist.", "Unclaimed"],
            ["npm", "caret is taken: a tiny package that inserts a caret and text beneath a string.", "Taken"],
            ["GitHub", "A user or organisation named caret already exists.", "Taken"],
            ["caret.io", "An established Markdown and text editor (Caret.io Ltd.; Mac, Windows, Linux; winget package Caret). Same broad category, text editing: the biggest trademark and discoverability risk.", "Risk"],
            ["caretpdf.com", "Does not resolve. That is not proof it is available; WHOIS has not been checked.", "Unverified"],
            ["getcaret.com", "Resolves, so presumably taken.", "Taken"],
            ["Search noise", "caret is a well-known R machine-learning package, and /Subtype /Caret is a PDF annotation name. That hurts search, but also ties to the insertion-caret mark.", "Noise"],
        ];
        var yb = DataTable(M, y - 8, [new("Where", 92, true), new("What the check found", W - 92 - 84), new("Reading", 84, false, true)], rows,
            "Availability checks for the name Caret: where it was checked, what was found and a reading.", 24);
        y = yb - 24;
        SubHeading(y, "Recommended next steps", Violet2);
        string[] steps =
        [
            "Run a formal trademark search (USPTO and EUIPO), with counsel, focused on software and text-editing classes because of caret.io.",
            "Check domain availability with WHOIS before relying on caretpdf.com or any variant.",
            "Reserve the NuGet package ids (Caret, Caret.Pdf) and the npm scope, and consider a GitHub organisation, if the name is chosen.",
            "Weigh the alternatives on the previous page: a compound such as Caret.Pdf may separate it from caret.io, but that is a legal question, not a design one.",
        ];
        var list = new Node("L", _sec);
        var yy = y - 22;
        foreach (var st in steps)
        {
            var li = new Node("LI", list);
            var lbl = new Node("Lbl", li);
            var body = new Node("LBody", li);
            var y0 = yy;
            Mc(lbl, () => Dot(M + 4, y0 + 3.5, 2.2, Violet2));
            Mc(body, () => Para(_t.Sans, 9.2, M + 16, y0, W - 20, st, Ink, 12.8));
            yy -= ParaH(_t.Sans, 9.2, W - 20, st, 12.8) + 7;
        }
        var note = "Checked by hand on 2026-10-31 against public registries and sites. Results can change; nothing here is a trademark opinion, and no name has been chosen.";
        var nh = ParaH(_t.Sans, 9, W - 40, note, 12.6) + 22;
        Art(() => { Box(_pg, M, yy - 6 - nh, W, nh, fill: "#F5F3FF", stroke: "#DDD6FE", r: 9); Box(_pg, M, yy - 6 - nh + 6, 3, nh - 12, fill: Violet2, r: 1.5); });
        T("P", () =>
        {
            Tx(_t.SansB, 8, M + 16, yy - 20, "STATUS OF THIS CHECK", Violet2, tracking: 1.3);
            Para(_t.Sans, 9, M + 16, yy - 33, W - 40, note, Ink, 12.6);
        });
    }

    // ---- 11: palettes ----------------------------------------------------------------------------------------------------------------

    sealed record Swatch(string Name, string Hex);

    sealed record Palette(string Name, string Status, string Note, Swatch[] Colours);

    static readonly Palette[] Palettes =
    [
        new("Ink and Indigo", "In use: samples 01\u201327", "The design system of the committed samples and of this document.",
        [
            new("Ink", "#0F172A"), new("Indigo deep", "#312E81"), new("Indigo", "#4F46E5"), new("Violet", "#7C3AED"), new("Teal", "#0D9488"), new("Amber", "#F59E0B"),
        ]),
        new("FreeDF midnight", "In use: web editor", "PinkInk primary and a lighter related accent in dark mode.",
        [
            new("Background", "#0E1013"), new("Raised", "#1B1E25"), new("Primary", "#BE185D"), new("Accent", "#E5A3BE"), new("Text", "#E8EAF0"), new("Success", "#3ECF8E"),
        ]),
        new("Vermilion and paper", "Exploratory", "Warm editorial: ink on paper with one hot accent.",
        [
            new("Ink", "#1C1917"), new("Paper", "#F5F0E6"), new("Vermilion", "#C2410C"), new("Rust", "#9A3412"), new("Deep teal", "#115E59"), new("Gold", "#CA8A04"),
        ]),
        new("Aurora", "Exploratory", "Dark, luminous: a night background with four neon-soft accents.",
        [
            new("Night", "#0B1020"), new("Indigo night", "#1E1B4B"), new("Mint", "#5EEAD4"), new("Lavender", "#A78BFA"), new("Pink", "#F472B6"), new("Sun", "#FBBF24"),
        ]),
    ];

    static string Grade(double r) => r >= 7 ? "AAA" : r >= 4.5 ? "AA" : r >= 3 ? "large only" : "fails";

    void BrandPalettePage()
    {
        var y = ContentPage("Part 2  /  FreeDF brand", Violet2, "Palettes, with measured contrast",
            "Four palettes. Each swatch shows its WCAG 2.x contrast against white and against ink (#0F172A), computed by this generator.",
            "Brand: palettes and contrast");
        y -= 14;
        double sw = (W - 5 * 8) / 6;
        var ink = (Color)"#0F172A";
        foreach (var p in Palettes)
        {
            T("H2", () => Tx(_t.SerifB, 13, M, y, p.Name, Ink));
            T("P", () =>
            {
                Tx(_t.Sans, 8.6, M + _t.SerifB.Width(p.Name, 13) + 12, y, p.Note, Slate);
                var (bg, fg) = p.Status.StartsWith("In use") ? ((Color)"#D1FAE5", (Color)"#064E3B") : ((Color)"#E0E7FF", (Color)"#312E81");
                var w = _t.SansB.Width(p.Status, 7.6) + 14;
                Pill(PW - M - w, y - 3.5, w, 14, p.Status, bg, fg, 7.6);
            });
            var alt = $"Palette {p.Name}. " + string.Join("; ", p.Colours.Select(c =>
                $"{c.Name} {c.Hex}: {Num(Contrast(c.Hex, White), "0.00")} to 1 with white text, {Num(Contrast(c.Hex, ink), "0.00")} to 1 with ink text"));
            var top = y - 12;
            Fig(alt, () =>
            {
                for (var i = 0; i < p.Colours.Length; i++)
                {
                    var c = p.Colours[i];
                    var x = M + i * (sw + 8);
                    Color col = c.Hex;
                    var cw = Contrast(col, White);
                    var ck = Contrast(col, ink);
                    Color? edge = Contrast(col, Paper) < 1.3 ? (Color?)(Color)"#94A3B8" : default(Color?);
                    Box(_pg, x, top - 46, sw, 46, fill: col, stroke: edge, r: 7);
                    var onC = cw >= ck ? White : ink;
                    Tx(_t.SansB, 8.4, x + 8, top - 18, c.Name, onC);
                    Tx(_t.Sans, 8, x + 8, top - 31, c.Hex, onC);
                    Tx(_t.Sans, 7.5, x, top - 58, $"White {Num(cw, "0.00")}:1 {Grade(cw)}", cw >= 4.5 ? Ink : Slate);
                    Tx(_t.Sans, 7.5, x, top - 68, $"Ink {Num(ck, "0.00")}:1 {Grade(ck)}", ck >= 4.5 ? Ink : Slate);
                }
            });
            y -= 106;
        }

        T("H2", () => Tx(_t.SansB, 13, M, y - 2, "Gradient ramps", Violet2));
        var ry = y - 16;
        Fig("Two gradient ramps. The first runs from indigo through violet to teal; the second runs from mint through lavender to pink and sun.",
            () =>
            {
                _pg.Save(); _pg.RRect(M, ry - 22, W / 2 - 6, 22, 8); _pg.Clip();
                _pg.Sh(Axial(M, 0, M + W / 2 - 6, 0, [(0, Indigo), (0.5, Violet), (1, TealL)]));
                _pg.Restore();
                _pg.Save(); _pg.RRect(M + W / 2 + 6, ry - 22, W / 2 - 6, 22, 8); _pg.Clip();
                _pg.Sh(Axial(M + W / 2 + 6, 0, M + W, 0, [(0, "#5EEAD4"), (0.4, "#A78BFA"), (0.75, "#F472B6"), (1, "#FBBF24")]));
                _pg.Restore();
            });
        var legend = "White text needs 4.5:1 (AA) for body text, 3:1 for large text and graphics, 7:1 for AAA. Teal #0D9488 reaches only the large-text bar and amber #F59E0B fails with white, so this document uses darker tints (#0F766E, #B45309) for any teal or amber text.";
        T("Caption", () => Para(_t.SansI, 8.8, M, ry - 38, W, legend, Slate, 12.4));

        var py = ry - 76;
        T("H2", () => Tx(_t.SansB, 13, M, py, "Text pairings that pass", Violet2));
        (string Bg, string Fg, string Name)[] pairs =
        [
            ("#F8FAFC", "#0F172A", "Ink on paper"), ("#4F46E5", "#FFFFFF", "White on indigo"), ("#312E81", "#FFFFFF", "White on indigo deep"),
            ("#5EEAD4", "#0B1020", "Night on mint"), ("#F59E0B", "#0F172A", "Ink on amber"), ("#C2410C", "#FFFFFF", "White on vermilion"),
        ];
        var pw = (W - 5 * 8) / 6;
        var palt = "Six text pairings that pass WCAG AA: " + string.Join("; ", pairs.Select(p => $"{p.Name} {Num(Contrast(p.Fg, p.Bg), "0.00")} to 1"));
        Fig(palt, () =>
        {
            for (var i = 0; i < pairs.Length; i++)
            {
                var p = pairs[i];
                var x = M + i * (pw + 8);
                Box(_pg, x, py - 78, pw, 56, fill: p.Bg, stroke: Contrast(p.Bg, Paper) < 1.3 ? Line_ : p.Bg, r: 8);
                Tx(_t.SerifB, 22, x + 10, py - 58, "Aa", p.Fg);
                Tx(_t.SansB, 8.4, x + pw - 8, py - 44, Num(Contrast(p.Fg, p.Bg), "0.00") + ":1", p.Fg, 'r');
                Tx(_t.Sans, 7.6, x, py - 90, p.Name, Slate);
            }
        });
    }

    // ---- 12: typography -------------------------------------------------------------------------------------------------------------

    void BrandTypePage()
    {
        var y = ContentPage("Part 2  /  FreeDF brand", Violet2, "Type pairings from fonts we already ship",
            "Only faces whose licences are already in samples/_gen/fonts: Source Serif 4, Source Sans 3, Source Code Pro (SIL OFL 1.1) and Bitstream Vera Sans.",
            "Brand: type pairings");
        y -= 12;
        (string Name, string Used, IFont Display, double DSize, IFont Deck, IFont Body, string Head, string Deck_, string Body_, bool Mono)[] panels =
        [
            ("Editorial", "Source Serif 4 Semibold + Source Sans 3 (the sample system)", _t.SerifB, 25, _t.SerifI, _t.Sans,
                "Pages that read well", "Display in serif, text in a humanist sans.", "A tagged PDF keeps its reading order even when the drawing order is different. That is the reason to structure it.", false),
            ("Product", "Source Sans 3 Semibold + Regular + Source Code Pro", _t.SansB, 24, _t.Sans, _t.Sans,
                "Open, edit, save", "One family, clear weights, code in mono.", "Call PdfEditor.Open(bytes), change a page, then Save with object streams.", true),
            ("Bookish", "Source Serif 4 Semibold + Regular + Italic", _t.SerifB, 25, _t.SerifI, _t.Serif,
                "A report in one voice", "Serif throughout, with italic for emphasis.", "Long reading likes a quiet serif. Headings carry weight, the body stays calm, and figures speak in captions.", false),
            ("Utility", "Bitstream Vera Sans Bold + Regular (TrueType)", _veraB, 23, _vera, _vera,
                "Plain and sturdy", "A compact TrueType pair for dense tables.", "Vera is wide and open, which helps at small sizes in dashboards and spec tables.", false),
        ];
        const double ph = 124;
        foreach (var p in panels)
        {
            var top = y;
            Art(() => { Shadow(M, top - ph, W, ph, 12, 0.06, 2, 4); Card(M, top - ph, W, ph, White, 12); Box(_pg, M, top - ph + 10, 4, ph - 20, fill: Violet2, r: 2); });
            T("H2", () => Tx(p.Display, p.DSize, M + 20, top - 40, p.Head, Ink));
            T("P", () =>
            {
                Tx(p.Deck, 11.5, M + 20, top - 60, p.Deck_, Slate);
                Para(p.Body, 9.4, M + 20, top - 80, 280, p.Body_, Ink, 13.2);
                if (p.Mono) Tx(_t.Mono, 8.6, M + 20, top - 110, "var pdf = PdfEditor.Open(bytes);", IndigoD);
            });
            T("P", () =>
            {
                var rx = M + 330;
                Tx(_t.SansB, 7.4, rx, top - 24, p.Name.ToUpperInvariant(), Violet2, tracking: 1.4);
                Para(_t.Sans, 8.6, rx, top - 38, W - 330 - 16, p.Used, Ink, 12);
                Tx(_t.Sans, 8.6, rx, top - 82, "Aa Bb Cc 0123 \u2192 \u00D7 \u00B1", Slate);
                Tx(p.Display, 30, rx, top - 114, "Aa", Violet2);
                Tx(p.Body, 30, rx + 56, top - 114, "g", Slate);
                Tx(p.Deck, 30, rx + 82, top - 114, "R", Indigo);
            });
            y -= ph + 10;
        }
        y -= 10;
        T("H2", () => Tx(_t.SansB, 13, M, y - 4, "Scale used in this document", Violet2));
        double[] sizes = [7.8, 8.6, 9.2, 12, 13, 16, 25, 64];
        T("P", () =>
        {
            var x = M;
            foreach (var s in sizes)
            {
                Tx(_t.SerifB, Math.Min(s, 34), x, y - 38, "Aa", Ink);
                Tx(_t.Sans, 8, x, y - 52, Num(s, "0.#") + " pt", Slate);
                x += Math.Max(48, _t.SerifB.Width("Aa", Math.Min(s, 34)) + 18);
            }
        });
        T("Caption", () => Para(_t.SansI, 8.6, M, y - 70, W,
            "Inter and JetBrains Mono appear in the editor's CSS stack but are not shipped in this repository, so they are not proposed. Licences: samples/_gen/fonts/LICENSES.txt and bitstream-vera-license.txt.", Slate, 12));
    }

    // ---- 13: mock applications ------------------------------------------------------------------------------------------------------

    void Wordmark(double x, double y, double size, Color c1, Color c2)
    {
        Tx(_t.SerifB, size, x, y, "csharp", c1);
        Tx(_t.SerifB, size, x + _t.SerifB.Width("csharp", size), y, "-pdf", c2);
    }

    void Glow(double x, double y, double w, double h, double r, Color a, Color b)
    {
        _pg.Save(); _pg.RRect(x, y, w, h, r); _pg.Clip();
        _pg.Sh(Axial(x, y + h, x + w, y, [(0, a), (1, b)]));
        _pg.Gs(("ca", 0.5));
        _pg.Sh(Radial(x + w * 0.85, y + h * 0.8, 0, x + w * 0.85, y + h * 0.8, Math.Max(w, h) * 0.8, [(0, Violet), (1, b)], false));
        _pg.Restore();
    }

    void BrandApplicationsPage()
    {
        var y = ContentPage("Part 2  /  FreeDF brand", Violet2, "Mock applications",
            "The Folio mark and the Ink and Indigo palette on surfaces a project actually needs. The wordmark uses today's repository name; any name from the previous pages drops in.",
            "Brand: mock applications");
        y -= 12;
        // row 1: cover, letterhead, social card
        var top = y;
        double cw = 122, chh = 172;
        Fig("Mock report cover: a dark indigo and teal gradient, the Folio mark, the words Measured, Named and Mapped in three colours and the csharp-pdf wordmark.", () =>
        {
            Shadow(M, top - chh, cw, chh, 6, 0.16, 4, 8);
            Glow(M, top - chh, cw, chh, 6, "#1E1B4B", "#042F2E");
            Mark(1, M + 12, top - 38, 22);
            Tx(_t.SerifB, 17, M + 12, top - 80, "Measured.", "#C7D2FE");
            Tx(_t.SerifB, 17, M + 12, top - 99, "Named.", "#5EEAD4");
            Tx(_t.SerifB, 17, M + 12, top - 118, "Mapped.", "#FCD34D");
            Wordmark(M + 12, top - 158, 10, White, "#5EEAD4");
        });
        var lx = M + cw + 14;
        Fig("Mock letterhead: a white page with the Folio mark and wordmark at the top, a rule and grey placeholder text lines.", () =>
        {
            Shadow(lx, top - chh, cw, chh, 3, 0.14, 3, 6);
            Box(_pg, lx, top - chh, cw, chh, fill: White, stroke: Mist, r: 3);
            Mark(1, lx + 12, top - 36, 20);
            Wordmark(lx + 38, top - 27, 11, Ink, Indigo);
            Rule(_pg, lx + 12, top - 46, lx + cw - 12, Indigo, 1.2);
            for (var i = 0; i < 9; i++) Box(_pg, lx + 12, top - 66 - i * 11, i % 4 == 3 ? 60 : cw - 24 - (i % 3) * 14, 3.4, fill: Mist, r: 1.7);
            Rule(_pg, lx + 12, top - chh + 24, lx + cw - 12, Mist);
            Box(_pg, lx + 12, top - chh + 12, 34, 4, fill: Indigo, r: 2);
        });
        var sx = lx + cw + 14;
        var sw = M + W - sx;
        var sh = sw * 630 / 1200;
        Fig("Mock social card at the 1200 by 630 proportion: dark gradient, the wordmark, the tagline Benchmarks, brand ideas, spec parity and a row of rising bars.", () =>
        {
            Shadow(sx, top - sh, sw, sh, 8, 0.16, 3, 6);
            Glow(sx, top - sh, sw, sh, 8, "#1E1B4B", "#0B1020");
            Mark(1, sx + 14, top - 40, 24);
            Wordmark(sx + 44, top - 30, 15, White, "#5EEAD4");
            Tx(_t.Sans, 8, sx + 14, top - 62, "Benchmarks, brand ideas, spec parity", "#CBD5E1");
            double[] hs = [10, 16, 13, 24, 20, 32];
            for (var i = 0; i < hs.Length; i++) Box(_pg, sx + sw - 20 - (hs.Length - i) * 14, top - sh + 12, 9, hs[i], fill: i == hs.Length - 1 ? "#5EEAD4" : "#6366F1", r: 2);
        });
        T("P", () =>
        {
            Tx(_t.SansB, 7.4, M, top - chh - 12, "REPORT COVER", Slate, tracking: 1.2);
            Tx(_t.SansB, 7.4, lx, top - chh - 12, "LETTERHEAD", Slate, tracking: 1.2);
            Tx(_t.SansB, 7.4, sx, top - sh - 12, "SOCIAL CARD 1200 \u00D7 630", Slate, tracking: 1.2);
        });
        // row 2: business cards and app icons
        var ry = top - chh - 36;
        double bw = 168, bh = 96;
        Fig("Mock business card, front: dark indigo gradient with the Folio mark and the wordmark.", () =>
        {
            Shadow(M, ry - bh, bw, bh, 8, 0.16, 3, 6);
            Glow(M, ry - bh, bw, bh, 8, "#1E1B4B", "#0B1020");
            Mark(1, M + 14, ry - 46, 24);
            Wordmark(M + 46, ry - 34, 14, White, "#5EEAD4");
            Tx(_t.Sans, 7.6, M + 14, ry - bh + 14, "Measured. Named. Mapped.", "#C7D2FE");
        });
        Fig("Mock business card, back: white with a name, role and contact placeholders, an indigo edge and a small Folio mark.", () =>
        {
            var bx = M + bw + 12;
            Shadow(bx, ry - bh, bw, bh, 8, 0.14, 3, 6);
            Box(_pg, bx, ry - bh, bw, bh, fill: White, stroke: Mist, r: 8);
            Box(_pg, bx, ry - bh, 6, bh, fill: Indigo, r: 3);
            Tx(_t.SerifB, 12, bx + 18, ry - 28, "Your Name", Ink);
            Tx(_t.Sans, 8, bx + 18, ry - 41, "Maintainer", Slate);
            Tx(_t.Mono, 7.2, bx + 18, ry - bh + 28, "name@example.org", IndigoD);
            Tx(_t.Mono, 7.2, bx + 18, ry - bh + 17, "github.com/your-handle", IndigoD);
            Mark(1, bx + bw - 30, ry - 32, 18);
        });
        var iy = ry;
        var ix0 = M + 2 * bw + 28;
        Fig("App icon study: Folio on an indigo to violet gradient at 56, 36 and 22 points, and Folio on a dark tile.", () =>
        {
            double ix = ix0;
            foreach (var s in new[] { 56.0, 36, 22 })
            {
                GradRect(ix, iy - s, s, s, s * 0.24, Indigo, Violet2);
                Mark(1, ix + s * 0.2, iy - s + s * 0.2, s * 0.6);
                ix += s + 8;
            }
            Box(_pg, ix0, iy - 96, 36, 36, fill: Ink, r: 8.6);
            Mark(1, ix0 + 7, iy - 89, 22);
        });
        T("P", () =>
        {
            Tx(_t.SansB, 7.4, M, ry - bh - 12, "BUSINESS CARD, FRONT", Slate, tracking: 1.2);
            Tx(_t.SansB, 7.4, M + bw + 12, ry - bh - 12, "BACK", Slate, tracking: 1.2);
            Tx(_t.SansB, 7.4, ix0, ry - bh - 12, "APP ICON, 56 / 36 / 22 AND DARK", Slate, tracking: 1.2);
        });

        // row 3: editor header and slide
        var ey = ry - bh - 36;
        var ew = W / 2 - 6;
        var ex = M + ew + 12;
        Fig("FreeDF editor header in the dark theme: the pink F tile, product name, File, Edit and View menus and a green Saved pill.", () =>
        {
            Shadow(M, ey - 50, ew, 50, 8, 0.16, 3, 6);
            Box(_pg, M, ey - 50, ew, 50, fill: "#0E1013", stroke: "#252932", r: 8);
            Mark(2, M + 12, ey - 37, 26);
            Tx(_t.SansB, 10, M + 46, ey - 26, "FreeDF", "#E8EAF0");
            Tx(_t.Sans, 8.4, M + 46, ey - 40, "File    Edit    View", "#B4BAC8");
            Pill(M + ew - 56, ey - 31, 46, 15, "Saved", "#143D2B", "#3ECF8E", 7.8);
        });
        Fig("Mock slide title: a light slide with a left indigo band, a headline and a thin rule.", () =>
        {
            Shadow(ex, ey - 50, ew, 50, 6, 0.12, 2, 4);
            Box(_pg, ex, ey - 50, ew, 50, fill: Paper, stroke: Mist, r: 6);
            Box(_pg, ex, ey - 50, 6, 50, fill: Indigo, r: 3);
            Tx(_t.SerifB, 13, ex + 18, ey - 22, "Benchmarks, honestly", Ink);
            Rule(_pg, ex + 18, ey - 30, ex + ew - 14, Indigo, 1);
            Tx(_t.Sans, 8, ex + 18, ey - 42, "16:9 title slide", Slate);
        });
        T("P", () =>
        {
            Tx(_t.SansB, 7.4, M, ey - 62, "EDITOR HEADER (CURRENT NAME AND MARK)", Slate, tracking: 1.2);
            Tx(_t.SansB, 7.4, ex, ey - 62, "TITLE SLIDE", Slate, tracking: 1.2);
        });

        // row 4: docs hero window
        var wy = ey - 86;
        double ww = W, wh = 150;
        Fig("Mock documentation home page in a browser window: a dark hero with the Folio mark, the headline Read, edit and write PDFs in C#, two buttons and a strip of three light feature cards.", () =>
        {
            Shadow(M, wy - wh, ww, wh, 8, 0.16, 4, 8);
            Box(_pg, M, wy - wh, ww, wh, fill: White, stroke: Line_, r: 8);
            Box(_pg, M, wy - 20, ww, 20, fill: "#E2E8F0", r: 8);
            Box(_pg, M, wy - 20, ww, 10, fill: "#E2E8F0");
            for (var i = 0; i < 3; i++) Dot(M + 12 + i * 11, wy - 10, 3, i == 0 ? "#F87171" : i == 1 ? "#FBBF24" : "#4ADE80");
            Box(_pg, M + 56, wy - 16, 200, 11, fill: White, r: 5.5);
            Glow(M + 1, wy - 108, ww - 2, 88, 1, "#1E1B4B", "#0B1020");
            Mark(1, M + 26, wy - 78, 34);
            Tx(_t.SerifB, 21, M + 74, wy - 54, "Read, edit and write PDFs in C#", White);
            Tx(_t.Sans, 9.2, M + 74, wy - 70, "No native dependencies. Tagged output. Honest spec coverage.", "#CBD5E1");
            Pill(M + 74, wy - 100, 74, 17, "Get started", "#5EEAD4", Ink, 8.4);
            Pill(M + 156, wy - 100, 78, 17, "Open editor", "#312E81", White, 8.4, null, "#6366F1");
            string[] feats = ["Parse and repair", "Render and edit", "Write tagged PDF"];
            var fw = (ww - 2 * 26 - 2 * 12) / 3;
            for (var i = 0; i < 3; i++)
            {
                var fx = M + 26 + i * (fw + 12);
                Box(_pg, fx, wy - wh + 8, fw, 34, fill: Paper, stroke: Mist, r: 8);
                Box(_pg, fx + 12, wy - wh + 18, 14, 14, fill: i == 0 ? Indigo : i == 1 ? Teal : Violet2, r: 4);
                Tx(_t.SansB, 9, fx + 34, wy - wh + 21, feats[i], Ink);
            }
        });
        T("P", () => Tx(_t.SansB, 7.4, M, wy - wh - 12, "DOCS HOME, BROWSER MOCK", Slate, tracking: 1.2));
    }
}
