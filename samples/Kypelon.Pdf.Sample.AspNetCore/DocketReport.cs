using System.Globalization;
using Kypelon.Pdf.Core;
using Kypelon.Pdf.Graphics;
using Kypelon.Pdf.Layout;
using Kypelon.Pdf.Text;

namespace Kypelon.Pdf.Sample.AspNetCore;

// All PDF output is produced by Kypelon.Pdf. The supplied four-page PDF is a design/data reference only.
public static class DocketReport
{
    private static readonly TextStyle Body = new() { Size = 7.7, LineHeight = 1.36, Color = Color.Hex("#4B5563") };
    private static readonly TextStyle Label = Body with { Bold = true, Color = Color.Hex("#202124") };
    private static readonly TextStyle Header = Label with { Color = Color.White };
    public const string DefaultNotes = "Reviewer note: The engagement team confirmed that the final archival package will include the approved financial statements, required completion documentation, applicable consultation evidence, and the final eDocket export. This intentionally long paragraph is included to validate line wrapping, page-width calculations, font metrics and paragraph spacing in the PDF export library. The implementation should wrap naturally without clipping, overlap, or horizontal overflow.";

    public static PdfDocument Build(ExportRequest request, FontFace regular, FontFace? bold)
    {
        var data = request.Docket ?? throw new PdfLayoutException("eDocket data is required.");
        bool picComplete = data.Stage != "pending", archived = data.Stage == "archived";
        string status = archived ? "ARCHIVED" : picComplete ? "READY FOR eAD1" : "IN PROGRESS";
        string period = Date(request.PeriodEndDate), deadline = Date(data.ArchiveDeadline);
        string manager = ShortName(data.Manager), partner = ShortName(data.Partner);
        string[][] checklists =
        [
            ["Client Service (CS)", "Yes", "COMPLETE", manager, "08 Sep 2026", "All mandatory questions completed. No outstanding exceptions."],
            ["EQCR", "No", "EXEMPTED", partner, "08 Sep 2026", "Engagement qualifies for exemption under the approved route."],
            ["Tax Review (TR)", "Yes", "COMPLETE", "S. Niran", "10 Sep 2026", "Tax section completed and reviewer conclusion recorded."],
            ["Non-Audit Clearance", "No", "N/A", "System", "08 Sep 2026", "Automatically set to Not Applicable based on engagement answers."],
            ["Cross Border Audit", "No", "N/A", "System", "08 Sep 2026", "No cross-border component selected."],
            ["e2313 Completion", "Yes", picComplete ? "COMPLETE" : "IN PROGRESS", picComplete ? partner : manager,
                picComplete ? "12 Sep 2026" : "-", picComplete ? "Lead Manager and PIC Completion sign-offs are complete." : "Lead Manager completed. PIC Completion sign-off remains outstanding."],
            ["eAD1 Readiness Gate", "Yes", archived ? "COMPLETE" : picComplete ? "READY" : "BLOCKED", "System", archived ? "12 Sep 2026" : "-",
                archived ? "All required sign-offs and electronic archival completed." : picComplete ? "Prerequisites satisfied. MIC preparation and PIC eAD1 sign-off may proceed." : "Blocked until all required CS / EQCR / TR outcomes and e2313 sign-offs are complete."]
        ];
        int complete = checklists.Count(r => r[2] is "COMPLETE" or "EXEMPTED" or "N/A");
        var paper = request.Paper switch { "a3" => PdfPageSize.A3, "letter" => PdfPageSize.Letter, "legal" => PdfPageSize.Legal, _ => PdfPageSize.A4 };
        if (request.Orientation == "landscape") paper = paper.Landscape();
        var document = Pdf.Document(doc =>
        {
            doc.DefaultFont(regular, bold);
            doc.Style(styles =>
            {
                styles.Default = new() { Size = 8.8, LineHeight = 1.4, Color = Color.Hex("#4B5563") };
                styles.Table = Body;
                styles.TableHeader = Header;
            });
            void Page(Action<FlowBuilder> content) => doc.Page(page =>
            {
                page.Size(paper).Margin(new PdfMargins(request.Margin, request.Margin, request.Margin, 18));
                page.Content(content);
                page.Footer(footer => footer.Add(new DocketFooterElement()));
            });

            Page(content =>
            {
                content.Text("eDOCKET").FontSize(8).Bold().Color("#6B7280").MarginBottom(3);
                content.Text(request.Title).FontSize(22).Bold().Color("#202124").LineHeight(1.25).Center().MarginBottom(6);
                Description(content, "Sample output for internal PDF-export library validation. All names, identifiers and values below are synthetic.", 8);
                content.Add(new DocketEngagementElement(request.EngagementName, status, archived || picComplete)).MarginBottom(10);
                content.Add(new DocketKpiElement(checklists.Length, complete, deadline));
                Section(content, "Engagement details");
                Table(content, [1.25, 3.85], null,
                [
                    ["eDocket ID", data.Id], ["Client", request.Client], ["Engagement code", data.EngagementCode],
                    ["Period end date", period], ["Business unit", data.BusinessUnit], ["Country", data.Country],
                    ["Engagement Partner / PIC", data.Partner], ["Lead Manager / MIC", data.Manager], ["Archival type", "Electronic archival"]
                ], true);
                Section(content, "Current readiness summary");
                Table(content, [.85, .92, 2.9, 1.3], ["Area", "Status", "Latest update", "Owner"],
                [
                    ["CS", "COMPLETE", "Client-service checklist completed", "MIC"],
                    ["EQCR", "EXEMPTED", "Exemption route approved", "PIC"],
                    ["TR", "COMPLETE", "Tax review checklist completed", "Tax reviewer"],
                    ["e2313", picComplete ? "COMPLETE" : "IN PROGRESS", picComplete ? "PIC Completion sign-off recorded" : "PIC Completion sign-off pending", "PIC"],
                    ["eAD1", archived ? "COMPLETE" : picComplete ? "READY" : "BLOCKED", archived ? "Electronic archival completed" : picComplete ? "Prerequisites satisfied" : "Waiting for e2313 PIC sign-off", "MIC"]
                ], true);
            });

            Page(content =>
            {
                PageTitle(content, "Readiness & Checklist Status", "This page exercises dense tabular content, multiple statuses, wrapped notes and conditional workflow outcomes.");
                Table(content, [1.8, .85, 1.15, 1.35, 1.2, 2], ["Checklist / Gate", "Required?", "Status", "Completed by", "Completed on", "Comment"], checklists, style: table =>
                {
                    var small = Body with { Size = 7, Color = Color.Hex("#6B7280") };
                    for (int i = 0; i < checklists.Length; i++)
                    {
                        table.CellStyle(i, 4, new(small));
                        table.CellStyle(i, 5, new(small));
                    }
                });
                Section(content, "Readiness gate", "eAD1 may proceed only when required checklists are Completed or Exempted and the prerequisite completion sign-offs are satisfied.");
                Table(content, [1.15, .8, 1.15], null,
                [
                    ["CS", "COMPLETE", "Ready"], ["EQCR", "EXEMPTED", "Ready"], ["TR", "COMPLETE", "Ready"],
                    ["e2313 PIC Completion", picComplete ? "COMPLETE" : "PENDING", picComplete ? "Ready" : "Blocks eAD1"]
                ], true, table =>
                {
                    for (int i = 0; i < 4; i++) table.CellStyle(i, 2, new(Label, Color.Hex(i == 3 && !picComplete ? "#FFF4E5" : "#EFF8E7")));
                });
                Section(content, "Exceptions / observations");
                Table(content, [1.25, 3.85], null,
                [
                    ["Open observation 01", "Reviewer list contains one user whose completion evidence was added after the original checklist date. No workflow breach recorded."],
                    ["Open observation 02", archived ? "Electronic archival is complete. Preserve the final evidence references." : $"Archival deadline is {deadline}. Monitor completion timing."],
                    ["System note", "No client-confidential attachments are embedded in this sample report. Attachments should be referenced by metadata only unless separately approved."]
                ], true);
            });

            Page(content =>
            {
                PageTitle(content, "Sign-off History", "This page is designed to test ordered sign-off sections, long names, dates, roles, status labels and evidence references.");
                Section(content, "e2313 Completion Sign-offs", top: 0);
                Table(content, [.5, 1.15, 1.6, .9, 1.2, .9], ["Step", "Role", "Signer", "Status", "Signed on", "Evidence ref."],
                [
                    ["1", "MIC / Lead Manager", data.Manager, "COMPLETE", "11 Sep 2026 15:42", "SIG-e2313-001"],
                    ["2", "PIC", data.Partner, picComplete ? "COMPLETE" : "PENDING", picComplete ? "12 Sep 2026 08:10" : "-", picComplete ? "SIG-e2313-002" : "-"],
                    ["3", "Report Signing Partner", "Not applicable", "N/A", "-", "Rule-based exemption"]
                ]);
                Section(content, "eAD1 Sign-Off");
                Table(content, [1.45, 1, 2.15], null,
                [
                    ["MIC preparation", archived ? "COMPLETE" : "PENDING", archived ? "Preparation recorded by MIC" : picComplete ? "Ready for MIC preparation" : "Blocked by e2313 PIC Completion"],
                    ["PIC eAD1 sign-off", archived ? "COMPLETE" : "NOT STARTED", archived ? "PIC sign-off recorded" : "Available after MIC preparation and readiness gate"],
                    ["Export to eDocket PDF", archived ? "COMPLETE" : "NOT STARTED", archived ? "Final export stored in archival repository" : "Generated after required sign-offs"]
                ], true);
                Section(content, "Sign-off evidence detail");
                Table(content, [1.25, 3.85], null,
                [
                    ["Evidence model", "Each sign-off stores signer identity, role, timestamp, status, and a stable evidence reference. A handwritten-signature image is not required for the summary report unless the source workflow stores one."],
                    ["Time zone", "Asia/Bangkok (UTC+07:00) for this sample."],
                    ["Auditability", "The PDF should show what was signed and when, but should not be treated as the authoritative source of workflow state. The application database remains the source of truth."],
                    ["Digital integrity", "For a custom export library, consider recording a document ID, generated-at timestamp, source record version, and optional file hash in the footer or metadata."]
                ], true);
                Section(content, "Long-text rendering test");
                content.Paragraph(request.Notes).MarginBottom(0);
            });

            Page(content =>
            {
                PageTitle(content, "Archival & Audit Trail", "Final-page sample for archival routing, event history, document metadata and footer behavior.");
                Section(content, "Archival routing", top: 0);
                Table(content, [1.25, 3.85], null,
                [
                    ["Archival method", "Electronic archival"], ["Target repository", data.Repository], ["AAT ID", data.AatId],
                    ["Archive deadline", deadline], ["Current archival status", archived ? "Archived - electronic archival complete" : picComplete ? "Ready for eAD1 - awaiting required sign-offs" : "Not ready - eAD1 sign-off not complete"],
                    ["Preservation flag", "Not required"]
                ], true);
                Section(content, "Workflow event history");
                Table(content, [1.3, 2.2, 1.4, 1.25], ["Timestamp", "Event", "Actor", "Result"], Events(request.Rows, data, manager, partner), style: table => table.Style(Body with { Size = 7, Color = Color.Hex("#6B7280") }, Header));
                content.Container(metadata =>
                {
                    Section(metadata, "Document metadata");
                    Table(metadata, [1.25, 3.85], null,
                    [
                        ["Report type", "eDocket Engagement Summary Report - sample"], ["Document ID", "PDF-" + data.Id + "-R1"],
                        ["Generated at", Generated(data.GeneratedAt).ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture) + " (UTC+07:00)"],
                        ["Source record version", data.SourceVersion.ToString(CultureInfo.InvariantCulture)],
                        ["Generated by", "Kypelon Export Studio - sample"], ["Classification", "Internal - Synthetic sample data"]
                    ], true);
                });
                content.Add(new DocketBannerElement("Library validation note.", "This sample intentionally includes: multi-page pagination, repeated table headers, wrapped text, status values, long identifiers, multiple font sizes, alternating rows, section banners, footer page numbers and mixed data density. It is suitable as a visual and structural reference when implementing a reusable PDF export library.", true)).Margin(new Insets(0, 14, 0, 0));
            });
        });
        document.Metadata.Title = request.Title;
        document.Metadata.Subject = request.EngagementName;
        document.Metadata.Creator = "Kypelon Export Studio - eDocket";
        document.Metadata.Keywords = $"eDocket, {data.Id}, source version {data.SourceVersion}, synthetic sample";
        document.Metadata.CreationDate = new DateTimeOffset(Generated(data.GeneratedAt), TimeSpan.FromHours(7));
        document.Options.Deterministic = true;
        document.Options.CompressStreams = request.Compress;
        document.Options.DebugLayout = request.DebugLayout;
        return document;
    }

    private static string Date(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    private static DateTime Generated(string value) => DateTime.ParseExact(value, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None);
    private static string ShortName(string value)
    {
        var parts = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? StringInfo.GetNextTextElement(parts[0]) + ". " + string.Join(" ", parts.Skip(1)) : value;
    }
    private static void Description(FlowBuilder flow, string value, double bottom = 4) => flow.Text(value).FontSize(7.4).Color("#6B7280").MarginBottom(bottom);
    private static void PageTitle(FlowBuilder flow, string title, string description)
    {
        flow.Text(title).FontSize(14).Bold().Color("#202124").MarginBottom(7);
        Description(flow, description);
    }
    private static void Section(FlowBuilder flow, string title, string? description = null, double top = 12) =>
        flow.Add(new DocketBannerElement(title, description)).Margin(new Insets(0, top, 0, 6));
    private static void Table(FlowBuilder flow, double[] widths, string[]? header, IEnumerable<string[]> rows, bool boldFirst = false, Action<TableBuilder>? style = null) => flow.Table(table =>
    {
        table.Columns(widths.Select(Column.Flex).ToArray()).Style(Body, Header).CellPadding(5).Colors("#202124", "#F8FAFA", "#E1E5E9");
        if (header is not null) table.Header(header);
        int row = 0;
        foreach (var values in rows)
        {
            table.Row(values);
            if (boldFirst) table.CellStyle(row, 0, new(Label));
            row++;
        }
        style?.Invoke(table);
    });
    private static IEnumerable<string[]> Events(int count, DocketFields data, string manager, string partner)
    {
        yield return ["08 Sep 2026 09:20", "CS completed", manager, "Success"];
        yield return ["08 Sep 2026 09:31", "EQCR exemption approved", partner, "Success"];
        yield return ["10 Sep 2026 13:05", "TR completed", "S. Niran", "Success"];
        yield return ["11 Sep 2026 15:42", "e2313 Lead Manager sign-off", manager, "Success"];
        if (data.Stage == "pending")
        {
            yield return ["11 Sep 2026 15:43", "Readiness evaluated", "System", "eAD1 blocked"];
            yield return ["12 Sep 2026 08:10", "Reminder generated", "System", "PIC notified"];
        }
        else if (data.Stage == "ready")
        {
            yield return ["12 Sep 2026 08:10", "e2313 PIC Completion sign-off", partner, "Success"];
            yield return ["12 Sep 2026 08:11", "Readiness evaluated", "System", "eAD1 ready"];
        }
        else
        {
            yield return ["12 Sep 2026 09:30", "eAD1 sign-offs completed", partner, "Success"];
            yield return ["12 Sep 2026 09:35", "Electronic archival completed", "System", "Archived"];
        }
        var start = new DateTime(2026, 9, 12, 10, 0, 0);
        for (int i = 6; i < count; i++)
            yield return [start.AddMinutes(i - 6).ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture), $"Audit trail event {i + 1:D4}", i % 2 == 0 ? "System" : manager, "Success"];
    }
}

