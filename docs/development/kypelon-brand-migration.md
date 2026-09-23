# Kypelon brand migration — 0.2.0-alpha.1

**Kypelon**  
Native PDF & Document Engine for .NET  
**by Konkaew**

Verified 2026-09-23 on Windows with SDK 10.0.301, .NET 8.0.28 and .NET 10.0.9.

## Scope and version

NovaPdf 0.2.0-alpha.1 was renamed before public release to **Kypelon.Pdf 0.2.0-alpha.1**. This is a brand/package/namespace migration only. The next engineering release remains **Kypelon.Pdf 0.2.0-alpha.2**. Its preparation-contract changes have not been started.

No compatibility packages, forwarding namespaces, external PDF engine, or new PDF feature were added. A source comparison against the captured pre-migration snapshot verifies all 41 C# source/test/sample/benchmark files differ only by enumerated brand substitutions and newline normalization. No layout, shaping, PDF serialization or test assertions were removed/reworked. Technical tests retain the same case counts.

No packages were published. NuGet.org searches returned no indexed results, but direct registry/API requests were inaccessible from this environment; public name/version availability is **not independently confirmed**. Version 0.2.0-alpha.1 is retained for local prerelease artifacts; check registry availability before any later publication.

## Identity mapping

The six rows map **project folder / csproj / assembly / PackageId / root namespace**:

| Pre-public identity | Current identity |
| --- | --- |
| NovaPdf | Kypelon.Pdf |
| NovaPdf.Core | Kypelon.Pdf.Core |
| NovaPdf.Graphics | Kypelon.Pdf.Graphics |
| NovaPdf.Text | Kypelon.Pdf.Text |
| NovaPdf.Layout | Kypelon.Pdf.Layout |
| NovaPdf.AspNetCore | Kypelon.Pdf.AspNetCore |

All runtime projects remain under src. Solution: NovaPdf.sln → Kypelon.sln. Project GUIDs and dependency direction remain intact.

| Development project | Current project/directory |
| --- | --- |
| NovaPdf.Tests | tests/Kypelon.Pdf.Tests |
| NovaPdf.Sample.Console | samples/Kypelon.Pdf.Sample.Console |
| NovaPdf.Sample.AspNetCore | samples/Kypelon.Pdf.Sample.AspNetCore |
| NovaPdf.Benchmarks | benchmarks/Kypelon.Pdf.Benchmarks |

Pdf.Document, PdfOptions, PdfDocument and other format/API type names remain unchanged. Import using Kypelon.Pdf; supporting namespace suffixes are unchanged.

## Consumer/configuration migration

- PackageReference: NovaPdf → Kypelon.Pdf, version 0.2.0-alpha.1; rename supporting references by the table.
- using NovaPdf / NovaPdf.Text etc. → using Kypelon.Pdf / Kypelon.Pdf.Text etc.
- Sample/test font configuration: NOVAPDF_FONT, NOVAPDF_BOLD_FONT, NOVAPDF_TEST_FONT → KYPELON_FONT, KYPELON_BOLD_FONT, KYPELON_TEST_FONT. No old-name aliases are retained.
- Optional pack property: NovaPdfRepositoryUrl → KypelonRepositoryUrl.
- Export diagnostics: X-NovaPdf-Pages / X-NovaPdf-Layout-Ms → X-Kypelon-Pages / X-Kypelon-Layout-Ms; UI and tests use the new headers.
- Routes remain /, /report, /api/status and /api/export. Download names use kypelon-.
- Producer defaults to Kypelon.Pdf 0.2.0-alpha.1; sample Creator values use Kypelon Export Studio. ToUnicode CMap's internal name becomes KypelonUnicode.
- README/UI use Kypelon, the subtitle and by Konkaew. PDF sample labels use KYPELON / REPORT STUDIO, KYPELON / ASSURANCE and Kypelon footers.

## Files changed

Kypelon.sln, Directory.Build.props, all project references/namespaces, README, CHANGELOG, current architecture/development examples, MIT attribution, sample UI and verification scripts were updated. Important new scripts:

