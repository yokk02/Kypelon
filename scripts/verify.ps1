param([string]$ThaiFont, [string]$NuGetSource)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
if ($ThaiFont) { $env:KYPELON_TEST_FONT = (Resolve-Path -LiteralPath $ThaiFont).Path }
$restoreArgs = @('restore', 'Kypelon.sln')
if ($NuGetSource) { $restoreArgs += @('--source', $NuGetSource, '-p:NuGetAudit=false') }
& dotnet @restoreArgs
if ($LASTEXITCODE) { throw 'Restore failed.' }
& dotnet build Kypelon.sln -c Release --no-restore
if ($LASTEXITCODE) { throw 'Build failed.' }
& dotnet test Kypelon.sln -c Release --no-build --logger trx --results-directory artifacts/tests
if ($LASTEXITCODE) { throw 'Tests failed.' }
& dotnet run --project samples/Kypelon.Pdf.Sample.Console -c Release -f net10.0 --no-build -- artifacts/pdf
if ($LASTEXITCODE) { throw 'Sample generation failed.' }
& dotnet pack Kypelon.sln -c Release --no-build --no-restore -o artifacts/packages
if ($LASTEXITCODE) { throw 'Packaging failed.' }
Write-Output 'Build, tests, samples and packages succeeded. Optional: python scripts/validate-pdfs.py and the benchmark commands in README.md.'