internal static class DocketText
{
    internal static MeasuredBlock Measure(LayoutContext context, string text, double width, double size, bool bold = false,
        string color = "#202124", HorizontalAlignment alignment = HorizontalAlignment.Left, bool pageNumber = false) =>
        new TextElement(text, new() { Size = size, Bold = bold, LineHeight = 1.35, Color = Color.Hex(color), Alignment = alignment })
        { IsPageNumber = pageNumber }.Measure(context, width);
}

internal sealed class DocketBannerElement(string title, string? description = null, bool note = false) : Element
{
    protected override MeasuredBlock MeasureContent(LayoutContext context, double width)
    {
        var heading = DocketText.Measure(context, title, width - 20, note ? 8.8 : 10.5, true);
        var body = description is null ? null : DocketText.Measure(context, description, width - 20, note ? 8.8 : 7.1, color: "#6B7280");
        double height = heading.Height + 10 + (body is null ? 0 : body.Height + 5);
        var commands = new List<RenderCommand>
        {
            new RectangleCommand(new(0, 0, width, height), Color.Hex(note ? "#F3F8EB" : "#F8FAFA"), note ? Color.Hex("#CCDFAD") : null, .4),
            new RectangleCommand(new(0, 0, 3, height), Color.Hex("#86BC25"), null, 0, CornerRadius: 1.5)
        };
        commands.AddRange(heading.Arrange(10, 5));
        if (body is not null) commands.AddRange(body.Arrange(10, heading.Height + 10));
        return new(width, height, commands);
    }
}

