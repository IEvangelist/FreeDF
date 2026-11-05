"""Cross-run veraPDF PDF/UA-1/2 profiles against this repo's PDF/UA validator.

  python tools/ua_crosscheck.py [--strict] <pdf-or-dir> [...]
  python tools/ua_crosscheck.py corpus <dir> <1|2> [--profile <xml>] [--misses] [--strict]

Needs a user-local veraPDF (VERAPDF env var, default ~/tools/verapdf/verapdf.bat) and a Java 11+ runtime
(JAVA_HOME). `corpus` prints total/compared/excluded counts, the file-verdict confusion matrix (the figures quoted
in docs/spec-backlog.md: vera-fail/ours-pass = misses, vera-pass/ours-fail = false positives) and a per-rule overlap
table, because a file-level agreement can hide a rule-level false negative. Files either side could not process land
in an `excluded` bucket (--strict makes that fatal); a failing dotnet run aborts instead of reporting a partial matrix.
Mapped family-ID overlap is not proof of an exact predicate. In veraPDF 1.30.3, -f ua2 selects the combined
ISO 32005 profile; use --profile with the standalone XML to measure its 92 UA-2 rules separately.
veraPDF is an independent oracle; nothing is vendored.

Reproduce the documented figures with the veraPDF-corpus checkout at <corpus>:
  python tools/ua_crosscheck.py corpus <corpus>/PDF_UA-1 1 --misses
  python tools/ua_crosscheck.py corpus <corpus>/PDF_UA-2 2 --misses

Tool regressions: python -m unittest discover -s tools -p "test_ua_crosscheck.py"
"""
import argparse
import glob
import json
import os
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VERA = os.environ.get("VERAPDF", os.path.expanduser(r"~\tools\verapdf\verapdf.bat"))
FLAVOURS = {1: "ua1", 2: "ua2"}
COMMAND_LINE_LIMIT = 24000

