using Kypelon.Pdf;
using Kypelon.Pdf.Core;
using Kypelon.Pdf.Layout;
using Kypelon.Pdf.Text;

namespace Kypelon.Pdf.Samples;

public static class Reports
{
    public static PdfDocument Hello() => Pdf.Document(d => d.Page(p => p.Content(c => { c.H1("Hello, Kypelon!"); c.Text("An original C# PDF writer and layout engine."); })));
    public static PdfDocument Simple(int pages = 10) => Pdf.Document(d =>
    {
        d.Page(p => { p.Header(h => h.Text("Kypelon baseline").Bold()); p.Content(c => { for (int i = 1; i <= pages; i++) { if (i > 1) c.PageBreak(); c.H1($"Quarterly review {i}"); for (int j = 0; j < 8; j++) c.Paragraph("Reliable reporting combines clear ownership, timely evidence and measurable outcomes. This paragraph exercises text measurement and line wrapping in the Kypelon.Pdf layout engine."); } }); p.Footer(f => f.PageNumber().Center()); });
    });
    public static PdfDocument Table(int rows, FontFace? font = null, ITextShaper? shaper = null) => Pdf.Document(d =>
    {
        if (font is not null)
            d.DefaultFont(font);
        if (shaper is not null) d.TextShaper(shaper);
        d.Page(p =>
        {
            p.Margin(28);
            p.Header(h => h.Text("WORKFORCE UTILISATION | FY26").Bold().Color("#14395B"));
            p.Content(c =>
        {
            c.H1("Employee performance");
            c.Text($"{rows:N0} records | Planning period: September 2026");
            c.Table(t => { t.Columns(Column.Fixed(38), Column.Flex(1.8), Column.Flex(1.3), Column.Flex(), Column.Flex(), Column.Flex(), Column.Flex(), Column.Flex()); t.Header("No.", "Employee", "Office", "Grade", "Hours", "Util.", "Jobs", "Status"); t.CellPadding(4).Style(new() { Size = 8 }); for (int i = 1; i <= rows; i++) t.Row(i, $"Employee {i:D4}", i % 2 == 0 ? "Bangkok" : "Singapore", "G" + i % 5, 130 + i % 30, $"{70 + i % 20}%", i % 7 + 1, "Active"); });
        });
            p.Footer(f => f.PageNumber().Center().FontSize(9));
        });
    });
    public static PdfDocument Business(FontFace regular, FontFace? bold = null) => Pdf.Document(d =>
    {
        d.DefaultFont(regular, bold).Style(s => { s.Default = new() { Size = 10.5, Color = Graphics.Color.Hex("#26364A") }; });
        d.Page(p =>
        {
            p.A4().Margin(36);
            p.Header(h => { h.Row(r => { r.Text("KYPELON / ASSURANCE").Bold().Color("#14395B"); r.Text("CONFIDENTIAL • FY26").AlignRight().FontSize(9).Color("#64748B"); }); h.Line("#2C6E9E", 2); });
            p.Content(c =>
            {
                c.Text("ENGAGEMENT REVIEW / SEPTEMBER 2026").FontSize(9).Bold().Color("#377CA4").MarginBottom(8);
                c.H1("FY26 Audit Engagement Report").FontSize(25);
                c.Paragraph("รายงาน Audit FY26 – บริษัท ABC จำกัด").FontSize(15).Color("#377CA4").MarginBottom(18);
                c.Grid(3, grid => { grid.Container(k => { k.Text("COMPLETION").FontSize(8); k.Text("86%").FontSize(25).Bold(); }).Padding(12).Background("#EDF4FA"); grid.Container(k => { k.Text("TEAM MEMBERS").FontSize(8); k.Text("12").FontSize(25).Bold(); }).Padding(12).Background("#EDF4FA"); grid.Container(k => { k.Text("OPEN ACTIONS").FontSize(8); k.Text("7").FontSize(25).Bold(); }).Padding(12).Background("#EDF4FA"); });
                c.H1("Client Information").FontSize(16);
                c.Table(t => { t.Columns(Column.Fixed(130), Column.Flex()); t.Row("Client", "บริษัท ทดสอบ จำกัด / ABC Company Limited"); t.Row("Engagement", "Statutory audit and financial controls review"); t.Row("Fiscal year", "ปีงบประมาณ 2569 / FY26"); t.Row("Manager", "Krittanai"); t.Row("Reporting office", "Bangkok, Thailand"); });
                c.H1("Engagement Information").FontSize(16);
                c.Paragraph("The team reviewed key financial controls, reconciled supporting evidence and assessed the status of management actions. Seven follow-up items remain open. The next review will focus on evidence completeness and action ownership.");
                c.H1("ข้อมูลโครงการ / Notes").FontSize(16);
                c.Paragraph("รายงานผลการตรวจสอบ • ข้อมูลพนักงาน • ภาษาไทย • ปีงบประมาณ 2569");
                c.Paragraph("Thai alpha note: Unicode mapping and copy/paste are supported. Contextual Thai mark positioning and dictionary word breaking are pending.").FontSize(8).Color("#64748B");
                c.PageBreak();
                c.H1("Team Members & Job Information").FontSize(20);
                c.Paragraph("The following work register spans multiple pages. Column headers repeat automatically; each record remains together.");
                c.Table(t => { t.Id("EngagementJobs").Columns(Column.Fixed(35), Column.Flex(1.8), Column.Flex(1.6), Column.Fixed(70)); t.Header("No.", "Workstream / Team", "Deliverable", "Progress"); for (int i = 1; i <= 90; i++) t.Row(i, $"Audit team {i:D2} / ทีมงาน", i % 3 == 0 ? "รายงานผลการตรวจสอบ" : "Evidence and controls review", $"{65 + i % 30}%"); });
                c.H1("Management response").FontSize(16);
                c.Paragraph("Management will complete the remaining actions by the next reporting cycle. Each owner will provide evidence of closure, with exceptions reviewed by the engagement manager.");
            });
            p.Footer(f => f.Row(r => { r.Text("Kypelon / Assurance reporting").FontSize(8).Color("#64748B"); r.PageNumber("Page {page} of {pages}").AlignRight().FontSize(8).Color("#64748B"); }));
        });
    });
    public static string ResolveFont(string? explicitPath = null)
    {
        var configured = explicitPath ?? Environment.GetEnvironmentVariable("KYPELON_TEST_FONT");
        if (configured is not null)
            return configured;
        string windows = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "tahoma.ttf");
        if (File.Exists(windows))
            return windows;
        return FontDiscovery.SystemTrueTypeFiles().FirstOrDefault(p => Path.GetFileName(p).Contains("Sarabun-Regular", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(p).Contains("NotoSansThai-Regular", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("Provide a Thai-capable static TrueType font as the second argument or set KYPELON_TEST_FONT.");
    }
}
