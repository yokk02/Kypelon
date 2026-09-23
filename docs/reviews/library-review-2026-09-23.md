> Historical evidence for Kypelon (formerly NovaPdf during pre-public alpha development). This report records the original names/results; use README.md and docs/development/kypelon-brand-migration.md for current commands and verification.

# NovaPdf — code and library review

วันที่ตรวจ: 23 กันยายน 2026 • รุ่นใน repository: 0.1.0-alpha.1

> Historical review of alpha.1. The alpha.2 correctness patch addresses F1/F2/F3/F4/F6/F7 and adds F9 no-break whitespace regressions. F5 remains open. See [hardening verification](../development/hardening-alpha.2.md) for current evidence; the original findings and probe results below are retained as before-fix evidence.

## ข้อค้นพบที่ยืนยันได้

ระดับความสำคัญ: P1 = ควรแก้ก่อนเปิดรับ input ที่ไม่เชื่อถือ/ขยายการใช้งานจริง; P2 = correctness หรือ hardening ที่ควรแก้ในรอบถัดไป; P3 = คุณภาพและเอกสาร ไม่มีการให้คะแนนความพร้อมเป็นเปอร์เซ็นต์

ข้อค้นพบทั้ง 7 ข้างล่างมีตัวอย่างทดลองที่รันจริงใน artifacts/review/probe/Program.cs และผลดิบใน artifacts/review/probe-results.json ตัวทดลองเป็น observational probes ไม่ใช่ regression tests ที่แก้ปัญหาแล้ว และ exit code 0 ของ probe ไม่ได้แปลว่าบั๊กหาย

### F1 — P1: ขนาดฟอนต์ถูกจำกัด แต่การจัดสรรหน่วยความจำยังขยายตามจำนวน table ได้

- ตำแหน่ง: src/NovaPdf.Text/FontFace.cs:47–65 โดยเฉพาะบรรทัด 59; ตรวจขนาดไฟล์ใน Load ที่บรรทัด 187 เป็นต้นไป
- FontFace คัดลอกทุก table ลง byte[] ก่อนตรวจ required tables โดยรับ table คนละชื่อที่อ้าง byte range ซ้อนกันได้ ขนาดไฟล์ไม่เท่ากับ budget หน่วยความจำที่ parser ใช้
- ทดลองฟอนต์ผิดรูปขนาด 1,048,576 bytes มี 32 descriptors ชี้ payload เดียวกัน: จัดสรร 34,581,936 bytes (~32.98 MiB) ก่อนแจ้งว่าไม่มี head table
- จากเพดาน 32 MiB และ 256 tables มีความเป็นไปได้เชิงโค้ดที่จะสร้างสำเนาหลาย GiB; ไม่ได้ทดลองขนาดใหญ่ดังกล่าว ไม่ได้พิสูจน์ remote exploit ในเว็บตัวอย่างซึ่งใช้ฟอนต์ที่กำหนดไว้
- ควรแก้: ตรวจ directory/ช่วงข้อมูล/aggregate budget ก่อนสร้างสำเนา เก็บ table เป็น slice ของ buffer เดียว อ่านเฉพาะ table ที่ต้องใช้ และเลือก cmap ก่อนคัดลอก ไม่ควรคัดลอกใหม่ทุก candidate
- Acceptance: malformed overlapping/duplicate/oversized table fixtures ถูกปฏิเสธด้วย exception ที่กำหนดและ bounded work; ไม่เกิด allocation amplification ตามจำนวน descriptor

### F2 — P2: SaveAsync ที่ถูกยกเลิกไว้แล้วทำให้ไฟล์ปลายทางถูกล้าง

