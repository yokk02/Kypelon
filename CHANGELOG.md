# Changelog

## 0.2.0-alpha.2 — first public alpha release candidate

- NP-201: centralized final render-command validation before stream/HTTP output or opening Save targets.
- NP-202: document and test intentional U+0020 wrap-boundary normalization; preserve protected whitespace rules.
- NP-203: immutable reusable PreparedPdfDocument, resolved page numbers, independent per-write resources and prepared ASP.NET results. Export Studio prepares once.
- Add default-ignorable adapter obligations and signed-positioning/non-monotonic-width regressions; no shaping adapter or new typography features.
- Public NuGet metadata, README, local/public package-consumer release gates and comparable preparation benchmarks. Publication status is recorded in the [release verification report](docs/development/release-alpha.2.md).
- Alpha API: advanced Thai GSUB/GPOS, bidi/fallback, Reader/Edit/signing and PDF/A/PDF/UA remain unimplemented. Mid-write I/O failures remain non-atomic.

## Brand migration — 2026-09-23

NovaPdf 0.2.0-alpha.1 was renamed before public release to **Kypelon.Pdf 0.2.0-alpha.1**, by Konkaew. Package IDs, namespaces, projects, metadata and sample branding changed; PDF/layout/text semantics did not. No compatibility packages or public NuGet publication. The next engineering release remains **Kypelon.Pdf 0.2.0-alpha.2** (preparation-contract workstream), not implemented here.

The release entries below describe development under the former NovaPdf name. Historical test and benchmark evidence retains its original identity.

## 0.2.0-alpha.1 — 2026-09-23

- Breaking text API: ITextShaper.Shape(TextRun), absolute UTF-16 TextCluster ownership, positioned glyphs, and validated immutable PreparedGlyphRun/TextLine. See the text-pipeline migration guide.
- F5: fit complete shaped candidate lines; retain their exact positions through paragraph/table pagination and rendering. No rendering-time shaper calls.
- Validate custom glyph IDs, source topology, advances/offsets and scaled positions before output; preflight CID encoding. Save prepares text before opening its destination; later failures remain non-atomic.
- Preserve surrogate/combining/Thai source ownership, normalized-to-original ranges, and alpha.2 no-break rules. Add explicit direction/script/language hints without automatic bidi itemization.
- Keep simple/ligature ToUnicode mappings; use Span ActualText for complex expansion/reordering/positioning without duplicating source clusters. Readers that ignore ActualText retain documented extraction limitations.
- Add portable source-cluster, contextual-width, mutation, call-count, sync/async/HTTP preflight and independent extraction regressions, plus equivalent-workload benchmarks.
- GSUB/GPOS, advanced Thai typography, bidi/fallback/segmentation and optional native adapters remain unimplemented.

## 0.1.0-alpha.2 — 2026-09-23

- F1: keep one immutable font program; use bounded table/cmap slices; reject overlapping nonempty ranges and directory aliases without allocation amplification.
- F2: pre-cancelled SaveAsync leaves an existing file unchanged and does not create a new file. Mid-write failures can still leave partial output.
- F3: validate stream dictionaries before output; destination write/flush failures permanently fault the writer. Secondary disposal errors do not mask an earlier output failure.
- F7: recursively validate exact reserved-reference ownership through arrays, dictionaries and stream dictionaries; reject cyclic/over-depth direct containers before output.
- F4: share CRLF/CR/LF and four-space tab normalization across wrapping and width measurement, including Auto table headers/body and cell styles.
- F9: keep tokens containing NBSP, narrow NBSP, zero-width no-break space or figure space intact; over-wide no-break tokens fail clearly rather than emergency-split. This is not full Unicode line breaking.
- F6: validate JPEG marker/scan structure and terminal EOI while preserving original baseline/progressive bytes. Entropy decoding and full progressive-sequence validation remain outside scope.
- Add portable synthetic-font, allocation, malformed-input, sync/async failure and passthrough regression tests.
- F5/contextual shaping, advanced Thai typography, bidi/fallback, subsetting and full atomic save remain deferred.

## 0.1.0-alpha.1 — 2026-09-23

- Original PDF 1.7 object model, forward-only serializer, classic xref, Flate streams and metadata.
- Top-left graphics, RGB/gray color abstraction, paths, transforms and clipping.
- Bounded static TrueType parser, cmap 4/12, full embedding and Unicode ToUnicode mappings.
- Own measure/arrange/page-plan pipeline with text, paragraphs, columns, rows, grids, containers, tables and page breaks.
- Fixed/flex/auto columns, repeated headers, row pagination, page headers/footers and page X of Y.
- JPEG/RGB/RGBA PNG support including alpha masks; debug bounds and diagnostic events.
- Async/non-seekable streaming, ASP.NET result, .NET 8/10 samples and automated regression suite.
- Local Export Studio with editable business, table, Unicode and AEFS notification templates.
- Native AEFS template, custom-element fluent entry point, rounded vector rectangles and HTTP(S) link annotations.
- Four-section eDocket report template with synthetic workflow presets, archival metadata and up to 5,000 audit events.
- Per-cell table text/background styles, style-aware auto sizing and repeated-header regression coverage.
- Thai contextual shaping remains incomplete and has an explicitly skipped regression.
