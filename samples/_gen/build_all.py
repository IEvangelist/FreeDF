"""Regenerates the samples: `python build_all.py [name-fragment]`. Each gen_*.py exposes OUT (file name) and build(path)."""
import glob
import importlib
import os
import sys

here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, here)
os.chdir(here)
only = sys.argv[1] if len(sys.argv) > 1 else ""
for f in sorted(glob.glob(os.path.join(here, "gen_*.py"))):
    mod = os.path.splitext(os.path.basename(f))[0]
    if only in mod:
        m = importlib.import_module(mod)
        m.build(os.path.join(here, "..", m.OUT))
        print("built", m.OUT)
