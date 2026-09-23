> Historical evidence for Kypelon (formerly NovaPdf during pre-public alpha development). This report records the original names/results; use README.md and docs/development/kypelon-brand-migration.md for current commands and verification.

# NovaPdf 0.1.0-alpha.2 — correctness and hardening verification

Verified 2026-09-23 on Windows, .NET SDK 10.0.301, runtimes 8.0.28 / 10.0.9. This is a focused patch, retaining the existing writer/layout architecture and report templates. No PDF generation dependency, shaping adapter or reader/edit/conformance subsystem was added.

## Status and reproduction

The original alpha.1 probe was rerun before runtime edits; its results are preserved at artifacts/hardening/reproduced-before.json. Permanent xUnit regressions were added first and built/run against alpha.1: **74 failed / 37 passed on each framework** among the initial 111 hardening cases. Additional cmap/figure-space cases were added during focused review. The final suite adds 116 cases to the original 103, including its one existing intentional skip.

| Issue | Reproduced before | Fix and permanent regression |
|---|---|---|
| F1 | 1 MiB / 32 overlapping descriptors allocated ~34.57 MB before rejecting missing tables | Own one font program; table descriptors and cmap candidates are spans into it. Duplicate tags and nonempty overlaps/directory aliases fail without payload copies. Allocation threshold, 256-entry directory, legal empty glyf/unknown tables, illegal empty required tables, arithmetic bounds, cmap 4/12 and repeated cmap candidates are covered in FontHardeningTests. |
| F2 | An already-cancelled SaveAsync erased KEEP or created a new empty destination | Check token before FileMode.Create. TextLayoutHardeningTests asserts cancellation plus unchanged content/no newly created file. Mid-write save remains non-atomic. |
| F3 | Bad stream dictionary emitted an object prefix; Finish could then succeed. Partial output errors left writer usable; Dispose could mask the original exception | Pre-serialize/validate before prefix. Active/Finished/Faulted/Disposed states; write/flush exceptions and in-operation cancellation fault the writer. Tests inject partial prefix/body/terminator/xref writes, flush failures, async cancellation and secondary Dispose failures, then assert every Reserve/Write/Finish variant rejects reuse. Pre-emission validation errors leave the reservation retryable. |
| F4 | Auto cells with CRLF/CR/tab failed on missing control glyphs although Flex wrapped them | Shared internal normalization in measurement/wrapping; Auto measures the widest normalized line. 32 header/body × Auto/Flex × style-override cases plus direct measurement cases cover CRLF, CR, LF and four-space tabs. |
| F6 | A 23-byte SOI/SOF/EOI sequence without SOS was accepted | Walk bounded markers/scans, validate supported frame and scan headers/selectors, reject empty/truncated scans and missing EOI. JpegHardeningTests covers malformed lengths, duplicate/undefined selectors, baseline and generated progressive fixture; original JPEG bytes remain identical in DCTDecode streams. |
| F7 | A nested /Pages 999 0 R or another writer's reference could be finalized | Validate exact reservation identity recursively through dictionaries/arrays/stream dictionaries before output; direct cycles/depth fail, shared acyclic containers and same-writer forward references remain valid. WriterHardeningTests covers manual same-number clones, foreign refs, forward refs and trailer behavior. |
| F9 | NBSP/narrow NBSP were treated as normal whitespace; emergency breaks split protected sequences | Tokens containing U+00A0/U+202F/U+FEFF/U+2007 remain intact and move to the next line; wider-than-full-line tokens raise a contextual error. Portable synthetic embedded-font regressions cover FY NBSP 26, 1 narrow-NBSP 000, A NBSP B, FEFF/figure-space, widths and ToUnicode preservation. Ordinary long-word/grapheme emergency breaks remain. |

The old artifacts/review/probe remains historical observational evidence, not a post-fix test runner: its unhandled manually constructed foreign reference now correctly throws. No old observation is presented as a passing regression.

## Tests and build

| Target | Passed | Failed | Skipped | Total |
|---|---:|---:|---:|---:|
| net8.0 | 218 | 0 | 1 | 219 |
| net10.0 | 218 | 0 | 1 | 219 |

The skip is ThaiStackedMarksReceiveContextualOffsets; advanced GPOS remains unimplemented. This is the same suite on two runtimes, not 438 different scenarios. TRX's per-result NotExecuted outcome is used for skip accounting (the adapter summary notExecuted counter is zero despite the one skipped result).

