> Kypelon (formerly NovaPdf during pre-public alpha development). Current paths/commands use the new brand; the original verification observations below predate the rename. Fresh checks are in [brand migration verification](kypelon-brand-migration.md).

# AEFS notification export

The user supplied AEFS-Outlook-Email-Template-Reconstructed.html. Its original content is preserved at [assets/templates/AEFS-Outlook-Email-Template-Reconstructed.html](../../assets/templates/AEFS-Outlook-Email-Template-Reconstructed.html); the file in Downloads was not modified.

The running local demo is http://127.0.0.1:5077/?template=aefs. Source: [AefsReport.cs](../../samples/Kypelon.Pdf.Sample.AspNetCore/AefsReport.cs). This is a native Kypelon.Pdf template, not HTML-to-PDF conversion.

## Data mapping

| HTML placeholder | ExportRequest field / web control |
| --- | --- |
| NOTIFICATION_TITLE | title |
| RECIPIENT_NAME | recipientName |
| MESSAGE_BODY | notes (plain text, explicit line breaks supported) |
| REQUEST_ID | requestId |
| ENGAGEMENT_NAME | engagementName |
| PERIOD_END_DATE | periodEndDate (ISO date input, displayed as dd MMMM yyyy) |
| REQUEST_TYPE | requestType |
| RECORD_URL | recordUrl (absolute HTTP/HTTPS link) |
| PREHEADER | Not drawn: the email source intentionally hides it |

The 620px white shell maps to 465pt, centered on the selected paper with a pale gray background. The 5px green bar, brand, heading, Details panel, dark button/green arrow and system footer follow the supplied design. Measurements determine row and section heights. The link rectangle exactly matches the button. No request URL is fetched while generating the document.

Tahoma/Tahoma Bold provide the environment's embedded fonts in place of Aptos/Segoe UI/Arial. ASCII request IDs use standard Courier; Unicode IDs use the document font. Font metrics, letter spacing and the small panel shadow differ slightly from browser CSS. Source responsive/Outlook behavior is not reproduced. The PDF template is an atomic single-page notification; overlong content is rejected before streaming, with dimensions and a suggestion to shorten content/use larger paper. The normal report/table presets continue to paginate.

## Verification on 2026-09-23

- Build and xUnit: .NET 8 and .NET 10; 95 passing, one pre-existing Thai GPOS skip per framework.
- Six AEFS live HTTP exports: default English, Thai/Unicode URI, wrapped/long fields, landscape Letter, debug, uncompressed.
- Independent pypdf strict parsing and PyMuPDF rendering: one page each, no repair, correct text and metadata, 7-bit URI encoding, matching button/link bounds, page-contained text, rounded vector paths.
- Fifteen invalid AEFS requests return 400; overflow returns 422 before PDF response; HTML-like input remains literal text. Repeated default requests have identical bytes.
- Six existing web exports still pass, including all 5,000 unique employee records; nine existing invalid-field cases, missing glyph and legacy /report pass.
- Rendered English, Thai and long-content PDFs were visually inspected. Browser tool reports no available browsers, so browser clicks/download UI/responsive appearance were not exercised. JavaScript syntax, template controls and served assets were checked separately.

Reproduce with the local server running:

~~~sh
python scripts/test-aefs-web.py
python scripts/test-export-web.py
~~~

Primary artifacts: artifacts/pdf/aefs-notification.pdf and artifacts/pdf/aefs-thai.pdf. Raster previews, additional PDF variants and machine-readable results are in artifacts/web-tests. Full TrueType embedding makes the default PDF about 1.16 MiB; no subsetting is claimed. Existing Thai mark-positioning limitations remain.