- ตำแหน่ง: src/NovaPdf/PdfDocument.cs:137–140 เทียบกับ WriteAsync:117
- SaveAsync เปิด FileMode.Create ก่อนเรียก WriteAsync ซึ่งตรวจ cancellation จึง truncate ไฟล์ก่อนรู้ว่างานไม่ควรเริ่ม
- ทดลองกับไฟล์ที่สร้างขึ้นเฉพาะสำหรับ probe ซึ่งมีข้อความ KEEP: token ถูกยกเลิกตั้งแต่ต้น; ได้ OperationCanceledException แต่ไฟล์จาก 4 bytes เหลือ 0 bytes
- README ระบุไว้แล้วว่า Save ไม่ใช่ atomic transaction และความผิดพลาดอาจทิ้งไฟล์บางส่วน ประเด็นนี้คือ side effect ที่หลีกเลี่ยงได้ในกรณี pre-cancelled โดยเฉพาะ
- ควรแก้: ตรวจ token ก่อนเปิดไฟล์ เพิ่ม regression case ที่ยืนยันว่าไฟล์เดิมไม่เปลี่ยน; atomic save ผ่าน temporary file/replace พิจารณาเป็น API ทางเลือกพร้อมระบุ semantics เมื่อเกิดความผิดพลาดระหว่างเขียน

### F3 — P2: Writer ยอม Finish หลังเริ่มเขียน object แล้ว serialization ล้มเหลว

- ตำแหน่ง: src/NovaPdf.Core/PdfFileWriter.cs:55–64, 101–107, 120–141; async path:110–118
- Stream path ส่ง object prefix ออกไปและบันทึก offset ก่อน serialize dictionary ถ้า dictionary มีชื่อที่ผิด เช่น null character จะ throw หลัง output เริ่มแล้ว แต่ writer ไม่มี faulted state
- ทดลอง: เขียน stream dictionary ที่มี bad\0key แล้วจับ PdfWriteException; Finish ยังสำเร็จ ได้ส่วนหนึ่งของไฟล์เป็น 2 0 obj ตามด้วย xref โดย object ยังไม่จบ
- Scope: เป็นข้อบกพร่องของ low-level failure handling ไม่ได้หมายความว่า PDF จากทุก template เสียอยู่แล้ว กรณี I/O failure ควรทดสอบเพิ่มเติม; probe นี้ทดสอบ serialization failure
- ควรแก้: validate/serialize dictionary ก่อน prefix และเปลี่ยน writer เป็น terminal faulted state เมื่อเกิดความผิดพลาดหลังเขียนไปบางส่วน; ปฏิเสธ Write/Finish ต่อ ไม่สัญญา rollback บน forward-only stream
- Acceptance: sync/async partial-write streams และ malformed dictionaries ไม่สามารถ finalise เป็นไฟล์ที่ดูเหมือนสำเร็จได้

### F4 — P2: Auto column ไม่ normalize CRLF และ tab เหมือน text wrapping

- ตำแหน่ง: src/NovaPdf.Layout/Table.cs:79–82 เทียบกับ src/NovaPdf.Text/TextShaping.cs:71
- Auto width แยกเฉพาะ LF แล้ว Measure ข้อความดิบ; Wrap แปลง CRLF/CR และขยาย tab ก่อน
- ทดลอง cell A\r\nB และ A\tB: Column.Flex จัดได้ 1 หน้า แต่ Column.Auto แจ้งว่า Courier ไม่มี glyph U+000D หรือ U+0009
- ผลกระทบ: ข้อความ Windows newline หรือ tab ในตารางที่ถูกต้องกลับ export ไม่ได้เพียงเปลี่ยนชนิด column
- ควรแก้: ใช้ text normalization เดียวกันใน auto measurement, wrapping และ rendering; เพิ่ม regression ทั้ง header/body และ style override

### F5 — P2: ITextShaper ยังเสียความถูกต้องของการตัดบรรทัดเมื่อ advance ขึ้นกับบริบท

- ตำแหน่ง: src/NovaPdf.Text/TextShaping.cs:89–104; render shapes อีกครั้งที่ src/NovaPdf/PdfDocument.cs:228 เป็นต้นไป
- Wrap วัด grapheme ทีละตัวเพื่อเลือก break แล้ววัดทั้งบรรทัดอีกครั้ง แต่ไม่ตรวจว่าความกว้างสุดท้ายยังอยู่ในพื้นที่หรือไม่
- ทดลอง deterministic custom shaper: A และ V แยกกันกว้างรวม 12pt แต่ AV ที่ shape ร่วมกันกว้าง 18pt; ให้พื้นที่ 15pt กลับได้หนึ่งบรรทัดกว้าง 18pt
- BasicTextShaper ปัจจุบันไม่มี contextual adjustment จึงไม่ใช่หลักฐานว่า template ปัจจุบันทั้งหมด overflow ประเด็นนี้เป็น defect ของ extension contract และเป็นข้อจำกัดที่ ADR/Thai roadmap รับรู้อยู่แล้ว
- ควรแก้ก่อนเสียบ advanced shaping adapter: ใช้ source ranges/clusters/direction/script/language ที่ชัดเจน เลือก break จาก shaped runs และเก็บ final GlyphRun ไว้ใช้ render โดยไม่วัดคนละบริบท; ออกแบบ ToUnicode/ActualText สำหรับ many-to-many/reordered glyphs ตาม semantics ที่ตรวจสอบได้
- Acceptance: contextual positive/negative adjustment, ligatures, Thai marks และข้อความผสมไม่ overflow/ตกหล่น/ซ้ำตอน extract; ไม่เพิ่ม dependency shaping ก่อนวาง extraction contract

