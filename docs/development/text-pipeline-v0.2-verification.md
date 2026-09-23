> Historical evidence for Kypelon (formerly NovaPdf during pre-public alpha development). This report records the original names/results; use README.md and docs/development/kypelon-brand-migration.md for current commands and verification.

# NovaPdf 0.2.0-alpha.1 — text pipeline verification

Verified 2026-09-23 on Windows with SDK 10.0.301, runtimes 8.0.28 / 10.0.9. Original PDF serialization/layout and existing report templates are preserved. This is a breaking alpha text API release; no shaping dependency, Reader/Edit/Signing/PDF-A/PDF-UA implementation was added.

## Status, reproduction and closure

| Case | Alpha.2 reproduction | v0.2 permanent assertion |
|---|---|---|
| F5 expansion | A=6pt, V=6pt, AV=18pt; available 15pt; old wrapper returns AV | Returns A and V, each 6pt, every final width ≤15pt; paragraph/table plans retain the checked runs |
| F5 contraction | A=10pt, V=10pt, AV=14pt; available 15pt; old wrapper returns two lines | Returns one AV line, width 14pt |
| Late custom glyph failure | Invalid Z glyph on page 2 throws after 281 output bytes, sync and async | Invalid result fails in Plan with zero stream/HTTP body bytes; invalid Save preparation preserves KEEP |
| Render re-shaping | Measurement result discarded; rendering invokes adapter again | A fitting final line calls the shaper exactly once; paginated writes have the same call count at first I/O and completion |
| Complex source ownership | Per-glyph strings cannot distinguish expansion and logical/visual order | Ligatures, expansion, reordered/interleaved glyphs, surrogate pairs and Thai combining ranges preserve logical source; independent extraction passes |

The four original reproduction cases were added/run before runtime changes and failed on **both frameworks**. Evidence: artifacts/text-v0.2/reproduction-before.txt and tests/before_*.trx. The old observational probe is retained as historical evidence, not relabeled as a passing v0.2 runner.

## Architecture and API

See [text-pipeline-v0.2.md](../architecture/text-pipeline-v0.2.md) for the old/new data-flow diagram, source/cluster topology, fitting strategy, preflight validation, immutable mutation semantics, extraction policy, API migration and optional adapter investigation.

Important changes:

- New TextSource.cs and LineBreaker.cs; revised TextShaping.cs and FontResource.cs.
- Layout Model.cs, Paginator.cs and Table.cs retain prepared lines and forward shaping hints.
- PdfDocument.cs prepares page numbers/custom commands, preflights CID assignment and renders exact glyph positions. Save prepares text before opening a file. Owned-output disposal is preserved when preparation fails.
- PdfCanvas.cs adds balanced Span ActualText operators. Core writer/parser hardening code is unchanged; Core's Producer version is updated only.
- TextPipelineReproductionTests.cs and TextPipelineTests.cs add permanent regressions; the existing font-encoding test migrates to the prepared API.
- scripts/validate-text-pipeline.py, benchmark TextPipelineBaseline.cs, README, CHANGELOG and architectural notes document and verify the change.

## Automated results

| Target | Passed | Failed | Skipped | Total |
|---|---:|---:|---:|---:|
| net8.0 | 317 | 0 | 1 | 318 |
| net10.0 | 317 | 0 | 1 | 318 |

There are **99 added test cases** over alpha.2's 219 total. The same suite runs twice, not 636 distinct cases. The one intentional skip remains ThaiStackedMarksReceiveContextualOffsets; no GPOS capability is claimed. New font-dependent tests use original portable synthetic sfnt data; existing real-Thai-font tests still require NOVAPDF_TEST_FONT or installed Windows Tahoma. See tests/NovaPdf.Tests/Fixtures.md.

Release solution build: **0 warnings, 0 errors**. Tests compiled in Release, not solely --no-build. Both package-consumer targets also built with zero warnings/errors. No Linux/AOT/CI run is claimed. No Git metadata is present, so no commit was created.

Executed from the repository root (restore used the existing local cache; no online vulnerability audit is claimed):

~~~text
dotnet restore NovaPdf.sln --source C:/Users/kstiw/.nuget/packages -p:NuGetAudit=false
dotnet build NovaPdf.sln -c Release --no-restore
dotnet test NovaPdf.sln -c Release --no-restore --logger "trx;LogFilePrefix=release" --results-directory artifacts/text-v0.2/tests
dotnet pack NovaPdf.sln -c Release --no-build --no-restore -o artifacts/packages
python scripts/inspect-packages.py 0.2.0-alpha.1
python scripts/validate-text-pipeline.py
~~~

## Independent PDF/extraction validation

Actually used: **pypdf 6.13.1** strict parsing and **PyMuPDF 1.27.2.3** extraction/rendering, rejecting repaired documents. qpdf, pdfinfo, pdftotext, mutool and Ghostscript were not found on PATH and were not run. This is not formal ISO/PDF-A/PDF-UA certification.

