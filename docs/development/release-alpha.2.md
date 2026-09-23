# Kypelon.Pdf 0.2.0-alpha.2 release verification

Kypelon — Native PDF & Document Engine for .NET — **by Konkaew**  
Date: 2026-09-23. Status: **local release gates passed; public publication blocked, not successful**.

## Publication status

No package was pushed. NUGET_API_KEY is absent from both the inherited process and Windows user environment (presence checked without reading its value into tool output). Requests to NuGet's authoritative flat-container API for all six IDs failed with EACCES in this environment. This does not prove availability or a collision. NuGet authentication/ownership and version availability remain unverified.

The six push outcomes are **NotAttempted**. The mandatory fresh **nuget.org-only** consumer has **not run**, for either framework. The local consumer is not a substitute for this gate. There are no verified public package URLs to report.

Evidence is in artifacts/release-alpha.2, including local-gates.json, publication-status.json and nuget-id-check.json. Old alpha.1 packages/PDFs/web evidence were moved intact to artifacts/history/kypelon-alpha.1; current artifacts/packages contains only the six alpha.2 packages and six matching symbol packages.

## Engineering closure

| Item | Reproduction / contract | Result and permanent regression |
| --- | --- | --- |
| NP-201 | Valid page 1; invalid page-2 rectangle Color(2,0,0). Before: Plan succeeded; sync/async/HTTP wrote 306 bytes on net8 or 312 on net10; Save/SaveAsync replaced KEEP with partial output. Six new tests failed on each original runtime. | Central RenderCommandValidation checks final text, rectangle, image and link commands before output. All six original reproductions pass. Thirty command variants cover invalid numbers, colors, dimensions, prepared-state mismatch, reservations, image resources and links, with zero sync/async/HTTP body bytes. Existing file contents survive deterministic preparation failure. |
| NP-202 | A-space, A-space-B, double spaces and word-space-word-space leave gaps between retained source ranges. | Documented intentional U+0020 wrap/end normalization; no wrapping algorithm change. Tests assert exact normalized ranges and omitted units. Protected U+00A0/U+202F/U+FEFF/U+2007, CRLF/CR and tab expansion remain covered. |
| NP-203 | Export Studio used Plan followed by WriteAsync, causing preparation twice. | Prepare returns immutable PreparedPdfDocument with resolved numbers, retained glyphs, metadata/options snapshot and independent writer/resource state on each write. Tests cover model/style/options/metadata mutation, no rendering shaper calls, repeated writes, files, cancellation, failed destination recovery, read-only collections and simultaneous independent outputs. Export Studio uses prepared.PdfFile. |
| Adapter contract | Invisible source cannot silently disappear from cluster coverage. | Synthetic leading/trailing/interior/all-default-ignorable tests and documented nonempty-cluster glyph obligation. No adapter implemented. |
| Positioning | Zero/signed advances, offsets, vertical advance and non-monotonic candidate widths. | Exact prepared advance/positions and PDF Tm regressions. Accepted lines fit their final advance. Width excludes ink bounds; greedy fitting does not promise a globally longest fitting prefix. |

Preparation also checks metadata serialization and the font CID count before output. It does not promise rollback for cancellation/I/O failure, constant memory, full ink containment for arbitrary custom commands, or safety certification for all untrusted input.

Important files:

- src/Kypelon.Pdf/PdfDocument.cs
- src/Kypelon.Pdf/PreparedPdfDocument.cs
- src/Kypelon.Pdf/RenderCommandValidation.cs
- src/Kypelon.Pdf.AspNetCore/PdfResult.cs
- src/Kypelon.Pdf.Text/TextShaping.cs and FontResource.cs
- tests/Kypelon.Pdf.Tests/PreparationReproductionTests.cs
- tests/Kypelon.Pdf.Tests/PreparedDocumentTests.cs
- tests/Kypelon.Pdf.Tests/TextReleaseContractTests.cs
- samples/Kypelon.Pdf.Sample.AspNetCore/Program.cs
- benchmarks/Kypelon.Pdf.Benchmarks/PreparedBaseline.cs and TextPipelineBaseline.cs
- Directory.Build.props, README.md, CHANGELOG.md
- docs/architecture/preparation-alpha.2.md
- scripts/inspect-packages.py, verify-release-pdfs.py, verify-release-gates.py, publish-alpha.2.ps1