- scripts/inspect-packages.py: exact package IDs/dependencies, metadata, both TFMs, XML documentation and symbol package checks.
- scripts/verify-brand-migration.py: C# changes limited to brand substitutions, pre/post PDF text comparison, metadata, pages, rendering, bounds and package-only output.
- scripts/scan-brand-migration.py: repository-wide case-insensitive inventory including generated files and archive members.

The complete old/new file inventory is artifacts/brand-migration/files-changed.json. The current independent consumer is artifacts/brand-migration/consumer; previous consumers under historical artifact directories remain evidence, not current build targets.

## Verification

Restore used the existing local dependency cache because remote package connectivity was unavailable; no online vulnerability-audit result is claimed.

~~~text
dotnet restore Kypelon.sln --source C:/Users/kstiw/.nuget/packages -p:NuGetAudit=false
dotnet build Kypelon.sln -c Release --no-restore
dotnet test tests/Kypelon.Pdf.Tests/Kypelon.Pdf.Tests.csproj -c Release -f net8.0 --no-restore
dotnet test tests/Kypelon.Pdf.Tests/Kypelon.Pdf.Tests.csproj -c Release -f net10.0 --no-restore
dotnet pack Kypelon.sln -c Release --no-build --no-restore -o artifacts/packages
python scripts/inspect-packages.py
python scripts/validate-text-pipeline.py
python scripts/verify-brand-migration.py
python scripts/scan-brand-migration.py
~~~

Build: **0 warnings, 0 errors**. Tests compile as part of the test commands.

| Target | Passed | Failed | Skipped | Total |
| --- | ---: | ---: | ---: | ---: |
| net8.0 | 317 | 0 | 1 | 318 |
| net10.0 | 317 | 0 | 1 | 318 |

The original ThaiStackedMarksReceiveContextualOffsets skip remains. No reduced pass count or fake GPOS pass.

The 23 existing HTTP export scenarios passed (6 general, 6 AEFS, 11 eDocket), including negative requests, deterministic output, Unicode, row/event completeness, repeated headers, URI links, page numbers and footer bounds. Routes and input data were unchanged. This is HTTP/API validation; no interactive browser-click test is claimed.

All seven console PDFs on each runtime passed scripts/validate-pdfs.py: 14 files total, including Unicode, business reports, the 500-row table, images and debug output.

## Five named PDFs

| Current artifact | Pages before → after |
| --- | ---: |
| artifacts/pdf/kypelon-business.pdf | 3 → 3 |
| artifacts/pdf/kypelon-table.pdf | 17 → 17 |
| artifacts/pdf/kypelon-unicode.pdf | 1 → 1 |
| artifacts/pdf/kypelon-aefs.pdf | 1 → 1 |
| artifacts/pdf/kypelon-edocket.pdf | 4 → 4 |

Business retains the existing 50-row fixture; table retains 500 rows. Extracted text matches the original files exactly after explicit brand substitutions, including the abbreviated NOVA header. No line-break or page-count changes were needed for these reports. Brand glyph widths/ink differ naturally; measured text remains inside page bounds. All pages render without document repair.

The larger web table remains 251 pages for 5,000 rows, and eDocket remains 139 pages for 5,000 events. No template geometry was adjusted.

## Extraction and independent validation

Actually run: pypdf 6.13.1 strict parsing and PyMuPDF 1.27.2.3 extraction/rendering. qpdf, pdfinfo/pdftotext, mutool and Ghostscript were not found and were not run.

All 18 prepared-text extraction fixtures passed their existing exact logical-source checks. Simple ToUnicode cases pass both readers; complex ActualText cases retain the existing pypdf interoperability limitations. No extraction strategy or shaping semantics changed. Six consumer PDFs also passed independent parsing/extraction/rendering and Producer checks.

Evidence: artifacts/brand-migration/extraction/results.json, behavior-verification.json, console-validation-net*.json and test-*-web.py.txt.

## NuGet packages

Authors: Konkaew. Product: Kypelon. Title and PackageId match each assembly. Description: Native C# PDF and document layout engine for .NET. MIT, both TFMs, XML docs, README and symbol packages are preserved.