- Nine synthetic source-cluster PDF cases per target, **18 total**, passed exact PyMuPDF logical extraction and rendering: ligature, native-width ligature, one-to-many, reordering, interleaving, surrogate pair, combining mark, full Thai corpus and reordered Thai clusters.
- pypdf also extracts simple, ligature, surrogate and combining/Thai ToUnicode cases exactly. Its installed extraction path ignores ActualText for complex cases: for example, AB visually reordered yields BA, and one-to-many A yields A plus a raw CID character. These observed compatibility limits are recorded in extraction/results.json, not hidden by normalizing/filtering extracted text.
- Seven console PDFs per runtime, **14 total**, passed existing structural/extraction/rendering checks, including all table rows, repeated headers, Thai text and image soft masks. Output: artifacts/text-v0.2/pdf/net8.0 and net10.0. Console page counts remain hello 1, business 5, long-table 14, Thai 1, debug 5, images 1, simple report 10.
- Six package-consumer PDFs passed strict parsing, extraction, rendering and Producer-version checks.

The synthetic font has empty outlines and validates source/encoding semantics only. Real business output uses the configured installed fonts; no font program is redistributed as a standalone repository/package asset.

## Existing web/report regression

All **23 existing export scenarios** passed scripts/test-export-web.py, test-aefs-web.py and test-edocket-web.py, including repeated table headers, complete row/event counts, page numbers, Thai extraction, wrapped fields, footer bounds, URI links, deterministic output and invalid-request handling. These were HTTP/API checks; browser clicking was not performed.

Requested filenames, all regenerated with v0.2 and independently checked:

| PDF path | Pages | Alpha.2 equivalent |
|---|---:|---:|
| artifacts/pdf/novapdf-business.pdf | 3 | 3 |
| artifacts/pdf/novapdf-table.pdf | 17 | 17 |
| artifacts/pdf/novapdf-unicode.pdf | 1 | 1 |
| artifacts/pdf/novapdf-aefs.pdf | 1 | 1 |
| artifacts/pdf/novapdf-edocket.pdf | 4 | 4 |

Business uses the existing 50-row web fixture; table uses the 500-row fixture. The larger 5,000-row Letter-landscape web table remains 251 pages; eDocket 5,000 events remains 139 pages. No template was changed to force the old counts. All pages of the five named PDFs render without repair and all extracted text-span bounds stay inside their pages. Visual review of business, Thai AEFS and the four eDocket pages found no new clipping/overlap; this is not a claim of correct Thai GPOS positioning. General overlap/ink-geometry certification is outside this release.

Export Studio reports 0.2.0-alpha.1 at http://127.0.0.1:5077/api/status and remains available locally while its process runs.

## Equivalent-workload performance

Fresh **alpha.2 before** measurements were taken before changing runtime code, using the same TextPipelineBaseline workloads. Two warmups, five synchronous generation-to-Stream.Null iterations with GC between iterations; document construction and font loading occur outside timing. Allocations are GC.GetAllocatedBytesForCurrentThread deltas, not peak live memory. Output is saved separately. This BCL harness is not BenchmarkDotNet and does not establish statistical significance; timing is noisy on this shared environment.

Table5000 is the unchanged existing Reports.Table(5000) Courier workload. MixedThaiEnglish500 has 500 four-column bilingual rows; ParagraphHeavy has 100 paragraphs of eight repeated Thai/English phrases. Both use installed Tahoma and a counting wrapper around BasicTextShaper. Only the wrapper's API signature changed between runs. No comparison with another library is made.

| Runtime | Workload | Pages | Mean ms before → after | Allocated MiB before → after | PDF bytes after (= before) |
|---|---|---:|---:|---:|---:|
| net8.0 | Table5000 | 136 | 315.25 → 375.58 | 229.22 → 252.56 | 608,256 |
| net8.0 | MixedThaiEnglish500 | 17 | 34.66 → 37.17 | 23.72 → 18.87 | 583,486 |
| net8.0 | ParagraphHeavy | 14 | 28.25 → 43.23 | 29.08 → 28.99 | 555,811 |
| net10.0 | Table5000 | 136 | 273.24 → 373.61 | 213.94 → 246.34 | 762,655 |
| net10.0 | MixedThaiEnglish500 | 17 | 22.98 → 23.69 | 23.10 → 18.65 | 684,682 |
| net10.0 | ParagraphHeavy | 14 | 29.24 → 33.43 | 29.00 → 28.95 | 649,827 |

| Workload | Shaper calls before | After |
|---|---:|---:|
| MixedThaiEnglish500 | 24,969 | 2,022 |
| ParagraphHeavy | 59,504 | 1,215 |
| Table5000 | Not instrumented | Not instrumented |

