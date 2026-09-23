using System.Diagnostics;
using System.Text.Json;
using Kypelon.Pdf;
using Kypelon.Pdf.Samples;
using Kypelon.Pdf.Text;

internal static class PreparedBaseline
{
    internal static void Run(string[] args)
    {
        string output = args.Length > 1 ? args[1] : "artifacts/release-alpha.2/prepared";
        Directory.CreateDirectory(output);
        var font = FontFace.Load(Environment.GetEnvironmentVariable("KYPELON_TEST_FONT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "tahoma.ttf"));
        var results = new List<object>();
        foreach (string scenario in new[] { "Table5000", "MixedThaiEnglish500", "ParagraphHeavy" })
        {
            var counter = new TextPipelineBaseline.CountingShaper();
            var document = TextPipelineBaseline.Create(scenario, font, counter, countTable: true);
            document.Options.Deterministic = true;
            foreach (bool retain in new[] { false, true })
            {
                void Generate()
                {
                    if (retain) document.Prepare().Write(Stream.Null);
                    else { _ = document.Plan(); document.Write(Stream.Null); }
                }
                for (int i = 0; i < 2; i++) Generate();
                var times = new List<double>(); var allocated = new List<long>(); var calls = new List<int>();
                for (int i = 0; i < 5; i++)
                {
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); counter.Calls = 0;
                    long start = GC.GetAllocatedBytesForCurrentThread(); var clock = Stopwatch.StartNew();
                    Generate(); clock.Stop();
                    allocated.Add(GC.GetAllocatedBytesForCurrentThread() - start); times.Add(clock.Elapsed.TotalMilliseconds); calls.Add(counter.Calls);
                }
                var prepared = document.Prepare();
                string file = Path.Combine(output, scenario + (retain ? "-prepared.pdf" : "-plan-write.pdf"));
                prepared.Save(file);
                results.Add(new { Scenario = scenario, Mode = retain ? "Prepare + Prepared.Write" : "Plan + Write (current runtime)",
                    Runtime = Environment.Version.ToString(), MeanMs = times.Average(), AllocatedBytes = allocated.Average(),
                    ShapingCalls = calls.Average(), OutputBytes = new FileInfo(file).Length, Pages = prepared.PageCount, Iterations = 5 });
            }
        }
        string json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output, "results.json"), json); Console.WriteLine(json);
    }
}
