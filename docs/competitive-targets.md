# Competitive targets: aspirations, not claims

No QuestPDF/iText/PDFsharp performance or quality comparison was run. Kypelon.Pdf has no demonstrated competitive win from this workstream.

| Goal | Measurement needed to establish success |
|---|---|
| Simple API | Independent developers implement the same report; compare lines of application code, completion time, compile errors and discoverability feedback. |
| Faster generation | Same fonts, rows, styles, compression and output requirements; isolated BenchmarkDotNet jobs, warmup, several launches, hardware/runtime disclosure and confidence intervals. |
| Lower memory | Equivalent 5,000-row report; managed allocation totals plus sampled peak live heap/private bytes. Allocation totals alone are not peak memory. |
| Streaming | Non-seekable destination tests; first output before all page bytes are rendered; no whole-document byte array except explicit ToArray. Measure retained page-plan growth separately. |
| Excellent tables | Every row exactly once, repeated headers, predictable widths/heights, no clipping; later add row fragmentation, spans and nested-table stress fixtures. |
| Excellent Thai | Native-language typography review, shaping golden tests against authoritative reference runs, exact extraction, dictionary breaks and mixed-language regressions. Currently incomplete. |
| Useful diagnostics | Time developers need to locate/fix deliberate overflow defects; actionable element IDs and geometry; no debug output when disabled. |
| No mandatory commercial PDF runtime | Dependency and license audit of every shipping package. Runtime currently uses the BCL and ASP.NET shared framework only. |
| Predictable pagination | Fixed-runtime deterministic byte tests plus semantic page/row inventories across versions. Document intended layout changes in release notes. |

The current performance document is an internal baseline and explicitly records environmental constraints.
