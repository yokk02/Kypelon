> Current Kypelon API names are used below. The pipeline was implemented as NovaPdf 0.2.0-alpha.1 before its pre-public rename; this brand migration does not change the contracts described here.

# Text pipeline — 0.2.0-alpha.1

## Decision and old flow

This release intentionally breaks the alpha shaping API. It preserves Kypelon.Pdf's own PDF writer, layout engine, font parser and business templates. No external shaping or PDF-generation dependency is added.

The alpha.2 flow was source normalization → grapheme-by-grapheme measurement → break selection → whole-line remeasurement without refitting → string-only TextCommand → another Shape call in rendering → per-glyph Unicode encoding. Paragraph fragmentation also reconstructed strings and repeated layout. F5 and late custom-shaper failures were consequences of this flow.

Before editing the runtime, four permanent reproduction tests failed on each target: positive AV expansion overflowed, negative AV adjustment broke too early, and invalid glyphs failed after 281 bytes on both sync and async output. The inspected call-site map is artifacts/text-v0.2/data-flow-before.md.

## Current flow

~~~mermaid
flowchart TD
    A[Original UTF-16 source] --> B[TextSource normalization + original-range map]
    B --> C[Legal opportunities and protected grapheme/no-break units]
    B --> D[Shape complete candidate TextRun]
    C --> E[Select and verify final line]
    D --> F[Validate clusters + positioned glyphs]
    F --> E
    E --> G[Immutable TextLine / PreparedGlyphRun]
    G --> H[Pagination retains the same run]
    H --> I[Resolve page numbers and prepare their runs]
    I --> J[Preflight per-session CID encoding]
    J --> K[Render exact positions, no shaper calls]
    K --> L[ToUnicode + ActualText when required]
    L --> M[Logical source extraction]
~~~

A whole-paragraph shaped run is a fitting search hint. Complete candidate lines are shaped and validated; the chosen candidate itself is retained. No final width is formed from independently shaped graphemes. The fast path shapes an already-fitting logical line once. When a paragraph splits between pages, fragments retain its prepared line objects instead of wrapping reconstructed strings.

Break discovery remains intentionally limited: supported whitespace, hyphen, explicit LF, grapheme emergency boundaries, and alpha.2 no-break-token protection. The search uses whole-paragraph cluster advances to choose an initial candidate, verifies it, backs off if necessary, and probes following candidates. This is greedy layout, not a globally optimal line-break solver for arbitrary non-monotonic adapter behavior. Every accepted line's exact advance fits its available width. Ordinary words may emergency-break; a protected token never does. Wider-than-line graphemes/no-break tokens fail explicitly. Full UAX #14 and Thai dictionary boundaries remain future work.

## Source and cluster contract

- TextSource owns immutable OriginalText and normalized Text. CRLF/CR → LF; tab → four spaces. Surrogate validation happens before segmentation. GetOriginalRange maps any normalized range back to original UTF-16 ownership. Expanded-tab fragments can map to the same original tab.
- TextRun selects a single-line, grapheme-aligned range in TextSource.Text and carries Font, Direction, Script and Language. Metrics are independent of point size. Offsets are **absolute normalized UTF-16 offsets**, not offsets relative to the substring, UTF-8 bytes or glyph indices.
- TextCluster is a nonempty source range. Clusters must partition the complete requested range in logical order, with no gaps/overlap and no boundary inside a surrogate pair or BCL grapheme sequence. Adjacent graphemes may be combined into one shaping cluster.
- PositionedGlyph references a cluster index. Glyphs are returned in visual drawing order and may revisit a cluster. Each cluster must own at least one glyph in this milestone. Multiple source characters can own one glyph; one cluster can own several glyphs; visual glyph order can differ from logical cluster order.
- Advances and offsets are signed font-unit numbers, positive Y upwards. Final horizontal advance must be nonnegative. Horizontal line width is the advance, **not an outline/ink bounding box**; fonts may have ink overhang. Nonzero Y advances are preserved when drawing, but vertical paragraph layout is not implemented.
- BasicTextShaper maps cmap scalars and hmtx advances, grouping combining sequences under their grapheme owner. It does not implement GSUB/GPOS, kerning, ligatures, bidi or dictionary segmentation. It explicitly rejects RTL requests; a capable custom adapter can return visual-order glyphs for an explicit direction. Direction hints do not perform automatic bidi itemization.

TextStyle carries the optional shaping hints; table Auto measurement and final cell wrapping use them consistently. There is no feature-set API or font-fallback itemizer yet.

## Validation and immutability

PreparedGlyphRun.Create snapshots adapter collections and validates font glyph bounds, exhaustive cluster topology, source boundaries, ownership, finite numerics and guardrails. Limits: 1,000,000 raw/normalized UTF-16 units, at most min(1,000,000, 16 × source length + 16) glyphs per shaped run, absolute per-glyph advance/offset ≤ 10,000,000 font units, cumulative advance ≤ 1,000,000,000 units, size ≤ 1,000,000pt, and prepared advance/position ≤ 1,000,000,000pt. Candidate fitting has a work budget of max(1,000,000, 64 × normalized source length) shaped UTF-16 units. These are explicit engineering guardrails, not a sandbox for arbitrary user adapter code.

Invalid results raise PdfFontException during planning, before any HTTP/stream output. FontResource accepts only prepared runs with the identical immutable font identity. Per-session CID assignment and its 65,534-entry limit are checked before destination writes. Save/SaveAsync now prepare text before opening the destination; later encoding/I/O failures can still leave an empty or partial file. This does not introduce atomic save.

