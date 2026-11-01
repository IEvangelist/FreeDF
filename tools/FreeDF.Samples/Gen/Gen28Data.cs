namespace FreeDF.Samples.Gen;

/// <summary>
/// Every number in sample 28, transcribed from the checked-in reports named on each row; nothing is estimated, re-rounded favourably or rescaled.
/// Times are in microseconds for the BenchmarkDotNet tables and in milliseconds for the browser-editor tables.
/// </summary>
sealed partial class Gen28
{
    public const string BenchReport = "bench/FreeDF.Bench/results/2026-10-30.md";
    public const string EditorReport = "docs/performance/2026-10-30.md";
    public const string AotReport = "docs/performance/2026-10-30-aot.md";
    public const string PoolReport = "docs/performance/2026-10-30-worker-pool.md";
    public const string AotBudgets = "docs/performance/2026-10-30-aot-budgets.json";

    public const string BenchHw = "Windows 11 25H2, Intel Core i9-10900 2.80 GHz (10 cores), .NET 10.0.14 (X64 RyuJIT, JIT - not AOT), BenchmarkDotNet 0.15.8";
    public const string BenchMode = "6 warm-ups, 15 x 1 s iterations, 1 launch. Error = half-width of the 99.9% confidence interval.";
    public const string BrowserHw = "Windows 11, Intel Core i9-10900, .NET 10.0.14, Playwright Chromium 1440 x 1000, device scale 1, 3 fresh contexts per run";

    /// <summary>One corpus document measured with both engines. Mean and Error are microseconds; Alloc is managed KB per operation.</summary>
    public sealed record Pair(string Corpus, double Ours, double OursErr, double Theirs, double TheirsErr, double OursAlloc, double TheirsAlloc, string Note = "");

    public static readonly Pair[] Open =
    [
        new("03 typography (embedded fonts)", 14.360, 0.2755, 45.656, 1.0970, 33.34, 79.1),
        new("06 patterns and shadings", 28.146, 0.9453, 70.269, 0.8445, 57.7, 103.61),
        new("09 image gallery", 11.392, 0.1650, 40.455, 0.6673, 25.23, 63.38),
        new("image.pdf (fixture)", 7.360, 0.1208, 30.441, 1.4111, 18.27, 52.22),
        new("text.pdf (fixture)", 18.780, 0.4892, 53.013, 0.8872, 42.94, 78.47),
        new("vector.pdf (fixture)", 12.959, 0.5939, 39.980, 0.4479, 30.13, 63.69),
    ];

    public static readonly Pair[] OpenExtract =
    [
        new("03 typography (embedded fonts)", 3484.168, 51.2711, 12401.096, 315.4400, 7566.68, 11443.86),
        new("06 patterns and shadings", 834.838, 9.6915, 2846.507, 88.8682, 2043.87, 3754.38),
        new("09 image gallery", 1379.904, 45.5965, 7351.858, 159.6835, 2952.27, 5257.55),
        new("image.pdf (fixture)", 86.805, 1.6885, 403.969, 74.8987, 556.61, 466.93),
        new("text.pdf (fixture)", 4716.248, 243.3145, 12446.381, 275.9349, 6292.76, 13951.99),
        new("vector.pdf (fixture)", 5796.548, 320.5123, 7649.616, 190.6593, 4952.71, 5673.51),
    ];

    public static readonly Pair[] Extract =
    [
        new("03 typography (embedded fonts)", 4424.294, 244.2610, 15366.445, 651.1550, 7533.18, 11383.81),
        new("06 patterns and shadings", 2197.874, 706.5228, 4676.479, 285.6477, 1986.58, 3649.42, "noisy: error is 32% of the mean"),
        new("09 image gallery", 2792.370, 511.6096, 5760.879, 402.5036, 2926.66, 5193.88),
        new("image.pdf (fixture)", 181.216, 23.3508, 264.070, 20.8660, 538.21, 414.74),
        new("text.pdf (fixture)", 4439.338, 184.9720, 10800.306, 499.1405, 6249.83, 13873.49),
        new("vector.pdf (fixture)", 5683.368, 94.8225, 7809.438, 155.3096, 4923.38, 5609.85),
    ];

    /// <summary>The legacy RenderBench (no comparison engine): mean and error in microseconds.</summary>
    public sealed record Stage(string Name, string What, double Mean, double Err, double AllocKb);

    public static readonly Stage[] Stages =
    [
        new("Parse", "PdfDocument.Load(text.pdf)", 18.816, 0.6050, 42.94),
        new("JPEG baseline decode", "photo.jpg", 8556.579, 210.5645, 14110.59),
        new("JPEG progressive decode", "photo-prog.jpg", 17163.764, 376.9097, 14121.32),
        new("Interpret text, no raster", "load + interpret text.pdf, null backend", 3477.364, 41.0258, 2445.59),
        new("Render image.pdf @ 96 dpi", "load + render all pages", 23404.905, 356.7340, 26900.91),
        new("Render text.pdf @ 96 dpi", "load + render all pages", 115921.535, 2204.4348, 19434.46),
        new("Render vector.pdf @ 96 dpi", "load + render all pages", 260196.145, 2117.9636, 69797.78),
    ];

