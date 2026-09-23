# Kypelon Export Studio

Native PDF & Document Engine for .NET — **by Konkaew**

Local web playground for the Kypelon.Pdf C# engine. It serves its own HTML/CSS/JavaScript through ASP.NET Core; no frontend package installation or hosted service is required.

## Start

Run from the repository root:

~~~sh
dotnet run --project samples/Kypelon.Pdf.Sample.AspNetCore -c Release -f net10.0 -- --urls http://127.0.0.1:5077
~~~

Open http://127.0.0.1:5077. The first PDF is generated automatically. Edit the settings and select **สร้างตัวอย่าง PDF** to regenerate, then **ดาวน์โหลด PDF** to save the exact previewed file. **เปิด PDF ในแท็บใหม่** provides an alternative to the embedded browser PDF viewer.

## Controls

- Business overview, paginated employee register, Thai/Unicode, AEFS notification and eDocket summary presets.
- Report title, client, multiline notes and 0–5,000 rows.
- A4, A3, Letter, Legal; portrait/landscape; page margins.
- Flate compression and debug layout.
- Actual page count, received file size and elapsed time including network transfer.
- Cancellation, validation errors, and a changed-settings indicator so the previous PDF is not mistaken for the updated settings.

Input is not persisted. PDF generation uses Kypelon.Pdf on the server and streams into the HTTP response. The browser buffers the received file as a Blob for preview and download. Prepare checks glyph coverage, final render commands and layout before the PDF response starts; the prepared result is streamed without repeating planning or shaping. This is a development playground and its elapsed time should not be used as an engine benchmark.

The PDF iframe relies on the browser's built-in PDF viewer. If embedded viewing is disabled, open the PDF in a separate tab or download it.

## AEFS notification template

Open http://127.0.0.1:5077/?template=aefs to select the imported design and generate the first example. The normal homepage keeps its business-report default.

Edit the title/message plus recipient, request ID, engagement, period end date, request type and destination URL. Select **สร้างตัวอย่าง PDF**, then download the previewed PDF. The dark **Open … request** button has a real HTTP(S) PDF link. The default aefs.example address is a placeholder; enter your own request URL.

The supplied HTML is preserved in assets/templates. AefsReport.cs recreates its 620px card, green accent, Details panel, CTA and footer with Kypelon.Pdf layout/graphics. It does not interpret HTML/CSS or use a browser to generate PDFs. Values are plain text. The notification is kept on one page; excessive content returns a clear 422 error. Paper size and margin remain configurable. Installed Tahoma replaces the HTML font fallback stack, ASCII IDs use Courier, letter spacing and the shadow are approximated, and hidden email preheader/Outlook conditional markup has no PDF counterpart.

See [template mapping and validation](../../docs/development/aefs-template.md).

## eDocket summary template

Open http://127.0.0.1:5077/?template=edocket for the four-page sample reconstructed from the supplied eDocket PDF. Edit engagement details, PIC/MIC names, dates, archival routing and metadata. The scenario selector changes synthetic pending/ready/archived states. The row count controls 6–5,000 audit-trail events. Default output is four A4 pages; longer content paginates with repeated table headers and page X of Y.

The report includes engagement summary/KPIs, checklists/readiness, sign-off history and archival/audit trail. It uses Kypelon.Pdf vector graphics/text, rather than embedding screenshots of the reference. See [mapping and verification](../../docs/development/edocket-template.md).

## Font configuration

Windows uses installed Tahoma/Tahoma Bold by default. Other environments should provide a static Thai-capable TrueType font using **KYPELON_FONT**; **KYPELON_BOLD_FONT** is optional. **KYPELON_TEST_FONT** is accepted as an alternative regular-font setting. If no configured/default font is available, the page explains that exports are unavailable.

Fonts are not copied into the repository. The engine's existing Thai shaping limitations still apply: Unicode text extraction works, but some mark positioning and word breaks remain incomplete.

## Endpoints

- GET / — web UI and static assets.
- GET /api/status — engine/font readiness.
- POST /api/export — JSON ExportRequest; success is application/pdf with attachment filename, X-Kypelon-Pages and X-Kypelon-Layout-Ms headers.
- GET /report — default report containing 500 employee rows.

Invalid fields return 400 validation-problem JSON. Unsupported glyph/layout conditions return 422 problem JSON before streaming starts. Requests are limited to 32 KiB. The sample is intended for local development; the launch command binds to loopback.

## Verification

With the server running and the existing Python PDF-validation dependencies installed:

~~~sh
python scripts/test-export-web.py
python scripts/test-aefs-web.py
python scripts/test-edocket-web.py
~~~

The test sends real HTTP requests, checks static assets and response headers, parses/renders PDFs independently, verifies Thai extraction, checks all 5,000 employee records, tests paper orientation/compression, and rejects invalid input. Evidence is written to artifacts/web-tests.

Verified six original export scenarios (including all 5,000 rows), six AEFS variants, 24 invalid-field requests, unsupported-glyph handling, AEFS overflow, literal markup, deterministic bytes and the legacy /report route. Independent readers confirm the AEFS button/link rectangles match, international URLs are ASCII encoded and Thai text extracts correctly. PDF raster previews were visually reviewed. Eleven eDocket exports and thirteen invalid eDocket requests also pass, including all 5,000 audit events (139 pages). The library suite passes 102 tests with one intentional Thai-shaping skip on each of .NET 8 and .NET 10.

Browser automation reports no available browser in this session. Interactive clicking, download UI and responsive visual appearance were therefore not verified through a browser. The live HTTP endpoints, served assets and JavaScript syntax were verified.