# veraPDF rule key (clause-testNumber) -> related implemented finding families, not complete predicates.
# Incomplete-inspection findings deliberately do not count as overlap. Absent keys remain unmapped (*).
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
        "7.18.6.2-1": ["PDFUA-MEDIA-CT"], "7.18.6.2-2": ["PDFUA-MEDIA-ALT"],
        "7.21.3.1-1": ["PDFUA-FONT-CMAP-SYSTEMINFO"], "7.21.3.2-1": ["PDFUA-FONT-CIDTOGID"], "7.21.3.3-1": ["PDFUA-FONT-CMAP-EMBEDDED"],
        "7.21.3.3-2": ["PDFUA-FONT-CMAP-WMODE"], "7.21.4.1-1": ["PDFUA-FONT-EMBEDDED"], "7.21.6-2": ["PDFUA-FONT-TRUETYPE-ENCODING", "PDFUA-FONT-TRUETYPE-DIFFERENCES"],
        "7.21.3.3-3": ["PDFUA-FONT-CMAP-REFERENCE"],
        "7.21.6-3": ["PDFUA-FONT-TRUETYPE-ENCODING"], "7.21.6-4": ["PDFUA-FONT-SYMBOLIC-CMAP"], "7.21.7-1": ["PDFUA-FONT-TOUNICODE"],
        "7.21.4.2-1": ["PDFUA-FONT-CHARSET-LIST"], "7.21.4.2-2": ["PDFUA-FONT-CIDSET-LIST"],
        "7.21.5-1": ["PDFUA-FONT-WIDTH"], "7.21.7-2": ["PDFUA-FONT-TOUNICODE-VALUE"], "7.21.8-1": ["PDFUA-FONT-NOTDEF"],
        "7.10-1": ["PDFUA-OC-NAME"], "7.10-2": ["PDFUA-OC-AS"],
        "7.11-1": ["PDFUA-FILESPEC-UF"], "7.16-1": ["PDFUA-ENCRYPT-ACCESS"],
        "7.15-1": ["PDFUA-XFA-DYNAMIC"],
    },
    2: {
        "5-2": ["PDFUA-XMP-ID-PART"], "5-4": ["PDFUA-XMP-ID-PREFIX"], "5-5": ["PDFUA2-XMP-ID-REV"], "6.1-1": ["PDFUA2-VERSION"], "6.2-1": ["PDFUA-MARKINFO"],
        "8.2.1-1": ["PDFUA-STRUCTTREE"], "8.2.2-1": ["PDFUA-CONTENT-UNTAGGED"], "8.2.4-1": ["PDFUA2-ROLEMAP-MISSING"], "8.2.4-2": ["PDFUA2-ROLEMAP-CIRCULAR"],
        "8.2.4-3": ["PDFUA2-ROLEMAP-SAME-NS"], "8.2.4-4": ["PDFUA2-ROLEMAP-MISSING"], "8.2.5.2-1": ["PDFUA2-DOCUMENT-ROOT"], "8.2.5.2-2": ["PDFUA2-DOCUMENT-NS"],
        "8.2.5.12-1": ["PDFUA2-HEADING-H"], "8.2.5.14-1": ["PDFUA2-NOTE"], "8.2.5.20-1": ["PDFUA-LINK-STRUCT"], "8.2.5.20-2": ["PDFUA2-LINK-TARGETS"], "8.2.5.25-1": ["PDFUA2-LIST-NUMBERING"], "8.2.5.25-2": ["PDFUA2-LI-CONTENT"],
        "8.2.5.14-4": ["PDFUA2-FENOTE-TYPE"],
        "8.2.5.26-2": ["PDFUA2-TABLE-ROWSPAN-END"],
        "8.2.5.26-3": ["PDFUA-TABLE-IRREGULAR"], "8.2.5.26-4": ["PDFUA-TABLE-IRREGULAR"], "8.2.5.27-1": ["PDFUA2-CAPTION-POSITION"],
        "8.2.5.8-1": ["PDFUA2-TOCI-REF"],
        "8.2.5.28.2-1": ["PDFUA-ALT-MISSING"], "8.2.5.29-1": ["PDFUA2-MATH-PARENT"], "8.8-1": ["PDFUA2-DEST-NOT-STRUCTURE"], "8.8-2": ["PDFUA2-DEST-NOT-STRUCTURE"],
        "8.9.2.2-1": ["PDFUA2-ANNOT-ARTIFACT"], "8.9.2.2-2": ["PDFUA2-ANNOT-ARTIFACT"], "8.9.2.4.9-1": ["PDFUA2-ANNOT-POPUP"], "8.9.2.4.10-1": ["PDFUA2-FILEATTACH-AFRELATIONSHIP"],
        "8.9.2.4.14-1": ["PDFUA-ANNOT-PRINTERMARK"], "8.9.2.4.15-1": ["PDFUA-ANNOT-TRAPNET"], "8.9.3.3-1": ["PDFUA-TABS"], "8.10.1-2": ["PDFUA2-FORM-WIDGETS"], "8.11.1-1": ["PDFUA-TITLE"],
        "8.4.5.4-2": ["PDFUA-FONT-CMAP-WMODE"], "8.4.5.4-3": ["PDFUA-FONT-CMAP-REFERENCE"],
        "8.4.5.7-2": ["PDFUA-FONT-TRUETYPE-ENCODING", "PDFUA-FONT-TRUETYPE-DIFFERENCES"], "8.4.5.7-3": ["PDFUA-FONT-TRUETYPE-ENCODING"],
        "8.4.5.7-4": ["PDFUA-FONT-SYMBOLIC-CMAP"],
        "8.4.5.6-1": ["PDFUA-FONT-WIDTH"], "8.4.5.8-2": ["PDFUA-FONT-TOUNICODE-VALUE"], "8.4.5.9-1": ["PDFUA-FONT-NOTDEF"],
        "8.11.1-2": ["PDFUA-XMP-MISSING"], "8.11.2-1": ["PDFUA-DISPLAYDOCTITLE"], "8.14.1-1": ["PDFUA2-FILESPEC-DESC"],
        "8.7-1": ["PDFUA-OC-NAME"],
        "8.4.4-2": ["PDFUA-LANG-INVALID"],
        "8.6-1": ["PDFUA2-TEXT-STRING-PUA"],
        "5-1": ["PDFUA-XMP-ID-MISSING"], "5-3": ["PDFUA-XMP-ID-PREFIX"],
        "8.4.4-1": ["PDFUA-LANG"],
        "8.4.5.3.1-1": ["PDFUA-FONT-CMAP-SYSTEMINFO"], "8.4.5.3.2-1": ["PDFUA-FONT-CIDTOGID"],
        "8.4.5.4-1": ["PDFUA-FONT-CMAP-EMBEDDED"], "8.4.5.5.1-1": ["PDFUA-FONT-EMBEDDED"],
        "8.4.5.8-1": ["PDFUA-FONT-TOUNICODE"],
        "8.10.2.3-2": ["PDFUA2-WIDGET-AA-CONTENTS"],
        "8.10.1-1": ["PDFUA-FORM-STRUCT"],
        "8.10.1-3": ["PDFUA2-XFA"],
        "8.10.3.3-1": ["PDFUA2-FORM-RV-V"],
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
    if rep is None or rep.get("jobEndStatus") != "normal":
        return None, ["<veraPDF produced no complete report>"]
    failed = [f"{r.get('clause')}-{r.get('testNumber')}" for r in rep.iter("rule") if r.get("status") == "failed"]
    return rep.get("isCompliant") == "true", failed


def command_batches(command, files):
    """Reserve room below Windows' 32767 UTF-16-unit process command-line limit."""
    batch = []
    for path in files:
        if len(subprocess.list2cmdline([*command, *batch, path]).encode("utf-16-le")) // 2 > COMMAND_LINE_LIMIT:
            if batch:
                yield batch
            batch = []
            if len(subprocess.list2cmdline([*command, path]).encode("utf-16-le")) // 2 > COMMAND_LINE_LIMIT:
                sys.exit(f"Input path exceeds the command-line budget: {path}")
        batch.append(path)
    if batch:
        yield batch


def ours(files):
    proj = os.path.join(ROOT, "tools", "FreeDF.UaDump", "FreeDF.UaDump.csproj")
    command = ["dotnet", "run", "-c", "Release", "--project", proj]
    batches = command_batches([*command, "--no-build", "--no-restore", "--"], files)
    res = {}
    for index, batch in enumerate(batches):
        flags = ["--no-build", "--no-restore"] if index else []
        run = subprocess.run([*command, *flags, "--", *batch], capture_output=True, text=True, encoding="utf-8-sig")
        if run.returncode != 0:
            sys.exit(f"FreeDF.UaDump batch {index + 1} failed with exit code {run.returncode}; refusing to report a partial matrix:\n{run.stdout[-2000:]}\n{run.stderr[-2000:]}")
        for line in run.stdout.splitlines():
            if line.startswith("{"):
                j = json.loads(line)
                res[(os.path.abspath(j["file"]), j.get("part"))] = j
    return res


def ours_failure(row):
    if "compliant" not in row:
        return row.get("error", "no result")
    if "VALIDATION-INTERNAL" in row.get("warnings", []):
        return "validation check-group incomplete (VALIDATION-INTERNAL)"
    return None


def vera_batch(directory, part, profile=None):
    """One veraPDF run over a directory tree; returns {abs path: (compliant, [failed rules])}."""
    selection = ["--profile", profile] if profile else ["-f", FLAVOURS[part]]
    run = subprocess.run([VERA, "-r", *selection, directory], capture_output=True, text=True)
    tree = parse_report(run.stdout)
    if tree is None:
        sys.exit(f"veraPDF produced no report (exit {run.returncode}):\n{run.stderr[-2000:]}")
    res, profiles = {}, set()
    for job in tree.iter("job"):
        name = os.path.abspath(job.find("item/name").text)
        rep = job.find("validationReport")
        if rep is None or rep.get("jobEndStatus") != "normal":
            res[name] = (None, ["<veraPDF produced no complete report>"])
            continue
        profiles.add(rep.get("profileName", "<unnamed>"))
        failed = sorted({f"{r.get('clause')}-{r.get('testNumber')}" for r in rep.iter("rule") if r.get("status") == "failed"})
        res[name] = (rep.get("isCompliant") == "true", failed)
    if profiles:
        print(f"veraPDF profiles: {', '.join(sorted(profiles))}")
    return res


def corpus(directory, part, profile=None, *, misses=False, strict=False):
    files = sorted(os.path.abspath(f) for f in collect([directory]))
    if not files:
        sys.exit(f"No PDFs found in corpus directory: {directory}")
    truth = vera_batch(directory, part, profile)
    mine = ours(files)
    cells, excluded = {}, []
    for f in files:
        ok, failed = truth.get(f, (None, ["<veraPDF produced no report for input>"]))
        j = mine.get((f, part), mine.get((f, None), {}))
        error = ours_failure(j)
        rel = os.path.relpath(f, directory)
        if ok is None:
            excluded.append((rel, f"veraPDF: {failed}"))
        elif error is not None:
            excluded.append((rel, f"ours: {error}"))
        else:
            key = ("vera-pass" if ok else "vera-fail", "ours-pass" if j["compliant"] else "ours-fail")
            cells.setdefault(key, []).append((rel, failed, j.get("errors")))
    compared = sum(len(v) for v in cells.values())
    print(f"total={len(files)} compared={compared} excluded={len(excluded)}")
    for k, v in sorted(cells.items()):
        print(f"{k[0]} / {k[1]}: {len(v)}")
    for rel, why in excluded:
        print(f"  EXCLUDED {rel}: {why}")
    for rel, failed, errs in cells.get(("vera-pass", "ours-fail"), []):
        print(f"  FALSE-POSITIVE {rel}: ours={errs}")
    if misses:
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
    if excluded and strict:
        sys.exit(2)


def main():
    args = [a for a in sys.argv[1:] if a != "--strict"]
    if args and args[0] == "corpus":
        parser = argparse.ArgumentParser(description="Compare a corpus against a standalone or explicit veraPDF profile.")
        parser.add_argument("directory", type=os.path.abspath)
        parser.add_argument("part", type=int, choices=(1, 2))
        parser.add_argument("--profile", type=os.path.abspath)
        parser.add_argument("--misses", action="store_true")
        options = parser.parse_args(args[1:])
        corpus(options.directory, options.part, options.profile, misses=options.misses, strict="--strict" in sys.argv)
        return
    files = [os.path.abspath(f) for f in collect(args)]
    mine = ours(files)
    excluded = 0
    for f in files:
        for part in (1, 2):
            compliant, failed = vera(f, part)
            j = mine.get((f, part), mine.get((f, None), {}))
            error = ours_failure(j)
            if compliant is None or error is not None:
                excluded += 1
                print(f"EXCLUDED {os.path.basename(f)} UA-{part}: vera={failed} ours={error}")
                continue
            ok = j["compliant"]
            print(f"{'AGREE' if ok == compliant else 'DISAGREE'} {os.path.basename(f)} UA-{part}: ours={'pass' if ok else 'fail'} {j.get('errors')} | vera={'pass' if compliant else 'fail'} {failed}")
    print(f"files={len(files)} excluded={excluded}")
    if excluded and "--strict" in sys.argv:
        sys.exit(2)


if __name__ == "__main__":
    main()
