# Specification references

Implementation is original, not copied from another PDF SDK. Sources used to check format requirements:

- [ISO 32000-1:2008 / PDF 1.7](https://developer.adobe.com/document-services/docs/assets/35e4369068f86065372c18787171a17e/PDF_ISO_32000-1.pdf): objects and file structure (clause 7), graphics (8), text/fonts (9), document information (14.3).
- [PDF Association specification archive](https://pdfa.org/resource/pdf-specification-archive/).
- [OpenType cmap](https://learn.microsoft.com/en-us/typography/opentype/spec/cmap): formats 4/12 and Unicode platform selection.
- [OpenType OS/2](https://learn.microsoft.com/en-us/typography/opentype/spec/os2): embedding permission bits.
- [OpenType specification](https://learn.microsoft.com/en-us/typography/opentype/spec/): head, hhea, hmtx, maxp, name, loca and glyf.
- [PNG specification](https://www.w3.org/TR/png/): chunks/CRC, color types, zlib image data and scanline filters.

Alpha subset: PDF 1.7 syntax, classic xref, generation-zero newly allocated objects, unencrypted output, simple page tree, RGB/gray drawing, static TrueType fonts, Type0 CID text, JPEG DCT and common PNG images. Passing structural and renderer checks is not conformance certification.