internal sealed class DocketEngagementElement(string name, string status, bool ready) : Element
{
    protected override MeasuredBlock MeasureContent(LayoutContext context, double width)
    {
        var label = DocketText.Measure(context, "Engagement:", width - 120, 8.8, true, "#4B5563");
        var engagement = DocketText.Measure(context, name, width - 120, 8.8, color: "#4B5563");
        var badge = DocketText.Measure(context, status, 96, 7.1, true, "#FFFFFF", HorizontalAlignment.Center);
        double height = Math.Max(label.Height + engagement.Height + 16, badge.Height + 16);
        var commands = new List<RenderCommand>
        {
            new RectangleCommand(new(0, 0, width, height), Color.Hex("#F8FAFA"), Color.Hex("#E1E5E9"), .4),
            new RectangleCommand(new(0, 0, 3, height), Color.Hex("#86BC25"), null, 0, CornerRadius: 1.5),
            new RectangleCommand(new(width - 106, (height - badge.Height - 8) / 2, 96, badge.Height + 8), Color.Hex(ready ? "#397344" : "#245B8E"), null, 0)
        };
        commands.AddRange(label.Arrange(10, 8));
        commands.AddRange(engagement.Arrange(10, 8 + label.Height));
        commands.AddRange(badge.Arrange(width - 106, (height - badge.Height) / 2));
        return new(width, height, commands);
    }
}

