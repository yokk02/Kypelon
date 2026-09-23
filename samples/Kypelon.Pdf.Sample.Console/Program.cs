using Kypelon.Pdf;
using Kypelon.Pdf.Graphics;
using Kypelon.Pdf.Samples;
using Kypelon.Pdf.Text;

string output = args.Length > 0 ? args[0] : "artifacts/pdf";
Directory.CreateDirectory(output);
string fontPath = Reports.ResolveFont(args.Length > 1 ? args[1] : null);
var font = FontFace.Load(fontPath);
string boldPath = Path.Combine(Path.GetDirectoryName(fontPath)!, "tahomabd.ttf");
var bold = File.Exists(boldPath) ? FontFace.Load(boldPath) : null;
async Task Save(PdfDocument document, string name)
{
    document.Options.Deterministic = true;
    document.Metadata.Title = name.Replace('-', ' ');
    document.Metadata.Author = "Kypelon sample";
    await document.SaveAsync(Path.Combine(output, name + ".pdf"));
    Console.WriteLine($"{name}.pdf: {document.Plan().Count} pages, {new FileInfo(Path.Combine(output, name + ".pdf")).Length:N0} bytes");
}
await Save(Reports.Hello(), "hello-world");
await Save(Reports.Business(font, bold), "business-report");
await Save(Reports.Table(500, font), "long-table-500-rows");
var thai = Pdf.Document(d => { d.DefaultFont(font, bold); d.Page(p => { p.Content(c => { c.H1("Thai / Unicode regression"); foreach (string text in new[] { "ภาษาไทย", "บริษัท ทดสอบ จำกัด", "รายงานผลการตรวจสอบ", "ข้อมูลพนักงาน", "ปีงบประมาณ 2569", "รายงาน Audit FY26 – บริษัท ABC จำกัด", "กิ กี กึ กื กุ กู ก่ ก้ ก๊ ก๋ กำ น้ำ ผู้ ปู่ ญู ฐุ", "เลขไทย ๐๑๒๓๔๕๖๗๘๙ / English 0123456789", "Mixed punctuation: (ไทย), 50% – FY26." }) c.Text(text).FontSize(16).MarginBottom(12); c.Spacer(10); c.Paragraph("Alpha limitation: no GSUB/GPOS, Thai contextual positioning or dictionary word breaking. This fixture is not a claim of complete Thai typography.").FontSize(10); }); p.Footer(f => f.PageNumber().Center()); }); });
await Save(thai, "thai-unicode");
var debug = Reports.Business(font, bold);
debug.Options.DebugLayout = true;
var diagnostics = new List<string>();
debug.Options.Diagnostic = e => diagnostics.Add($"{e.Element}: measured={e.MeasuredHeight:F2}, available={e.AvailableHeight:F2}, decision={e.Decision}, remainingRows={e.RemainingRows}");
await Save(debug, "debug-layout");
await File.WriteAllLinesAsync(Path.Combine(output, "debug-layout.log"), diagnostics);
var images = Pdf.Document(d => d.Page(p => p.Content(c => { c.H1("JPEG and PNG images"); c.Text("JPEG pass-through / RGB PNG / RGBA soft mask"); c.Row(r => { r.Image(PdfImage.Load("assets/images/sample.jpg")); r.Image(PdfImage.Load("assets/images/sample-rgb.png")); }); c.Spacer(20); c.Container(r => r.Image(PdfImage.Load("assets/images/sample-rgba.png")).Width(200)).Background("#14395B").Padding(20); })));
await Save(images, "images");
await Save(Reports.Simple(), "simple-10-pages");
Console.WriteLine($"Font used locally: {fontPath}. Font files are not redistributed.");
