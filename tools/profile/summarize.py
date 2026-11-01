"""Summarise results .jsonl into markdown tables: median ms per (file, op) by engine, plus peak working set.
Usage: python summarize.py results.jsonl [more.jsonl ...]
"""
import json, sys, collections

rows = []
for p in sys.argv[1:]:
    for line in open(p, encoding="utf-8-sig"):
        try: rows.append(json.loads(line))
        except Exception: pass
engines = ["freedf", "pdfpig", "pdfium", "mupdf", "pikepdf"]
by = collections.defaultdict(dict)
size = {}
for r in rows:
    size[r["file"]] = r.get("bytes", 0)
    engine = "freedf" if r["engine"] == "adobye" else r["engine"]
    by[(r["file"], r["op"])][engine] = r
for op in sorted({k[1] for k in by}):
    print(f"\n### {op} (median ms; `first` in parentheses for open/text; ERR = error)\n")
    print("| file | MB | " + " | ".join(engines) + " |\n|---|---:|" + "---:|" * len(engines))
    for f in sorted({k[0] for k in by if k[1] == op}, key=lambda x: size[x]):
        cells = []
        for e in engines:
            r = by[(f, op)].get(e)
            if r is None: cells.append("")
            elif r.get("error"): cells.append("ERR")
            elif r.get("median_ms") is None: cells.append("-")
            else:
                c = f"{r['median_ms']:.1f}"
                if op in ("open", "text_sampled_pages_with_open"): c += f" ({r['first_ms']:.0f})"
                cells.append(c)
        print(f"| {f} | {size[f]/1048576:.1f} | " + " | ".join(cells) + " |")
print("\n### peak working set MB (whole process, all ops)\n")
print("| file | " + " | ".join(engines) + " |\n|---|" + "---:|" * len(engines))
for f in sorted(size, key=lambda x: size[x]):
    cells = []
    for e in engines:
        v = [r["peak_ws_mb"] for (ff, op), d in by.items() if ff == f for ee, r in d.items() if ee == e and r.get("peak_ws_mb")]
        cells.append(f"{max(v):.0f}" if v else "")
    print(f"| {f} | " + " | ".join(cells) + " |")
errs = [(r["file"], "freedf" if r["engine"] == "adobye" else r["engine"], r["op"], r["error"]) for r in rows if r.get("error")]
if errs:
    print("\n### errors\n")
    for e in errs: print("-", *e)
