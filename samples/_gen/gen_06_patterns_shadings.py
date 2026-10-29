import struct
from theme import *


def coord(v, lo, hi):
    return int(round(min(max((v - lo) / (hi - lo), 0), 1) * 65535))


def pack_vertex(flag, x, y, col, box_):
    x0, y0, x1, y1 = box_
    return struct.pack(">BHH3B", flag, coord(x, x0, x1), coord(y, y0, y1), *[int(c * 255) for c in rgb(col)])


def build(path):
    d = Doc("Patterns and shadings", subject="Tiling patterns and shading types 1-7", lang="en-US")
    t = Type(d)
    pg = d.page(*A4)
    background(pg, P["paper"])
    page_header(pg, t, "Patterns and shadings", "Graphics  /  02", 1, 1)
    pg.text(t.sans, 11, M, pg.h - 106, "Tiling patterns (coloured and uncoloured) and all seven shading types, including Coons and tensor-product meshes.", color=P["slate"])
    cols, tw, th, gap = 3, (pg.w - 2 * M - 2 * 16) / 3, 128, 16
    top = pg.h - 132
    org = [0, 0]

    def tile(i, title, sub, draw):
        c, r = i % cols, i // cols
        x, y = M + c * (tw + gap), top - th - r * (th + 36)
        org[:] = [x, y]
        pg.q()
        pg.rrect(x, y, tw, th, 8)
        pg.clip()
        pg.translate(x, y)
        draw(tw, th)
        pg.Q()
        box(pg, x, y, tw, th, stroke=P["mist"], r=8, lw=0.8)
        pg.text(t.sans_b, 8.5, x, y - 13, title, color=P["ink"])
        pg.text(t.sans, 7.5, x, y - 23, sub, color=P["muted"], kern=False)

    # 0: coloured tiling pattern (hex dots)
    cp = XForm(d, (0, 0, 20, 20))
    cp.fill(P["indigo"]); cp.circle(5, 5, 4); cp.f()
    cp.fill(P["amber"]); cp.circle(15, 15, 4); cp.f()
    cp.stroke(P["mist"]); cp.width(0.6); cp.move(0, 10); cp.line(20, 10); cp.S()
    cref = cp.build("pattern", 20, 20)

    def t0(w, h):
        pg.pattern_fill(cref); pg.rect(0, 0, w, h); pg.f()
    tile(0, "Tiling, PaintType 1", "coloured cell with its own colours", t0)

    # 1: uncoloured tiling pattern painted in two colours
    up = XForm(d, (0, 0, 12, 12))
    up.width(1.2); up.move(0, 0); up.line(12, 12); up.move(-2, 10); up.line(2, 14); up.move(10, -2); up.line(14, 2); up.S()
    uref = up.build("pattern", 12, 12, paint=2)

    def t1(w, h):
        pg.fill(P["paper"]); pg.rect(0, 0, w, h); pg.f()
        pg.pattern_fill(uref, cs="[/Pattern /DeviceRGB]", comps=rgbs(P["rose"])); pg.rect(0, 0, w / 2, h); pg.f()
        pg.pattern_fill(uref, cs="[/Pattern /DeviceRGB]", comps=rgbs(P["teal"])); pg.rect(w / 2, 0, w / 2, h); pg.f()
    tile(1, "Tiling, PaintType 2", "uncoloured cell; colour supplied at fill time", t1)

    # 2: tiling pattern with matrix (rotated, scaled)
    mp = XForm(d, (0, 0, 16, 16), matrix=(0.866, 0.5, -0.5, 0.866, 0, 0))
    mp.fill(P["violet"]); mp.rect(0, 0, 8, 8); mp.f(); mp.fill(P["sky"]); mp.rect(8, 8, 8, 8); mp.f()
    mp.stroke(P["white"]); mp.width(0.8); mp.rect(0, 0, 16, 16); mp.S()
    mref = mp.build("pattern", 16, 16)

    def t2(w, h):
        pg.pattern_fill(mref); pg.rect(0, 0, w, h); pg.f()
    tile(2, "Tiling with /Matrix", "pattern space rotated by 30 degrees", t2)

    # 3 axial
    def t3(w, h):
        pg.sh(axial(0, 0, w, h, [(0, P["indigo_d"]), (0.5, P["violet"]), (1, P["amber"])]))
    tile(3, "Axial (type 2)", "three-stop stitching function", t3)

    # 4 radial concentric
    def t4(w, h):
        pg.sh(radial(w / 2, h / 2, 0, w / 2, h / 2, 70, [(0, "#FFFFFF"), (0.35, P["sky"]), (1, P["indigo_d"])], extend=True))
    tile(4, "Radial (type 3)", "concentric circles, extended", t4)

    # 5 radial focal / cone
    def t5(w, h):
        pg.fill(P["ink"]); pg.rect(0, 0, w, h); pg.f()
        pg.sh(radial(40, 64, 6, w - 40, 64, 40, [(0, P["amber"]), (1, P["rose"])], extend=False))
    tile(5, "Radial, cone", "two circles of different radii, no extension", t5)

    # 6 function-based
    fn = d.stream("/FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1]", b"{ 2 copy mul 1 exch sub 3 1 roll }", compress=False)

    def t6(w, h):
        pg.sh(f"<< /ShadingType 1 /ColorSpace /DeviceRGB /Domain [0 1 0 1] /Matrix [{n(w)} 0 0 {n(h)} 0 0] /Function {fn} 0 R >>")
    tile(6, "Function-based (type 1)", "PostScript calculator function f(x, y)", t6)

    # 7 free-form Gouraud triangles
    def mesh_stream(dict_extra, data):
        return d.stream(dict_extra, data)

    def t7(w, h):
        box_ = (0, 0, w, h)
        cs = [P["rose"], P["amber"], P["indigo"], P["teal"], P["violet"], P["sky"]]
        data = b""
        verts = [(8, 8, cs[0]), (w - 8, 14, cs[1]), (w / 2, h - 8, cs[2]),
                 (w - 8, h - 10, cs[3]), (w * 0.15, h - 20, cs[4])]
        data += pack_vertex(0, *verts[0], box_) + pack_vertex(0, *verts[1], box_) + pack_vertex(0, *verts[2], box_)
        data += pack_vertex(1, *verts[3], box_)      # continues with vb, vc of previous triangle
        data += pack_vertex(2, *verts[4], box_)
        r = mesh_stream(f"/ShadingType 4 /ColorSpace /DeviceRGB /BitsPerCoordinate 16 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 {n(w)} 0 {n(h)} 0 1 0 1 0 1]", data)
        pg.sh(f"{r} 0 R")
    tile(7, "Free-form triangle mesh (type 4)", "Gouraud-shaded triangles with edge flags", t7)

    # 8 lattice mesh
    def t8(w, h):
        rows, cols_ = 5, 6
        data = b""
        pal = [P["indigo"], P["sky"], P["emerald"], P["amber"], P["rose"], P["violet"]]
        for r_ in range(rows):
            for c_ in range(cols_):
                x = 6 + c_ * (w - 12) / (cols_ - 1)
                y = 6 + r_ * (h - 12) / (rows - 1) + 10 * math.sin(c_ * 1.1 + r_)
                data += struct.pack(">HH3B", coord(x, 0, w), coord(min(max(y, 0), h), 0, h), *[int(v * 255) for v in rgb(mix(pal[c_], pal[(c_ + r_) % 6], 0.5))])
        rr = mesh_stream(f"/ShadingType 5 /ColorSpace /DeviceRGB /BitsPerCoordinate 16 /BitsPerComponent 8 /VerticesPerRow {cols_} /Decode [0 {n(w)} 0 {n(h)} 0 1 0 1 0 1]", data)
        pg.sh(f"{rr} 0 R")
    tile(8, "Lattice-form mesh (type 5)", "grid of vertices with bilinear colour", t8)

    def patch(points, colors, w, h, tensor):
        data = struct.pack(">B", 0)
        for (x, y) in points:
            data += struct.pack(">HH", coord(x, 0, w), coord(y, 0, h))
        for c in colors:
            data += bytes(int(v * 255) for v in rgb(c))
        return data

    # 9 Coons patch
    def t9(w, h):
        x0, y0, x1, y1 = 10, 10, w - 10, h - 10
        pts = [(x0, y0), (x0 - 6, y0 + 40), (x0 + 14, y0 + 70), (x0, y1),            # left edge up
               (x0 + 40, y1 + 8), (x1 - 40, y1 - 24), (x1, y1),                       # top edge right
               (x1 + 8, y1 - 40), (x1 - 12, y0 + 40), (x1, y0),                       # right edge down
               (x1 - 40, y0 - 14), (x0 + 40, y0 + 22)]                                # bottom edge left
        data = patch(pts, [P["rose"], P["amber"], P["emerald"], P["indigo"]], w, h, False)
        s = mesh_stream(f"/ShadingType 6 /ColorSpace /DeviceRGB /BitsPerCoordinate 16 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 {n(w)} 0 {n(h)} 0 1 0 1 0 1]", data)
        pg.sh(f"{s} 0 R")
    tile(9, "Coons patch mesh (type 6)", "one patch, four curved boundary edges", t9)

    # 10 tensor patch
    def t10(w, h):
        x0, y0, x1, y1 = 10, 10, w - 10, h - 10
        pts = [(x0, y0), (x0, y0 + 30), (x0, y0 + 60), (x0, y1),
               (x0 + 40, y1), (x1 - 40, y1), (x1, y1),
               (x1, y1 - 30), (x1, y0 + 30), (x1, y0),
               (x1 - 40, y0), (x0 + 40, y0),
               (x0 + 30, y0 + 30), (x0 + 30, y1 - 30), (x1 - 30, y1 - 30), (x1 - 30, y0 + 70)]
        data = patch(pts, [P["indigo"], P["sky"], P["emerald"], P["amber"]], w, h, True)
        s = mesh_stream(f"/ShadingType 7 /ColorSpace /DeviceRGB /BitsPerCoordinate 16 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 {n(w)} 0 {n(h)} 0 1 0 1 0 1]", data)
        pg.sh(f"{s} 0 R")
    tile(10, "Tensor-product patch (type 7)", "sixteen control points incl. interior", t10)

    # 11 shading as a pattern fill, applied to stroked text-like shape
    shp_cache = {}

    def t11(w, h):
        ox, oy = org
        shp = d.add(f"<< /Type /Pattern /PatternType 2 /Matrix [1 0 0 1 {n(ox)} {n(oy)}] /Shading {axial(0, 0, 150, 120, [(0, P['teal']), (1, P['violet'])])} >>")
        pg.fill(P["ink"]); pg.rect(0, 0, w, h); pg.f()
        pg.q(); pg.pattern_fill(shp); pg.pattern_fill(shp, stroke=True)
        pg.width(10); pg.join(1)
        pg.move(20, 30); pg.curve(40, 110, 70, 110, 80, 60); pg.curve(90, 20, 110, 20, w - 20, 100); pg.S(); pg.Q()
    tile(11, "Shading pattern (PatternType 2)", "a shading used as the stroke colour", t11)
    d.save(path)


OUT = "06-patterns-shadings.pdf"
