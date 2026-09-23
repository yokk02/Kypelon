using System.Globalization;
using Kypelon.Pdf.Core;
using Kypelon.Pdf.Graphics;
using Kypelon.Pdf.Layout;
using Kypelon.Pdf.Text;

namespace Kypelon.Pdf.Sample.AspNetCore;

// A native layout recreation of assets/templates/AEFS-Outlook-Email-Template-Reconstructed.html.
// CSS pixels are mapped to 0.75 points. This is a typed template, not an HTML renderer.
public static class AefsReport
{
    public static PdfDocument Build(ExportRequest request, FontFace regular, FontFace? bold)
    {
        var paper = request.Paper switch
        {
            "a3" => PdfPageSize.A3,
            "letter" => PdfPageSize.Letter,
            "legal" => PdfPageSize.Legal,
            _ => PdfPageSize.A4
        };
        if (request.Orientation == "landscape") paper = paper.Landscape();
        var document = Pdf.Document(doc =>
        {
            doc.DefaultFont(regular, bold);
            doc.Page(page =>
            {
                page.Size(paper).Margin(0);
                page.Content(content => content.Add(new AefsNotificationElement(request, paper.Height)));
            });
        });
        document.Metadata.Title = request.Title;
        document.Metadata.Subject = request.EngagementName;
        document.Metadata.Creator = "Kypelon Export Studio — AEFS template";
        document.Options.Deterministic = true;
        document.Options.DebugLayout = request.DebugLayout;
        document.Options.CompressStreams = request.Compress;
        return document;
    }
}

public sealed class AefsNotificationElement(ExportRequest request, double pageHeight) : Element
{
    private static Color Hex(string value) => Color.Hex(value);

