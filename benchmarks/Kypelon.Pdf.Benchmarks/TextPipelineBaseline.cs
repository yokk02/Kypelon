using System.Diagnostics;
using System.Text.Json;
using Kypelon.Pdf;
using Kypelon.Pdf.Samples;
using Kypelon.Pdf.Text;

internal static class TextPipelineBaseline
{
    internal static void Run(string[] args)
    {
        string output = args.Length > 1 ? args[1] : "artifacts/text-v0.2/benchmark";
        Directory.CreateDirectory(output);
        var font = FontFace.Load(Environment.GetEnvironmentVariable("KYPELON_TEST_FONT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "tahoma.ttf"));
        var results = new List<object>();
        foreach (string name in new[] { "Table5000", "MixedThaiEnglish500", "ParagraphHeavy" })
        {
            var counter = new CountingShaper();
            var document = Create(name, font, counter);
            document.Options.Deterministic = true;
            for (int i = 0; i < 2; i++) document.Write(Stream.Null);
            var times = new List<double>(); var allocations = new List<long>(); var calls = new List<int>();
            for (int i = 0; i < 5; i++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); counter.Calls = 0;
                long before = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
                document.Write(Stream.Null); watch.Stop();
                allocations.Add(GC.GetAllocatedBytesForCurrentThread() - before); times.Add(watch.Elapsed.TotalMilliseconds); calls.Add(counter.Calls);
            }
            string file = Path.Combine(output, name + ".pdf"); document.Save(file);
            results.Add(new { Scenario = name, Runtime = Environment.Version.ToString(), Pages = document.Plan().Count, MeanMs = times.Average(), MinMs = times.Min(), MaxMs = times.Max(), AllocatedBytes = allocations.Average(), OutputBytes = new FileInfo(file).Length, ShapingCalls = name == "Table5000" ? (double?)null : calls.Average(), Iterations = 5 });
        }
        string json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output, "results.json"), json); Console.WriteLine(json);
    }
    internal static PdfDocument Create(string name, FontFace font, ITextShaper counter, bool countTable = false) => name == "Table5000" ? Reports.Table(5000, shaper: countTable ? counter : null) : Pdf.Document(d =>
            {
                d.DefaultFont(font).TextShaper(counter);
                d.Page(p =>
                {
                    p.Header(h => h.Text("Text pipeline baseline"));
                    p.Content(c =>
                    {
                        if (name == "MixedThaiEnglish500") c.Table(t =>
                        {
                            t.Columns(Column.Fixed(35), Column.Flex(2), Column.Flex(), Column.Fixed(80));
                            t.Header("No.", "Employee / พนักงาน", "Report / รายงาน", "FY26");
                            for (int i = 1; i <= 500; i++) t.Row(i, $"Employee {i} ข้อมูลพนักงาน", "บริษัท ABC จำกัด", "2569");
                        });
                        else for (int i = 0; i < 100; i++) c.Paragraph(string.Join(" ", Enumerable.Repeat("Audit FY26 รายงานผลการตรวจสอบ บริษัท ABC จำกัด ข้อมูลพนักงาน", 8)));
                    });
                    p.Footer(f => f.PageNumber());
                });
            });
    internal sealed class CountingShaper : ITextShaper
    {
        internal int Calls;
        public GlyphRun Shape(TextRun input) { Calls++; return BasicTextShaper.Instance.Shape(input); }
    }
}
