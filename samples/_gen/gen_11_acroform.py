from theme import *


def build(path):
    d = Doc("Fillable form", subject="AcroForm: text, multiline, comb, checkbox, radio, combo, list, push buttons", lang="en-US")
    t = Type(d)
    helv = d.std("Helvetica")
    helv_b = d.std("Helvetica-Bold")
    zadb = d.std("ZapfDingbats", "")
    pg = d.page(*A4)
    background(pg, P["paper"])
    page_header(pg, t, "Event registration", "Forms  /  AcroForm", 1, 1)
    pg.text(t.sans, 11, M, pg.h - 106, "A fillable PDF form. Every widget has normal (and where relevant down) appearance streams.", color=P["slate"])
    fields = []
    W = pg.w - 2 * M
    fr = helv.ref

    def wapp(w, h, draw, extra_res=""):
        xf = XForm(d, (0, 0, w, h))
        draw(xf)
        return xf.build()

    def field_box(c, w, h, fill=P["white"], stroke=P["muted"], r=3):
        c.fill(fill); c.stroke(stroke); c.width(1); c.rrect(0.5, 0.5, w - 1, h - 1, r); c.B()

    def tx_ap(w, h, val, size=11, multiline=False, comb=0, maxlen=0, quad=0):
        def draw(c):
            field_box(c, w, h)
            c.raw("/Tx BMC q")
            c.raw(f"BT /Helv {size} Tf 0.06 0.09 0.16 rg")
            if multiline:
                y = h - 6 - size
                for ln in wrap(helv, size, val, w - 12, kern=False):
                    c.raw(f"1 0 0 1 6 {n(y)} Tm {pstr(ln)} Tj"); y -= size * 1.3
            elif comb:
                cw = (w - 2) / comb
                for i, ch in enumerate(val):
                    c.raw(f"1 0 0 1 {n(1 + i * cw + (cw - helv.width(ch, size, kern=False)) / 2)} {n((h - size) / 2 + 2.5)} Tm {pstr(ch)} Tj")
            else:
                c.raw(f"1 0 0 1 6 {n((h - size) / 2 + 2.5)} Tm {pstr(val)} Tj")
            c.raw("ET Q EMC")
            if comb:
                c.stroke(P["mist"]); c.width(0.75)
                for i in range(1, comb):
                    x = 1 + i * (w - 2) / comb
                    c.move(x, 3); c.line(x, h - 3); c.S()
        xf = XForm(d, (0, 0, w, h))
        xf.rd["Font"]["Helv"] = f"{fr} 0 R"
        draw(xf)
        return xf.build()

    def label(x, y, s):
        pg.text(t.sans_b, 9, x, y, s.upper(), color=P["slate"], tracking=0.8, kern=False)

    def text_field(name, x, ytop, w, h, val="", multiline=False, comb=0, tooltip=""):
        ff = (4096 if multiline else 0) | ((1 << 24) if comb else 0)
        a = tx_ap(w, h, val, multiline=multiline, comb=comb)
        extra = f"/MaxLen {comb} " if comb else ""
        r = d.add(f"<< /Type /Annot /Subtype /Widget /FT /Tx /T {pstr(name)} /TU {pstr(tooltip or name)} /Ff {ff} {extra}/V {pstr(val)} "
                  f"/Rect [{n(x)} {n(ytop - h)} {n(x + w)} {n(ytop)}] /F 4 /DA (/Helv 11 Tf 0.06 0.09 0.16 rg) /MK << /BC [0.58 0.64 0.72] /BG [1 1 1] >> "
                  f"/P {pg.ref} 0 R /AP << /N {a} 0 R >> >>")
        pg.annots.append(r); fields.append(r)

    half = (W - 18) / 2
    y = pg.h - 150
    label(M, y, "Full name"); label(M + half + 18, y, "Email")
    text_field("fullName", M, y - 6, half, 26, "Ada Lovelace")
    text_field("email", M + half + 18, y - 6, half, 26, "ada@example.org")
    y -= 56
    label(M, y, "Organisation"); label(M + half + 18, y, "Postal code (comb, 8 cells)")
    text_field("org", M, y - 6, half, 26, "Analytical Engines Ltd")
    text_field("postal", M + half + 18, y - 6, half, 26, "SW1A1AA", comb=8)
    y -= 56
    label(M, y, "Comments (multiline)")
    text_field("comments", M, y - 6, W, 70, "Looking forward to the workshop on document engineering. Please seat me near the demo stage and send the slides afterwards.", multiline=True)
    y -= 110

    # combo + list
    def choice(name, x, ytop, w, h, opts, val, combo):
        ff = 131072 if combo else 0
        sel = opts.index(val) if val in opts else 0
        row = 15

        def draw(c):
            field_box(c, w, h)
            c.raw("/Tx BMC q")
            if combo:
                c.raw(f"BT /Helv 11 Tf 0.06 0.09 0.16 rg 1 0 0 1 6 {n((h - 11) / 2 + 2.5)} Tm {pstr(val)} Tj ET")
                c.fill(P["slate"]); c.move(w - 18, h / 2 + 3); c.line(w - 8, h / 2 + 3); c.line(w - 13, h / 2 - 3); c.close(); c.f()
            else:
                for i, o in enumerate(opts):
                    yy = h - 4 - (i + 1) * row
                    if i == sel:
                        c.fill(P["indigo"]); c.rect(2, yy, w - 4, row); c.f()
                    c.raw(f"BT /Helv 10.5 Tf {'1 1 1' if i == sel else '0.06 0.09 0.16'} rg 1 0 0 1 8 {n(yy + 4)} Tm {pstr(o)} Tj ET")
            c.raw("Q EMC")
        xf = XForm(d, (0, 0, w, h)); xf.rd["Font"]["Helv"] = f"{fr} 0 R"; draw(xf); a = xf.build()
        opt = " ".join(pstr(o) for o in opts)
        extra = "" if combo else f"/I [{sel}] "
        r = d.add(f"<< /Type /Annot /Subtype /Widget /FT /Ch /T {pstr(name)} /Ff {ff} /Opt [{opt}] /V {pstr(val)} {extra}"
                  f"/Rect [{n(x)} {n(ytop - h)} {n(x + w)} {n(ytop)}] /F 4 /DA (/Helv 11 Tf 0 g) /MK << /BC [0.58 0.64 0.72] >> /P {pg.ref} 0 R /AP << /N {a} 0 R >> >>")
        pg.annots.append(r); fields.append(r)

    label(M, y, "Ticket type (combo box)"); label(M + half + 18, y, "Track (list box)")
    choice("ticket", M, y - 6, half, 26, ["Standard", "Student", "Speaker", "Sponsor"], "Speaker", True)
    choice("track", M + half + 18, y - 6, half, 56, ["Rendering", "Parsing", "Editing"], "Parsing", False)
    y -= 76

    # checkboxes
    label(M, y, "Options (check boxes)")
    opts = [("newsletter", "Subscribe to the newsletter", True), ("lunch", "Vegetarian lunch", False), ("badge", "Print my badge in advance", True)]
    for i, (nm, text, on) in enumerate(opts):
        cy = y - 24 - i * 24
        s = 16

        def box_ap(checked):
            def draw(c):
                field_box(c, s, s, r=3)
                if checked:
                    c.stroke(P["indigo"]); c.width(2); c.cap(1); c.join(1); c.move(3.5, 8.5); c.line(6.8, 4.5); c.line(12.5, 12); c.S()
            xf = XForm(d, (0, 0, s, s)); xf.rd["Font"]["ZaDb"] = f"{zadb.ref} 0 R"; draw(xf); return xf.build()
        on_a, off_a = box_ap(True), box_ap(False)
        r = d.add(f"<< /Type /Annot /Subtype /Widget /FT /Btn /T {pstr(nm)} /V /{'Yes' if on else 'Off'} /AS /{'Yes' if on else 'Off'} "
                  f"/Rect [{n(M)} {n(cy)} {n(M + s)} {n(cy + s)}] /F 4 /DA (/ZaDb 0 Tf 0 g) /MK << /CA (4) /BC [0.58 0.64 0.72] >> /P {pg.ref} 0 R "
                  f"/AP << /N << /Yes {on_a} 0 R /Off {off_a} 0 R >> /D << /Yes {on_a} 0 R /Off {off_a} 0 R >> >> >>")
        pg.annots.append(r); fields.append(r)
        pg.text(t.sans, 11, M + 26, cy + 3.5, text, color=P["ink"])

    # radio group
    rx = M + half + 18
    label(rx, y, "Day (radio buttons)")
    grp = d.new()
    kids = []
    for i, day in enumerate(["Thursday", "Friday", "Saturday"]):
        cy = y - 24 - i * 24
        s = 16

        def radio_ap(checked):
            def draw(c):
                c.fill(P["white"]); c.stroke(P["muted"]); c.width(1); c.circle(s / 2, s / 2, s / 2 - 0.5); c.B()
                if checked:
                    c.fill(P["indigo"]); c.circle(s / 2, s / 2, 4.2); c.f()
            xf = XForm(d, (0, 0, s, s)); draw(xf); return xf.build()
        on_a, off_a = radio_ap(True), radio_ap(False)
        st = "Yes" if i == 1 else "Off"
        k = d.add(f"<< /Type /Annot /Subtype /Widget /Parent {grp} 0 R /AS /{st if st == 'Off' else day} /Rect [{n(rx)} {n(cy)} {n(rx + s)} {n(cy + s)}] /F 4 "
                  f"/MK << /BC [0.58 0.64 0.72] /CA (l) >> /P {pg.ref} 0 R /AP << /N << /{day} {on_a} 0 R /Off {off_a} 0 R >> >> >>")
        d.put(k, d.objs[k].decode("latin-1").replace("/AS /Yes", f"/AS /{day}"))
        kids.append(k)
        pg.annots.append(k)
        pg.text(t.sans, 11, rx + 26, cy + 3.5, day, color=P["ink"])
    d.put(grp, f"<< /FT /Btn /Ff {32768 | 16384} /T (day) /V /Friday /Kids [{' '.join(f'{k} 0 R' for k in kids)}] >>")
    # make Friday on
    for k in kids:
        s = d.objs[k].decode("latin-1")
        if "/AS /Off" in s and "Friday" in s:
            d.put(k, s.replace("/AS /Off", "/AS /Friday"))
    fields.append(grp)

    # push buttons
    y -= 24 + 3 * 24 + 10
    bw, bh = 130, 32

    def button(name, x, caption, action, primary):
        def draw(c):
            c.fill(P["indigo"] if primary else P["white"]); c.stroke(P["indigo"]); c.width(1.2); c.rrect(0.6, 0.6, bw - 1.2, bh - 1.2, 6); c.B()
            w = helv_b.width(caption, 11, kern=False)
            c.text(helv_b, 11, (bw - w) / 2, (bh - 11) / 2 + 2.5, caption, color=P["white"] if primary else P["indigo"], kern=False)
        a = wapp(bw, bh, draw)
        r = d.add(f"<< /Type /Annot /Subtype /Widget /FT /Btn /Ff 65536 /T {pstr(name)} /Rect [{n(x)} {n(y - bh)} {n(x + bw)} {n(y)}] /F 4 "
                  f"/MK << /CA {pstr(caption)} /BG [1 1 1] >> /DA (/Helv 11 Tf 0 g) /A {action} /P {pg.ref} 0 R /AP << /N {a} 0 R >> >>")
        pg.annots.append(r); fields.append(r)
    button("submit", M, "Submit", "<< /S /SubmitForm /F << /FS /URL /F (https://example.org/submit) >> /Flags 4 >>", True)
    button("reset", M + bw + 14, "Reset form", "<< /S /ResetForm >>", False)
    pg.text(t.sans, 9, M + 2 * bw + 28, y - 20, "Submit posts FDF; Reset clears values.", color=P["muted"])

    d.cat.append(f"/AcroForm << /Fields [{' '.join(f'{f} 0 R' for f in fields)}] /DA (/Helv 0 Tf 0 g) /DR << /Font << /Helv {helv.ref} 0 R /ZaDb {zadb.ref} 0 R >> >> /NeedAppearances false >>")
    d.save(path)


OUT = "11-acroform.pdf"
