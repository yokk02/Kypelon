using Kypelon.Pdf.Core;
using Kypelon.Pdf.Layout;
using Kypelon.Pdf.Text;

namespace Kypelon.Pdf.Sample.AspNetCore;

public sealed record ExportRequest
{
    public string Template { get; init; } = "business";
    public string Title { get; init; } = "FY26 Audit Engagement Report";
    public string Client { get; init; } = "บริษัท ทดสอบ จำกัด";
    public string Notes { get; init; } = "รายงาน Audit FY26 – บริษัท ABC จำกัด\nรายงานผลการตรวจสอบ และข้อมูลพนักงาน ปีงบประมาณ 2569";
    public string RecipientName { get; init; } = "Krittanai";
    public string RequestId { get; init; } = "AEFS-2026-0042";
    public string EngagementName { get; init; } = "ABC Company Limited – FY26 Audit";
    public string PeriodEndDate { get; init; } = "2026-12-31";
    public string RequestType { get; init; } = "eForm";
    public string RecordUrl { get; init; } = "https://aefs.example/requests/AEFS-2026-0042";
    public DocketFields? Docket { get; init; } = new();
    public int Rows { get; init; } = 50;
    public string Paper { get; init; } = "a4";
    public string Orientation { get; init; } = "portrait";
    public int Margin { get; init; } = 36;
    public bool DebugLayout { get; init; }
    public bool Compress { get; init; } = true;

    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (Template is not ("business" or "table" or "unicode" or "aefs" or "edocket")) errors["template"] = ["เลือกประเภทเอกสารที่รองรับ"];
        if (string.IsNullOrWhiteSpace(Title) || Title.Length > 120) errors["title"] = ["ชื่อรายงานต้องมี 1–120 ตัวอักษร"];
        if (Template != "aefs" && (string.IsNullOrWhiteSpace(Client) || Client.Length > 160)) errors["client"] = ["ชื่อบริษัทต้องมี 1–160 ตัวอักษร"];
        if (Notes is null || Notes.Length > 4000) errors["notes"] = ["เนื้อหาต้องไม่เกิน 4,000 ตัวอักษร"];
        if (Rows is < 0 or > 5000) errors["rows"] = ["จำนวนแถวต้องอยู่ระหว่าง 0–5,000"];
        if (Paper is not ("a4" or "a3" or "letter" or "legal")) errors["paper"] = ["ขนาดกระดาษไม่ถูกต้อง"];
        if (Orientation is not ("portrait" or "landscape")) errors["orientation"] = ["แนวกระดาษไม่ถูกต้อง"];
        if (Margin is < 20 or > 64) errors["margin"] = ["ระยะขอบต้องอยู่ระหว่าง 20–64 pt"];
        if (Template == "aefs")
        {
            Required("recipientName", RecipientName, 120, "ชื่อผู้รับ");
            Required("requestId", RequestId, 80, "Request ID");
            Required("engagementName", EngagementName, 240, "Engagement Name");
            Required("requestType", RequestType, 40, "ประเภทคำขอ");
            if (!DateOnly.TryParseExact(PeriodEndDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
                errors["periodEndDate"] = ["เลือกวันสิ้นสุดรอบบัญชีที่ถูกต้อง"];
            if (RecordUrl is null || RecordUrl.Length > 2048 || RecordUrl.Any(char.IsControl) ||
                !Uri.TryCreate(RecordUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host))
                errors["recordUrl"] = ["ลิงก์คำขอต้องเป็น HTTP หรือ HTTPS และไม่เกิน 2,048 ตัวอักษร"];
        }
        if (Template == "edocket")
        {
            Required("engagementName", EngagementName, 240, "Engagement Name");
            if (!DateOnly.TryParseExact(PeriodEndDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
                errors["periodEndDate"] = ["เลือกวันสิ้นสุดรอบบัญชีที่ถูกต้อง"];
            if (Rows < 6) errors["rows"] = ["eDocket ต้องมีเหตุการณ์ตัวอย่างอย่างน้อย 6 รายการ"];
            if (Docket is null) errors["docket"] = ["กรอกข้อมูล eDocket"];
            else foreach (var error in Docket.Validate()) errors[error.Key] = error.Value;
        }
        return errors;

        void Required(string key, string? value, int maximum, string label)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
                errors[key] = [$"{label} ต้องมี 1–{maximum} ตัวอักษร"];
        }
    }
}

public static class ExportReport
{
    public static PdfDocument Build(ExportRequest request, FontFace regular, FontFace? bold)
    {
        if (request.Template == "edocket") return DocketReport.Build(request, regular, bold);
        if (request.Template == "aefs") return AefsReport.Build(request, regular, bold);
        var document = Pdf.Document(doc =>
        {
            doc.DefaultFont(regular, bold);
            doc.Style(style =>
            {
                style.Default = new() { Size = 10.5, Color = Graphics.Color.Hex("#344454") };
                style.H1 = new() { Size = 24, Bold = true, Color = Graphics.Color.Hex("#164D52") };
                style.Table = new() { Size = 9 };
                style.TableHeader = new() { Size = 9, Bold = true, Color = Graphics.Color.Hex("#164D52") };
            });
            doc.Page(page =>
            {
                var paper = request.Paper switch
                {
                    "a3" => PdfPageSize.A3,
                    "letter" => PdfPageSize.Letter,
                    "legal" => PdfPageSize.Legal,
                    _ => PdfPageSize.A4
                };
                page.Size(request.Orientation == "landscape" ? paper.Landscape() : paper).Margin(request.Margin);
                page.Header(header =>
                {
                    header.Row(row =>
                    {
                        row.Text("KYPELON / REPORT STUDIO").Bold().Color("#164D52");
                        row.Text("FY26 • INTERNAL REPORT").AlignRight().FontSize(8).Color("#687B7C");
                    });
                    header.Line("#2B8584", 1.5);
                });
                page.Content(content =>
                {
                    content.Text(request.Template == "unicode" ? "THAI & UNICODE" : request.Template == "table" ? "WORKFORCE REGISTER" : "ENGAGEMENT OVERVIEW")
                        .FontSize(9).Bold().Color("#2B8584").MarginBottom(6);
                    content.H1(request.Title);
                    content.Text(request.Client).FontSize(14).MarginBottom(16);
                    if (request.Template == "business")
                    {
                        content.Grid(3, grid =>
                        {
                            Card(grid, "RECORDS / รายการ", request.Rows.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            Card(grid, "FISCAL YEAR", "FY26");
                            Card(grid, "REPORT STATUS", "In review");
                        });
                        content.H1("ข้อมูลโครงการ / Engagement").FontSize(16);
                        content.Table(table =>
                        {
                            table.Columns(Column.Fixed(110), Column.Flex());
                            table.Row("Client / บริษัท", request.Client);
                            table.Row("Fiscal year", "ปีงบประมาณ 2569 / FY26");
                            table.Row("Prepared for", "Management review");
                        });
                        content.Spacer(14);
                    }
                    if (!string.IsNullOrWhiteSpace(request.Notes))
                        content.Paragraph(request.Notes).MarginBottom(16);
                    if (request.Template == "unicode")
                    {
                        content.H1("ตัวอย่างภาษาไทย").FontSize(16);
                        foreach (var line in new[] { "ภาษาไทย", "บริษัท ทดสอบ จำกัด", "รายงานผลการตรวจสอบ", "ข้อมูลพนักงาน", "ปีงบประมาณ 2569", "กิ กี กึ กื กุ กู ก่ ก้ ก๊ ก๋ กำ น้ำ ผู้ ปู่ ญู ฐุ", "เลขไทย ๐๑๒๓๔๕๖๗๘๙ / English 0123456789" })
                            content.Text(line).FontSize(14).MarginBottom(9);
                    }
                    if (request.Rows > 0)
                    {
                        content.H1("ข้อมูลพนักงาน / Employee register").FontSize(16);
                        content.Table(table =>
                        {
                            table.Id("EmployeeRegister").Columns(Column.Fixed(38), Column.Flex(1.6), Column.Flex(), Column.Fixed(65));
                            table.Header("No.", "Employee / พนักงาน", "Office / สำนักงาน", "Utilisation");
                            table.Colors("#E4F0EE", "#F5F9F8", "#CFDFDC").AlignColumn(3, HorizontalAlignment.Right);
                            for (var i = 1; i <= request.Rows; i++)
                                table.Row(i, $"Employee {i:D4}", i % 2 == 0 ? "กรุงเทพฯ" : "Singapore", $"{70 + i % 20}%");
                        });
                    }
                });
                page.Footer(footer => footer.Row(row =>
                {
                    row.Text("Kypelon • " + request.Client).FontSize(8).Color("#687B7C");
                    row.PageNumber("Page {page} of {pages}").AlignRight().FontSize(8).Color("#687B7C");
                }));
            });
        });
        document.Metadata.Title = request.Title;
        document.Metadata.Subject = request.Client;
        document.Metadata.Creator = "Kypelon Export Studio";
        document.Options.Deterministic = true;
        document.Options.CompressStreams = request.Compress;
        document.Options.DebugLayout = request.DebugLayout;
        return document;
    }

    private static void Card(FlowBuilder grid, string label, string value) => grid.Container(card =>
    {
        card.Text(label).FontSize(8).Color("#526D6A");
        card.Text(value).FontSize(22).Bold().Color("#164D52");
    }).Padding(12).Background("#ECF4F1");
}

public sealed record PlaygroundFonts(FontFace? Regular, FontFace? Bold)
{
    public static PlaygroundFonts Load(IConfiguration configuration)
    {
        var configured = configuration["KYPELON_FONT"] ?? configuration["KYPELON_TEST_FONT"];
        var regularPath = configured;
        if (regularPath is null)
        {
            var windows = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "tahoma.ttf");
            regularPath = File.Exists(windows) ? windows : FontDiscovery.SystemTrueTypeFiles().FirstOrDefault(file =>
                Path.GetFileName(file).Equals("Sarabun-Regular.ttf", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(file).Equals("NotoSansThai-Regular.ttf", StringComparison.OrdinalIgnoreCase));
        }
        if (regularPath is null) return new(null, null);
        var boldPath = configuration["KYPELON_BOLD_FONT"];
        if (boldPath is null)
        {
            var file = Path.GetFileName(regularPath);
            var candidate = Path.Combine(Path.GetDirectoryName(regularPath)!, file.Equals("tahoma.ttf", StringComparison.OrdinalIgnoreCase) ? "tahomabd.ttf" : file.Replace("-Regular", "-Bold", StringComparison.Ordinal));
            if (candidate != regularPath && File.Exists(candidate)) boldPath = candidate;
        }
        return new(FontFace.Load(regularPath), boldPath is null ? null : FontFace.Load(boldPath));
    }
}
