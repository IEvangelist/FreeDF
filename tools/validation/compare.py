"""Compare our PDF/A and PDF/UA verdicts (AdoBye.ValidateDump) with veraPDF MRR reports.

Usage: compare.py <ours.jsonl> <vera-dir> [out.md]
  <vera-dir> holds vera-<flavour>.xml (or vera-<flavour>-partN.xml) for flavours 1b, 2b, 3b, ua1 (verapdf --format mrr).
Prints verdict disagreements; with out.md also writes the rule compatibility table.
"""
import os, re, sys, json
import xml.etree.ElementTree as ET
from collections import defaultdict

FLAVOURS = {"1b": "PdfA1b", "2b": "PdfA2b", "3b": "PdfA3b", "ua1": "PdfUA1"}


def load_vera(path):
    out = {}
    text = open(path, encoding="utf-8", errors="replace").read()
    for block in re.findall(r"<job>.*?</job>", text, re.S):
        job = ET.fromstring(block)
        name = os.path.normcase(job.find("item/name").text)
        vr = job.find("validationReport")
        if vr is None:
            out[name] = None
            continue
        failed = {}
        for rule in vr.iter("rule"):
            if rule.get("status") == "failed":
                key = f'{rule.get("clause")}-{rule.get("testNumber")}'
                failed[key] = (rule.findtext("description") or "").strip()
        out[name] = {"compliant": vr.get("isCompliant") == "true", "failed": failed}
    return out


def clause_of(c):
    if not c:
        return None
    m = re.search(r"(\d+(?:\.\d+)+)\s*$", c)
    return m.group(1) if m else c


def related(a, b):
    return a == b or a.startswith(b + ".") or b.startswith(a + ".")


def main():
    ours_path, vdir = sys.argv[1], sys.argv[2]
    ours = {}
    for line in open(ours_path, encoding="utf-8"):
        j = json.loads(line)
        ours[os.path.normcase(j["file"])] = j["result"]
    vera = {}
    for fl in FLAVOURS:
        merged = {}
        for fn in sorted(os.listdir(vdir)):
            if re.fullmatch(rf"vera-{fl}(-part\d+)?\.xml", fn):
                merged.update(load_vera(os.path.join(vdir, fn)))
        vera[fl] = merged
    md = []
    for fl, prof in FLAVOURS.items():
        agree = fp = fn_ = skipped = 0
        looser = defaultdict(int)
        fp_files = []
        rule_files = defaultdict(list)
        vera_rule_files = defaultdict(list)
        desc = {}
        for path, vr in vera[fl].items():
            o = ours.get(path)
            if vr is None or o is None or prof not in o:
                skipped += 1
                continue
            op = o[prof]
            for f in op["findings"]:
                rule_files[(f["rule"], clause_of(f["clause"]))].append((path, vr))
            for k, dsc in vr["failed"].items():
                vera_rule_files[k].append((path, op["compliant"]))
                desc[k] = dsc
            if op["compliant"] == vr["compliant"]:
                agree += 1
            elif not op["compliant"]:
                fp += 1
                fp_files.append((os.path.basename(path), sorted({f["rule"] for f in op["findings"]})))
            else:
                fn_ += 1
                for k in vr["failed"]:
                    looser[k] += 1
        print(f"{prof}: agree={agree} ours_stricter={fp} ours_looser={fn_} skipped(veraPDF crash/no report)={skipped}")
        print("   rules veraPDF fails on files we accept:", dict(sorted(looser.items(), key=lambda x: -x[1])[:8]))
        for name, rules in fp_files:
            print("   stricter:", name, rules)
        md.append(f"\n## {prof}\n")
        md.append(f"Files compared: {agree + fp + fn_}; verdict agreement {agree}; ours stricter {fp}; ours looser {fn_}; not comparable {skipped} (veraPDF crashed or produced no report).\n")
        md.append("\n### Our rules -> veraPDF\n\n| our rule | our clause | files | veraPDF also fails a related clause | veraPDF accepts the file | closest veraPDF rule(s) | status |\n|---|---|---:|---:|---:|---|---|")
        for (rule, cl), files in sorted(rule_files.items()):
            also = accepts = 0
            co = defaultdict(int)
            for _, vr in files:
                keys = [k for k in vr["failed"] if related(k.rsplit("-", 1)[0], cl or "")]
                if keys:
                    also += 1
                if vr["compliant"]:
                    accepts += 1
                for k in vr["failed"]:
                    co[k] += 1
            top = ", ".join(k for k, _ in sorted(co.items(), key=lambda x: -x[1])[:3])
            if accepts == len(files):
                status = "stricter (veraPDF has no equivalent check)"
            elif also == len(files):
                status = "matched"
            elif also:
                status = "partly matched"
            else:
                status = "matched in verdict, clause numbering differs" if accepts == 0 else "stricter in some files"
            md.append(f"| {rule} | {cl} | {len(files)} | {also} | {accepts} | {top} | {status} |")
        md.append("\n### veraPDF rules -> ours\n\n| veraPDF clause-test | files failing | we also reject the file | description | status |\n|---|---:|---:|---|---|")
        for k, files in sorted(vera_rule_files.items(), key=lambda x: -len(x[1])):
            rej = sum(1 for _, comp in files if not comp)
            status = "covered by verdict (not proven per rule)" if rej == len(files) else "not implemented / looser"
            md.append(f"| {k} | {len(files)} | {rej} | {desc[k][:110].replace('|', '/')} | {status} |")
    if len(sys.argv) > 3:
        open(sys.argv[3], "w", encoding="utf-8").write("\n".join(md) + "\n")


main()