Exact local nupkg paths:

- C:/ProjectMM/hjkl/artifacts/packages/Kypelon.Pdf.0.2.0-alpha.1.nupkg
- C:/ProjectMM/hjkl/artifacts/packages/Kypelon.Pdf.Core.0.2.0-alpha.1.nupkg
- C:/ProjectMM/hjkl/artifacts/packages/Kypelon.Pdf.Graphics.0.2.0-alpha.1.nupkg
- C:/ProjectMM/hjkl/artifacts/packages/Kypelon.Pdf.Text.0.2.0-alpha.1.nupkg
- C:/ProjectMM/hjkl/artifacts/packages/Kypelon.Pdf.Layout.0.2.0-alpha.1.nupkg
- C:/ProjectMM/hjkl/artifacts/packages/Kypelon.Pdf.AspNetCore.0.2.0-alpha.1.nupkg

Matching .snupkg files are alongside each package. Every archive was opened and verified. No package depends on NovaPdf, and no old DLL is present. The old package feed moved intact to artifacts/history/novapdf-prebrand/packages, outside the current feed.

## Package-only consumer

A fresh isolated restore from artifacts/packages into artifacts/brand-migration/consumer-cache resolved exactly six Kypelon.Pdf packages, all type=package/version=0.2.0-alpha.1, with no ProjectReference. Both runtime builds passed with zero warnings/errors. PDF/file/HTTP streaming, deterministic equality, pre-cancellation preservation, contextual 14pt AV fitting and single-call prepared rendering all passed.

~~~text
dotnet restore artifacts/brand-migration/consumer/Consumer.csproj --source artifacts/packages --packages artifacts/brand-migration/consumer-cache -p:NuGetAudit=false
dotnet build artifacts/brand-migration/consumer/Consumer.csproj -c Release --no-restore
dotnet artifacts/brand-migration/consumer/bin/Release/net8.0/Consumer.dll artifacts/brand-migration/consumer-output/net8.0
dotnet artifacts/brand-migration/consumer/bin/Release/net10.0/Consumer.dll artifacts/brand-migration/consumer-output/net10.0
~~~

## Historical references and stale-name gate

Current source namespaces/projects/packages, README code examples, sample UI, metadata and current consumer contain no old identity. Case-insensitive scanning also covers the abbreviated NOVA brand.

Intentional retained references are fully enumerated by file/member, line or byte offset, count and reason in artifacts/brand-migration/stale-name-scan.json and its readable .md inventory. The gate requires **zero unclassified matches**. It scans repository files and decompresses archive members once; nested archives are scanned as stored bytes. It excludes only its own generated reports to avoid recursion. Package metadata and decoded PDF text have separate semantic checks.

Retained categories:

- Explicit migration mapping/history in README, CHANGELOG, this report, architectural notes and migration audit scripts/evidence.
- Original MIT copyright notice, retained alongside the new Kypelon/Konkaew attribution.
- Historical release/review/benchmark/verification documents, marked as original pre-public evidence.
- Archived original probes, TRX/logs, package consumers/caches, packages, PDFs, ZIPs and manifests. Names/results were not rewritten to pretend earlier runs used Kypelon.
- Copies of README's explicit migration paragraph inside current packages and isolated consumer caches.
- The additional whole-word NOVA check also finds the Portuguese adjective nova (new) in ten external compiler/analyzer resource files/archive members (28 occurrences). These are documented non-brand false positives, not old product references; third-party resources are untouched.

See artifacts/HISTORY.md for archived locations. No Git metadata was available, so no commit was created.

## Limits and next release

Thai advanced GPOS/GSUB, bidi, fallback, segmentation, subsetting and other previously deferred capabilities remain unsupported. ActualText reader differences, retained plan memory, greedy line breaking, non-atomic saves and existing preparation-contract issues are unchanged. No performance improvement or new robustness guarantee is claimed. Historical benchmark values retain their original identities.

This migration ends at Kypelon.Pdf 0.2.0-alpha.1. The separate 0.2.0-alpha.2 preparation-contract workstream remains pending.