No PDF writer/layout algorithm redesign, advanced shaping adapter, or new typography/PDF feature was added.

## Fresh build and tests

SDK 10.0.301; runtimes .NET 8.0.28 and 10.0.9 on Windows. Restore used the already installed local NuGet cache because external network access is restricted. NuGetAudit was disabled for that offline restore; no online vulnerability audit is claimed.

~~~powershell
dotnet restore Kypelon.sln --source C:/Users/kstiw/.nuget/packages -p:NuGetAudit=false
dotnet build Kypelon.sln -c Release --no-restore
dotnet test tests/Kypelon.Pdf.Tests/Kypelon.Pdf.Tests.csproj -c Release -f net8.0 --no-restore
dotnet test tests/Kypelon.Pdf.Tests/Kypelon.Pdf.Tests.csproj -c Release -f net10.0 --no-restore
dotnet pack Kypelon.sln -c Release --no-build --no-restore -o artifacts/packages
~~~

| Gate | net8.0 | net10.0 |
| --- | ---: | ---: |
| Passed | 384 | 384 |
| Failed | 0 | 0 |
| Skipped | 1 | 1 |
| Total | 385 | 385 |

Release build: **0 warnings, 0 errors**. Tests rebuilt their targets; these are fresh results, not reused TRX or no-build-only tests. The original suite had 317 passed / 1 skipped: 67 passing regression cases were added. ThaiStackedMarksReceiveContextualOffsets remains the sole skip.

TRX: artifacts/release-alpha.2/tests/release-net8.0.trx and release-net10.0.trx. Before-fix reproduction TRX/logs and source-before.json are preserved as explicitly historical alpha.1 evidence.

## PDF, web and extraction validation

Independent tools actually used: **pypdf 6.13.1** strict parsing and **PyMuPDF 1.27.2.3** parsing/extraction/rendering. qpdf, Poppler pdfinfo/pdftotext, mutool and Ghostscript executables were unavailable; they were not run.

| Current artifact | Pages |
| --- | ---: |
| artifacts/pdf/kypelon-business.pdf | 3 |
| artifacts/pdf/kypelon-table.pdf | 17 |
| artifacts/pdf/kypelon-unicode.pdf | 1 |
| artifacts/pdf/kypelon-aefs.pdf | 1 |
| artifacts/pdf/kypelon-edocket.pdf | 4 |

All five were regenerated through the current Export Studio. Every decompressed page content stream is byte-for-byte identical to its alpha.1 counterpart. Page counts, wraps, positions and report drawing commands therefore remain unchanged; producer metadata is now Kypelon.Pdf 0.2.0-alpha.2. Independent parsing/rendering reported no repair requirement; extracted text bounds remain on-page. A visual spot-check of the regenerated business and AEFS first pages and eDocket pages 2/4 found no clipping or header/footer collision.

Three existing web suites passed: 6 general export, 6 AEFS and 11 eDocket scenarios (23 total), including their invalid-input checks. Table checks confirm all 500 distinct rows, repeated headers and Page X of Y; Unicode checks confirm Thai text; AEFS links remain URI annotations. Existing scripts check wrapped text, footer separation and continuation. Large web scenarios retain 251 pages for the 5,000-row export and 139 pages for the 5,000-event eDocket report.

The console sample generated and validated seven PDFs per framework (14). The isolated package consumer generated another seven per framework (14). All 18 source-cluster extraction fixtures pass in PyMuPDF, including ligature, expansion, reordering, surrogate, combining and Thai cases. pypdf does not honor ActualText consistently for complex output: its raw extraction discrepancies are recorded rather than hidden or treated as a new typography capability.

