using FreeDF.Samples.Kit;
using static FreeDF.Samples.Kit.Helpers;

namespace FreeDF.Samples.Gen;

sealed partial class Gen28
{
    // ---- 6: editor before / after -----------------------------------------------------------------------------------------------

    void EditorDeltaPage()
    {
        var y = ContentPage("Part 1  /  Benchmarks  /  browser editor", Teal, "Editor: before and after",
            "A bounded checkpoint, not completion of the responsiveness work. Bars to the left are faster. Three rows point right, and they are drawn that way.",
            "Editor: before and after");
        y -= 12;
        SubHeading(y, "Matched median latency change, non-AOT build", Teal);
        var yb = 0.0;
        Fig("Diverging bar chart of the change in median latency between a control build and the candidate. Faster: images open 524 to 374 milliseconds (minus 28.6 percent), text-edit commit 313 to 176 (minus 43.8), edit through thumbnail 370 to 224 (minus 39.5), save 877 to 541 (minus 38.3), zoom 823 to 673 (minus 18.2), uncached 137 dpi render 854 to 719 (minus 15.8) and large save 1292 to 325 (minus 74.8). Not faster: large open 452 to 456 (plus 0.9), large recovery wait 1116 to 1556 (plus 39.4) and large uncached 240 dpi render 3440 to 3524 (plus 2.4).",
            () => yb = DivergingBars(M, y - 6, W, BeforeAfter, -80, 50, [-75, -50, -25, 0, 25, 50]));
        FigureCaption(M, yb - 2, "Figure 6. * Recovery wait includes the intentional 1500 ms write-coalescing timer, so a faster download can lengthen the wait. The large fresh render is still about 3.5 s.");
        y = yb - 34;
        SubHeading(y, "Main-thread blocking (log scale)", Teal);
        (double, string)[] ticks = [(1, "1 ms"), (10, "10 ms"), (100, "100 ms")];
        Fig("Dumbbell chart on a logarithmic axis of the longest main-thread heartbeat gap, before and after. Images save 42.8 to 14.6 milliseconds, images recovery 50.3 to 3.1, large open 102.2 to 2.8, large save 247.9 to 10.0 and large recovery 332.0 to 0.7.",
            () => yb = DumbbellsLog(M, y - 6, W, Heartbeat, 0.5, 500, ticks));
        FigureCaption(M, yb - 2, "Figure 7. Maximum heartbeat gap, across three runs. This is UI blocking, not total operation time: it does not make a multi-second raster fast.");
        Evidence(yb - 34, EditorReport, "2026-10-30", BrowserHw,
            "Release, local static WASM, non-AOT. Medians of three runs, four C#-generated samples plus a generated 12-page fixture. First-page PNG hashes identical before and after in every repetition. p95 is the largest of three.");
    }

    // ---- 7: JIT vs AOT ---------------------------------------------------------------------------------------------------------------------

