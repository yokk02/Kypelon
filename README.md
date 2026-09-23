# Kypelon

Native PDF & Document Engine for .NET

**by Konkaew**

**Alpha — 0.2.0-alpha.2 · .NET 8 and .NET 10 · MIT**

**Release status:** 0.2.0-alpha.2 is a locally verified release candidate. NuGet.org publication is pending. Build from source and use the local package feed until public publication is confirmed.

Kypelon.Pdf is an original C# PDF 1.7 writer and document-layout engine for business reports, invoices and tabular documents. It owns PDF serialization, graphics, text preparation and pagination. No third-party PDF-generation engine is used.

This is an **alpha API**, not a promise of production safety for arbitrary untrusted input. Unicode embedding and Thai source/extraction foundations are available; advanced Thai typography is incomplete.

## Installation

The versioned NuGet commands below target the pending public release. For the current source candidate, run `dotnet pack Kypelon.sln -c Release -o artifacts/packages`, then append `--source ./artifacts/packages` when installing into a consumer.

~~~sh
dotnet add package Kypelon.Pdf --version 0.2.0-alpha.2
~~~

For ASP.NET Core:

~~~sh
dotnet add package Kypelon.Pdf.AspNetCore --version 0.2.0-alpha.2
~~~

The runtime packages target **net8.0** and **net10.0**. NuGet resolves Core, Graphics, Text and Layout dependencies automatically. Core has no external dependencies; ASP.NET integration uses the ASP.NET shared framework. All packages include XML API documentation.

## Minimal example

~~~csharp
using Kypelon.Pdf;

var document = Pdf.Document(doc =>
{
    doc.Page(page =>
    {
        page.A4().Margin(32);
        page.Content(content =>
        {
            content.H1("Hello from Kypelon.Pdf");
            content.Paragraph("A native C# document engine.");
        });
        page.Footer(footer =>
            footer.PageNumber("Page {page} of {pages}").Center());
    });
});

document.Save("hello-kypelon.pdf");
~~~

The default Courier font supports printable ASCII. Supply an embedded TrueType font for Unicode or proportional typography. Fonts are never silently substituted.

## Prepare once, write several times

~~~csharp
document.Options.Deterministic = true;
var prepared = document.Prepare();

Console.WriteLine(prepared.PageCount);
prepared.Save("report.pdf");
await prepared.SaveAsync("report-copy.pdf");
await prepared.WriteAsync(outputStream, cancellationToken, leaveOpen: true);
~~~

PreparedPdfDocument owns immutable page plans, resolved page numbers, validated render commands, prepared glyph runs, metadata and output options. Later model/style/metadata changes do not change the snapshot. Rendering never calls ITextShaper. Each write has independent PDF writer and font/image resource state; sequential reuse and simultaneous writes to separate streams are supported. The mutable document and builders must not be changed concurrently during Prepare.

PdfDocument.Write/WriteAsync/Save/SaveAsync remain available and prepare internally. Plan returns prepared page plans for diagnostics; calling Plan and then Write repeats preparation. Retain Prepare's result when both the page count and the PDF are needed.

Deterministic preparation failures occur before destination bytes are emitted, and before Save opens an existing file. A pre-cancelled SaveAsync preserves the file. **I/O failures, cancellation during output, or resource exhaustion can still leave partial output; saves are not atomic.**

## ASP.NET Core

~~~csharp
using Kypelon.Pdf;
using Kypelon.Pdf.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/report", (HttpContext context) =>
{
    var document = Pdf.Document(doc =>
        doc.Page(page => page.Content(content =>
            content.Text("Hello from Kypelon.Pdf"))));

    var prepared = document.Prepare(context.RequestAborted);
    return prepared.PdfFile("report.pdf");
});

app.Run();
~~~

PdfFile streams asynchronously to HttpResponse.Body, leaves it open and respects RequestAborted. It accepts either a prepared snapshot or a PdfDocument. A document result prepares once before setting PDF response headers. It does not require an intermediate whole-document byte array.

## Tables and pagination

Inside a page's Content callback:

~~~csharp
content.Table(table =>
{
    table.Columns(Column.Fixed(35), Column.Flex(2),
                  Column.Flex(), Column.Fixed(80));
    table.Header("No.", "Employee", "Country", "Utilisation");
    for (var i = 1; i <= 500; i++)
        table.Row(i, $"Employee {i}", "Thailand", $"{70 + i % 20}%");
});
~~~

Fixed widths are points (72 points per inch). Flexible columns share remaining width; Auto columns measure content with a width cap. Rows wrap cells and move intact to the next page; headers repeat automatically. A row too tall for a full content region fails with a contextual layout error. Paragraphs can split between final prepared lines. Headers/footers reserve space, and Page X of Y is resolved before rendering. Explicit PageBreak and KeepTogether are supported.

## Thai and Unicode

~~~csharp
using Kypelon.Pdf;
using Kypelon.Pdf.Text;