### F6 — P2: JPEG ที่ไม่มี scan data ผ่านการตรวจรับ

- ตำแหน่ง: src/NovaPdf.Graphics/PdfImage.cs:88–98
- Parser พบ SOF แล้วดูเพียงว่า 2 bytes ท้ายเป็น EOI จึงไม่ตรวจส่วนที่เหลือของโครงสร้าง scan
- ทดลอง byte sequence 23 bytes มี SOI + SOF สำหรับ RGB 1×1 + EOI แต่ไม่มี SOS/scan: PdfImage.Load ยอมรับและได้ภาพ 1×1
- ผลกระทบ: input ที่ไม่ใช่ JPEG ที่ decode ได้สามารถถูกฝังลง PDF ทำให้ภาพเสียใน viewer; การผ่าน loader ยังไม่ใช่การยืนยัน decode validity
- ควรแก้: เดิน marker/segment ให้ครบและตรวจ scan structure ที่จำเป็นภายใต้ขอบเขต format ที่รองรับ ระบุชัดว่าตรวจเชิงโครงสร้างระดับใด ไม่ต้องสร้าง image decoder เต็มรูปแบบเพื่อแสร้งว่า validate ทุก entropy byte ได้
- Acceptance: SOF-only, missing/truncated scan และ progressive fixtures มีผลรับ/ปฏิเสธที่ถูกต้อง; valid JPEG passthrough ยังใช้ได้

### F7 — P2: Nested indirect reference ไม่ตรวจว่าอยู่ใน writer นี้

- ตำแหน่ง: src/NovaPdf.Core/PdfFileWriter.cs:59, 69–70, 125 และ src/NovaPdf.Core/PdfObjects.cs:147–154
- มี ownership checks สำหรับ object ที่ Write และ trailer root/info แต่ references ใน PdfDictionary/PdfArray serialize ได้โดยไม่มี writer context
- ทดลอง catalog /Pages ชี้ new PdfIndirectReference(new PdfObjectId(999)) ทั้งที่มีการ reserve เพียง object 1: Write และ Finish สำเร็จ ได้ reference 999 ซึ่งไม่มี object/xref entry
- Scope: advanced caller สามารถสร้างโครงสร้าง PDF ที่ผิดเองได้อยู่แล้ว ประเด็นนี้คือ validation policy ที่ไม่ครบ ไม่ใช่ bug ใน page tree ของ fluent path ที่ทดลองผ่าน
- ควรแก้หรือกำหนด contract ให้ชัด: ใน strict writer ตรวจ nested references ว่าถูก reserve และเป็น owner เดียวกัน; future imported/external references ต้องผ่าน mapping ที่ออกแบบไว้ ไม่พึ่งเลข object ที่ตรงกันโดยบังเอิญ

## สิ่งที่มีจริงและควรเก็บไว้

