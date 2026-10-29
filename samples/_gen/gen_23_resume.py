from theme import *


def build(path):
    d = Doc("Résumé: Ada Marlowe", author="Ada Marlowe", subject="Two-column résumé: sidebar, skill meters, timeline", keywords="resume, CV, engineer", lang="en-US")
    t = Type(d)
    pg = d.page(*A4)
    background(pg, P["white"])
    SW = 196
    box(pg, 0, 0, SW, pg.h, fill=P["ink"])
    pg.q(); pg.rect(0, 0, SW, pg.h); pg.clip(); pg.gs(ca=0.35)
    pg.sh(radial(0, pg.h, 0, 0, pg.h, 340, [(0, P["indigo"]), (1, P["ink"])]))
    pg.Q()
    # monogram
    pg.q(); pg.fill(P["white"]); pg.circle(SW / 2, pg.h - 92, 40); pg.f(); pg.Q()
    pg.q(); pg.fill(P["indigo"]); pg.circle(SW / 2, pg.h - 92, 36); pg.f(); pg.Q()
    pg.text(t.serif_b, 30, SW / 2 - t.serif_b.width("AM", 30) / 2, pg.h - 102, "AM", color="#FFFFFF")

    sx, sw = 24, SW - 48

    def side_head(y, s):
        pg.text(t.sans_b, 8, sx, y, s.upper(), color="#A5B4FC", tracking=1.8, kern=False)
        pg.q(); pg.stroke(P["indigo"]); pg.width(1.5); pg.move(sx, y - 6); pg.line(sx + 18, y - 6); pg.S(); pg.Q()
    y = pg.h - 170
    side_head(y, "Contact")
    for j, (k, v) in enumerate((("Email", "ada@marlowe.example"), ("Phone", "+44 20 7946 0123"), ("Web", "marlowe.example"), ("Based in", "Bristol, UK"))):
        pg.text(t.sans, 7.5, sx, y - 24 - j * 28, k.upper(), color="#94A3B8", tracking=1, kern=False)
        pg.text(t.sans, 9.5, sx, y - 36 - j * 28, v, color="#F1F5F9", kern=False)
    y -= 150
    side_head(y, "Skills")
    skills = [("PDF & graphics", 0.95), ("C# / .NET", 0.9), ("Python", 0.82), ("Typography", 0.78), ("Accessibility", 0.7), ("Rust", 0.55)]
    for j, (k, v) in enumerate(skills):
        yy = y - 26 - j * 30
        pg.text(t.sans, 9.5, sx, yy, k, color="#E2E8F0")
        box(pg, sx, yy - 11, sw, 4, fill="#334155", r=2)
        pg.q(); pg.rrect(sx, yy - 11, sw * v, 4, 2); pg.clip()
        pg.sh(axial(sx, 0, sx + sw, 0, [(0, P["indigo"]), (1, P["sky"])]))
        pg.Q()
    y -= 230
    side_head(y, "Languages")
    for j, (k, v) in enumerate((("English", "Native"), ("French", "Fluent"), ("German", "Conversational"))):
        pg.text(t.sans_b, 9.5, sx, y - 24 - j * 18, k, color="#F1F5F9")
        pg.text(t.sans, 9, sx + 70, y - 24 - j * 18, v, color="#94A3B8")
    y -= 110
    side_head(y, "Interests")
    paragraph(pg, t.sans, 9, sx, y - 24, sw, "Letterpress, ultramarathons, hand-drawn maps and the odd 1990s demoscene remake.", color="#CBD5E1", leading=13.5)

    # main column
    mx = SW + 40
    mw = pg.w - mx - 40
    pg.text(t.serif_b, 40, mx, pg.h - 82, "Ada Marlowe", color=P["ink"])
    pg.text(t.sans_b, 11, mx, pg.h - 106, "SENIOR GRAPHICS ENGINEER", color=P["indigo"], tracking=2, kern=False)
    paragraph(pg, t.serif, 11, mx, pg.h - 138, mw,
              "Engineer who turns specifications into pixels. Twelve years building document renderers, font pipelines and print tooling; happiest where PDF operators meet careful typography.",
              color=P["slate"], leading=16.5)

    def main_head(y, s):
        pg.text(t.sans_b, 9, mx, y, s.upper(), color=P["ink"], tracking=1.8, kern=False)
        rule(pg, mx + t.sans_b.width(s.upper(), 9, tracking=1.8) + 10, y + 3, mx + mw)

    y = pg.h - 222
    main_head(y, "Experience")
    jobs = [("2021 – now", "Principal Engineer, Graphite Docs", "Led the rendering core rewrite: 4× faster page rasterisation, full transparency model, CFF and TrueType font stacks. Mentored a team of six.", P["indigo"]),
            ("2017 – 2021", "Staff Engineer, Quill & Co.", "Designed the colour-managed PDF export path (ICC, DeviceN, soft masks) used for 40 M documents a year.", P["teal"]),
            ("2013 – 2017", "Software Engineer, Foundry Print", "Built imposition and trapping tools; shipped a PostScript-to-PDF distiller and an accessibility checker.", P["violet"]),
            ("2011 – 2013", "Junior Developer, Pixelbox", "Maintained a Flash-to-PDF converter, then migrated it to a cross-platform C++ library.", P["amber"])]
    y -= 12
    pg.q(); pg.stroke(P["mist"]); pg.width(1.5); pg.move(mx + 4, y - 18); pg.line(mx + 4, y - 4 * 92 + 62); pg.S(); pg.Q()
    for (when, role, desc, c) in jobs:
        pg.q(); pg.fill("#FFFFFF"); pg.stroke(c); pg.width(2.2); pg.circle(mx + 4, y - 12, 4.2); pg.B(); pg.Q()
        pg.text(t.sans_b, 8, mx + 20, y - 8, when.upper(), color=c, tracking=1.2, kern=False)
        pg.text(t.serif_b, 13, mx + 20, y - 26, role, color=P["ink"])
        paragraph(pg, t.sans, 9.5, mx + 20, y - 43, mw - 20, desc, color=P["slate"], leading=13.5)
        y -= 92
    y -= 6
    main_head(y, "Education")
    for j, (yr, deg, where) in enumerate((("2008 – 2011", "BSc Computer Science (First)", "University of Bath"), ("2007", "A-levels: Maths, Further Maths, Art", "Redland High School"))):
        yy = y - 26 - j * 36
        pg.text(t.sans_b, 8, mx, yy, yr.upper(), color=P["muted"], tracking=1.2, kern=False)
        pg.text(t.sans_b, 10.5, mx + 84, yy, deg, color=P["ink"])
        pg.text(t.sans, 9.5, mx + 84, yy - 14, where, color=P["slate"])
    y -= 120
    main_head(y, "Selected projects")
    for j, (name, desc) in enumerate((("csharp-pdf", "A from-scratch PDF parser, renderer and editor in C#."), ("inkwell", "Font subsetter and CFF-to-glyf converter."), ("loupe", "Pixel-diff harness for cross-renderer comparison."))):
        yy = y - 26 - j * 20
        pg.text(t.mono, 9.5, mx, yy, name, color=P["indigo"], kern=False)
        pg.text(t.sans, 9.5, mx + 84, yy, desc, color=P["slate"])
    d.save(path)


OUT = "23-resume.pdf"