Lower invocation counts do not mean every workload is faster. Table5000 takes more time and allocation; immutable snapshots, topology checks and retained glyph data carry a cost. The measured increase is bounded, not a catastrophic repeat-shaping growth, but remains an optimization target. Mixed-report allocation decreases and paragraph allocation is approximately flat; elapsed-time changes do not justify a broad performance claim. PDF sizes/page counts stay identical for equivalent runs on the same runtime. .NET 8/10 compressed sizes differ as before.

Raw samples and PDFs: artifacts/text-v0.2/before/{net8.0,net10.0}/results.json and after/{net8.0,net10.0}/results.json. The earlier docs/performance-baseline.md remains historical; these fresh runs are the appropriate before/after comparison.

~~~text
dotnet benchmarks/NovaPdf.Benchmarks/bin/Release/net8.0/NovaPdf.Benchmarks.dll --text-pipeline artifacts/text-v0.2/after/net8.0
dotnet benchmarks/NovaPdf.Benchmarks/bin/Release/net10.0/NovaPdf.Benchmarks.dll --text-pipeline artifacts/text-v0.2/after/net10.0
~~~

## NuGet and isolated consumer

All six packages contain net8.0/net10.0 binaries, XML docs, README and MIT metadata. Matching snupkg files were generated. No package was published.

Exact nupkg paths:

- C:/ProjectMM/hjkl/artifacts/packages/NovaPdf.0.2.0-alpha.1.nupkg
- C:/ProjectMM/hjkl/artifacts/packages/NovaPdf.Core.0.2.0-alpha.1.nupkg
- C:/ProjectMM/hjkl/artifacts/packages/NovaPdf.Graphics.0.2.0-alpha.1.nupkg
- C:/ProjectMM/hjkl/artifacts/packages/NovaPdf.Text.0.2.0-alpha.1.nupkg
- C:/ProjectMM/hjkl/artifacts/packages/NovaPdf.Layout.0.2.0-alpha.1.nupkg
- C:/ProjectMM/hjkl/artifacts/packages/NovaPdf.AspNetCore.0.2.0-alpha.1.nupkg

artifacts/text-v0.2/consumer/Consumer.csproj is outside NovaPdf.sln, with independent build/package props and **PackageReference only**. Restore from the local feed into consumer-cache-final resolved exactly six NovaPdf libraries, all type=package and version=0.2.0-alpha.1. Both runtimes passed normal PDF/file/HTTP streaming, deterministic equality, pre-cancellation, migrated TextRun/cluster API, the 14pt contextual AV case and exactly one shaper invocation for a final rendered line.

~~~text
dotnet restore artifacts/text-v0.2/consumer/Consumer.csproj --source C:/ProjectMM/hjkl/artifacts/packages --packages C:/ProjectMM/hjkl/artifacts/text-v0.2/consumer-cache-final -p:NuGetAudit=false
dotnet build artifacts/text-v0.2/consumer/Consumer.csproj -c Release --no-restore
dotnet artifacts/text-v0.2/consumer/bin/Release/net8.0/Consumer.dll artifacts/text-v0.2/consumer-output/net8.0
dotnet artifacts/text-v0.2/consumer/bin/Release/net10.0/Consumer.dll artifacts/text-v0.2/consumer-output/net10.0
~~~

## Remaining limitations and recommendation

F5 and the custom-shaper output-time validation problem are addressed. Real GSUB/GPOS/kerning/ligatures, bidi itemization, font fallback, Thai segmentation, rich text and subsetting remain unimplemented. Synthetic shapers prove the data model, not actual OpenType typography. The existing Thai contextual-offset test remains skipped. Fitting is greedy, not full UAX #14 or global optimal breaking; width is advance-based rather than an ink bounding box. Complex extraction requires ActualText-aware readers. Plans/encoded text scale with document size. Save remains non-atomic after preparation.

The foundation is ready for an isolated advanced-shaper **prototype**. Shipping an optional adapter should wait for pinned native deployment/AOT/license-size review, Windows/Linux tests, real-font Thai visual/extraction regressions and performance checks. No next-workstream implementation was started.

## Evidence index

- artifacts/text-v0.2/data-flow-before.md, reproduction-before.txt and tests/before_*.trx
- artifacts/text-v0.2/build.txt, test.txt and tests/release_*.trx
- artifacts/text-v0.2/extraction/results.json and per-framework fixture PDFs
- artifacts/text-v0.2/validation-net8.0.json and validation-net10.0.json
- artifacts/text-v0.2/web-results.json and test-*-web.py.txt
- artifacts/text-v0.2/before and after benchmark folders
- artifacts/text-v0.2/pack.txt, packages.json, consumer-build.txt, consumer-runs.json and consumer-dependencies.json
- artifacts/text-v0.2/final-evidence.json and edocket-review.png
