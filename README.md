# AdoBye

Free, fast PDF parser, renderer and editor in C# (ISO 32000-2). Say goodbye to the paywall.

- `src/` core, document, content and render libraries (`AdoBye.*`)
- `tools/AdoBye.EditorWeb` browser editor: `dotnet run -c Release --project tools/AdoBye.EditorWeb -- file.pdf`
- `samples/` 26 example PDFs, see `samples/README.md`
- `docs/spec-matrix.md` spec coverage and known gaps

Build and test: `dotnet build AdoBye.sln -c Release` then `dotnet test AdoBye.sln -c Release`.

NativeAOT: production libraries enable compatibility analysis; see `tools/AdoBye.AotSmoke/README.md` for the native publish/run validation and optional, experimental host-level GC notes.