TextLine owns the prepared run, point size, exact width and line metrics. TextCommand adds immutable placement, color, alignment and reserved width. Existing custom string-only TextCommands are prepared during PdfDocument.Plan; a command with a mismatching prepared snapshot is rejected. Final command/page collections are read-only. Plans are snapshots: changing an element, style or font setting afterward affects a subsequent Plan/Write, not the existing prepared state. A subsequent Write still builds a fresh plan; a public reusable PreparedPdfDocument API is not introduced.

Prepared objects and immutable FontFace instances can be shared safely. Builders/documents/resources remain mutable and must not be mutated while planning/writing. User shapers must be deterministic, must not mutate returned data concurrently with preparation, and must supply their own concurrency policy. BasicTextShaper is stateless. There is no process-global shaping cache. Candidate memoization is local to a line-fitting operation. Prepared glyphs and encoded CIDs remain resident with page plans; compressed content remains one page at a time. This is not constant-memory layout.

## Extraction policy

CID → ToUnicode remains the normal path. Scalar cmap sequences retain scalar mappings even when several glyphs share a combining cluster; one-glyph ligatures can map to the cluster's complete logical source. Unicode aliases retain different CIDs. Source ranges, not independently authored Unicode strings on every glyph, own extraction semantics.

For a cluster without a lossless scalar decomposition, only its first visual glyph gets its source mapping; additional glyphs have no independent ToUnicode entry. Reordered/interleaved glyphs, ambiguous expansion and adjusted positioning use a single /Span marked-content sequence with /ActualText around the **whole final line**. The replacement is the normalized logical line exactly once. It is serialized as an escaped PDF text string (UTF-16BE with BOM when needed). This prevents cluster duplication and visual-order extraction in readers supporting ActualText. It does not create a structure tree or imply PDF/UA compliance.

The mechanism follows ISO 32000-1 sections 9.10, 14.6 and 14.9.4: [PDF 1.7 specification](https://opensource.adobe.com/dc-acrobat-sdk-docs/standards/pdfstandards/pdf/PDF32000_2008.pdf). Span replacement is also described in the [PDF Association syntax guide](https://pdfa.org/wp-content/uploads/2023/07/Tagged-PDF-Best-Practice-Guide.pdf).

Independent tests use PyMuPDF for ActualText-aware extraction and pypdf for strict parsing and ordinary ToUnicode extraction. In the installed pypdf 6.13.1, complex ActualText spans are ignored: reordered text follows visual order, and extra unmapped glyphs may appear as raw CID characters. This reader limitation is recorded explicitly; Kypelon.Pdf does not promise complex extraction with readers that ignore ActualText. Simple text, ligatures, surrogate pairs and the existing Thai corpus remain extractable via ToUnicode alone.

## Public API migration

Version is **0.2.0-alpha.1**, not an alpha.3 patch.

Old:
~~~csharp
GlyphRun Shape(FontFace font, string text);
new ShapedGlyph(glyphId, unicodeString, advance, offsetX, offsetY);
~~~

New:
~~~csharp
GlyphRun Shape(TextRun input);
// A two-source-character ligature, one glyph; native font-unit metrics:
return new GlyphRun(
    [new TextCluster(input.SourceStart, input.SourceLength)],
    [new PositionedGlyph(glyphId, Cluster: 0, AdvanceX: advance)]);
~~~

Custom adapters must build the complete logical cluster partition and reference it from visual-order glyphs. Never assume one glyph per UTF-16 code unit. Use input.Source.Text and input.SourceStart/SourceLength; preserve explicit hints. Native adapter byte offsets must be translated before returning.

LineBreaker.Wrap still returns lines with Text/Width, now immutable prepared TextLine objects; constructing an arbitrary TextLine(text, width) is removed. LineBreaker.Prepare is the single-line entry point. FontResource.Encode now takes PreparedGlyphRun, e.g. Encode(LineBreaker.Prepare(text, font, size, shaper).Run). BasicTextShaper.Shape(font, text) remains a convenience producing raw adapter output; use Prepare for validated encoding. Ordinary fluent document APIs are unchanged.

## Optional adapter investigation — no dependency shipped

An isolated Kypelon.Pdf.Text.HarfBuzz package is a plausible next experiment, not part of this release. Its only responsibility would be shaping; Core and PDF serialization would stay independent. [HarfBuzz's license](https://github.com/harfbuzz/harfbuzz/blob/main/COPYING) is permissive Old MIT; a pinned binding/native bundle still requires its own license/notice review.

[HarfBuzz cluster documentation](https://harfbuzz.github.io/working-with-harfbuzz-clusters.html) describes source cluster merging and reversed visual ordering. An adapter should reconcile the selected cluster level with Kypelon.Pdf's grapheme ownership, construct sorted logical source ranges, and handle omitted/default-ignorable characters without losing coverage. It must not pass native cluster indices through without converting their coordinate system.

HarfBuzzSharp provides managed bindings plus native assets, with different Windows and Linux deployment choices; see the [upstream package matrix](https://github.com/mono/SkiaSharp/blob/main/documentation/dev/packages.md). No Skia drawing/PDF API is needed. Before shipping: pin managed/native versions; test Windows and Linux (including libc/RID choices); measure package/deployed size; validate native loading, disposal and ownership; run trimming/NativeAOT publishes; and test adapter concurrency with per-call buffers and immutable shared font data. AOT support, deployed size, ABI/API stability and cross-platform typography are **not verified here**. Package size depends on the selected assets and has not been measured. Recheck APIs against the pinned release rather than assuming upstream main is a stable contract.

The new foundation is ready for that isolated prototype. Shipping it should wait for real licensed-font Thai GSUB/GPOS visual fixtures, extraction checks, native deployment tests and performance measurements. Bidi itemization, fallback, segmentation, rich text, subsetting, reader/editing/signing/conformance remain separate workstreams.