    protected override MeasuredBlock MeasureContent(LayoutContext context, double width)
    {
        const double px = .75;
        double shellWidth = Math.Min(620 * px, width - request.Margin * 2);
        double shellX = (width - shellWidth) / 2, shellY = request.Margin;
        double pad = 28 * px, innerWidth = shellWidth - pad * 2;
        double left = shellX + pad, y = shellY + 5 * px;
        var commands = new List<RenderCommand>();
        var regular = context.Font;
        var bold = context.BoldFont ?? regular;
        var ink = Hex("#202020");

        MeasuredBlock Measure(string text, double fontPx, double linePx, double available,
            bool strong = false, string color = "#202020", FontFace? face = null,
            HorizontalAlignment alignment = HorizontalAlignment.Left) =>
            new TextElement(text, new TextStyle
            {
                Size = fontPx * px, LineHeight = linePx / fontPx, Bold = strong,
                Color = Hex(color), Font = face, Alignment = alignment
            }).Measure(context, available);

        void Place(MeasuredBlock block, double x, double top) => commands.AddRange(block.Arrange(x, top));
        void Fill(Rect bounds, string color) => commands.Add(new RectangleCommand(bounds, Hex(color), null, 0));

        // Masthead: green punctuation and a right-aligned notification label.
        y += 22 * px;
        Place(Measure("AEFS", 20, 26, innerWidth, true), left, y);
        double brandWidth = LineBreaker.Measure("AEFS", bold, 20 * px, context.Shaper);
        Place(Measure(".", 20, 26, 12, true, "#86BC25"), left + brandWidth, y);
        Place(Measure("SYSTEM NOTIFICATION", 11, 16, innerWidth, true, "#6B7280", alignment: HorizontalAlignment.Right), left, y + 5 * px);
        y += (26 + 10 + 8) * px;

        var heading = Measure(request.Title, 28, 36, innerWidth, true);
        Place(heading, left, y);
        y += heading.Height + (8 + 8) * px;
        var greeting = Measure("Hi " + request.RecipientName + ",", 14, 22, innerWidth, color: "#4B4E52");
        Place(greeting, left, y);
        y += greeting.Height + 12 * px;
        var message = Measure(request.Notes, 14, 22, innerWidth, color: "#4B4E52");
        Place(message, left, y);
        y += message.Height + 20 * px;

        // Details has only horizontal separators. Measure both cells before placing each row.
        double detailsY = y, headerHeight = 40 * px, radius = 10 * px;
        double labelWidth = Math.Min(204 * px, innerWidth * .40), cellPad = 12 * px;
        string period = DateOnly.ParseExact(request.PeriodEndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .ToString("dd MMMM yyyy", CultureInfo.InvariantCulture);
        var rows = new[] { ("Request ID", request.RequestId), ("Engagement Name", request.EngagementName), ("Period End Date", period) };
        var cells = rows.Select((row, index) => (
            Label: Measure(row.Item1, 13, 20, labelWidth - 2 * cellPad, true, "#4B4E52"),
            Value: Measure(row.Item2, index == 0 ? 12 : 13, 20, innerWidth - labelWidth - 2 * cellPad,
                face: index == 0 && row.Item2.All(c => c is >= ' ' and <= '~') ? FontFace.Courier : null))).ToArray();
        double detailsHeight = headerHeight + cells.Sum(c => Math.Max(c.Label.Height, c.Value.Height) + cellPad * 2);
        commands.Add(new RectangleCommand(new(left, y + 1.5, innerWidth, detailsHeight), Hex("#D7DCE1"), null, 0, CornerRadius: radius));
        commands.Add(new RectangleCommand(new(left, y, innerWidth, detailsHeight), Color.White, null, 0, CornerRadius: radius));
        commands.Add(new RectangleCommand(new(left, y, innerWidth, headerHeight), Hex("#F7F9FC"), null, 0, CornerRadius: radius));
        Fill(new(left, y + headerHeight - radius, innerWidth, radius), "#F7F9FC");
        Place(Measure("Details", 14, 20, innerWidth - 28 * px, true), left + 14 * px, y + 10 * px);
        y += headerHeight;
        Fill(new(left, y, innerWidth, px), "#E9EDF3");
        for (int i = 0; i < cells.Length; i++)
        {
            var cell = cells[i];
            double height = Math.Max(cell.Label.Height, cell.Value.Height) + 2 * cellPad;
            Place(cell.Label, left + cellPad, y + cellPad);
            Place(cell.Value, left + labelWidth + cellPad, y + cellPad);
            if (context.DebugLayout)
            {
                commands.Add(new RectangleCommand(new(left, y, labelWidth, height), null, Hex("#E04CA8"), .25, true));
                commands.Add(new RectangleCommand(new(left + labelWidth, y, innerWidth - labelWidth, height), null, Hex("#3B82F6"), .25, true));
            }
            y += height;
            if (i < cells.Length - 1) Fill(new(left, y, innerWidth, px), "#EEF2F7");
        }
        commands.Add(new RectangleCommand(new(left, detailsY, innerWidth, detailsHeight), null, Hex("#E2E5E9"), px, CornerRadius: radius));

        // The button and its PDF annotation share the same measured rectangle.
        y += 24 * px;
        string buttonLabel = "Open " + request.RequestType + " request";
        double arrowWidth = LineBreaker.Measure("→", bold, 14 * px, context.Shaper);
        double buttonTextWidth = Math.Min(LineBreaker.Measure(buttonLabel, bold, 14 * px, context.Shaper), innerWidth - 36 * px - arrowWidth - 8 * px);
        var button = Measure(buttonLabel, 14, 18, buttonTextWidth + .001, true, "#FFFFFF");
        double buttonWidth = buttonTextWidth + 36 * px + arrowWidth + 8 * px;
        double buttonHeight = button.Height + 24 * px;
        var buttonBounds = new Rect(left, y, buttonWidth, buttonHeight);
        commands.Add(new RectangleCommand(buttonBounds, ink, null, 0));
        Place(button, left + 18 * px, y + 12 * px);
        Place(Measure("→", 14, 18, arrowWidth + .001, true, "#A8DB49"), left + 18 * px + buttonTextWidth + 8 * px, y + (buttonHeight - 18 * px) / 2);
        commands.Add(new LinkCommand(request.RecordUrl, buttonBounds));
        y += buttonHeight + 24 * px;

        var footer = Measure("This is a system-generated notification from AEFS. Please do not reply to this email.", 11, 17, innerWidth, color: "#6B7280");
        double footerHeight = footer.Height + (16 + 20) * px;
        Fill(new(shellX, y, shellWidth, footerHeight), "#F7F9FC");
        Fill(new(shellX, y, shellWidth, px), "#E9EDF3");
        Place(footer, left, y + 16 * px);
        y += footerHeight;
        if (y + request.Margin > pageHeight + .001)
            throw new PdfLayoutException($"AEFS notification needs {y - shellY:0.##}pt but only {pageHeight - request.Margin * 2:0.##}pt is available. Shorten the message or select a larger portrait page. This notification template stays on one page.");

        commands.Insert(0, new RectangleCommand(new(0, 0, width, pageHeight), Hex("#F4F6F8"), null, 0));
        commands.Insert(1, new RectangleCommand(new(shellX, shellY, shellWidth, y - shellY), Color.White, null, 0));
        commands.Insert(2, new RectangleCommand(new(shellX, shellY, shellWidth, 5 * px), Hex("#86BC25"), null, 0));
        return new(width, pageHeight, commands);
    }
}
