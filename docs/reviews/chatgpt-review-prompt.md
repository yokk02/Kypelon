> Historical evidence for Kypelon (formerly NovaPdf during pre-public alpha development). This report records the original names/results; use README.md and docs/development/kypelon-brand-migration.md for current commands and verification.

คุณคือ Principal C#/.NET Library Architect และผู้เชี่ยวชาญ PDF/Text/Layout Engine

ช่วยทำ independent technical review โครงการ NovaPdf จาก source code และหลักฐานที่แนบ แล้วบอกว่าเรามีอะไรจริง ขาดอะไร มีบั๊กอะไร และควรลงทุนพัฒนาต่ออย่างไร

ตอบภาษาไทย ใช้ชื่อ API และ technical terms ภาษาอังกฤษตามเหมาะสม ตรงไปตรงมา ไม่เขียน marketing ไม่ให้คะแนนความพร้อมเป็นเปอร์เซ็นต์โดยไม่มีเกณฑ์

## 1. เป้าหมายและข้อจำกัด

NovaPdf เป็น native C# PDF SDK ที่เราสร้าง PDF serializer และ layout engine เอง เป้าหมายคือ business reports, audit reports, eDocket, invoices และตารางจำนวนมาก พร้อม Thai/Unicode และ ASP.NET Core streaming

ห้ามเสนอให้เปลี่ยนเป็น wrapper ของ iText, QuestPDF, PDFsharp, Chromium, wkhtmltopdf หรือ PDF generation engine อื่น ส่วนการใช้เครื่องมือภายนอกเพื่ออ่าน/validate/render PDF ที่เราสร้างเองทำได้ และไม่ได้เป็น runtime dependency

ใช้ .NET BCL เป็นหลัก โครงการ target net8.0/net10.0, build ด้วย .NET 10 SDK, nullable/implicit usings/deterministic/warnings-as-errors, license MIT, version ปัจจุบัน 0.1.0-alpha.1

Optional non-PDF shaping dependency พิจารณาได้เมื่อจำเป็น แต่ต้องแยก adapter, ไม่ให้ Core พึ่ง dependency นี้ และตรวจ license/portability/native deployment ก่อนเสนอ

งานครั้งนี้เป็น review และแผนพัฒนาที่ลงมือทำได้ ไม่ใช่คำสั่งให้เขียนใหม่ทั้งหมดหรือแก้ไฟล์ทันที รักษางานที่ใช้งานได้และเสนองานเป็นช่วงเล็กที่ตรวจรับได้

## 2. สิ่งที่แนบและวิธีอ่าน

- novapdf-review-source.zip: snapshot source, tests, samples, scripts, docs และหลักฐาน review
- เริ่มอ่าน docs/reviews/library-review-2026-09-23.md เป็นบริบท แต่ให้ตรวจ code เองก่อนเห็นด้วยกับ findings
- artifacts/review/probe/Program.cs และ probe-results.json: ตัวทดลอง defect ที่รันจริงแบบจำกัดขนาด
- artifacts/review/tests-output.txt และ web-check-output.json: ผลรันทดสอบล่าสุด
- docs/performance-baseline.md: ผล performance ที่บันทึกไว้ก่อนงาน template ล่าสุด ไม่ใช่ benchmark ที่รันใหม่ใน review
- SOURCE-MANIFEST.json: hash ของไฟล์ใน snapshot และ REVIEW-BUNDLE.md: ขอบเขต/วิธีใช้ bundle

ZIP ไม่รวม bin/obj, NuGet cache, generated PDF binaries, font binaries, หรือ HTML/PDF ต้นฉบับที่ผู้ใช้ส่งเป็น design reference โดยไม่ได้แนบมาใน bundle นี้ หากต้องประเมิน visual fidelity ต้องขอ reference และ output PDFs เพิ่ม ไม่ควรแต่งผลการเปรียบเทียบขึ้นเอง

หากเปิดไฟล์แนบไม่ได้ ให้แจ้งตรง ๆ และขอเฉพาะไฟล์ที่จำเป็น อย่าอ้างว่าอ่าน repository แล้วจาก prompt อย่างเดียว อย่าพยายามอ่าน C:\ProjectMM\hjkl จากเครื่องของผู้เขียน เพราะเครื่องของคุณไม่มี local path นั้น

## 3. โครงสร้างและ dependency ที่ควรตรวจ

