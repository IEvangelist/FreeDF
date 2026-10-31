"""Profile pypdfium2 / PyMuPDF / pikepdf on one PDF. One engine per process, JSON lines out.
Usage: python profile_engines.py <pdfium|mupdf|pikepdf> <file.pdf> [--iters N] [--pages K] [--dpi 72,150] [--budget S] [--ops ...]
"""
import argparse, io, json, os, sys, time

ap = argparse.ArgumentParser()
ap.add_argument("engine"); ap.add_argument("file")
ap.add_argument("--iters", type=int, default=5); ap.add_argument("--pages", type=int, default=8)
ap.add_argument("--dpi", default="72,150"); ap.add_argument("--budget", type=float, default=60)
ap.add_argument("--ops", default="open,text,render,thumb,save")
a = ap.parse_args()
data = open(a.file, "rb").read()
name = os.path.basename(a.file)
ops = a.ops.split(",")
dpis = [float(x) for x in a.dpi.split(",")]


def peak_ws_mb():
    try:
        import ctypes
        from ctypes import wintypes

        class PMC(ctypes.Structure):
            _fields_ = [("cb", wintypes.DWORD), ("PageFaultCount", wintypes.DWORD),
                        ("PeakWorkingSetSize", ctypes.c_size_t), ("WorkingSetSize", ctypes.c_size_t),
                        ("QuotaPeakPagedPoolUsage", ctypes.c_size_t), ("QuotaPagedPoolUsage", ctypes.c_size_t),
                        ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t), ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
                        ("PagefileUsage", ctypes.c_size_t), ("PeakPagefileUsage", ctypes.c_size_t)]
        pmc = PMC(); pmc.cb = ctypes.sizeof(PMC)
        h = ctypes.windll.kernel32.GetCurrentProcess()
        h = ctypes.c_void_p(h)
        ctypes.windll.psapi.GetProcessMemoryInfo(h, ctypes.byref(pmc), pmc.cb)
        return round(pmc.PeakWorkingSetSize / 1048576, 1)
    except Exception:
        return None


def emit(op, ms, extra=None, error=None):
    s = sorted(ms)
    print(json.dumps({"engine": a.engine, "file": name, "bytes": len(data), "op": op, "n": len(s),
        "first_ms": round(ms[0], 3) if ms else None, "median_ms": round(s[len(s) // 2], 3) if s else None,
        "min_ms": round(s[0], 3) if s else None, "max_ms": round(s[-1], 3) if s else None,
        "peak_ws_mb": peak_ws_mb(), "cpu_ms": round(time.process_time() * 1000), "extra": extra, "error": error}), flush=True)


def run(fn, n):
    ms = []; last = None; t0 = time.perf_counter()
    for _ in range(n):
        t = time.perf_counter(); last = fn(); ms.append((time.perf_counter() - t) * 1000)
        if time.perf_counter() - t0 > a.budget: break
    return ms, last


def sample(count):
    if count <= a.pages: return list(range(count))
    return sorted({i * (count - 1) // (a.pages - 1) for i in range(a.pages)})


def guard(op, fn):
    try: fn()
    except Exception as e: emit(op, [], None, f"{type(e).__name__}: {(str(e).splitlines() or [''])[0]}")


def timed_pages(idx, fn):
    per = []
    for i in idx:
        t = time.perf_counter(); fn(i); per.append((time.perf_counter() - t) * 1000)
    return per


def nop(): pass


op_text = op_text_all = op_render = nop
if a.engine == "pdfium":
    import pypdfium2 as p

    def op_open():
        ms, pc = run(lambda: len(p.PdfDocument(data)), a.iters); emit("open", ms, {"pages": pc})

    def op_text():
        def f():
            d = p.PdfDocument(data); c = 0
            for i in sample(len(d)): c += len(d[i].get_textpage().get_text_range())
            return c
        ms, c = run(f, min(a.iters, 3)); emit("text_sampled_pages_with_open", ms, {"chars": c})

    def op_text_all():
        def f():
            d = p.PdfDocument(data); c = 0
            for i in range(len(d)): c += len(d[i].get_textpage().get_text_range())
            return c
        ms, c = run(f, min(a.iters, 2)); emit("text_all_pages_with_open", ms, {"chars": c})

    def op_render():
        d = p.PdfDocument(data)
        for dpi in dpis:
            emit(f"render_{dpi:g}dpi_per_page", timed_pages(sample(len(d)), lambda i: d[i].render(scale=dpi / 72).to_numpy()))
        emit("thumb_24dpi_per_page", timed_pages(sample(len(d)), lambda i: d[i].render(scale=24 / 72).to_numpy()))

    def op_save():
        def f():
            d = p.PdfDocument(data); b = io.BytesIO(); d.save(b); return len(b.getvalue())
        ms, n = run(f, min(a.iters, 3)); emit("save_full", ms, {"out_bytes": n})
elif a.engine == "mupdf":
    import pymupdf

    def op_open():
        ms, pc = run(lambda: pymupdf.open(stream=data, filetype="pdf").page_count, a.iters); emit("open", ms, {"pages": pc})

    def op_text():
        def f():
            d = pymupdf.open(stream=data, filetype="pdf"); c = 0
            for i in sample(d.page_count): c += len(d[i].get_text())
            return c
        ms, c = run(f, min(a.iters, 3)); emit("text_sampled_pages_with_open", ms, {"chars": c})

    def op_text_all():
        def f():
            d = pymupdf.open(stream=data, filetype="pdf"); c = 0
            for pg in d: c += len(pg.get_text())
            return c
        ms, c = run(f, min(a.iters, 2)); emit("text_all_pages_with_open", ms, {"chars": c})

    def op_render():
        d = pymupdf.open(stream=data, filetype="pdf")
        for dpi in dpis:
            emit(f"render_{dpi:g}dpi_per_page", timed_pages(sample(d.page_count), lambda i: d[i].get_pixmap(dpi=int(dpi))))
        emit("thumb_24dpi_per_page", timed_pages(sample(d.page_count), lambda i: d[i].get_pixmap(dpi=24)))

    def op_save():
        ms, n = run(lambda: len(pymupdf.open(stream=data, filetype="pdf").tobytes()), min(a.iters, 3)); emit("save_full", ms, {"out_bytes": n})
elif a.engine == "pikepdf":
    import pikepdf

    def op_open():
        ms, pc = run(lambda: len(pikepdf.open(io.BytesIO(data)).pages), a.iters); emit("open", ms, {"pages": pc})

    def op_save():
        def f():
            d = pikepdf.open(io.BytesIO(data)); b = io.BytesIO(); d.save(b); return len(b.getvalue())
        ms, n = run(f, min(a.iters, 3)); emit("save_full", ms, {"out_bytes": n})
else:
    sys.exit("unknown engine")

if "open" in ops: guard("open", op_open)
if "text" in ops: guard("text", op_text)
if "text_all" in ops: guard("text_all", op_text_all)
if "render" in ops or "thumb" in ops: guard("render", op_render)
if "save" in ops: guard("save", op_save)
