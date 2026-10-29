import os
import pikepdf
from theme import *


def secure_page(title, kicker, badge, rows, path_plain):
    d = Doc(title, subject=kicker, lang="en-US")
    t = Type(d)
    pg = d.page(*A4)
    background(pg, P["paper"])
    box(pg, 0, pg.h - 250, pg.w, 250, fill=P["ink"])
    pg.q(); pg.rect(0, pg.h - 250, pg.w, 250); pg.clip()
    pg.gs(ca=0.5)
    pg.sh(radial(pg.w - 40, pg.h - 20, 0, pg.w - 40, pg.h - 20, 420, [(0, P["indigo"]), (1, P["ink"])]))
    pg.Q()
    pg.text(t.sans_b, 9, M, pg.h - 60, kicker.upper(), color="#A5B4FC", tracking=1.8, kern=False)
    pg.text(t.serif_b, 40, M, pg.h - 130, title, color="#FFFFFF")
    w = t.sans_b.width(badge, 9, tracking=1.4) + 24
    box(pg, M, pg.h - 200, w, 24, fill=P["rose"], r=12)
    pg.text(t.sans_b, 9, M + 12, pg.h - 192.5, badge, color="#FFFFFF", tracking=1.4, kern=False)
    y = pg.h - 310
    for head, body in rows:
        pg.text(t.sans_b, 8, M, y, head.upper(), color=P["indigo"], tracking=1.6, kern=False)
        y = paragraph(pg, t.serif, 12.5, M, y - 20, pg.w - 2 * M - 40, body, color=P["ink"], leading=19) - 22
    d.save(path_plain)


def encrypt(src, dst, user, owner, allow=None, R=6):
    with pikepdf.open(src) as pdf:
        pdf.save(dst, encryption=pikepdf.Encryption(user=user, owner=owner, R=R,         allow=allow or pikepdf.Permissions()))
    os.remove(src)
