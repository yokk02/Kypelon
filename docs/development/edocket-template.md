> Kypelon (formerly NovaPdf during pre-public alpha development). Current paths/commands use the new brand; the original verification observations below predate the rename. Fresh checks are in [brand migration verification](kypelon-brand-migration.md).

# eDocket summary template

The user-provided four-page PDF is preserved at [assets/templates/eDocket_PDF_Summary_Report_Sample.pdf](../../assets/templates/eDocket_PDF_Summary_Report_Sample.pdf). The original file in Downloads was not changed. Its pages were extracted and rendered for reference; no PDF library or browser generates Kypelon.Pdf output.

Open http://127.0.0.1:5077/?template=edocket to fill the example and export it. [DocketReport.cs](../../samples/Kypelon.Pdf.Sample.AspNetCore/DocketReport.cs) composes four native page templates:

1. Engagement summary, four KPI cells, engagement details and readiness summary.
2. Checklist outcomes, readiness gate and observations.
3. Completion sign-offs, eAD1 status, evidence details and a long reviewer note.
4. Archival routing, workflow events and document metadata.

The default A4/48pt-margin sample generates four pages. Longer notes and tables continue onto additional pages. Headers repeat within flowing tables; footer numbering uses the total planned page count. Document metadata stays together as a section. No page raster is embedded in the generated PDF: text is selectable, tables are vector geometry, and Unicode fonts are embedded.

## Editable data

Common request fields supply title, client, notes, engagementName and periodEndDate. The nested DocketFields record supplies id, engagementCode, businessUnit, country, partner, manager, archiveDeadline, repository, aatId, generatedAt, sourceVersion and stage. The web controls serialize these fields to POST /api/export. Dates use ISO input; displayed report dates use invariant English month abbreviations. Generated-at input is explicitly UTC+07:00; PDF metadata retains the same instant in UTC. The fixed default timestamp keeps the example reproducible.

The three scenario choices (pending, ready, archived) are synthetic test fixtures. They update the summary, prerequisite gate, evidence references and archival status consistently. They do not query an application database, authorize a workflow transition, apply a digital signature, or implement a production approval-rules engine. The report retains the source's synthetic-data and auditability notes.

The rows setting is the number of workflow events, from 6 to 5,000. Six named sample events are followed by uniquely numbered synthetic events. These events are intended for export/pagination testing.

## Design choices

- Preserve the white pages, dark headings, green section accents, pale alternating rows, status fills and footer motif.
- Use embedded Tahoma/Tahoma Bold instead of the reference's Helvetica. Font metrics and exact line breaks therefore differ slightly.
- Render table header labels in white. The supplied PDF renders them dark on its dark header bands.
- Label the seven-module metric accurately: completed/exempt/N/A outcomes all contribute to the settled count. The pending fixture retains the reference's 7 / 5 / 2 counts.
- Use Kypelon.Pdf as the creator in generated metadata. The supplied reference's producer is not copied.
- Implement cell-specific text/background styles in Kypelon.Pdf.Layout. Measurement, auto widths and rendering use the same resolved style, including repeated headers on continuation pages.
- Existing advanced Thai shaping limitations remain; correct Unicode extraction does not establish correct GPOS typography.

## Verification on 2026-09-23

~~~sh
dotnet test Kypelon.sln -c Release --no-restore
python scripts/test-edocket-web.py
python scripts/test-aefs-web.py
python scripts/test-export-web.py
~~~

The .NET 8 and .NET 10 suites each pass 102 tests with one pre-existing Thai GPOS skip. New unit coverage checks cell styles changing measured height, styled auto-column widths, style retention through pagination and invalid cell indices.

Eleven live eDocket scenarios pass: pending/ready/archived, Thai, long fields, custom IDs/dates/version, 500 and 5,000 events, landscape Letter, debug and uncompressed output. Thirteen malformed requests are rejected with structured 400 responses. Independent pypdf strict parsing and PyMuPDF rendering verify page counts, all event IDs, text extraction, metadata instants, repeated headers, white header text and page/footer bounds. Default bytes are deterministic. The twelve existing business/AEFS export scenarios also pass.

| Scenario | Pages | PDF bytes |
| --- | ---: | ---: |
| Default sample | 4 | 1,237,880 |
| 500 events | 18 | 1,290,293 |
| 5,000 events | 139 | 1,770,478 |
| Long fields / 60 note lines | 5 | 1,240,369 |

The primary output is artifacts/pdf/edocket-summary.pdf. Extra fixtures, rendered pages and machine-readable results are in artifacts/web-tests/edocket. All four final sample pages were visually reviewed, along with Thai and long-paragraph continuation fixtures. Browser automation reports no enabled browsers; actual UI clicks/download dialogs/responsive appearance were not browser-tested. Served assets, form field uniqueness and JavaScript syntax were verified.
