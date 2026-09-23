using System.Reflection;
using Microsoft.AspNetCore.Http;
using Kypelon.Pdf;
using Kypelon.Pdf.AspNetCore;
using Kypelon.Pdf.Layout;
using Kypelon.Pdf.Text;

string output = args[0];
Directory.CreateDirectory(output);
string fontPath = args.Length > 1 ? args[1] : Environment.GetEnvironmentVariable("KYPELON_TEST_FONT")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "tahoma.ttf");
string version = typeof(Pdf).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
if (version != "0.2.0-alpha.2") throw new Exception("Wrong package version: " + version);
if (typeof(Pdf).Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product != "Kypelon") throw new Exception("Wrong product");
if (typeof(Pdf).Assembly.GetName().Name != "Kypelon.Pdf") throw new Exception("Wrong assembly identity");

var basic = Pdf.Document(doc => doc.Page(page => page.Content(content => content.Text("Hello from Kypelon.Pdf"))));
basic.Save(Path.Combine(output, "hello-kypelon.pdf"));

var table = Pdf.Document(d => d.Page(p =>
{
    p.Header(h => h.Text("Package table"));
    p.Content(c => c.Table(t =>
    {
        t.Columns(Column.Fixed(50), Column.Flex());
        t.Header("No.", "Employee");
        for (int i = 1; i <= 500; i++) t.Row(i, $"Employee {i:D4}");
    }));
    p.Footer(f => f.PageNumber());
}));
table.Save(Path.Combine(output, "table.pdf"));

var font = FontFace.Load(fontPath);
var unicode = Pdf.Document(d => { d.DefaultFont(font); d.Page(p => p.Content(c => c.Text("ภาษาไทย บริษัท ทดสอบ จำกัด | Unicode FY26"))); });
await unicode.SaveAsync(Path.Combine(output, "unicode.pdf"));

var element = new TextElement("Snapshot", new() { Size = 10 });
var document = Pdf.Document(d => d.Page(p => { p.Content(c => c.Add(element)); p.Footer(f => f.PageNumber()); }));
document.Options.Deterministic = true; document.Metadata.Title = "Prepared consumer";
var prepared = document.Prepare();
if (prepared.PageCount != 1) throw new Exception("Page count");
element.Text = "Changed after prepare"; document.Metadata.Title = "Changed";
prepared.Save(Path.Combine(output, "prepared.pdf"));
await prepared.SaveAsync(Path.Combine(output, "prepared-repeat.pdf"));
var http = new DefaultHttpContext();
await using (var file = File.Create(Path.Combine(output, "http.pdf")))
{
    http.Response.Body = file;
    await prepared.PdfFile("consumer.pdf").ExecuteAsync(http);
    if (http.Response.ContentType != "application/pdf") throw new Exception("HTTP content type");
}
byte[] expected = await File.ReadAllBytesAsync(Path.Combine(output, "prepared.pdf"));
foreach (string name in new[] { "prepared-repeat.pdf", "http.pdf" })
{
    byte[] actual = await File.ReadAllBytesAsync(Path.Combine(output, name));
    if (!expected.SequenceEqual(actual)) throw new Exception("Snapshot output differs");
}

string keep = Path.Combine(output, "keep.txt"); await File.WriteAllTextAsync(keep, "KEEP");
try { await document.SaveAsync(keep, new CancellationToken(true)); throw new Exception("Expected cancellation"); }
catch (OperationCanceledException) { if (await File.ReadAllTextAsync(keep) != "KEEP") throw new Exception("Truncated cancelled target"); }
using (var cancelled = new MemoryStream())
{
    try { await prepared.WriteAsync(cancelled, new CancellationToken(true)); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { if (cancelled.Length != 0) throw new Exception("Cancellation emitted bytes"); }
}

var shaper = new PairShaper();
var shaped = Pdf.Document(d =>
{
    d.TextShaper(shaper);
    d.Page(p => p.Content(c => c.Text("AV").FontSize(10).Width(15)));
});
var snapshot = shaped.Prepare(); int calls = shaper.Calls;
var line = snapshot.Pages.SelectMany(p => p.Commands).OfType<TextCommand>().Single().Prepared!;
if (line.Width != 14 || line.Text != "AV") throw new Exception("Contextual fitting");
await snapshot.SaveAsync(Path.Combine(output, "contextual.pdf"));
if (calls != 1 || shaper.Calls != calls) throw new Exception("Repeated shaping");
Console.WriteLine($"PASS {version} / {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}: basic, 500 rows, Thai/Unicode, prepared snapshot, ASP.NET streaming, cancellation, contextual shaper.");
sealed class PairShaper : ITextShaper
{
    internal int Calls;
    public GlyphRun Shape(TextRun input)
    {
        Calls++;
        var result = BasicTextShaper.Instance.Shape(input);
        return result with { Glyphs = result.Glyphs.Select(g => g with { AdvanceX = input.Text == "AV" ? 700 : 1000 }).ToArray() };
    }
}