Full solution Release build: **0 warnings, 0 errors**. Tests were run with compilation enabled, not solely --no-build. Restore used the existing local NuGet cache because network package access is restricted; NuGetAudit=false was passed to offline restore only. No online vulnerability audit is claimed.

Executed commands from the repository root:

~~~text
dotnet restore NovaPdf.sln --source C:/Users/kstiw/.nuget/packages -p:NuGetAudit=false
dotnet build NovaPdf.sln -c Release --no-restore
dotnet test NovaPdf.sln -c Release --no-restore --logger "trx;LogFilePrefix=release" --results-directory artifacts/hardening/tests
dotnet pack NovaPdf.sln -c Release --no-build --no-restore -o artifacts/packages
python scripts/inspect-packages.py 0.1.0-alpha.2
~~~

New regressions generate their own synthetic font bytes under the repository MIT license; see tests/NovaPdf.Tests/Fixtures.md. They do not depend on Tahoma. The pre-existing real-Thai-font test still uses NOVAPDF_TEST_FONT or Windows Tahoma explicitly; this patch does not make that legacy test hermetic or claim cross-platform CI was run.

## F1 allocation measurement

GC.GetAllocatedBytesForCurrentThread measured only FontFace.Load plus the caught parser exception, after warmup and fixture construction. Input is exactly 1,048,576 bytes with 32 descriptors, not a multi-GiB fixture.

| Runtime | Before bytes allocated | After bytes allocated |
|---|---:|---:|
| .NET 8 | 34,574,880 | 1,052,800 |
| .NET 10 | 34,574,808 | 1,052,256 |

Approximately 97% lower allocation for this malformed-input case. This is not an end-to-end performance comparison or a peak-live-memory claim. The regression permits at most two input sizes plus 256 KiB for metadata/exception overhead; a per-table copy implementation exceeds that bound. A separate repeated-cmap-candidate regression guards against selection-time payload copying.

Evidence: before_*.trx and release_*.trx under artifacts/hardening/tests, plus artifacts/hardening/final-evidence.json.

## Independent PDF and web validation

Available and actually run: **pypdf 6.13.1** strict parsing/extraction and **PyMuPDF 1.27.2.3** structural opening/rendering with repair rejection. qpdf, pdfinfo, pdftotext, mutool and Ghostscript were not found on PATH; none is claimed. No formal ISO/PDF-A/PDF-UA certification or browser-interaction run is claimed.

- Console samples generated with the patched engine on both runtimes: 7 PDFs each, 14 total. All pages were independently rendered; required Thai extraction, 500-row preservation, repeated headers/page numbering and PNG soft masks passed scripts/validate-pdfs.py.
- Output folders: artifacts/hardening/pdf/net8.0 and artifacts/hardening/pdf/net10.0. Each contains hello-world.pdf, business-report.pdf, long-table-500-rows.pdf, thai-unicode.pdf, debug-layout.pdf, images.pdf and simple-10-pages.pdf.
- Updated ASP.NET server reports 0.1.0-alpha.2 at /api/status. scripts/test-export-web.py, test-aefs-web.py and test-edocket-web.py passed all 23 listed export scenarios plus invalid-input, overflow and deterministic-output checks.
- eDocket remains 4 pages by default and 139 pages with 5,000 audit events. General table 5,000 rows on Letter landscape remains 251 pages. No row-loss or repeated-header regression was found in these workloads.
- The local Export Studio remains available at http://127.0.0.1:5077 using the patch. Server PID is operational state, not a release guarantee.