internal sealed class DocketKpiElement(int total, int complete, string deadline) : Element
{
    protected override MeasuredBlock MeasureContent(LayoutContext context, double width)
    {
        var values = new[] { total.ToString(CultureInfo.InvariantCulture), complete.ToString(CultureInfo.InvariantCulture), (total - complete).ToString(CultureInfo.InvariantCulture), deadline };
        var labels = new[] { "Checklist modules", "Complete / exempt / N/A", "Outstanding", "Archival deadline" };
        double cell = width / 4;
        var large = values.Select(value => DocketText.Measure(context, value, cell - 10, 17, true, alignment: HorizontalAlignment.Center)).ToArray();
        var small = labels.Select(value => DocketText.Measure(context, value, cell - 10, 7.5, color: "#6B7280", alignment: HorizontalAlignment.Center)).ToArray();
        double largeHeight = large.Max(b => b.Height), smallHeight = small.Max(b => b.Height), height = largeHeight + smallHeight + 12;
        var commands = new List<RenderCommand>();
        for (int i = 0; i < 4; i++)
        {
            commands.Add(new RectangleCommand(new(i * cell, 0, cell, height), Color.White, Color.Hex("#E1E5E9"), .4));
            commands.Add(new RectangleCommand(new(i * cell, largeHeight + 6, cell, .4), Color.Hex("#E1E5E9"), null, 0));
            commands.AddRange(large[i].Arrange(i * cell + 5, 6));
            commands.AddRange(small[i].Arrange(i * cell + 5, largeHeight + 6));
        }
        return new(width, height, commands);
    }
}

internal sealed class DocketFooterElement : Element
{
    protected override MeasuredBlock MeasureContent(LayoutContext context, double width)
    {
        var label = DocketText.Measure(context, "eDocket Summary Report - Synthetic Sample", width - 140, 7.2, color: "#6B7280");
        var number = DocketText.Measure(context, "Page {page} of {pages}", 90, 7.2, color: "#6B7280", alignment: HorizontalAlignment.Right, pageNumber: true);
        var commands = new List<RenderCommand> { new RectangleCommand(new(0, 0, 42, 1.5), Color.Hex("#86BC25"), null, 0) };
        commands.AddRange(label.Arrange(54, 0));
        commands.AddRange(number.Arrange(width - 90, 0));
        return new(width, Math.Max(label.Height, number.Height), commands);
    }
}
