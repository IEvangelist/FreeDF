from theme import *


def build(path):
    d = Doc("Optional content layers", subject="OCGs, default visibility, layer order and groups, radio-button group, print/view usage, OCMD, /OC on XObjects and annotations", lang="en-US")
    t = Type(d)
    pg = d.page(*A4)
    background(pg, P["paper"])
    page_header(pg, t, "Layers", "Optional content  /  OCG", 1, 1)
    pg.text(t.sans, 11, M, pg.h - 106, "Toggle layers in the viewer's Layers panel. The alternate palette starts hidden.", color=P["slate"])

    def ocg(name, on=True, usage=""):
        return d.add(f"<< /Type /OCG /Name {utf16(name)} {usage} >>")
    base = ocg("Terrain")
    grid = ocg("Grid")
    routes = ocg("Routes")
    labels = ocg("Labels")
    alt = ocg("Alternate palette (hidden)")
    wm = ocg("Print-only watermark", usage="/Usage << /Print << /PrintState /ON >> /View << /ViewState /OFF >> >>")
    day, night = ocg("Day"), ocg("Night")
    props = {}

    def layer(c, ref):
        k = c.reg("Properties", f"{ref} 0 R")
        c.raw(f"/OC /{k} BDC")

    mx, my, mw, mh = M, 150, pg.w - 2 * M, 520
    pg.q(); pg.rrect(mx, my, mw, mh, 14); pg.clip()
    # sky/day vs night as an OCG radio group
    layer(pg, day); pg.fill("#E0F2FE"); pg.rect(mx, my, mw, mh); pg.f(); pg.raw("EMC")
    layer(pg, night); pg.fill("#0F172A"); pg.rect(mx, my, mw, mh); pg.f(); pg.raw("EMC")
    layer(pg, base)
    for i, (col, amp, yy) in enumerate(((P["emerald"], 40, 470), (P["teal"], 55, 390), (P["indigo"], 45, 300), (P["indigo_d"], 60, 200))):
        pg.q(); pg.gs(ca=0.85); pg.fill(col)
        pg.move(mx, my)
        for k in range(0, 41):
            x = mx + k * mw / 40
            pg.line(x, my + yy - 150 + math.sin(k * 0.45 + i * 1.7) * amp + math.sin(k * 0.17 + i) * 25)
        pg.line(mx + mw, my); pg.close(); pg.f(); pg.Q()
    pg.raw("EMC")
    layer(pg, alt)
    pg.q(); pg.gs(ca=0.6, BM="/Multiply"); pg.fill(P["rose"]); pg.circle(mx + 360, my + 330, 150); pg.f(); pg.fill(P["amber"]); pg.circle(mx + 200, my + 190, 110); pg.f(); pg.Q()
    pg.raw("EMC")
    layer(pg, grid)
    pg.q(); pg.stroke("#FFFFFF"); pg.gs(CA=0.35); pg.width(0.6)
    for i in range(0, 11):
        pg.move(mx + i * mw / 10, my); pg.line(mx + i * mw / 10, my + mh); pg.S()
    for j in range(0, 11):
        pg.move(mx, my + j * mh / 10); pg.line(mx + mw, my + j * mh / 10); pg.S()
    pg.Q(); pg.raw("EMC")
    layer(pg, routes)
    pg.q(); pg.stroke(P["amber"]); pg.width(3.2); pg.cap(1); pg.join(1); pg.dash([9, 6])
    pg.move(mx + 40, my + 90); pg.curve(mx + 120, my + 260, mx + 190, my + 60, mx + 250, my + 230); pg.curve(mx + 300, my + 350, mx + 360, my + 300, mx + 440, my + 440); pg.S()
    pg.dash([]); pg.stroke(P["rose"]); pg.move(mx + 60, my + 420); pg.curve(mx + 150, my + 330, mx + 200, my + 420, mx + 300, my + 380); pg.S(); pg.Q()
    for (px, py, nm) in ((mx + 40, my + 90, "A"), (mx + 250, my + 230, "B"), (mx + 440, my + 440, "C")):
        pg.q(); pg.fill("#FFFFFF"); pg.stroke(P["amber"]); pg.width(2.5); pg.circle(px, py, 8); pg.B(); pg.Q()
    pg.raw("EMC")
    layer(pg, labels)
    for (px, py, nm, left) in ((mx + 40, my + 90, "Harbour", False), (mx + 250, my + 230, "Ridge", False), (mx + 440, my + 440, "Summit", True)):
        w = t.sans_b.width(nm, 10)
        bx = px - 12 - w - 14 if left else px + 12
        box(pg, bx, py - 8, w + 14, 20, fill=P["ink"], r=10)
        pg.text(t.sans_b, 10, bx + 7, py - 1.5, nm, color="#FFFFFF")
    pg.raw("EMC")
    layer(pg, wm)
    pg.q(); pg.gs(ca=0.18); pg.fill("#000000"); pg.translate(mx + mw / 2, my + mh / 2); pg.rotate(35)
    pg.text(t.serif_b, 70, -t.serif_b.width("PRINT COPY", 70) / 2, -22, "PRINT COPY", color="#000000", kern=False); pg.Q()
    pg.raw("EMC")
    pg.Q()

    # a form XObject with /OC and an OCMD requiring Routes AND Labels
    ocmd = d.add(f"<< /Type /OCMD /OCGs [{routes} 0 R {labels} 0 R] /P /AllOn >>")
    xf = XForm(d, (0, 0, 190, 40))
    box(xf, 0, 0, 190, 40, fill=P["indigo"], r=8)
    xf.text(t.sans_b, 10, 14, 15, "Visible when Routes + Labels on", color="#FFFFFF", kern=False)
    xr = xf.build(extra=f"/OC {ocmd} 0 R")
    pg.q(); pg.cm(1, 0, 0, 1, M, 100); pg.xobj(xr); pg.Q()

    # legend
    pg.text(t.sans_b, 8, M + 210, 126, "LAYER USAGE", color=P["indigo"], tracking=1.6, kern=False)
    pg.text(t.sans, 9, M + 210, 108, "On: Terrain, Grid, Routes, Labels. Off: Alternate palette.", color=P["slate"], kern=False)
    pg.text(t.sans, 9, M + 210, 94, "Day / Night is a radio group; the watermark prints only.", color=P["slate"], kern=False)

    order = f"[{base} 0 R {grid} 0 R {routes} 0 R {labels} 0 R {alt} 0 R [(Background) {day} 0 R {night} 0 R] {wm} 0 R]"
    d.cat.append(f"/OCProperties << /OCGs [{base} 0 R {grid} 0 R {routes} 0 R {labels} 0 R {alt} 0 R {wm} 0 R {day} 0 R {night} 0 R] "
                 f"/D << /Name (Default) /BaseState /ON /OFF [{alt} 0 R {night} 0 R {wm} 0 R] /Order {order} /RBGroups [[{day} 0 R {night} 0 R]] "
                 f"/AS [<< /Event /Print /Category [/Print] /OCGs [{wm} 0 R] >> << /Event /View /Category [/View] /OCGs [{wm} 0 R] >>] >> >>")
    d.save(path, version="1.7")


OUT = "15-optional-content.pdf"