    /// <summary>Editor, matched before/after runs (non-AOT, 2026-10-30.md). Median milliseconds.</summary>
    public sealed record Delta(string Name, double Before, double After, string Note = "");

    public static readonly Delta[] BeforeAfter =
    [
        new("Images: open / first editable page", 524, 374),
        new("Images: text-edit backend commit", 313, 176),
        new("Images: edit through thumbnail refresh", 370, 224),
        new("Images: save / download", 877, 541),
        new("Images: zoom to final resolution", 823, 673),
        new("Images: uncached 137-dpi render", 854, 719),
        new("Large: open / first editable page", 452, 456),
        new("Large: save / download", 1292, 325),
        new("Large: recovery wait *", 1116, 1556),
        new("Large: uncached 240-dpi render", 3440, 3524),
    ];

    /// <summary>Main-thread heartbeat maximum (ms), worst of three runs: UI blocking, not operation time.</summary>
    public static readonly Delta[] Heartbeat =
    [
        new("Images: save / download", 42.8, 14.6),
        new("Images: recovery", 50.3, 3.1),
        new("Large: open", 102.2, 2.8),
        new("Large: save / download", 247.9, 10.0),
        new("Large: recovery", 332.0, 0.7),
    ];

    /// <summary>Non-AOT versus AOT, same harness and fixtures (aot.md): p50 / p95 milliseconds.</summary>
    public sealed record Aot(string Name, double Jit50, double Jit95, double Aot50, double Aot95);

    public static readonly Aot[] AotRows =
    [
        new("Cold editor ready", 762, 794, 742, 756),
        new("Showcase open", 697, 700, 330, 334),
        new("Typography open", 233, 249, 110, 118),
        new("Images open", 376, 390, 160, 175),
        new("Report open", 232, 233, 116, 118),
        new("Large open", 469, 470, 270, 275),
        new("Images uncached 137-dpi render", 721, 749, 217, 219),
        new("Report uncached 137-dpi render", 1236, 1370, 287, 320),
        new("Large uncached 240-dpi render", 3504, 3518, 770, 773),
        new("Images zoom to exact replacement", 688, 689, 439, 440),
        new("Large zoom to exact replacement", 770, 783, 353, 373),
        new("Typography uncached navigation", 183, 183, 132, 133),
        new("Report uncached navigation", 149, 149, 116, 117),
        new("Large uncached navigation", 149, 149, 115, 116),
        new("Images save / download", 542, 578, 205, 209),
    ];

    /// <summary>Strict interaction goals still missed in the matched AOT run (2026-10-30-aot-budgets.json): p50/p95 measured against p50/p95 budgets.</summary>
    public sealed record Goal(string Name, double P50, double P95, double Budget50, double Budget95);

    public static readonly Goal[] Misses =
    [
        new("Startup: longest main-thread task", 76, 79, 50, 50),
        new("Showcase: open", 330, 333.5, 250, 500),
        new("Large: open", 270, 274.7, 250, 500),
        new("Typography: uncached next page", 131.7, 132.9, 100, 200),
        new("Report: uncached next page", 116.4, 116.5, 100, 200),
        new("Large: uncached next page", 115.0, 115.5, 100, 200),
    ];

    /// <summary>The latest integrated profile (spec-matrix.md): three misses remain; p50/p95 against the same budgets.</summary>
    public static readonly Goal[] LatestMisses =
    [
        new("Startup: longest main-thread task", 71, 72, 50, 50),
        new("Showcase: open", 281.2, 301.1, 250, 500),
        new("Typography: cached next page", 103.1, 103.1, 100, 200),
    ];

    /// <summary>Worker-pool band rendering (worker-pool.md): one worker versus pool of three replicas, milliseconds (non-AOT, Chromium, hardwareConcurrency forced to 4).</summary>
    public sealed record Pool(string Name, double One, double Many, double ThrottledOne, double ThrottledMany);

    public static readonly Pool[] PoolRows =
    [
        new("Cold 239-dpi page (incl. replica boot)", 3920, 1361, 3749, 1699),
        new("Warm 239-dpi page, 3 bands", 406, 220, 436, 296),
        new("4 uncached pages @ 137 dpi", 854, 504, 853, 678),
        new("12 thumbnails @ 41 dpi", 1857, 751, 1952, 790),
        new("Print: 12 pages @ 150 dpi", 7186, 2645, 7736, 2965),
    ];

    /// <summary>Three-point trails (before checkpoint, after checkpoint, AOT study): three separate matched runs of one harness; not one continuous series.</summary>
    public sealed record Trail(string Name, double A, double B, double C);

    public static readonly Trail[] Trails =
    [
        new("Images: open", 524, 374, 160),
        new("Images: save / download", 877, 541, 205),
        new("Images: uncached 137-dpi render", 854, 719, 217),
        new("Large: open", 452, 456, 270),
        new("Large: uncached 240-dpi render", 3440, 3524, 770),
    ];
}
