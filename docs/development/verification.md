> Historical evidence for Kypelon (formerly NovaPdf during pre-public alpha development). This report records the original names/results; use README.md and docs/development/kypelon-brand-migration.md for current commands and verification.

# Verification and environment

## Current release: 0.2.0-alpha.1

The source-cluster text pipeline was built, tested and packed for both targets: **317 passed, 0 failed, 1 intentionally skipped per framework**, zero build warnings/errors. Contextual fitting, retained prepared runs, ActualText-aware extraction, all existing web exports and package-only consumers passed. See [the v0.2 verification report](text-pipeline-v0.2-verification.md) for migration, measured performance costs, reader limitations and exact artifacts.

## Historical patch: 0.1.0-alpha.2

The correctness/hardening patch was built, tested and packed on both targets: **218 passed, 0 failed, 1 intentionally skipped per framework**, with **zero build warnings/errors**. Package-only consumers and independent PDF/web regressions passed. See [the alpha.2 verification report](hardening-alpha.2.md) for reproduction, allocation numbers, exact package paths and evidence.

The sections below preserve the original alpha.1 foundation run; its 82-test count predates later template tests and this patch.

## Historical foundation environment

This release was developed from an empty workspace. Existing user files were not replaced. PowerShell script execution policy also prevented running scripts/verify.ps1 here, so its individual dotnet commands were run directly. The normal shell execution helper failed at startup; local commands were executed through the available Node child-process tool with an explicit environment. No permission escalation was used.

## Repeatable checks

~~~sh
dotnet restore NovaPdf.sln
dotnet build NovaPdf.sln -c Release --no-restore
dotnet test NovaPdf.sln -c Release --no-build --logger trx --results-directory artifacts/tests
dotnet run --project samples/NovaPdf.Sample.Console -c Release -f net10.0 -- artifacts/pdf /path/to/ThaiFont.ttf
python scripts/validate-pdfs.py
dotnet run --project benchmarks/NovaPdf.Benchmarks -c Release -f net10.0 -- --baseline
dotnet run --project benchmarks/NovaPdf.Benchmarks -c Release -f net8.0 -- --baseline
dotnet pack NovaPdf.sln -c Release --no-build -o artifacts/packages
~~~

Set NOVAPDF_TEST_FONT for the installed-font integration test on non-Windows systems. The in-memory sfnt fixture is generated from original test code and is not a production font.

## Environment constraints

- .NET SDK 10.0.301; runtimes 8.0.28 and 10.0.9. Both target frameworks built and tested.
- Network socket access to NuGet and font-download hosts was denied. Existing cached xUnit packages were used through an offline restore source: C:/Users/kstiw/.nuget/packages. This machine-specific path is not hard-coded into project configuration.
- NuGet vulnerability auditing could not access its online source during offline restore; it was disabled for that command only. No online vulnerability audit is claimed.
- BenchmarkDotNet 0.15.8 was cached, but required System.Collections.Immutable/Reflection.Metadata 9.0.0 packages were absent, with additional dependency-version gaps. Its optional path was not executed. The BCL harness was executed on both runtimes.
- qpdf, pdfinfo, pdftotext, mutool and Ghostscript executables were not found on PATH. No results from those tools are claimed.
- Available Python pypdf and PyMuPDF independently read/extracted/rendered the files. PyMuPDF's engine was used solely as a validator, never for PDF generation. Neither Python package is a NovaPdf runtime or NuGet dependency.

## Historical alpha.1 foundation coverage

Foundation-run result: **82 passed, 0 failed, 1 intentionally skipped per target framework** (166 total test cases across the two runs). Release build completed with **zero warnings and zero errors**. All six runtime packages and six symbol packages were generated; a separate consumer restored from the local feed and generated PDFs on both target frameworks.

The consolidated xUnit project covers culture-invariant PDF numbers, escaping/names, arrays/dictionaries, stream lengths, Flate round-trips, xref offsets, object reservations, page tree/trailer/metadata, graphics/state/transform operators, font metrics/cmaps/embedding restrictions/invalid inputs, ToUnicode aliases/surrogates, text measurement/wrapping, element geometry, paragraph and table pagination, repeated headers/page numbers, exact row preservation, debug isolation, deterministic output, async-only non-seekable destinations, ownership/cancellation and ASP.NET response headers.

A contextual Thai mark-positioning fixture is intentionally skipped with an explicit reason. This is tracked missing behavior, not passing coverage. Independent Python integration checks verify actual extraction of the required Thai corpus and all 500 employee rows, page headers/counts and PNG masks; every generated page is rendered and files requiring MuPDF repair fail validation.

The ASP.NET sample was launched on 127.0.0.1:5077. GET /report returned HTTP 200, application/pdf, an attachment filename and a parsable PDF. The process was then stopped. The response body was also tested using a stream that rejects all synchronous I/O.

Evidence lives in artifacts/tests, artifacts/validation, artifacts/pdf and artifacts/benchmark. Those generated files are intentionally ignored by Git.

## Review conclusions

- No whole-document serialization buffer exists in Write/WriteAsync; ToArray is the explicit exception.
- Production planning avoids diagnostic rectangle allocations. Plans still retain all arranged text/geometry, so memory grows with content.
- Literal/name escaping, Unicode maps, stream boundaries and xrefs have independent assertions. Fonts/images enforce bounded metadata access.
- Rows never split silently or disappear; impossible rows fail before any PDF bytes are emitted by high-level writing.
- Async destination I/O is genuine; compression and layout remain CPU-synchronous.
- XML documentation warnings are errors for all runtime projects. No blanket warning suppression is used.
- Packaging was smoke-tested from the local feed, including transitive packages and both TFMs.
- Remaining risks: incomplete shaping, mutable public graph APIs, limited advanced layout, high managed allocation totals, no exhaustive font-outline validation, no sustained fuzzing or PDF conformance certification.