- src/NovaPdf.Core: PDF objects/serializer/writer/page model/metadata/URI link dictionaries
- src/NovaPdf.Graphics: canvas operators, colors, JPEG/PNG
- src/NovaPdf.Text: FontFace, FontResource, shaping/measurement/wrapping
- src/NovaPdf.Layout: element model, typed render commands, paginator/table
- src/NovaPdf: fluent builders และ PdfDocument write/render orchestration
- src/NovaPdf.AspNetCore: streaming IResult
- tests/NovaPdf.Tests: consolidated xUnit suite
- samples/NovaPdf.Sample.Console และ samples/NovaPdf.Sample.AspNetCore
- benchmarks/NovaPdf.Benchmarks: BCL baseline + optional BenchmarkDotNet path

ทิศทางที่ตั้งใจ: Public Fluent API → Document Model → Layout/Page Plans → Graphics Commands → PDF Objects → Writer → Stream

runtime csproj ทั้งหกใช้เฉพาะ NovaPdf project references และ Microsoft.AspNetCore.App framework reference ไม่มี third-party PDF generation package

## 4. สถานะที่ตรวจพบเบื้องต้น

มี PDF 1.7 writer, classic xref, indirect objects, trailer, stream lengths, Flate, page tree, metadata, deterministic output และ forward-only/non-seekable output

มี lines/rectangles/Bezier/rounded corners/transforms/clipping, logical top-left coordinates, RGB/gray helper, JPEG passthrough และ non-interlaced 8-bit RGB/RGBA PNG พร้อม soft mask

มี static standalone TrueType parsing, cmap 4/12, metrics, full embedding, Type0/CIDFontType2/Identity-H, CIDToGIDMap และ ToUnicode รองรับ Unicode extraction ใน corpus ที่ทดสอบ

มี fluent API, paragraphs, row/column/basic grid/container, padding/margin/borders/background, fixed/flex/auto tables, per-cell styles, repeated table headers, row pagination, page header/footer และ page X of Y

มี debug layout/diagnostic callback, Write/WriteAsync/Save/SaveAsync/ToArray และ ASP.NET streaming

มี Export Studio สำหรับแก้ข้อมูลแล้ว preview/download PDF, business/Thai/table templates, AEFS notification และ eDocket report โดย templates เขียนด้วย C# typed elements ไม่ใช่ HTML renderer หรือ PDF importer

## 5. Findings ที่ผู้ตรวจรอบแรกทำซ้ำได้ — กรุณาตรวจยืนยัน/โต้แย้ง

อย่าถือว่าทั้งหมดเป็น release blocker ระดับเดียวกัน ให้พิจารณา affected API/input/impact และแยก bug ออกจาก contract ที่ยังไม่ชัด

1. **Font allocation amplification (P1):** FontFace.cs:47–65 คัดลอกทุก table ก่อน validate required tables รับ unique table tags ที่ชี้ byte range ซ้ำกัน ตัวอย่างฟอนต์ผิดรูป 1,048,576 bytes / 32 tables จัดสรร 34,581,936 bytes ก่อนปฏิเสธ ไม่มีการทดลอง input หลาย GiB และยังไม่ใช่หลักฐานว่าเว็บตัวอย่างเปิดให้โจมตีผ่าน font upload
2. **Pre-cancelled SaveAsync truncates existing file (P2):** PdfDocument.cs:137–140 เปิด FileMode.Create ก่อน WriteAsync ตรวจ token ตัวทดลองกับไฟล์ KEEP ได้ cancellation แต่ไฟล์เหลือ 0 bytes แม้ Save จะประกาศอยู่แล้วว่าไม่ใช่ atomic transaction
3. **Writer finalizes after partial object failure (P2):** PdfFileWriter.cs:101–107 เขียน prefix ก่อน stream dictionary serialization; bad null-character key ทำให้ throw แต่ Finish ยังสำเร็จโดยมี incomplete object ไม่มี terminal faulted state
4. **Auto column CRLF/tab inconsistency (P2):** Table.cs:82 วัดข้อความดิบต่างจาก Wrap normalization; A\r\nB และ A\tB ใช้ Flex ได้ แต่ Auto แจ้ง missing glyph U+000D/U+0009
5. **Contextual shaping can overflow line width (P2):** TextShaping.cs:89–104 เลือก break โดยบวกความกว้าง grapheme แยกกัน ตัวทดลอง deterministic shaper ให้ AV กว้าง 18pt ในช่อง 15pt แต่ Wrap ยังคืนบรรทัดเดียว เป็น extension-contract gap ที่กระทบ advanced shaping; BasicTextShaper ปัจจุบันไม่มี contextual adjustment
6. **Malformed JPEG accepted (P2):** PdfImage.cs:88–98 พบ SOF แล้วเช็ค EOI ท้ายไฟล์; byte array 23 bytes ไม่มี SOS/scan กลับ Load ผ่าน ไม่ได้อ้างว่าเขียน full JPEG decoder แล้ว
7. **Nested foreign/unreserved reference accepted (P2):** PdfFileWriter ตรวจ owner เฉพาะ object ที่เขียนและ trailer; dictionary สามารถชี้ /Pages 999 0 R โดยไม่มี object 999 แล้ว Finish ผ่าน เป็น low-level validation-policy gap ไม่ใช่หลักฐานว่า fluent page tree ผิดทั้งหมด