| ส่วน | สิ่งที่ตรวจพบในโค้ด |
|---|---|
| Core | PDF 1.7, object model, name/string escaping, invariant numbers, classic xref, trailer, page tree, Flate, metadata, deterministic mode |
| Graphics | เส้น/สี่เหลี่ยม/Bezier/rounded rectangles, fill/stroke, RGB/gray helper, transforms, clip, top-left logical coordinates |
| Text | static standalone TrueType parser, cmap 4/12, metrics, full embedding, Type0/CIDFontType2, CIDToGIDMap, ToUnicode, explicit missing-glyph errors |
| Layout | paragraphs, spacer/line/image, column/row/basic grid/container, box styles, fixed/flex/auto table, per-cell style, row pagination, repeated header, page X of Y |
| Diagnostics | debug bounds และ callback การตัดสินใจ pagination |
| API | Fluent builders, public low-level writer, custom Element และ typed render commands, Write/WriteAsync/Save/ToArray |
| ASP.NET | IResult เขียน HttpResponse.Body โดยตรง, RequestAborted, attachment filename encoding; local Export Studio |
| Templates | business/table/Thai, AEFS notification และ eDocket report, clickable HTTP(S) links, audit-trail หลายหน้า |
| Distribution | 6 projects/packages, net8.0/net10.0, XML docs, MIT, local nupkg/snupkg รุ่น 0.1.0-alpha.1 |

ตรวจ runtime csproj ทั้งหกแล้ว: ใช้ project references ของ NovaPdf และ Microsoft.AspNetCore.App framework reference เท่านั้น ไม่มี third-party runtime PackageReference ที่สร้าง PDF ให้เรา Python PDF readers/renderers อยู่ฝั่งตรวจผล ไม่ใช่ PDF generation engine

Dependency direction มีเหตุผล: Public API → Layout → Graphics/Text → Core → Stream ควรรักษาทิศทางนี้ และเก็บ workflow/data เฉพาะ AEFS/eDocket ไว้ใน samples/application layer

## สิ่งที่ยังขาด แยกจากบั๊ก

### Text และภาษาไทย — ช่องว่างหลักของผลิตภัณฑ์

- ยังไม่มี GSUB/GPOS, kerning, bidi, ligatures, font fallback หรือ Thai dictionary line breaking
- Unicode mapping และ text extraction ของ corpus ที่ทดสอบผ่าน แต่สระ/วรรณยุกต์ซ้อนอาจชนหรือผิดตำแหน่ง จึงยังอ้าง Thai typography ที่สมบูรณ์ไม่ได้
- ShapedGlyph มี Unicode string ต่อ glyph แต่ไม่มี source range หรือ cluster ownership ที่พอสำหรับ extraction ของหลาย glyph ต่อ source cluster/การ reorder; การเติม HarfBuzz adapter อย่างเดียวไม่แก้ทั้ง pipeline
- รองรับ static TrueType เท่านั้น ไม่มี CFF, TTC/OTC, variable/color fonts; Standard PDF fonts ที่ทำไว้คือ Courier/Courier-Bold แบบ printable ASCII
- Full embedding ถูกต้องกว่าการ subset ที่ไม่ครบ แต่ไฟล์ตัวอย่างสั้นภาษาไทย/AEFS ยังประมาณ 1.2 MB; subsetting และ composite glyph closure/checksum tests ยังไม่มี

### Layout และตาราง

- ยังไม่มี rowspan/colspan, row fragmentation, nested-table pagination, keep-with-next, widow/orphan หรือ justification
- Row แบ่งช่องเท่ากัน; grid เป็นการจัดแถวพื้นฐาน; styled column/container/row เป็น atomic layout ไม่สามารถไหลข้ามหน้าแบบ CSS
- ไม่มี mixed inline text styles/runs ที่สมบูรณ์ และยังไม่มี layout semantics สำหรับเอกสาร tagged/accessibility
- Template สวยเฉพาะตัวอย่างไม่เท่ากับ general HTML/CSS support: AEFS และ eDocket สร้างใหม่ด้วย C# typed elements ไม่ได้ import HTML/PDF เข้า layout engine

### Streaming และประสิทธิภาพ