var font = FontFace.Load("fonts/Sarabun-Regular.ttf");
var document = Pdf.Document(doc =>
{
    doc.DefaultFont(font);
    doc.Page(page => page.Content(content =>
    {
        content.H1("ข้อมูลโครงการ");
        content.Paragraph("รายงาน Audit FY26 – บริษัท ABC จำกัด");
        content.Paragraph("ข้อมูลพนักงาน ปีงบประมาณ 2569");
    }));
});

await document.SaveAsync("thai-report.pdf");
~~~

Supply your own appropriately licensed static TrueType font covering the text. The sample path is not a bundled font. Full font embedding respects OS/2 embedding permissions. Type0/CIDFontType2, Identity-H and UTF-16BE ToUnicode mappings preserve Unicode source, including supplementary characters.

The built-in shaper uses cmap mappings and native advances. It does **not** perform GSUB, GPOS, kerning, bidi, fallback, real ligatures or Thai dictionary segmentation. Thai marks can overlap or be misplaced. Correct extraction is not proof of correct typography.

Custom ITextShaper implementations receive source-aware TextRun values and return clusters plus positioned glyphs. Final shaped runs are validated and retained. Complex mappings use ActualText; readers that ignore it can return visual-order or raw-CID text. No advanced shaping adapter is included.

Wrapping normalizes CRLF/CR to LF and tabs to four spaces. Ordinary U+0020 spaces at paragraph ends and consumed wrap boundaries are intentionally omitted from layout/extraction; the complete normalized/original source remains available through the run input. U+00A0, U+202F, U+FEFF and U+2007 are not trimmed or treated as normal break spaces. This is a limited greedy policy, not full UAX #14. TextLine.Width is horizontal typographic advance, not ink bounds.

## Current capabilities

- PDF 1.7 objects, classic xref, metadata, Flate compression and forward-only stream output.
- Top-left graphics, lines, rectangles, rounded rectangles, fills, strokes, transforms and clipping.
- Text/paragraphs, columns, rows, basic grids, containers, reusable styles, padding, borders and backgrounds.
- Tables with fixed/flex/auto columns, repeated headers, pagination, cell styling and diagnostics.
- JPEG baseline/progressive passthrough with structural checks; non-interlaced 8-bit RGB/RGBA PNG and alpha masks.
- HTTP(S) link annotations through custom layout commands.
- Prepared document reuse, synchronous/asynchronous APIs, cancellation and ASP.NET streaming.

For visual layout diagnostics set document.Options.DebugLayout = true **before** Prepare. Options.Diagnostic accepts pagination events. Metadata supports Title, Author, Subject, Keywords, Creator, Producer and dates.

Output is serialized page by page. Page plans, glyph data, fonts and decoded images remain in memory; this is **not a constant-memory layout pipeline**. ToArray explicitly buffers the whole PDF. Full font embedding increases PDF size.

## Alpha limitations

- Public APIs can change between alpha releases.
- No real GSUB/GPOS, bidi, font fallback or complete Thai/Unicode typography.
- No TrueType subsetting, CFF, TTC/OTC, variable fonts or color fonts.
- No rich text, justification, widow/orphan rules, table spans, row fragmentation or nested-table pagination.
- No Reader/Edit, merge/split/stamp, forms, encryption, digital signatures, PDF/A or PDF/UA conformance. Output is not accessibility-tagged.
- JPEG scan coefficients/progressive completeness are not decoded; uncommon PNG modes and color management are unsupported.
- Prepared validation is deterministic-input preflight, not atomic I/O or a security certification. Input guards exist, but sustained fuzzing and broader platform testing remain future work.

## Development and samples

Build with a .NET 10 SDK and install .NET 8/10 runtimes to execute both test targets.

~~~sh
dotnet restore Kypelon.sln
dotnet build Kypelon.sln -c Release --no-restore
dotnet test tests/Kypelon.Pdf.Tests/Kypelon.Pdf.Tests.csproj -c Release -f net8.0 --no-restore
dotnet test tests/Kypelon.Pdf.Tests/Kypelon.Pdf.Tests.csproj -c Release -f net10.0 --no-restore
dotnet run --project samples/Kypelon.Pdf.Sample.AspNetCore -c Release -f net10.0
dotnet pack Kypelon.sln -c Release -o artifacts/packages
~~~

The Export Studio provides business, table, Unicode, AEFS and eDocket examples. Supply KYPELON_FONT/KYPELON_BOLD_FONT to the web sample or KYPELON_TEST_FONT to real-font tests/benchmarks; Windows samples default to installed Tahoma. Installed fonts are not redistributed as repository/package assets. Portable synthetic fonts cover the source-cluster tests.

Optional Python development tools pypdf/PyMuPDF independently inspect/render PDFs; they are not runtime dependencies. Benchmark results describe this environment only and do not establish superiority over another library. For a local candidate feed, append --source ./artifacts/packages to installation commands.

## License and identity

MIT licensed. See LICENSE in the source distribution. **Kypelon is by Konkaew.**

Kypelon.Pdf was called NovaPdf during pre-public development. Old test/benchmark evidence retains its original identity. No compatibility packages are provided. Repository metadata is included only when a real URL is supplied through KypelonRepositoryUrl; no project/repository URL is invented.
