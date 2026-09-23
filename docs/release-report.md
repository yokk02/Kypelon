> Historical evidence for Kypelon (formerly NovaPdf during pre-public alpha development). This report records the original names/results; use README.md and docs/development/kypelon-brand-migration.md for current commands and verification.

# NovaPdf 0.1.0-alpha.1 — release report

## Status

Working local alpha implemented from an empty workspace. Original C# writer and layout engine; no existing PDF generation engine is used at runtime. Both net8.0 and net10.0 build with zero warnings/errors. Packages are generated locally, not published. MIT license; project-level contributor metadata without an invented company or repository URL.

## Repository changes

NovaPdf.sln contains six runtime projects (Core, Graphics, Text, Layout, NovaPdf and AspNetCore), one consolidated xUnit project, console/ASP.NET samples and a benchmark executable. Shared deterministic/nullable/warnings-as-errors configuration, central test/benchmark package versions, editor configuration, XML documentation, README, change log, MIT license, architecture/specification/roadmap documents and verification scripts are included. Generated binaries/artifacts are Git-ignored. No existing repository or user work was replaced; no Git history was present.

## PDF engine

PDF 1.7 header/binary marker, typed direct values, names/strings/arrays/dictionaries, reserved indirect objects/references, exact stream lengths, Flate, forward-only classic xref, trailer/startxref/EOF, typed pages/resources/content streams, catalog/page tree and metadata. Core operates independently of layout. Graphics provides paths, RGB/gray color abstraction, fill/stroke, transforms, clipping, top-left coordinates and upright text/images.

Write/WriteAsync support non-seekable destinations and explicit leaveOpen. Async I/O never falls back to synchronous destination writes. Save/SaveAsync and explicit whole-document ToArray convenience are included. Layout/compression are CPU-synchronous and cancellation is checked between practical work units. One page's encoded/compressed content is retained at a time, with all page plans/font/image resources retained separately.

## Layout

Text/paragraph, spacer, line, image, column, row, basic equal-column grid, container, table and page break. Box dimensions, ranges, padding, margins, alignment, fill/borders, reusable styles and keep-together are supported with documented atomic/flow rules. Tables implement fixed/flex/capped-auto columns, cell padding/alignment, row heights, alternating fill, borders, automatic row pagination and repeated headers. Header/footer geometry is reserved; total pages are resolved before rendering. Debug bounds and diagnostic events are opt-in.

## Fonts and Thai

Static TrueType full embedding, cmap 4/12, metrics/advances, font names and OS/2 embedding permission checks. Type0/CIDFontType2 with Identity-H, explicit CIDToGIDMap and Unicode-alias-preserving UTF-16BE ToUnicode. Standard Courier/Courier-Bold support printable ASCII. FontFace is immutable and shareable; resource encoders are document-local.

Required Thai phrases, mixed English/Thai and Thai digits extract correctly in independent tests. The regression sample uses installed Tahoma/Tahoma Bold; font files are not redistributed. BasicTextShaper does not implement GSUB/GPOS, contextual mark attachment, Sara Am shaping, kerning, bidi or Thai dictionary word segmentation. Visual Thai correctness is incomplete. A skipped contextual-positioning fixture and detailed Thai roadmap make this limitation explicit.

## Samples

| Artifact | Pages | Bytes |
|---|---:|---:|
| artifacts/pdf/aspnet-report.pdf | 17 | 38,096 |
| artifacts/pdf/business-report.pdf | 5 | 1,232,637 |
| artifacts/pdf/debug-layout.pdf | 5 | 1,245,599 |
| artifacts/pdf/hello-world.pdf | 1 | 1,167 |
| artifacts/pdf/images.pdf | 1 | 13,336 |
| artifacts/pdf/long-table-500-rows.pdf | 14 | 715,905 |
| artifacts/pdf/package-consumer.pdf | 1 | 1,292 |
| artifacts/pdf/simple-10-pages.pdf | 10 | 10,102 |
| artifacts/pdf/thai-unicode.pdf | 1 | 1,218,275 |