ผล probe exit 0 หมายถึงการทดลองทำงานจนรายงานผล ไม่ใช่การรับรองว่าบั๊กถูกแก้แล้ว อย่านับ probe เหล่านี้เป็น passing regression tests ของ library

## 6. ช่องว่างที่ประกาศไว้อยู่แล้ว

- BasicTextShaper ไม่มี GSUB/GPOS, kerning, ligatures, bidi, fallback หรือ Thai dictionary segmentation
- Thai extraction ผ่านไม่ได้พิสูจน์ว่า mark positioning ถูก มี skipped test ชื่อ ThaiStackedMarksReceiveContextualOffsets
- ShapedGlyph มี Unicode string ต่อ glyph แต่ไม่มี source-range/cluster ownership ที่แข็งแรงสำหรับ many-to-many และ reordered extraction; ต้องออกแบบก่อนเสียบ shaping adapter
- ไม่มี font subsetting, CFF/TTC/variable/color fonts; default Courier เป็น printable ASCII; full embedding ทำให้ PDF ภาษาไทย/AEFS สั้น ๆ ยังประมาณ 1.2 MB
- ไม่มี rowspan/colspan, row fragmentation, nested-table pagination, keep-with-next, widow/orphan, justification หรือ rich inline formatting ที่ครบ
- Rows/containers/styled columns เป็น atomic; grid พื้นฐาน ไม่ใช่ CSS layout engine
- Primary writer ไม่เก็บ final PDF ทั้งไฟล์ แต่ document/page plans/fonts/images ยังอยู่ใน RAM ไม่ใช่ constant-memory layout
- เว็บ preflight Plan แล้ว WriteAsync ทำ Plan ซ้ำ; มีการวัด/shape ซ้ำ ควรประเมิน immutable prepared-plan API ก่อน optimization
- การตรวจทุก render error ก่อนเริ่ม response ยังไม่รับประกัน; async IO มี cancellation แต่ CPU measurement/compression ยัง synchronous
- ไม่มี portable pinned Thai font fixture, CI workflow, sustained fuzz campaign, browser E2E หรือ release/API-compatibility automation ใน snapshot นี้
- ยังไม่มี Reader/Edit/Merge/Split/Stamp/Forms/Encryption/Signatures/PDF-A/PDF-UA/Tagged PDF/full HTML renderer สิ่งเหล่านี้อยู่นอก first alpha scope อย่าเสนอทำทั้งหมดก่อนแก้ correctness
- HTTP(S) link annotations มีแล้ว แต่ general annotation subsystem ยังไม่ครบ

## 7. หลักฐานการ build/test และ performance

Review วันที่ 2026-09-23:

- Release build ของ test project และ runtime dependencies ทั้งสอง targets ผ่าน 0 warnings/0 errors
- xUnit หลัง build: net8.0 = 102 passed / 1 skipped / 0 failed; net10.0 = 102 passed / 1 skipped / 0 failed เป็น cases ชุดเดียวกันรันสอง frameworks
- HTTP scripts business/export, AEFS และ eDocket ผ่าน รวม 23 PDF export cases พร้อม invalid/overflow cases เพิ่มเติม ใช้ pypdf strict/PyMuPDF ตาม assertions ในแต่ละ script
- eDocket default 4 หน้า, 5,000 audit events 139 หน้า; general table 5,000 rows บน Letter landscape 251 หน้า
- ไม่มี browser click verification หรือ formal conformance certification ใน review นี้
- มี local nupkg/snupkg 6 packages รุ่น 0.1.0-alpha.1 จากงานก่อน ยังไม่ได้ publish NuGet; review นี้ไม่ได้ rerun pack/package-consumer tests
- ไม่พบ Git metadata จึงเป็น snapshot review ไม่ใช่ commit-diff review

Historical baseline (ไม่ได้รันใหม่): 5,000 rows × 8 columns, 136 pages, Courier, Stream.Null, 2 warmups + 5 iterations; ไม่รวม model/font loading/disk:

