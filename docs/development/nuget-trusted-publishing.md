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

The repository README gained a candidate-status note after the original local review ZIP; CI packages need their own final artifact review. The original local release-review ZIP and package bytes remain unchanged.

## Sources

- [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
- [NuGet/login](https://github.com/NuGet/login)
- [Cross-run artifact download](https://github.com/actions/download-artifact#download-artifacts-from-other-workflow-runs-or-repositories)

## Verification status

Local verification on 2026-09-23 passed: build with 0 warnings/errors; 384 passed, 0 failed and the single Thai GPOS skip on each target framework; 12 publication-helper tests; all 23 existing web-export scenarios plus invalid-request checks; 18 extraction fixtures; all five report PDFs parsed/rendered; all benchmark guards; six package/symbol pairs inspected; and all seven isolated package-consumer scenarios on each target framework. Manifest review, YAML and embedded PowerShell syntax checks passed. The local restore used the existing package cache because this agent cannot reach external network endpoints. The workflow uses the normal public restore on its hosted runner. Evidence is in artifacts/ci-release/evidence.

Workflow dispatch, OIDC exchange, actual publication and public installation require GitHub-hosted execution. Local tests do not claim those external steps ran. Run verify after pushing this commit; the local working-tree candidate is not the hosted candidate to publish.