JPEG validation remains structural passthrough: marker bounds, frame/scan parameter shapes, selectors, nonempty entropy segments and EOI. It does not decode coefficients or prove Huffman/restart/progressive completeness. See the [T.81 B.2 reference](https://www.w3.org/Graphics/JPEG/itu-t81.pdf) and architecture decision 17.

## NuGet and package-only consumer

Generated package version: **0.1.0-alpha.2**. Packages remain local; no publishing occurred. All six packages contain net8.0/net10.0 assemblies, XML docs, README and MIT metadata; matching symbol packages were generated. Package inspection verifies dependencies are NovaPdf packages only (plus the ASP.NET shared framework reference).

Exact nupkg paths:

- C:/ProjectMM/hjkl/artifacts/packages/NovaPdf.0.1.0-alpha.2.nupkg
- C:/ProjectMM/hjkl/artifacts/packages/NovaPdf.Core.0.1.0-alpha.2.nupkg
- C:/ProjectMM/hjkl/artifacts/packages/NovaPdf.Graphics.0.1.0-alpha.2.nupkg
- C:/ProjectMM/hjkl/artifacts/packages/NovaPdf.Text.0.1.0-alpha.2.nupkg
- C:/ProjectMM/hjkl/artifacts/packages/NovaPdf.Layout.0.1.0-alpha.2.nupkg
- C:/ProjectMM/hjkl/artifacts/packages/NovaPdf.AspNetCore.0.1.0-alpha.2.nupkg

Isolated consumer: artifacts/hardening/consumer/Consumer.csproj, excluded from NovaPdf.sln and using **PackageReference only**, with independent Directory.Build/Packages configuration. It restored exclusively from artifacts/packages into a separate consumer-cache. project.assets.json contains exactly the six NovaPdf libraries, each with type=package and version=0.1.0-alpha.2; no project references are present.

Consumer build: zero warnings/errors on both targets. Consumer runs passed on .NET 8.0.28 and 10.0.9: simple table PDF (including normalized Auto cells), ASP.NET IResult streaming, identical deterministic file/HTTP output and unchanged pre-cancelled destination. Four resulting PDFs passed independent strict read/render checks with alpha.2 Producer metadata.

~~~text
dotnet restore artifacts/hardening/consumer/Consumer.csproj --source C:/ProjectMM/hjkl/artifacts/packages --packages C:/ProjectMM/hjkl/artifacts/hardening/consumer-cache -p:NuGetAudit=false
dotnet build artifacts/hardening/consumer/Consumer.csproj -c Release --no-restore
dotnet artifacts/hardening/consumer/bin/Release/net8.0/Consumer.dll artifacts/hardening/consumer-output/net8.0
dotnet artifacts/hardening/consumer/bin/Release/net10.0/Consumer.dll artifacts/hardening/consumer-output/net10.0
~~~

## Important files changed

- Runtime: src/NovaPdf.Text/FontFace.cs, TextShaping.cs, new TextNormalization.cs; src/NovaPdf.Core/PdfFileWriter.cs and PdfObjects.cs; src/NovaPdf.Graphics/PdfImage.cs; src/NovaPdf.Layout/Table.cs; src/NovaPdf/PdfDocument.cs.
- Permanent regressions: FontHardeningTests.cs, WriterHardeningTests.cs, TextLayoutHardeningTests.cs, JpegHardeningTests.cs; extended synthetic FontFixture in TextGraphicsTests.cs; Fixtures.md.
- Fixture/tooling: assets/images/sample-progressive.jpg; scripts/create-image-fixtures.py and inspect-packages.py.
- Release metadata: Directory.Build.props, Core/PageModel.cs Producer and ASP.NET sample /api/status version.
- Behavior documentation: README.md, CHANGELOG.md, architecture decisions, historical review status note, verification documents.

No Git metadata is present in this workspace, so no commit was created. Previous user templates and unrelated files were preserved.

## Remaining issues and next workstream

F5 remains open: contextual shaping still chooses breaks by separately measured graphemes and does not guarantee final shaped-line width. ShapedGlyph/source-cluster semantics, GSUB/GPOS, bidi, fallback, Thai dictionary breaking, ActualText and rich inline text remain deferred. This patch introduces no shaping adapter and does not claim complete Thai typography or full UAX #14.

Font outline validation/fuzzing is not exhaustive. JPEG validation is not a decoder. The document tree/page plans/fonts/images remain resident, so the pipeline is not constant-memory. Atomic save, PDF reading/editing/signing/conformance, broader table features, portable real-font CI and general performance optimization remain outside this patch.

Recommended next workstream: design source ranges/cluster ownership and extraction semantics, measure final shaped lines, retain renderable glyph runs, then evaluate an isolated non-PDF shaping adapter. Keep Core dependency-free and verify Thai typography separately from Unicode extraction.

## Evidence index

- artifacts/hardening/build.txt and release-checks.json: solution build/test and console sample runs.
- artifacts/hardening/tests/before_*.trx and release_*.trx: before-fix failures and final tests/allocation output.
- artifacts/hardening/pack-and-web.json: packaging and current-server HTTP checks.
- artifacts/hardening/validation-net8.0.json and validation-net10.0.json: independent console PDF checks.
- artifacts/hardening/packages.json, consumer-build.txt, consumer-runs.json, consumer-dependencies.json: package checks.
- artifacts/hardening/final-evidence.json: allocation/test summary, independent consumer PDF validation and server version.
