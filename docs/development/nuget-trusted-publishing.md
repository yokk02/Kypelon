# NuGet Trusted Publishing for Kypelon

This configuration uses GitHub Actions OIDC and NuGet/login. It requires no persistent API key. No package is published on push, pull request or tag creation.

## Policy fields

| NuGet field | Value |
| --- | --- |
| Policy Name | Kypelon NuGet Publish |
| Package Owner | Konkaew |
| CI/CD Provider | GitHub Actions |
| Repository Owner | yokk02 |
| Repository | Kypelon |
| Workflow File | publish-nuget.yml |
| Environment | release |
| Push scope | Push new packages and package versions |
| Glob Patterns | Kypelon.Pdf* |

Do not select unlist/relist. The glob includes the six existing package IDs and future IDs beginning Kypelon.Pdf. The publisher itself restricts alpha.2 to exactly the six current packages.

The workflow must exist on GitHub at .github/workflows/publish-nuget.yml. Enter only the filename in NuGet. Its environment matches the publish job's environment: release. Configure this environment under GitHub Settings → Environments → release.

NuGet/login requires your individual NuGet profile username, not an email address. It defaults to Konkaew. If Konkaew is an organization/package owner rather than your login username, set the GitHub environment/repository variable NUGET_USER to your actual NuGet username. No permanent NUGET_API_KEY secret is needed.

## Manual two-stage workflow

1. Run Kypelon NuGet Publish on main with mode verify. It requests no OIDC credential and cannot push packages.
2. Verification freshly restores/builds, checks 384 passed + 1 known skip per framework, runs independent extraction and live web export checks, benchmarks all three workloads, inspects six nupkg/snupkg pairs and runs fresh package-only consumers on .NET 8/10.
3. Download and review kypelon-nuget-candidate. It contains the exact packages, PDFs, tests, benchmarks, consumer outputs and manifest.json. The run summary displays the manifest SHA-256 and run ID.
4. After the final review, run the same workflow on the same main commit with mode publish, the successful verify run ID, and the reviewed manifest SHA-256.
5. The publishing job checks workflow/run provenance, commit, evidence hashes, package hashes and dependency order before NuGet/login. It publishes those existing bytes without rebuilding an approved package.
6. A push failure or duplicate response stops dependent publication. Inspect partial-publication state before retrying. A duplicate does not prove that the existing public package is ours.
7. Successful push commands alone are not release success. The final step waits for indexing, checks public metadata and runs clean nuget.org-only consumers on both frameworks. Check the NuGet page's README rendering and symbol-indexing status afterward as well.

The engine is unchanged. scripts/package-consumer contains the existing seven-scenario alpha.2 package-only consumer, copied from ignored artifacts for CI reuse.

Windows runners are used because the real-font report regressions use installed Tahoma by default. Missing font coverage fails verification. Portable synthetic fixtures still cover text semantics. Fonts are not redistributed as source assets. Python PDF tools inspect/render output; Kypelon remains the only PDF generation engine.

The workflow installs both SDK/runtime families; installing only .NET 10 is not enough to guarantee execution of net8.0 tests. Timings from hosted runners must not be treated as directly comparable to the local performance baseline.

The repository README temporarily gained a candidate-status note after the original local review ZIP. Final package review removed that note before public publication; each new CI candidate still needs its own artifact review. The original local release-review ZIP and package bytes remain unchanged.

## Sources

