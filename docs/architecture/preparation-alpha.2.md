# Preparation and text contracts — 0.2.0-alpha.2

Kypelon · Native PDF & Document Engine for .NET · by Konkaew

## NP-201: final command gate

All final TextCommand, RectangleCommand, ImageCommand and LinkCommand values pass RenderCommandValidation before any destination is opened/written. Diagnostics identify page/command. Text checks finite origin/font size, color, nonnegative reservation, retained-run identity, resolved page numbers and absolute positioned glyph coordinates. Rectangle checks bounds, colors, nonnegative line width/radius. Image checks a non-null immutable image and positive bounds. Existing HTTP(S)/page link checks remain.

Geometry/numeric values to be serialized must fit the existing PDF number limit of ±1e12. Text retains its existing 1,000,000pt size and shaped-run limits. Valid zero-width strokes are allowed; rounded corners retain clamping to the shorter side. Custom graphics may intentionally extend outside a page; this is numeric/semantic command validation, not a blanket ink-containment rule.

Preparation also validates metadata syntax and distinct font CID limits without retaining writer state. Destination errors, memory exhaustion and cancellation during output are not rollback-safe. Save completes deterministic preparation before FileMode.Create, but no atomic-save API is promised.

## NP-203: prepared snapshot

~~~mermaid
flowchart LR
  Model[Mutable document / style] --> Prepare[Prepare: layout and final validation]
  Prepare --> Snapshot[Immutable pages / glyph runs / options / metadata]
  Snapshot --> W1[Independent writer and resources]
  Snapshot --> W2[Independent writer and resources]
  W1 --> S1[Stream 1]
  W2 --> S2[Stream 2]
~~~

PreparedPdfDocument exposes PageCount and read-only Pages. It owns resolved page numbers and exactly the shaped data measured for final lines. No PdfDocument, ITextShaper or PdfFileWriter is retained. Fonts/images are already immutable. Implicit creation time is captured on preparation; repeated writes of one snapshot use the same metadata.

Each Write/WriteAsync creates a new writer, font CID encoder and image resource map. Sequential reuse, recovery on a new destination after I/O failure, and concurrent writes to separate streams are tested. Mutable document/builders must not be changed concurrently with Prepare. Changes made after Prepare affect only a later preparation. Cancellation and leaveOpen remain per-write choices.

PdfDocument.Write/Save prepare internally. Plan returns Prepare().Pages for diagnostics; it cannot be passed back into Write as a cache. Export Studio now calls Prepare once and streams prepared.PdfFile, avoiding Plan + Write's duplicate preparation.

## NP-202: intentional wrap-space normalization

We choose explicit layout/extraction normalization, not synthetic ownership for invisible line-boundary spaces. SourceStart/SourceLength describe the normalized UTF-16 characters actually passed to the retained shaper. U+0020 consumed at wrap boundaries or at paragraph ends is intentionally absent from visible line runs and PDF extraction. We do not render trailing spaces just to manufacture ownership.

The whole TextSource still retains original and normalized input. Consumers requiring source accounting can compare run ranges against that source; omitted U+0020 ranges are not claimed by a different cluster. LF paragraph separators also lie between runs. Initial leading spaces are preserved when they fit; spaces consumed while advancing to the next wrapped line are elided.

| Input | Width at Courier 10pt | Line texts | Owned normalized ranges | Intentionally omitted indices |
| --- | ---: | --- | --- | --- |
| A + space | 100pt | A | [0,1) | 1 |
| A B | 6pt | A / B | [0,1), [2,3) | 1 |
| A  B | 6pt | A / B | [0,1), [3,4) | 1,2 |
| word word + space | 24pt | word / word | [0,4), [5,9) | 4,9 |

This documents the existing alpha.1 wrapping behavior; no boundary algorithm was changed. CRLF/CR → LF and tabs → four spaces remain unchanged. U+00A0, U+202F, U+FEFF and U+2007 remain protected tokens, never ordinary trim spaces.

## Future adapter obligations (tests/documentation only)

Clusters must exhaustively partition the input at valid grapheme boundaries, and each nonempty source cluster must own at least one valid glyph. This alpha has no glyphless cluster or separate invisible-source object.

- Leading default-ignorable source: absorb it into an adjacent visible cluster if the real shaping semantics permit, or emit a distinct cluster owning a genuinely invisible glyph with zero advance.
- Trailing default-ignorable source: same obligation; do not shorten source coverage.
- Invisible source between visible clusters: associate it with an adjacent cluster or represent it separately without losing logical order/coverage.
- An all-default-ignorable run: a nonempty run still needs exhaustive source ownership and at least one valid, genuinely nonpainting glyph with zero advance. An empty input may return empty collections.

The adapter must choose a suitable glyph from the actual font (e.g. a verified blank glyph); the engine cannot certify its outline is invisible. A missing-glyph box is not an acceptable invisible substitute. Merely dropping source or returning a glyphless nonempty cluster is rejected. Tests use original empty-outline synthetic glyphs, not a shipped shaping adapter.

## Positioning / fitting contract

PositionedGlyph metrics are signed native font units, with positive Y upwards. Individual AdvanceX may be negative or zero; total horizontal advance must be nonnegative. OffsetX, OffsetY and AdvanceY affect exact rendered positions. TextLine.Width is the sum of AdvanceX scaled to points; it excludes glyph ink overhang.

Greedy fitting does not promise the globally longest fitting prefix for arbitrary non-monotonic shapers. Every accepted line is tested against its own complete final shaped width. Synthetic tests cover expansion/contraction, non-monotonic candidates, zero/negative advances, positive then negative advances and all positioning axes.

No real GSUB/GPOS, bidi, fallback or Thai segmentation capability was added. ThaiStackedMarksReceiveContextualOffsets remains skipped.
