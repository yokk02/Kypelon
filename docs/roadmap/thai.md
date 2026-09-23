# Thai / Unicode alpha status

## Working foundation

- Scalar Unicode mapping, format-4/12 cmap, glyph advances and font ascender/descender metrics.
- Full static TrueType embedding with explicit CID mapping, Identity-H and ToUnicode.
- Unicode alias preservation and UTF-16 surrogate pairs in ToUnicode tests.
- Copy/extraction regression passes for ภาษาไทย, บริษัท ทดสอบ จำกัด, รายงานผลการตรวจสอบ, ข้อมูลพนักงาน, ปีงบประมาณ 2569, mixed Audit FY26 text and Thai digits.
- Grapheme-preserving emergency wrapping; mixed Thai/English does not corrupt the encoding.
- Position-capable shaper interface outside Core. No native shaping dependency has been added.

## Not correct enough for a Thai typography claim

BasicTextShaper applies no GSUB/GPOS, kerning, contextual alternates, mark-to-base or mark-to-mark attachment. Native cmap glyphs may look acceptable for some sequences but fail for others. Sara Am decomposition/reordering, upper marks near tall consonants, stacked tone/vowel marks, and lower marks with descending consonants need real shaping. The sample visibly exercises กิ กี กึ กื กุ กู ก่ ก้ ก๊ ก๋ กำ น้ำ ผู้ ปู่ ญู ฐุ. Contextual positioning is a deliberately skipped xUnit regression; it must not be enabled by weakening its expectation.

Thai dictionary word segmentation is absent. Cluster boundaries prevent splitting a base from its combining marks but are not linguistically correct word boundaries. The line breaker is designed for the included basic shaper. A future contextual shaper must evaluate whole candidate lines (not assume cluster widths are additive).

RTL/bidi, script fallback, CFF/variable fonts, vertical text, emoji color fonts and complete complex-script extraction semantics are outside this alpha. Do not market all Unicode scripts as typographically supported.

## Next implementation block

1. Add a cluster/source-range glyph-run model and validate many-to-many extraction, including ActualText when necessary.
2. Choose an isolated permissively licensed shaping adapter only after license/platform review; keep Core free of native dependencies. Alternatively implement a tightly scoped Thai OpenType subset with reference data.
3. Add Thai language segmentation and line-level reshape at every break.
4. Create reference glyph/position fixtures for multiple openly licensed Thai fonts, version them with licenses, and obtain a native typography review.
5. Add text extraction and visual regressions, then remove the known-gap skip only when it actually passes.

The local sample uses installed Tahoma/Tahoma Bold. Those files are not redistributed. Supply KYPELON_TEST_FONT or a console font path for other environments. Sarabun examples in the README illustrate explicit registration; Sarabun was not downloaded or tested here because network fetches were blocked.
