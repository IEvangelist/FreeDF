"""Cross-run veraPDF PDF/UA-1/2 profiles against this repo's PDF/UA validator.

  python tools/ua_crosscheck.py [--strict] <pdf-or-dir> [...]
  python tools/ua_crosscheck.py corpus <dir> <1|2> [--misses] [--strict]

Needs a user-local veraPDF (VERAPDF env var, default ~/tools/verapdf/verapdf.bat) and a Java 11+ runtime
(JAVA_HOME). `corpus` prints total/compared/excluded counts, the file-verdict confusion matrix (the figures quoted
in docs/spec-backlog.md: vera-fail/ours-pass = misses, vera-pass/ours-fail = false positives) and a per-rule overlap
table, because a file-level agreement can hide a rule-level false negative. Files either side could not process land
in an `excluded` bucket (--strict makes that fatal); a failing dotnet run aborts instead of reporting a partial matrix.
veraPDF is only an independent oracle; nothing is vendored.

Reproduce the documented figures with the veraPDF-corpus checkout at <corpus>:
  python tools/ua_crosscheck.py corpus <corpus>/PDF_UA-1 1 --misses
  python tools/ua_crosscheck.py corpus <corpus>/PDF_UA-2 2 --misses
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

# veraPDF rule key (clause-testNumber) -> our rule ids that cover it. Keys absent here are reported as unmapped
# (marked *), never silently counted as caught.
RULE_MAP = {
    1: {
        "5-1": ["PDFUA-XMP-ID-MISSING"], "5-2": ["PDFUA-XMP-ID-PART"], "5-3": ["PDFUA-XMP-ID-PREFIX"], "5-4": ["PDFUA-XMP-ID-PREFIX"], "5-5": ["PDFUA-XMP-ID-PREFIX"],
        "6.2-1": ["PDFUA-MARKINFO"], "7.1-1": ["PDFUA-ARTIFACT-IN-TAGGED"], "7.1-2": ["PDFUA-TAGGED-IN-ARTIFACT"], "7.1-3": ["PDFUA-CONTENT-UNTAGGED"],
        "7.1-4": ["PDFUA-SUSPECTS"], "7.1-5": ["PDFUA-ROLEMAP-MISSING"], "7.1-6": ["PDFUA-ROLEMAP-CIRCULAR"], "7.1-7": ["PDFUA-ROLEMAP-STANDARD"],
        "7.1-8": ["PDFUA-XMP-MISSING"], "7.1-9": ["PDFUA-TITLE"], "7.1-10": ["PDFUA-DISPLAYDOCTITLE"], "7.1-11": ["PDFUA-STRUCTTREE"],
        "7.2-2": ["PDFUA-LANG-OUTLINE"], "7.2-24": ["PDFUA-LANG-ANNOT"], "7.2-25": ["PDFUA-LANG-TU"], "7.2-29": ["PDFUA-LANG-INVALID"],
        "7.2-33": ["PDFUA-LANG-XMP"], "7.2-34": ["PDFUA-LANG"], "7.2-42": ["PDFUA-TABLE-IRREGULAR"], "7.2-43": ["PDFUA-TABLE-IRREGULAR"],
        "7.3-1": ["PDFUA-ALT-MISSING"], "7.5-1": ["PDFUA-TABLE-HEADER-CONNECTION"],
        "7.18.1-1": ["PDFUA-ANNOT-STRUCT", "PDFUA-ANNOT-UNTAGGED"], "7.18.1-2": ["PDFUA-ANNOT-CONTENTS"], "7.18.1-3": ["PDFUA-FORM-TU"],
        "7.18.2-1": ["PDFUA-ANNOT-TRAPNET"], "7.18.3-1": ["PDFUA-TABS"], "7.18.5-1": ["PDFUA-LINK-STRUCT", "PDFUA-ANNOT-UNTAGGED"],
        "7.18.5-2": ["PDFUA-LINK-DESCRIPTION"], "7.18.8-1": ["PDFUA-ANNOT-PRINTERMARK"], "7.20-1": ["PDFUA-XOBJECT-REF"], "7.20-2": ["PDFUA-FORM-REUSED-TAGGED"],
        "7.21.3.1-1": ["PDFUA-FONT-CMAP-SYSTEMINFO"], "7.21.3.2-1": ["PDFUA-FONT-CIDTOGID"], "7.21.3.3-1": ["PDFUA-FONT-CMAP-EMBEDDED"],
        "7.21.3.3-2": ["PDFUA-FONT-CMAP-WMODE"], "7.21.4.1-1": ["PDFUA-FONT-EMBEDDED"], "7.21.6-2": ["PDFUA-FONT-TRUETYPE-ENCODING"],
        "7.21.6-3": ["PDFUA-FONT-TRUETYPE-ENCODING"], "7.21.7-1": ["PDFUA-FONT-TOUNICODE"],
    },
    2: {
        "5-2": ["PDFUA-XMP-ID-PART"], "5-4": ["PDFUA-XMP-ID-PREFIX"], "5-5": ["PDFUA2-XMP-ID-REV"], "6.1-1": ["PDFUA2-VERSION"], "6.2-1": ["PDFUA-MARKINFO"],
        "8.2.1-1": ["PDFUA-STRUCTTREE"], "8.2.2-1": ["PDFUA-CONTENT-UNTAGGED"], "8.2.4-1": ["PDFUA2-ROLEMAP-MISSING"], "8.2.4-2": ["PDFUA2-ROLEMAP-CIRCULAR"],
        "8.2.4-3": ["PDFUA2-ROLEMAP-SAME-NS"], "8.2.4-4": ["PDFUA2-ROLEMAP-MISSING"], "8.2.5.2-1": ["PDFUA2-DOCUMENT-ROOT"], "8.2.5.2-2": ["PDFUA2-DOCUMENT-NS"],
        "8.2.5.12-1": ["PDFUA2-HEADING-H"], "8.2.5.14-1": ["PDFUA2-NOTE"], "8.2.5.20-1": ["PDFUA-LINK-STRUCT"], "8.2.5.20-2": ["PDFUA2-LINK-TARGETS"], "8.2.5.25-1": ["PDFUA2-LIST-NUMBERING"], "8.2.5.25-2": ["PDFUA2-LI-CONTENT"],
        "8.2.5.26-3": ["PDFUA-TABLE-IRREGULAR"], "8.2.5.26-4": ["PDFUA-TABLE-IRREGULAR"], "8.2.5.27-1": ["PDFUA2-CAPTION-POSITION"],
        "8.2.5.28.2-1": ["PDFUA-ALT-MISSING"], "8.2.5.29-1": ["PDFUA2-MATH-PARENT"], "8.8-1": ["PDFUA2-DEST-NOT-STRUCTURE"], "8.8-2": ["PDFUA2-DEST-NOT-STRUCTURE"],
        "8.9.2.2-1": ["PDFUA2-ANNOT-ARTIFACT"], "8.9.2.4.9-1": ["PDFUA2-ANNOT-POPUP"], "8.9.2.4.10-1": ["PDFUA2-FILEATTACH-AFRELATIONSHIP"],
        "8.9.2.4.15-1": ["PDFUA-ANNOT-TRAPNET"], "8.9.3.3-1": ["PDFUA-TABS"], "8.10.1-2": ["PDFUA2-FORM-WIDGETS"], "8.11.1-1": ["PDFUA-TITLE"],
        "8.11.1-2": ["PDFUA-XMP-MISSING"], "8.11.2-1": ["PDFUA-DISPLAYDOCTITLE"], "8.14.1-1": ["PDFUA2-FILESPEC-DESC"],
    },
}


def collect(args):
    for a in args:
        if os.path.isdir(a):
            yield from sorted(glob.glob(os.path.join(a, "**", "*.pdf"), recursive=True))
        else:
            yield a


def parse_report(out):
    if "<?xml" not in out:
        return None
    return ET.fromstring(out[out.index("<?xml"):])


def vera(path, part):
    try:
        tree = parse_report(subprocess.run([VERA, "-f", FLAVOURS[part], path], capture_output=True, text=True).stdout)
    except ET.ParseError:
        tree = None
    rep = tree.find(".//validationReport") if tree is not None else None
    if rep is None:
        return None, ["<veraPDF produced no report>"]
    failed = [f"{r.get('clause')}-{r.get('testNumber')}" for r in rep.iter("rule") if r.get("status") == "failed"]
    return rep.get("isCompliant") == "true", failed


def ours(files):
    proj = os.path.join(ROOT, "tools", "FreeDF.UaDump", "FreeDF.UaDump.csproj")
    run = subprocess.run(["dotnet", "run", "-c", "Release", "--project", proj, "--", *files], capture_output=True, text=True)
    if run.returncode != 0:
        sys.exit(f"FreeDF.UaDump failed with exit code {run.returncode}; refusing to report a partial matrix:\n{run.stdout[-2000:]}\n{run.stderr[-2000:]}")
    res = {}
    for line in run.stdout.splitlines():
        if line.startswith("{"):
            j = json.loads(line)
            res[(os.path.abspath(j["file"]), j.get("part"))] = j
    return res


def vera_batch(directory, part):
    """One veraPDF run over a directory tree; returns {abs path: (compliant, [failed rules])}."""
    run = subprocess.run([VERA, "-r", "-f", FLAVOURS[part], directory], capture_output=True, text=True)
    tree = parse_report(run.stdout)
    if tree is None:
        sys.exit(f"veraPDF produced no report (exit {run.returncode}):\n{run.stderr[-2000:]}")
    res = {}
    for job in tree.iter("job"):
        name = os.path.abspath(job.find("item/name").text)
        rep = job.find("validationReport")
        if rep is None:
            res[name] = (None, ["<veraPDF produced no report>"])
            continue
        failed = sorted({f"{r.get('clause')}-{r.get('testNumber')}" for r in rep.iter("rule") if r.get("status") == "failed"})
        res[name] = (rep.get("isCompliant") == "true", failed)
    return res


def corpus(directory, part):
    truth = vera_batch(directory, part)
    mine = ours(sorted(truth))
    cells, excluded = {}, []
    for f, (ok, failed) in sorted(truth.items()):
        j = mine.get((f, part), {})
        rel = os.path.relpath(f, directory)
        if ok is None:
            excluded.append((rel, f"veraPDF: {failed}"))
        elif "compliant" not in j:
            excluded.append((rel, f"ours: {j.get('error', 'no result')}"))
        else:
            key = ("vera-pass" if ok else "vera-fail", "ours-pass" if j["compliant"] else "ours-fail")
            cells.setdefault(key, []).append((rel, failed, j.get("errors")))
    compared = sum(len(v) for v in cells.values())
    print(f"total={len(truth)} compared={compared} excluded={len(excluded)}")
    for k, v in sorted(cells.items()):
        print(f"{k[0]} / {k[1]}: {len(v)}")
    for rel, why in excluded:
        print(f"  EXCLUDED {rel}: {why}")
    for rel, failed, errs in cells.get(("vera-pass", "ours-fail"), []):
        print(f"  FALSE-POSITIVE {rel}: ours={errs}")
    if "--misses" in sys.argv:
        for rel, failed, errs in cells.get(("vera-fail", "ours-pass"), []):
            print(f"  MISS {rel}: vera={failed}")

    # A file-level agreement can hide a rule-level false negative: ours may fail the file for a different reason.
    rule_map = RULE_MAP[part]
    per_rule = {}
    for key in (("vera-fail", "ours-fail"), ("vera-fail", "ours-pass")):
        for rel, failed, errs in cells.get(key, []):
            for r in failed:
                row = per_rule.setdefault(r, {"files": 0, "rule_caught": 0, "verdict_only": 0, "unmapped": r not in rule_map})
                row["files"] += 1
                if set(errs or []) & set(rule_map.get(r, [])):
                    row["rule_caught"] += 1
                elif key[1] == "ours-fail":
                    row["verdict_only"] += 1
    print("per-rule overlap (veraPDF rule: failing files / caught by the mapped rule / failed only through other rules; * = unmapped)")
    for r, row in sorted(per_rule.items()):
        print(f"  {r}{'*' if row['unmapped'] else ''}: {row['files']} / {row['rule_caught']} / {row['verdict_only']}")
    if excluded and "--strict" in sys.argv:
        sys.exit(2)


def main():
    args = [a for a in sys.argv[1:] if a != "--strict"]
    if args and args[0] == "corpus":
        corpus(os.path.abspath(args[1]), int(args[2]))
        return
    files = [os.path.abspath(f) for f in collect(args)]
    mine = ours(files)
    excluded = 0
    for f in files:
        for part in (1, 2):
            compliant, failed = vera(f, part)
            j = mine.get((f, part), {})
            if compliant is None or "compliant" not in j:
                excluded += 1
                print(f"EXCLUDED {os.path.basename(f)} UA-{part}: vera={failed} ours={j.get('error', 'no result')}")
                continue
            ok = j["compliant"]
            print(f"{'AGREE' if ok == compliant else 'DISAGREE'} {os.path.basename(f)} UA-{part}: ours={'pass' if ok else 'fail'} {j.get('errors')} | vera={'pass' if compliant else 'fail'} {failed}")
    print(f"files={len(files)} excluded={excluded}")
    if excluded and "--strict" in sys.argv:
        sys.exit(2)


if __name__ == "__main__":
    main()