## Performance

BCL baseline harness, 2 warmups and 5 measured iterations per case. Document/font creation is outside the timed generation; preparation is included. Allocated bytes are measured on the generation thread. There is no cross-library claim and no peak-memory claim.

The direct-Write workload/data is identical between alpha.1 and alpha.2. The earlier net8/net10 baseline runs overlapped; later runs were sequential, so elapsed comparisons are noisy regression observations, not statistically controlled speed claims. Output size/page counts are unchanged within each framework. Validation increases direct-Write allocation by approximately 4–6%; elapsed times increased 14–62% in these observations. No extra shaping calls occurred in the instrumented direct-Write cases.

| Framework / workload | alpha.1 → alpha.2 mean ms | allocated bytes before → after | shaping calls before → after | PDF bytes / pages |
| --- | ---: | ---: | ---: | ---: |
| net8.0 / Table5000 | 460.08 → 654.83 | 264824939 → 274359749 | not instrumented | 608260 / 136 |
| net8.0 / MixedThaiEnglish500 | 39.94 → 49.70 | 19786008 → 21014088 | 2022 → 2022 | 583490 / 17 |
| net8.0 / ParagraphHeavy | 49.43 → 61.15 | 30400776 → 31693008 | 1215 → 1215 | 555814 / 14 |
| net10.0 / Table5000 | 450.16 → 515.21 | 257489973 → 267844042 | not instrumented | 762659 / 136 |
| net10.0 / MixedThaiEnglish500 | 27.69 → 41.12 | 19550952 → 20780936 | 2022 → 2022 | 684686 / 17 |
| net10.0 / ParagraphHeavy | 34.18 → 55.25 | 30357536 → 31651928 | 1215 → 1215 | 649831 / 14 |

The following comparison runs **both calling styles on alpha.2**, including preparation each iteration. It is not a claim about old-version timings. Prepare avoids the second plan and halves shaping calls in all three workloads.

| Framework / workload | Plan+Write → Prepare+Write ms | allocated bytes | shaping calls | PDF bytes / pages |
| --- | ---: | ---: | ---: | ---: |
| net8.0 / Table5000 | 952.48 → 584.72 | 435855184 → 274357243 | 80294 → 40147 | 608260 / 136 |
| net8.0 / MixedThaiEnglish500 | 64.93 → 45.33 | 31286088 → 21014088 | 4044 → 2022 | 583490 / 17 |
| net8.0 / ParagraphHeavy | 82.82 → 51.95 | 55727944 → 31693739 | 2430 → 1215 | 555814 / 14 |
| net10.0 / Table5000 | 851.84 → 499.99 | 425492506 → 267836322 | 80294 → 40147 | 762659 / 136 |
| net10.0 / MixedThaiEnglish500 | 55.67 → 29.07 | 30859414 → 20780808 | 4044 → 2022 | 684686 / 17 |
| net10.0 / ParagraphHeavy | 73.89 → 39.73 | 55593096 → 31651280 | 2430 → 1215 | 649831 / 14 |

Raw results and PDFs: artifacts/release-alpha.2/{before,after,prepared}/{net8.0,net10.0}/results.json. The difference between framework PDF sizes is unchanged from alpha.1; do not compare different runtime output sizes as a release regression. No observed pathological allocation, unbounded growth or accidental repeated rendering-time shaping.

## Packages and local consumer

Every package was inspected for exact ID/version, dependency IDs/versions, both lib TFMs, current DLL names, XML documentation, README, MIT license, Konkaew authorship, factual release notes and tags. Symbols contain portable PDBs and only NuGet-supported symbol-package file extensions. No NovaPdf runtime dependency or DLL is present.

