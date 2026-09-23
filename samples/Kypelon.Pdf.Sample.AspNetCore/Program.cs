using System.Diagnostics;
using System.Globalization;
using Kypelon.Pdf.AspNetCore;
using Kypelon.Pdf.Core;
using Kypelon.Pdf.Layout;
using Kypelon.Pdf.Sample.AspNetCore;
using Kypelon.Pdf.Text;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 32 * 1024);
var fonts = PlaygroundFonts.Load(builder.Configuration);
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/status", () => Results.Ok(new
{
    engine = "Kypelon",
    version = "0.2.0-alpha.2",
    ready = fonts.Regular is not null,
    font = fonts.Regular?.Name,
    maxRows = 5000
}));
app.MapPost("/api/export", (ExportRequest request, HttpContext context) => Export(request, context));
app.MapGet("/report", (HttpContext context) => Export(new ExportRequest { Rows = 500 }, context));
app.Run();

IResult Export(ExportRequest request, HttpContext context)
{
    var errors = request.Validate();
    if (errors.Count > 0)
        return Results.ValidationProblem(errors);
    if (fonts.Regular is null)
        return Results.Problem(statusCode: 503, title: "ยังไม่มีฟอนต์สำหรับ export",
            detail: "ตั้งค่า KYPELON_FONT ให้ชี้ไปยังไฟล์ TrueType ที่รองรับภาษาไทย แล้วเริ่มเว็บใหม่");
    try
    {
        var watch = Stopwatch.StartNew();
        var document = ExportReport.Build(request, fonts.Regular, fonts.Bold);
        // Preflight catches unsupported glyphs and geometry before the response starts.
        var prepared = document.Prepare(context.RequestAborted);
        var pages = prepared.PageCount;
        watch.Stop();
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Kypelon-Pages"] = pages.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers["X-Kypelon-Layout-Ms"] = watch.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture);
        return prepared.PdfFile("kypelon-" + request.Template + ".pdf");
    }
    catch (Exception exception) when (exception is PdfFontException or PdfLayoutException or PdfWriteException or ArgumentException)
    {
        return Results.Problem(statusCode: 422, title: "ไม่สามารถสร้าง PDF ด้วยข้อมูลนี้", detail: exception.Message);
    }
}