- [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
- [NuGet/login](https://github.com/NuGet/login)
- [Cross-run artifact download](https://github.com/actions/download-artifact#download-artifacts-from-other-workflow-runs-or-repositories)

## Verification status

Local verification on 2026-09-23 passed: build with 0 warnings/errors; 384 passed, 0 failed and the single Thai GPOS skip on each target framework; 12 publication-helper tests; all 23 existing web-export scenarios plus invalid-request checks; 18 extraction fixtures; all five report PDFs parsed/rendered; all benchmark guards; six package/symbol pairs inspected; and all seven isolated package-consumer scenarios on each target framework. Manifest review, YAML and embedded PowerShell syntax checks passed. The local restore used the existing package cache because this agent cannot reach external network endpoints. The workflow uses the normal public restore on its hosted runner. Evidence is in artifacts/ci-release/evidence.

Workflow dispatch, OIDC exchange, actual publication and public installation require GitHub-hosted execution. Local tests do not claim those external steps ran. Run verify after pushing this commit; the local working-tree candidate is not the hosted candidate to publish.

## Windows README portability fix (2026-09-23)

Hosted verify run [35859083336](https://github.com/yokk02/Kypelon/actions/runs/35859083336) reached package inspection and failed its LF-only README heading assertion. A CRLF README reproduces the same failure locally. The inspector now normalizes CRLF to LF for the branding comparison only; it does not rewrite packaged README bytes or change artifact hashing. Wrong headings, missing signatures, missing READMEs and invalid UTF-8 still fail.

Six permanent package-inspection tests cover LF/CRLF and those rejection cases; all six and the existing twelve publication-helper tests pass. The inspector also passed against the six existing real packages and copies of all six with CRLF READMEs, with their symbol packages. The original release packages were preserved. The workflow runs both test suites and uploads verification diagnostics on failure, without presenting a failed run as an approved candidate.

No runtime source changed. This patch requires a new verify dispatch on the new main commit; rerunning the old run uses the old code. Hosted verification, OIDC and publication after this patch have not yet run.

## Consumer source isolation fix (2026-09-23)

Hosted run [35860672731](https://github.com/yokk02/Kypelon/actions/runs/35860672731) passed package inspection but stopped because the package-only consumer restore recorded more than one source. A local reproduction using the SDK's existing library-packs hook restored successfully with two sources despite --source; the old strict gate rejected that result. The exact hosted source list has not been downloaded.

The consumer now generates its own NuGet.Config, uses --configfile and a fresh --packages directory, disables implicit SDK library-packs/fallback folders, and clears additional MSBuild restore sources/fallbacks. The original six-package identity/version check and exact-one-source check remain; it additionally checks the exact config file and package folders. Local and public consumers share this isolation path. Restore config and source/cache evidence are included in the candidate/public-consumer outputs. This does not alter machine NuGet settings or library runtime code.

Five real-SDK regression tests inject an offline SDK feed, an inherited NuGet.Config, environment feeds/fallbacks and an existing cache. They verify local-only and public-only configuration, actual package provenance, rejection of an existing cache, and failure when the approved feed lacks a package that the old command found in the SDK feed/warm cache. The public-configuration test has no package reference and is not a public NuGet installation claim. All 23 release-script tests pass, and all seven real Kypelon consumer scenarios plus independent PDF validation pass on net8.0 and net10.0 with an injected extra SDK feed. The new hosted verify and public publication remain pending.

References: [dotnet restore --configfile](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-restore#options), [NuGet MSBuild restore properties](https://learn.microsoft.com/en-us/nuget/reference/msbuild-targets#restore-properties). The installed Microsoft.NET.NuGetOfflineCache.targets supplied the SDK library-packs reproduction mechanism.

## Final artifact review of hosted run 35868711606

The verify run on fce3fef passed. Local review verified all 81 package/evidence hashes, exact package metadata and dependency order, 384 passed/0 failed/1 Thai GPOS skip per framework, zero build warnings/errors, isolated seven-scenario consumers on both frameworks, and 18 source-cluster extraction fixtures. The five PDFs have 3 business, 17 table, 1 Unicode, 1 AEFS and 4 eDocket pages. Independent pypdf 6.13.1 and PyMuPDF 1.27.2.3 parsed/rendered them; visual review covered the business/eDocket/Unicode/AEFS pages and first/middle/last table pages. Known advanced Thai typography and readers that ignore ActualText remain documented limitations.

Package review found the temporary README notice saying NuGet publication was pending in all six packages. That text would be misleading in an installed public package, so it was removed and two regression cases now reject those prepublication instructions. All 25 release-script tests pass. A local repack passed all six package/symbol checks and retained identical lib DLL/XML bytes. No runtime source changed.

The downloaded run remains unchanged as historical review evidence; do not publish its older README. Dispatch verify on the README-fix commit and review that candidate's new manifest hash before publishing. OIDC/public push/public installation are still pending.