Additional 5,000-row PDFs live under artifacts/benchmark/net8 and net10. Each has 136 pages. The business report contains bilingual client/engagement information, KPI cards, notes, team/work registers and multi-page sections.

## Tests and validation

- 83 xUnit cases per framework: **82 passed, 0 failed, 1 intentionally skipped**. Across both frameworks: 164 passed, 2 skipped.
- Structural tests check byte offsets, stream lengths, compression, escaping, object state, page tree/trailer and deterministic output. Other tests cover fonts, Unicode mappings, malformed inputs, graphics/images, wrapping/layout/pagination, exact row preservation, debug isolation, cancellation, stream ownership and ASP.NET headers.
- Independent pypdf 6.13.1 strict parsing and PyMuPDF 1.27.2.3 extraction/rendering succeeded on the sample PDFs. No MuPDF repair was needed. Required Thai phrases and every 500-row employee record were checked; every sample page was rendered. Business, Thai, images, debug and table-continuation pages were visually inspected.
- Both 5,000-row reports independently contain all 5,000 distinct records exactly once with correct page numbering. All 272 pages rendered.
- qpdf, pdfinfo, pdftotext, mutool and Ghostscript were unavailable; no such checks are claimed. No PDF/A/UA conformance claim is made.
- Live ASP.NET GET /report returned 200, application/pdf and attachment headers. Async-only response-stream tests passed.
- Fresh-cache consumer restored the final local packages and generated PDFs on both runtimes. Package inspection confirmed XML documentation, both TFMs, MIT metadata and no external NuGet runtime dependencies.

## Performance

.NET 10.0.9, Ryzen 7 5800X3D, two warmups / five measured runs, layout+serialization to Stream.Null; model construction/disk I/O excluded. Built-in fonts, Flate enabled.

| Scenario | Pages | Mean ms | Managed MiB/op | Output bytes |
|---|---:|---:|---:|---:|
| Simple10Pages | 10 | 5.74 | 2.66 | 10,052 |
| Table500 | 14 | 58.99 | 22.20 | 77,260 |
| Table5000 | 136 | 196.04 | 214.42 | 762,655 |

The .NET 8 results, ranges, process high-water marks, caveats and raw paths are in docs/performance-baseline.md. These are BCL-harness observations, not BenchmarkDotNet measurements. BDN dependencies were unavailable offline; its optional integration remains unexecuted. No comparison against another library is claimed. Allocations remain high and plans are not constant-memory.

## NuGet

Version 0.1.0-alpha.1. Exact package paths (with matching .snupkg siblings):

- artifacts/packages/NovaPdf.0.1.0-alpha.1.nupkg
- artifacts/packages/NovaPdf.AspNetCore.0.1.0-alpha.1.nupkg
- artifacts/packages/NovaPdf.Core.0.1.0-alpha.1.nupkg
- artifacts/packages/NovaPdf.Graphics.0.1.0-alpha.1.nupkg
- artifacts/packages/NovaPdf.Layout.0.1.0-alpha.1.nupkg
- artifacts/packages/NovaPdf.Text.0.1.0-alpha.1.nupkg

## Known gaps and architecture review

Advanced shaping/source-cluster semantics, subsetting, RTL, fallback chains, CFF/collections/variable fonts, spans/nested tables/row fragmentation, widow/orphan/keep-with-next, color management and accessibility tagging remain absent. Reader/editing, merge/split/stamp, forms, annotations, encryption/signatures and PDF/A/UA are roadmap items. Full-font embedding makes Unicode sample files large. Builders/graphs/options are mutable and should not be modified or shared concurrently during writing. Parsers have bounded access but not a sustained fuzz/security audit.

Before v0.2, upgrade glyph runs to source-cluster ranges and shape complete candidate lines; compact page plans and retain measured runs; add adversarial-input fuzzing and multi-reader CI. Avoid freezing the current public graph/adapter contracts as stable until those changes are reviewed.

## Next workstream

Thai text shaping and line breaking: source-cluster-aware glyph runs, an isolated tested shaping implementation, Thai word segmentation, exact extraction through many-to-many/reordered runs, openly licensed reference-font fixtures and native-language visual review. Safe subsetting and allocation reduction follow as separate measured changes.
