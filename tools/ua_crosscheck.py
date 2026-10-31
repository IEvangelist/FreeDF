"""Cross-run veraPDF PDF/UA-1/2 profiles against this repo's PDF/UA validator.

  python tools/ua_crosscheck.py <pdf-or-dir> [...]
  python tools/ua_crosscheck.py corpus <dir> <1|2> [--misses]     confusion matrix vs veraPDF over a corpus (e.g. veraPDF-corpus)

Needs a user-local veraPDF (VERAPDF env var, default ~/tools/verapdf/verapdf.bat) and a Java 11+ runtime
(JAVA_HOME). Prints, per file and PDF/UA part, our error rule ids next to veraPDF's failed rules so every
disagreement can be reconciled by hand. veraPDF is only an independent oracle; nothing is vendored.
"""
import glob
import json
import os
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VERA = os.environ.get("VERAPDF", os.path.expanduser(r"~\tools\verapdf\verapdf.bat"))
FLAVOURS = {1: "ua1", 2: "ua2"}


def collect(args):
    for a in args:
        if os.path.isdir(a):
            yield from sorted(glob.glob(os.path.join(a, "**", "*.pdf"), recursive=True))
        else:
            yield a


def vera(path, part):
    out = subprocess.run([VERA, "-f", FLAVOURS[part], path], capture_output=True, text=True).stdout
    try:
        tree = ET.fromstring(out[out.index("<?xml"):] if "<?xml" in out else out)
    except ET.ParseError:
        return None, ["<veraPDF failed>"]
    rep = tree.find(".//validationReport")
    if rep is None:
        return None, ["<no report>"]
    failed = [f"{r.get('clause')}-{r.get('testNumber')}" for r in rep.iter("rule") if r.get("status") == "failed"]
    return rep.get("isCompliant") == "true", failed


def ours(files):
    proj = os.path.join(ROOT, "tools", "AdoBye.UaDump", "AdoBye.UaDump.csproj")
    out = subprocess.run(["dotnet", "run", "-c", "Release", "--project", proj, "--", *files],
                         capture_output=True, text=True).stdout
    res = {}
    for line in out.splitlines():
        if line.startswith("{"):
            j = json.loads(line)
            res[(os.path.abspath(j["file"]), j.get("part"))] = j
    return res


def vera_batch(directory, part):
    """One veraPDF run over a directory tree; returns {abs path: (compliant, [failed rules])}."""
    out = subprocess.run([VERA, "-r", "-f", FLAVOURS[part], directory], capture_output=True, text=True).stdout
    tree = ET.fromstring(out[out.index("<?xml"):])
    res = {}
    for job in tree.iter("job"):
        name = job.find("item/name").text
        rep = job.find("validationReport")
        if rep is None:
            res[os.path.abspath(name)] = (None, ["<no report>"])
            continue
        failed = sorted({f"{r.get('clause')}-{r.get('testNumber')}" for r in rep.iter("rule") if r.get("status") == "failed"})
        res[os.path.abspath(name)] = (rep.get("isCompliant") == "true", failed)
    return res


def corpus(directory, part):
    """Confusion matrix of our verdict against veraPDF over a corpus (veraPDF is the oracle)."""
    truth = vera_batch(directory, part)
    mine = ours(sorted(truth))
    cells = {}
    for f, (ok, failed) in sorted(truth.items()):
        j = mine.get((f, part), {})
        if ok is None or "compliant" not in j:
            continue
        key = ("vera-pass" if ok else "vera-fail", "ours-pass" if j["compliant"] else "ours-fail")
        cells.setdefault(key, []).append((os.path.relpath(f, directory), failed, j.get("errors")))
    for k, v in sorted(cells.items()):
        print(f"{k[0]} / {k[1]}: {len(v)}")
    for rel, failed, errs in cells.get(("vera-pass", "ours-fail"), []):
        print(f"  FALSE-POSITIVE {rel}: ours={errs}")
    if "--misses" in sys.argv:
        for rel, failed, errs in cells.get(("vera-fail", "ours-pass"), []):
            print(f"  MISS {rel}: vera={failed}")


def main():
    if sys.argv[1] == "corpus":
        corpus(os.path.abspath(sys.argv[2]), int(sys.argv[3]))
        return
    files = [os.path.abspath(f) for f in collect(sys.argv[1:])]
    mine = ours(files)
    for f in files:
        for part in (1, 2):
            compliant, failed = vera(f, part)
            j = mine.get((f, part), {})
            ok = j.get("compliant")
            flag = "AGREE" if ok == compliant else "DISAGREE"
            print(f"{flag} {os.path.basename(f)} UA-{part}: ours={'pass' if ok else 'fail'} {j.get('errors', j.get('error'))} | vera={'pass' if compliant else 'fail'} {failed}")


if __name__ == "__main__":
    main()
