"""Structural validation of every sample with pikepdf/qpdf (needs the passwords for the encrypted ones)."""
import glob
import os
import sys
import pikepdf

PASSWORDS = {"17-encrypted-user-password": "user123", "18-encrypted-owner-restricted": "owner123"}
here = os.path.dirname(os.path.abspath(__file__))
bad = 0
for f in sorted(glob.glob(os.path.join(here, "..", "*.pdf"))):
    name = os.path.splitext(os.path.basename(f))[0]
    try:
        with pikepdf.open(f, password=PASSWORDS.get(name, "")) as p:
            import io, logging
            records = []
            h = logging.Handler(); h.emit = lambda r: records.append(r.getMessage())
            logging.getLogger("pikepdf").addHandler(h); logging.getLogger("pikepdf").setLevel(logging.WARNING)
            for pg in p.pages:
                pg.contents_coalesce() if False else None
            for o in p.objects:
                pass
            p.save(io.BytesIO())
            problems = records
            print(("OK   " if not problems else "WARN ") + name, len(p.pages), "pages", f"{os.path.getsize(f) / 1024:.0f} KB")
            for pr in problems[:5]:
                print("      ", pr)
            bad += bool(problems)
    except Exception as e:
        print("FAIL", name, e)
        bad += 1
sys.exit(1 if bad else 0)