- ไม่ buffer PDF ทั้งไฟล์ใน primary server write path แต่เก็บ document tree และ page/render plans ทั้งหมด รวมถึง fonts/images ใน RAM; ไม่ใช่ constant-memory layout
- Plan สร้างคำสั่ง/ข้อความและมีการ measure/shape ซ้ำ; PdfCanvas ใช้ StringBuilder และแปลงเป็น bytes ต่อหน้า; มีทางลด allocations โดยไม่แตะสถาปัตยกรรม PDF writer
- เว็บเรียก Plan เพื่อ preflight ที่ Program.cs:40 แล้ว PdfResult เรียก WriteAsync ซึ่ง Plan ใหม่อีกครั้งที่ PdfDocument.cs:119 ควรมี immutable prepared document/plan ที่ใช้ซ้ำได้โดยกำหนดเรื่อง mutation ให้ชัด
- Plan ยังไม่ได้ตรวจทุก render command/font encoding; อย่าอธิบาย preflight ว่าป้องกันทุก failure ก่อน response เริ่ม กรณี custom elements และ mid-write failure ควรมี tests เพิ่ม
- Frontend response.blob() เก็บไฟล์ครบเพื่อ preview/download เป็นพฤติกรรมของ demo ไม่ใช่หลักฐานว่า server buffering ทั้งไฟล์
- ยังไม่มี benchmark ที่ครอบคลุม embedded Thai font, HTTP concurrency, end-to-end model/font load, slow clients, cancellation latency และ live heap ต่อ operation

### คุณภาพและการปล่อย library

- ไม่พบ CI workflow ใน snapshot นี้; ต้องมี clean checkout build/test/pack บน Windows/Linux และ net8/net10 พร้อม independently validated PDFs
- Thai font test ใช้ NOVAPDF_TEST_FONT หรือ Tahoma บนเครื่อง จึงยังไม่ self-contained บน Linux/CI ควรมี fixture ฟอนต์ที่อนุญาตแจกพร้อม license/provenance และ pin version
- มี malformed-input tests บางส่วน แต่ยังไม่มี sustained fuzzing/property tests/bounded-allocation corpus หรือ cross-library differential validation campaign
- ชุด xUnit เดิมไม่ได้ตรวจพบ F1–F7; ต้องเพิ่ม meaningful regression tests ก่อนแก้และเก็บไว้หลังแก้
- ไม่มี browser interaction/download/responsive E2E ที่ยืนยันด้วย browser ในการ review นี้; HTTP scripts ตรวจ API และ PDF แต่ไม่แทน browser QA
- ยังไม่มี release automation, API compatibility checks, package-consumer matrix หรือที่อยู่ repository/publishing ที่กำหนดจริง; local packages ไม่เท่ากับเผยแพร่บน NuGet แล้ว
- docs/development/verification.md ยังระบุผล foundation 82 passed ขณะที่ชุดปัจจุบันเป็น 102 passed; ควรระบุว่าเป็น historical run หรือปรับ current verification summary
- ไม่มี Git metadata ใน workspace นี้ จึง review working-tree snapshot ไม่ใช่ commit diff และไม่สามารถรับรองประวัติการเปลี่ยนแปลงจาก Git ได้

### งานอนาคตที่ไม่ควรเอามาขวาง alpha นี้

Reader/edit, merge/split/stamp, forms, encryption/signatures, PDF/A, PDF/UA และ full HTML renderer ยังไม่ได้ทำตาม scope เดิม HTTP(S) link annotation มีแล้ว แต่ไม่ใช่ annotation subsystem ที่ครบ อย่าให้รายการ roadmap นี้แซง correctness, Thai text และการทดสอบ release

## หลักฐานการตรวจครั้งนี้