- .NET 10: 196.04 ms mean, 214.42 MiB managed allocations/op, PDF 762,655 bytes
- .NET 8: 247.71 ms mean, 227.00 MiB allocations/op, PDF 608,256 bytes
- PeakWorkingSet64 เป็น peak ของ process ตลอดอายุ ไม่ใช่ per-operation peak managed heap
- BenchmarkDotNet path มี source แต่ยังไม่ได้รันตามบันทึกเดิม; ไม่มี fair comparison กับ QuestPDF/iText

## 8. สิ่งที่ต้องการจาก review ของคุณ

A. เริ่มด้วย findings ใหม่หรือยืนยัน findings เดิม เรียง P0/P1/P2/P3 พร้อม file:line, เงื่อนไขที่ทำให้เกิด, ผลกระทบ, minimal repro และแนวแก้ แยก verified / inferred / needs investigation ให้ชัด ถ้าหักล้างข้อเดิมให้บอกเหตุผล

B. ประเมิน architecture: dependency direction, public/internal boundaries, model mutability/thread safety, streaming/failure semantics, object ownership, cancellation, determinism และ prepared-plan design อะไรควรเก็บ อะไรต้องเปลี่ยนก่อน v0.2 อะไรเลื่อนได้

C. เจาะ text pipeline: source cluster ↔ glyph mapping, line breaking, positioning, ToUnicode/ActualText, Thai marks/segmentation/bidi/fallback และ font embedding ระบุว่า foundational work อะไรต้องมาก่อน optional adapter/subsetting

D. ประเมิน developer experience ด้วย workflow report จริง: reusable components/themes, rich text, pagination/table rules, debugging/error context และ ASP.NET integration แยก reusable SDK capabilities ออกจาก sample-specific hardcoding และ full HTML support

E. ประเมิน security/robustness แบบมีหลักฐาน: parser bounds/aggregate allocation, malformed fonts/images, partial output, reference integrity, large inputs และ failure/cancellation tests ห้ามสรุปว่า production-safe เพียงเพราะมี size limit

F. สรุป readiness เป็นตาราง: มีแล้วและทดสอบอะไร / มีแต่ข้อจำกัด / ยังไม่มี / จงใจเลื่อน ระบุขอบเขต alpha ที่เหมาะสมโดยไม่เหมารวมว่าใช้งานไม่ได้ทั้งหมด

G. เสนอ 3–5 workstreams เรียง dependency/impact แยก correctness patch ออกจาก feature release แต่ละงานมีเป้าหมาย, files/modules, API changes, acceptance criteria, meaningful tests, risk และขนาด S/M/L พร้อมสมมติฐาน ไม่เดาจำนวนวันแบบแม่นยำปลอม

H. ให้ release gate ที่พิสูจน์ได้: clean build/test/pack, portable fonts, malformed-input corpus, independent PDF validation, package consumer, performance/live heap/Unicode/concurrency และ browser export checks อย่าเสนอ snapshot tests อย่างเดียว

I. ปิดด้วย prompt สำหรับ coding agent เพื่อเริ่มเฉพาะ workstream แรกที่คุณเลือก โดยให้ inspect → reproduce → fix → test → report, ไม่เขียน engine ใหม่, ไม่เพิ่ม PDF engine dependency และไม่ไล่ทำ roadmap ทั้งหมดในรอบเดียว

## 9. วินัยการตอบ

- อ่านโค้ดจริงก่อนสรุป หากไม่มีเครื่องมือรัน ให้ทำ static review และบอกว่าผลรันทดสอบเป็นหลักฐานที่ได้รับ ไม่ใช่คุณรันเอง
- อย่ารายงาน test/benchmark/validator ผ่านถ้ายังไม่ได้รันเองหรือไม่มีผลแนบ
- อ้าง source file และบรรทัดทุก actionable finding ไม่ใช้แค่คำกว้าง ๆ ว่า “เพิ่ม tests” หรือ “optimize performance”
- ไม่กล่าวว่าเร็วกว่า/ดีกว่า library อื่นโดยไม่มี equivalent measured workload หากเปรียบเทียบ licensing/API/shaping adapters จากข้อมูลภายนอก ให้ใช้เอกสาร primary/current และอ้างแหล่งข้อมูล
- ไม่ตีความ Unicode text extraction ว่าเท่ากับ correct Thai rendering
- ไม่ตีความ streaming ว่าเท่ากับ constant-memory ทั้ง pipeline
- ไม่ขยาย scope reader/edit/signatures/PDF-A/PDF-UA มาแย่งงาน correctness และ text pipeline โดยไม่มีเหตุผลจาก use case
- ไม่เชื่อตาม review รอบแรกโดยอัตโนมัติ เป้าหมายคือหา blind spots และแผนที่ลงมือทำได้