    void AotPage()
    {
        var y = ContentPage("Part 1  /  Benchmarks  /  browser editor", Teal, "Editor: JIT versus AOT",
            "The same harness, fixtures and hardware, built twice: non-AOT (interpreter plus jiterpreter) and ahead-of-time compiled WebAssembly. Frame-bound rows barely move, and the chart shows that too.",
            "Editor: JIT vs AOT");
        y -= 12;
        SubHeading(y, "Median latency, non-AOT versus AOT", Teal);
        var rows = AotRows.Select(a =>
        {
            var s = a.Jit50 / a.Aot50;
            return new BarRow(a.Name, null, a.Jit50, Num(a.Jit50, "0"), a.Aot50, Num(a.Aot50, "0"), Ratio(s), s >= 1.5 ? "#CCFBF1" : "#FEF3C7", s >= 1.5 ? "#134E4A" : "#78350F");
        }).ToList();
        var yb = 0.0;
        var largest = AotRows.Max(a => a.Jit50 / a.Aot50);
        var smallest = AotRows.Min(a => a.Jit50 / a.Aot50);
        Fig("Paired bar chart of 15 editor operations, p50 milliseconds, non-AOT then AOT: cold editor ready 762 and 742; showcase open 697 and 330; typography open 233 and 110; images open 376 and 160; report open 232 and 116; large open 469 and 270; images uncached 137 dpi render 721 and 217; report uncached 137 dpi render 1236 and 287; large uncached 240 dpi render 3504 and 770; images zoom 688 and 439; large zoom 770 and 353; typography uncached navigation 183 and 132; report uncached navigation 149 and 116; large uncached navigation 149 and 115; images save 542 and 205. Speed-ups range from 1.03 times on cold start to 4.55 times on the large render.",
            () => yb = PairedBars(M, y - 6, W, rows, ("Non-AOT", Pig), ("AOT", Teal), 20.5, 176));
        FigureCaption(M, yb - 12, $"Figure 8. p50 in ms, pill = non-AOT \u00F7 AOT, from {Num(smallest, "0.00")}\u00D7 (cold start, no real change) to {Num(largest, "0.00")}\u00D7. In every cell p95 is within {Num(AotRows.Max(a => Math.Max(a.Jit95 / a.Jit50, a.Aot95 / a.Aot50) - 1) * 100, "0.0")}% of p50. Cached zoom (about 57\u201360 ms) and cached navigation (96\u201398 ms) are frame-bound and unchanged.");
        y = yb - 52;
        SubHeading(y, "What it costs to download", Teal);
        BarRow[] pay =
        [
            new("Static asset count", null, 217, "217", 56, "56", Ratio(56.0 / 217), "#CCFBF1", "#134E4A"),
            new("Uncompressed bytes", null, 30795234, "30.80 MB", 32118389, "32.12 MB", Ratio(32118389.0 / 30795234), "#FEF3C7", "#78350F"),
            new("Gzip-equivalent bytes", null, 11930271, "11.93 MB", 10738626, "10.74 MB", Ratio(10738626.0 / 11930271), "#CCFBF1", "#134E4A"),
        ];
        Fig("Paired bar chart of startup download for non-AOT versus AOT: static assets 217 versus 56; uncompressed bytes 30,795,234 versus 32,118,389; gzip-equivalent bytes 11,930,271 versus 10,738,626.",
            () => yb = PairedBars(M, y - 6, W, pay, ("Non-AOT", Pig), ("AOT", Teal), 26, 176));
        FigureCaption(M, yb - 12, "Figure 9. Pill = AOT \u00F7 non-AOT (below 1\u00D7 is smaller). AOT is slightly larger raw. Gzip totals are estimates, not measured Pages transfer sizes.");
        Evidence(yb - 34, AotReport, "2026-10-30", BrowserHw,
            "Release, unthrottled, uncompressed local server; 3 fresh contexts per variant. 15 of 15 first-page pixel hashes match the non-AOT control. Public slow-network cold start is not measured.");
    }

    // ---- 8: goals ----------------------------------------------------------------------------------------------------------------------------

    void GoalsPage()
    {
        var y = ContentPage("Part 1  /  Benchmarks  /  browser editor", Teal, "Editor: goals versus reality",
            "The interaction goals are strict. Each bar is the measured p50 as a multiple of its p50 budget, so everything right of the line is a miss, and every one of them is shown.",
            "Editor: goals vs reality");
        y -= 12;
        SubHeading(y, "Strict goals still missed", Teal);
        var yb = 0.0;
        Fig("Bullet chart of six strict goals missed in the matched AOT run, as measured p50 divided by the p50 budget: startup longest task 76 versus 50 milliseconds (1.52 times); showcase open 330 versus 250 (1.32); large open 270 versus 250 (1.08); typography uncached next page 131.7 versus 100 (1.32); report uncached next page 116.4 versus 100 (1.16); large uncached next page 115.0 versus 100 (1.15).",
            () => yb = Bullets(M, y - 6, W, Misses, 1.75, "MATCHED AOT RUN: 6 MISSES"));
        y = yb - 4;
        Fig("Bullet chart of three strict goals missed in the latest integrated profile: startup longest task 71 versus 50 milliseconds (1.42 times); showcase open 281.2 versus 250 (1.12); typography cached next page 103.1 versus 100 (1.03).",
            () => yb = Bullets(M, y, W, LatestMisses, 1.75, "LATEST INTEGRATED PROFILE: 3 MISSES"));
        FigureCaption(M, yb - 2, "Figure 10. Green band: goal met. p50 and p95 values and budgets are printed at the right of each bar.");
        y = yb - 34;

        var p95ok = Misses.Count(g => g.P95 < g.Budget95);
        string[] points =
        [
            $"{p95ok} of the {Misses.Length} AOT-run misses are p50-only; their p95 stays inside its budget. Startup misses both.",
            "Image open (160 / 175 ms) now meets its 250 / 500 ms budget with AOT.",
            "CI treats these as KNOWN MISSES and fails any group that exceeds 2.5\u00D7 its recorded AOT p50 or p95 (50 ms minimum).",
            "A goal met on a faster runner prints MET and never erases the recorded miss. No goal was loosened.",
        ];
        var h = 28 + points.Sum(p => ParaH(_t.Sans, 9.2, W - 40, p, 12.8) + 6);
        Art(() => { Card(M, y - h, W, h, White, 10); Box(_pg, M, y - h + 8, 3, h - 16, fill: Teal, r: 1.5); });
        T("P", () =>
        {
            Tx(_t.SansB, 8, M + 16, y - 16, "HOW TO READ THE MISSES", Teal, tracking: 1.4);
            var yy = y - 31;
            foreach (var p in points) yy = Para(_t.Sans, 9.2, M + 16, yy, W - 40, p, Ink, 12.8) - 6;
        });
        Evidence(y - h - 16, AotBudgets + " (six) and docs/spec-matrix.md, latest integration (three)", "2026-10-30", BrowserHw,
            "Original budgets unchanged: p50 and p95 strictly below budget. The six-miss run and the three-miss run are different profiles; do not subtract one from the other.");
    }

