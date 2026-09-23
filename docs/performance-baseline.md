> Historical evidence for Kypelon (formerly NovaPdf during pre-public alpha development). This report records the original names/results; use README.md and docs/development/kypelon-brand-migration.md for current commands and verification.

# Performance baseline — 0.1.0-alpha.1

Observed 2026-09-23 on Windows 10.0.26200 x64, AMD Ryzen 7 5800X3D (16 logical processors), .NET SDK 10.0.301. Runtime versions appear below. This is a local engineering baseline, not a cross-library comparison or production capacity guarantee.

| Runtime | Scenario | Pages | Mean ms | Min–max ms | Allocated MiB/op | Output bytes | Process peak MiB* |
|---|---|---:|---:|---:|---:|---:|---:|
| .NET 8.0.28 | Simple10Pages | 10 | 6.17 | 5.77–6.56 | 2.68 | 8,028 | 33.93 |
| .NET 8.0.28 | Table500 | 14 | 44.51 | 38.33–58.67 | 22.70 | 61,700 | 74.94 |
| .NET 8.0.28 | Table5000 | 136 | 247.71 | 233.94–269.97 | 227.00 | 608,256 | 115.96 |
| .NET 10.0.9 | Simple10Pages | 10 | 5.74 | 5.45–6.27 | 2.66 | 10,052 | 34.70 |
| .NET 10.0.9 | Table500 | 14 | 58.99 | 40.60–69.47 | 22.20 | 77,260 | 74.53 |
| .NET 10.0.9 | Table5000 | 136 | 196.04 | 180.38–216.58 | 214.42 | 762,655 | 115.63 |

## Method

Release build, one process per runtime, two warmups and five measured iterations per scenario. The document model is composed before timing. Each iteration performs layout, pagination, page numbering, graphics serialization, Flate compression, font resource serialization and xref/trailer output into Stream.Null. Model construction, disk I/O and font loading are excluded. Scenarios use built-in Courier, not embedded Thai fonts. Table scenarios have eight columns and 500/5,000 body rows with repeated headers. Simple10Pages contains headings and eight wrapping paragraphs per page.

Stopwatch measures elapsed time. GC.GetAllocatedBytesForCurrentThread measures total managed allocations; collections run before each measured iteration. Tiny harness allocations are included. It does not report peak live memory. Output size is measured from a separately saved PDF. The difference in compressed bytes between .NET 8 and 10 is expected from runtime compression changes; fixed-runtime determinism passes.

*Process.GetCurrentProcess().PeakWorkingSet64 is a coarse process-lifetime high-water mark, read after saving and recounting pages. It includes runtime/native memory, previous scenarios, layout plans and transient buffers. It is neither a per-operation peak nor peak managed heap. Memory values are MiB (1,048,576 bytes).*

## Interpretation

A 5,000-row report creates 136 pages. The output path does not hold the complete serialized PDF; page plans still retain all text and geometry. Managed allocation totals remain high (~214 MiB on .NET 10 and ~227 MiB on .NET 8). Do not describe this as a proven low-allocation implementation. Next improvements should compact page/cell plans, reuse shaped runs, reduce string/operator formatting allocations and profile live heap.

Removing disabled debug rectangles and replacing per-character shaping allocations during basic measurement reduced the earlier .NET 10 5,000-row allocation total from about 411 MB to 225 MB (decimal). That is a development observation, not a fair comparison against another library. Run-to-run timing variation is substantial, especially for 500 rows; five iterations do not establish a statistically robust performance advantage.

## Reproduce

~~~sh
dotnet run --project benchmarks/NovaPdf.Benchmarks -c Release -f net10.0 -- --baseline
dotnet run --project benchmarks/NovaPdf.Benchmarks -c Release -f net8.0 -- --baseline
~~~

Raw observations: artifacts/benchmark/baseline-8.json and baseline-10.json. Environment: artifacts/benchmark/environment.json. PDFs: artifacts/benchmark/net8 and artifacts/benchmark/net10.

BenchmarkDotNet source is included behind an optional build property:

~~~sh
dotnet run --project benchmarks/NovaPdf.Benchmarks -c Release -f net10.0 -p:UseBenchmarkDotNet=true -- --bdn --filter '*' --job short
~~~

**BenchmarkDotNet was not run.** Network access was blocked and its cached dependency set lacked System.Collections.Immutable and System.Reflection.Metadata 9.0.0, with further version gaps. No dependencies were faked or downgraded to make it appear to run. The executed BCL harness remains the default so normal restore/build/test/pack work with the available offline dependencies.
