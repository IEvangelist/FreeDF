# AdoBye

Free, fast PDF parser, renderer and editor in C# (ISO 32000-2). Say goodbye to the paywall.

- `src/` core, document, content and render libraries (`PortableDocument.*`)
- `tools/AdoBye.EditorWeb` browser editor: `dotnet run -c Release --project tools/AdoBye.EditorWeb -- file.pdf`
- `samples/` 26 example PDFs, see `samples/README.md`
- `docs/spec-matrix.md` spec coverage and known gaps

Build and test: `dotnet build PortableDocument.slnx -c Release` then `dotnet test PortableDocument.slnx -c Release`.

The browser editor can select image and simple vector paint objects inside uniquely used indirect Form XObjects invoked directly from page content. Shared or repeated Forms (including hidden layer invocations) remain group-editable, without rewriting shared children. Ownership checks are conservative: unused resource aliases and streams containing inline images can also disable child edits. While Form child edits exist, graphic-group edits and page import/delete/reorder/rotate/insert/duplicate return a visible conflict; reset the child edits, or save and reopen before those operations. This is not a copy-on-write, arbitrary nested-Form, or general complex-path/clipping editor.

The HTTP editor regression runs with `dotnet test tests/AdoBye.Tests/AdoBye.Tests.csproj --filter FullyQualifiedName~FormObjectHttpTests`. It builds the editor, starts a test-owned localhost server for each case, verifies save/reopen output and rejected operations, and stops each server afterward.

`PortableDocument.slnx` is the canonical solution. Package versions are managed centrally in root `Directory.Packages.props`; project `PackageReference` entries omit versions. Change dependency versions there, while keeping tool-only dependencies referenced only by the relevant tools.

NativeAOT: production libraries enable compatibility analysis; see `tools/AdoBye.AotSmoke/README.md` for the native publish/run validation and optional, experimental host-level GC notes.

## Library API

The library projects, assemblies, and namespaces are `PortableDocument.Core`, `PortableDocument.Document`, `PortableDocument.Content`, and `PortableDocument.Render`. The existing static entry class is now `PortableDocument.Render.PortableDocument`, replacing `PdfRenderer` without changing its methods or behavior:

```csharp
using PortableDocument.Document;
using Pdf = PortableDocument.Render.PortableDocument;

var document = PdfDocument.Load(bytes);
var spans = Pdf.ExtractText(document.Pages[0]);
var raster = Pdf.RenderPage(document.Pages[0], dpi: 96);
```

Update old library references/usings to the corresponding `PortableDocument.*` names. The local `Pdf` alias avoids confusion between the root namespace and the static entry class; it is not another facade. Product/tool names remain unchanged while the product name is undecided. Historical benchmark reports retain their original identities and measurements.

### Interactive optional-content layers

`PortableDocument.Content.OptionalContentState` provides reusable, in-memory layer state for a document's default or named configuration. Its `Groups`, `RadioButtonGroups`, and `IsLocked` expose registered layers and configuration rules. Pass a registered group dictionary to `SetVisibility`, then render with `new PdfRenderOptions { OptionalContentState = state }`. State and `OptionalContentConfigurationName` are mutually exclusive.

Selecting a radio-group member hides its peers; turning it off does not select a replacement. A locked target, or a visible locked peer that would need to turn off, rejects the entire change with `InvalidOperationException`. Supply the current render event and zoom to `SetVisibility` when usage applications affect locked peers. User overrides take precedence over usage applications; untouched layers continue to follow the render event and zoom.

Omitted `/RBGroups` in a named configuration inherits the default configuration's groups; `[]` clears them. `/Locked` does not inherit. These rules do not normalize initial visibility or prevent configuration/usage-driven changes. Malformed rule references are ignored with warnings available on the state and rendered page. Changes do not modify or persist PDF dictionaries; existing editor saves preserve the original rules. This API never runs embedded actions or scripts.