    // ---- 9: pool and trails ----------------------------------------------------------------------------------------------------------

    void PoolPage()
    {
        var y = ContentPage("Part 1  /  Benchmarks  /  browser editor", Teal, "Editor: worker pool and the trail so far",
            "Three render replicas split a page into bands and finish sooner in these runs. The pool stays opt-in until replicas share the fallback font assets.",
            "Editor: worker pool");
        y -= 12;
        SubHeading(y, "One worker versus a pool of three replicas", Teal);
        var rows = PoolRows.Select(p => new BarRow(p.Name, $"4\u00D7 CPU throttle: {Num(p.ThrottledOne, "0")} \u2192 {Num(p.ThrottledMany, "0")} ms", p.One, Num(p.One, "0") + " ms", p.Many, Num(p.Many, "0") + " ms", Ratio(p.One / p.Many), "#E0E7FF", IndigoD)).ToList();
        var yb = 0.0;
        Fig("Paired bar chart of render time with one worker versus a pool, in milliseconds: cold 239 dpi page 3920 versus 1361 (2.88 times); warm 239 dpi page 406 versus 220 (1.85); four uncached pages at 137 dpi 854 versus 504 (1.69); twelve thumbnails 1857 versus 751 (2.47); print twelve pages at 150 dpi 7186 versus 2645 (2.72). Under a 4 times CPU throttle the speed-ups range from 1.26 to 2.61 times for the same cases.",
            () => yb = PairedBars(M, y - 6, W, rows, ("1 worker", Pig), ("Pool (3 replicas)", Indigo), 34, 190));
        FigureCaption(M, yb - 12, "Figure 11. Decoded pixels are SHA-256-identical to the single-worker output in every scenario. Largest frame gap during the run: 79 ms (1 worker) vs 87 ms (pool), no gain. 3\u00D7 was not reached for warm or navigation renders.");
        y = yb - 52;
        SubHeading(y, "Before \u2192 after \u2192 AOT", Teal);
        Fig("Sparkline table of five operations across three separate matched runs, in milliseconds: images open 524, 374, 160; images save 877, 541, 205; images uncached 137 dpi render 854, 719, 217; large open 452, 456, 270; large uncached 240 dpi render 3440, 3524, 770.",
            () => yb = TrailChart(M, y - 4, W, Trails));
        FigureCaption(M, yb - 2, "Figure 12. Three separate matched runs with one harness, not one continuous series. Pill = first \u00F7 last. Large open was flat across the first checkpoint (452 \u2192 456) and only moved with AOT.");
        Evidence(yb - 34, PoolReport + "; " + EditorReport + "; " + AotReport, "2026-10-30", "Pool report: hardware not restated; Chromium with hardwareConcurrency forced to 4. Trails: " + BrowserHw,
            "Pool: non-AOT Release publish, fixture is 20-report.pdf plus six 09-images pages (12 pages). AOT and pool were not measured together. Raster-heavy pages still cost hundreds of milliseconds.");
    }
}
