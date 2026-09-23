# Highest-value next workstream

**v0.2: Thai shaping and text-system hardening.** Deliver source-cluster-aware glyph runs, a tested isolated shaping implementation, Thai word segmentation, per-line reshape, fallback chains and exact extraction. Use redistributable pinned fonts and reference positioning data. Keep PDF serialization independent.

In parallel with that release scope (not as empty projects):

- Move page plans toward compact line/cell records and retained shaped runs. Avoid repeated measurement/encoding allocations. Benchmark live heap as well as total allocations.
- Introduce safe TrueType subsetting after composite-glyph closure, loca/glyf rebuilding, checksum and independent renderer tests exist.
- Add paragraph widow/orphan and keep-with-next controls; multi-page row fragmentation before nested tables/spans.
- Fuzz fonts/images and malformed low-level objects, add multi-reader CI (qpdf/Poppler/MuPDF), and review public immutability before freezing API contracts.
- Expand image color-space/profile handling and PDF Standard 14 font metrics only with dedicated fixtures.

Reader/editing, signatures, PDF/A and PDF/UA should follow dedicated specifications and conformance suites. They are not incidental features to bolt onto the alpha writer.