- Build tests/NovaPdf.Tests/NovaPdf.Tests.csproj -c Release --no-restore ซึ่งรวม runtime dependencies ทั้งหก target ทั้งสอง frameworks: สำเร็จ, 0 warnings/0 errors
- หลัง build รัน xUnit ใหม่: net8.0 = 102 passed, 1 skipped, 0 failed; net10.0 = 102 passed, 1 skipped, 0 failed เป็นชุดเดียวกันรันสอง frameworks ไม่ใช่ 204 test cases ที่ต่างกัน
- Skip: ThaiStackedMarksReceiveContextualOffsets เป็นข้อจำกัด GPOS ที่ประกาศไว้ ไม่ใช่ shaping test ที่ผ่าน
- รัน scripts/test-export-web.py, test-aefs-web.py, test-edocket-web.py ผ่านทั้งหมดกับ local server ที่ทำงานอยู่: PDF export cases 6 + 6 + 11 = 23 cases พร้อม invalid-input/overflow checks เพิ่มเติม
- PDF ที่ทดสอบผ่าน pypdf strict parsing และ PyMuPDF ตาม assertions ใน scripts; ตรวจ extraction/pagination/repeated headers/URI/rendering ตามแต่ scenario ไม่ใช่ formal ISO conformance certification
- eDocket default = 4 หน้า; 5,000 audit events = 139 หน้า; general table 5,000 rows บน Letter landscape = 251 หน้า คนละ workload กับ benchmark 136 หน้า
- ไม่รัน browser click tests, qpdf, Ghostscript หรือ formal PDF/A/PDF/UA validator ใน review นี้ และไม่ได้ rerun benchmarks/pack หรือ solution ทั้งชุด มี packages อยู่จากรอบก่อน ไม่ได้อ้างว่ารอบนี้ตรวจ package consumer ใหม่
- หลักฐานดิบ: artifacts/review/tests-output.txt, tests/*.trx, web-check-output.json, probe-results.json และ probe/Program.cs

## Performance ที่มีหลักฐานเดิม

อ้างอิง docs/performance-baseline.md ซึ่งบันทึกไว้ก่อนงาน template ล่าสุด ไม่ใช่ benchmark ที่รันใหม่ในการ review นี้:

| Workload | Runtime | Mean | Allocated bytes (MiB/op) | PDF size |
|---|---|---:|---:|---:|
| 5,000 rows × 8 columns / 136 pages | .NET 10.0.9 | 196.04 ms | 214.42 | 762,655 bytes |
| 5,000 rows × 8 columns / 136 pages | .NET 8.0.28 | 247.71 ms | 227.00 | 608,256 bytes |

BCL harness, 2 warmups + 5 measured iterations, built-in Courier, timed output to Stream.Null; ไม่รวม model construction, font loading หรือ disk I/O. PeakWorkingSet64 เป็น process-lifetime high-water mark ไม่ใช่ peak managed memory ต่อ operation. BenchmarkDotNet ยังไม่ได้รันตามบันทึกเดิม ไม่มีผลเปรียบเทียบที่ใช้กล่าวว่าเร็วกว่า QuestPDF/iText ได้

## ลำดับงานที่แนะนำ

1. **Correctness/hardening patch:** F1/F2/F3 ก่อน ตามด้วย F4/F6/F7 พร้อม regression tests, malformed corpus และ portable CI fixture ไม่เพิ่ม feature ใหญ่ใน patch นี้
2. **Text pipeline สำหรับ v0.2:** แก้ F5 พร้อม source-cluster model, shaped-line measurement, reuse runs, extraction semantics แล้วค่อยประเมิน optional non-PDF shaping adapter ที่ license เหมาะสมและไม่เข้า Core
3. **Pagination ใช้งานทั่วไป:** keep-with-next, widow/orphan, rich inline runs และ row splitting ตามลำดับความจำเป็นของ reports จริง; spans/nested tables แยกงานพร้อม specification/tests
4. **Prepared document และ performance:** ใช้ plan เดิม render ได้, ลด allocation จากการ measure/shape ซ้ำ, profile live heap และ Unicode/concurrency benchmark ก่อนออกแบบ font subsetting
5. **Release gate:** clean build/test/pack บน CI, package consumer smoke, independent PDF validation, browser export checks, versioned baseline และเอกสารข้อจำกัดตรงกับผลจริง

ข้อสรุปเชิงวิศวกรรม: มี native PDF engine และ vertical slice ที่ทำงานจริง เหมาะเป็น alpha สำหรับทดลองเอกสารที่ควบคุมรูปแบบ/input ได้ แต่ยังไม่พร้อมอ้างว่าเป็น general-purpose production SDK หรือรองรับ Thai typography สมบูรณ์ ควรแก้ defect และวาง text pipeline ให้ถูกก่อนขยาย roadmap

ขอบเขตงานรอบนี้: review + isolated probes + handoff documents เท่านั้น ไม่ได้แก้ runtime library และไม่ได้เปลี่ยน API

## v0.2 follow-up status

The original review above is historical. 0.2.0-alpha.1 addresses F5 and custom-ITextShaper late validation with complete-candidate fitting, immutable prepared runs, source clusters and ActualText. See [the v0.2 verification report](../development/text-pipeline-v0.2-verification.md). Advanced OpenType/Thai typography remains open; this is not a claim that GSUB/GPOS or bidi has been implemented.
