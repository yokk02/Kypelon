using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
#if BENCHMARKDOTNET
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
#endif
using Kypelon.Pdf;
using Kypelon.Pdf.Samples;

if (args.FirstOrDefault() == "--prepared") { PreparedBaseline.Run(args); return; }

if (args.FirstOrDefault() == "--text-pipeline") { TextPipelineBaseline.Run(args); return; }

if (!args.Contains("--bdn", StringComparer.Ordinal))
{
    Directory.CreateDirectory("artifacts/benchmark");
    var results = new List<object>();
    foreach (string name in new[] { "Simple10Pages", "Table500", "Table5000" })
    {
        var document = PdfBenchmarks.Create(name);
        document.Options.Deterministic = true;
        for (int i = 0; i < 2; i++)
            document.Write(Stream.Null);
        var times = new List<double>();
        var allocations = new List<long>();
        for (int i = 0; i < 5; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            document.Write(Stream.Null);
            watch.Stop();
            allocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
            times.Add(watch.Elapsed.TotalMilliseconds);
        }
        string frameworkDirectory = "artifacts/benchmark/net" + Environment.Version.Major.ToString(CultureInfo.InvariantCulture);
        Directory.CreateDirectory(frameworkDirectory);
        string file = frameworkDirectory + "/" + name + ".pdf";
        document.Save(file);
        results.Add(new
        {
            Scenario = name,
            Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Pages = document.Plan().Count,
            MeanMs = times.Average(),
            MinMs = times.Min(),
            MaxMs = times.Max(),
            AllocatedBytes = allocations.Average(),
            OutputBytes = new FileInfo(file).Length,
            ProcessPeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
            Iterations = 5
        });
    }
    string json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText("artifacts/benchmark/baseline-" + Environment.Version.Major.ToString(CultureInfo.InvariantCulture) + ".json", json);
    Console.WriteLine(json);
}
else
{
#if BENCHMARKDOTNET
    BenchmarkSwitcher.FromAssembly(typeof(PdfBenchmarks).Assembly).Run(args.Where(a => a != "--bdn").ToArray());
#else
    throw new InvalidOperationException("Build with -p:UseBenchmarkDotNet=true to enable BenchmarkDotNet. The default baseline has no external dependencies.");
#endif
}

#if BENCHMARKDOTNET
[MemoryDiagnoser]
#endif
public class PdfBenchmarks
{
#if BENCHMARKDOTNET
    [Params("Simple10Pages","Table500","Table5000")]
#endif
    public string Scenario { get; set; } = "Simple10Pages";
    private PdfDocument document = null!;
    public static PdfDocument Create(string scenario) => scenario switch { "Simple10Pages" => Reports.Simple(10), "Table500" => Reports.Table(500), "Table5000" => Reports.Table(5000), _ => throw new ArgumentException("Unknown scenario") };
#if BENCHMARKDOTNET
    [GlobalSetup]
#endif
    public void Setup()
    {
        document = Create(Scenario);
        document.Options.Deterministic = true;
    }
#if BENCHMARKDOTNET
    [Benchmark]
#endif
    public void Generate() => document.Write(Stream.Null);
}
