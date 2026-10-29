# Parser and text-extraction benchmarks

These BenchmarkDotNet 0.15.8 cases compare AdoBye with PdfPig 0.1.16 on the same in-memory PDF bytes. `PdfOpenBench` measures opening a document and reading its page count. `TextExtractionBench` measures a fresh open plus decoding text spans/letters on each invocation, avoiding differences from cached page text. BenchmarkDotNet reports elapsed time and allocated bytes.

The parse corpus includes the existing text, vector, and image fixtures plus typography, pattern/shading, and image samples. The text corpus uses the text fixture and embedded-typography sample. AdoBye text counts come from `PdfRenderer.ExtractText` spans; PdfPig counts decoded `Page.Letters`. Setup checks page counts for each comparison and verifies both extractors produce text for the text corpus. It reports character counts but does not require identical text ordering or whitespace.

Run the comparisons from the repository root:

```powershell
dotnet run -c Release --project bench/AdoBye.Bench -- --filter "*PdfOpenBench*"
dotnet run -c Release --project bench/AdoBye.Bench -- --filter "*TextExtractionBench*"
```

BenchmarkDotNet records the runtime, operating system, CPU, and detailed results under `BenchmarkDotNet.Artifacts`. The results compare parsing and text extraction only; PdfPig does not provide a directly comparable raster renderer in this harness.

PdfPig is an Apache-2.0 third-party dependency referenced only by this benchmark project. Its license remains separate from AdoBye's licensing.