- artifacts/packages/Kypelon.Pdf.0.2.0-alpha.2.nupkg
- artifacts/packages/Kypelon.Pdf.AspNetCore.0.2.0-alpha.2.nupkg
- artifacts/packages/Kypelon.Pdf.Core.0.2.0-alpha.2.nupkg
- artifacts/packages/Kypelon.Pdf.Graphics.0.2.0-alpha.2.nupkg
- artifacts/packages/Kypelon.Pdf.Layout.0.2.0-alpha.2.nupkg
- artifacts/packages/Kypelon.Pdf.Text.0.2.0-alpha.2.nupkg

SHA-256 fingerprints are in artifacts/release-alpha.2/packages.json; each nupkg has an adjacent matching snupkg.

Dependency-derived publication order:

1. Kypelon.Pdf.Core
2. Kypelon.Pdf.Graphics
3. Kypelon.Pdf.Text
4. Kypelon.Pdf.Layout
5. Kypelon.Pdf
6. Kypelon.Pdf.AspNetCore

The new isolated consumer at artifacts/release-alpha.2/consumer has no ProjectReference and resolves all six dependencies via PackageReference from artifacts/packages only, into a fresh local-consumer-cache. Both framework builds have 0 warnings/errors. All seven scenarios pass on both: basic PDF, 500-row table, Thai/Unicode, prepared snapshots, ASP.NET streaming, cancellation and synthetic contextual shaping. The Thai scenario uses an explicitly supplied installed Tahoma font; that font is not redistributed.

The current runtime/sample/test/benchmark/consumer source scan has zero old product names. Explicit historical evidence and migration documentation remain unchanged.

## Publication handoff and outstanding mandatory gates

scripts/publish-alpha.2.ps1 was dry-checked only: its local-check path passed in an earlier process launched with ExecutionPolicy Bypass. A later normal invocation was rejected by this machine's script execution policy; no persistent policy setting was changed, and no push ran. Use a permitted release environment to run the script. It verifies locked source/evidence/package hashes, derives the dependency order, and reads NUGET_API_KEY from the calling process. It never writes the key to source; captured CLI output is redacted before logging. A nonzero push result or duplicate response stops the script before dependents. A duplicate requires independent review, not an assumption that the public package is ours.

After network access and credentials are available, use the reviewed artifacts. The already-authorized publication command is:

~~~powershell
./scripts/publish-alpha.2.ps1 -Publish
~~~

The script uses https://api.nuget.org/v3/index.json. Adjacent snupkg files use NuGet CLI's supported automatic symbol push flow; see the [official symbol-package documentation](https://learn.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg). Symbol server validation/indexing must still be checked after upload.

Remaining gates, not completed here:

1. Check real NuGet API package/version metadata; the push result is authoritative for publish permission/ID conflicts.
2. Execute the six primary/symbol pushes in the derived order. Stop on authentication, collision or package rejection.
3. Confirm indexed ID/version, dependencies, license, tags and rendered README through NuGet.
4. Create a new consumer directory and new empty NuGet cache, using only nuget.org as its package source. Do not reuse local-consumer-cache or add the local feed.
5. Repeat the seven consumer scenarios for net8.0/net10.0, independently parse/render their PDFs, and preserve public restore/assets logs proving the remote source.
6. Only then mark the public release successful and report public URLs.

Intended public installation command (availability is **not verified**):

~~~sh
dotnet add package Kypelon.Pdf --version 0.2.0-alpha.2
~~~

## Known alpha limitations and security

Alpha API; no real GSUB/GPOS, bidi, font fallback, Thai dictionary segmentation, complete Thai typography, rich text, subsetting, Reader/Edit/signing or PDF/A/PDF/UA conformance. Advanced complex extraction requires an ActualText-aware reader. Full embedded fonts, page plans and decoded images remain in memory. Mid-write failures are not atomic. No shaping-adapter workstream was begun.

No API key or credential value was obtained or entered into source, logs, packages or artifacts. Only credential-presence booleans were recorded. Public release remains blocked by missing credentials and restricted NuGet network access.
