# Publishes only the reviewed 0.2.0-alpha.2 package family. No key is written to disk.
# Without -Publish this only checks local evidence and credential presence.
param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidenceRoot = Join-Path $releaseRoot 'artifacts/release-alpha.2'
$gate = Get-Content -Raw -LiteralPath (Join-Path $evidenceRoot 'local-gates.json') | ConvertFrom-Json
if (!$gate.localGatesPassed -or $gate.version -ne '0.2.0-alpha.2') { throw 'Local release gates have not passed.' }
foreach ($file in $gate.fileHashes.PSObject.Properties) {
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $releaseRoot $file.Name)).Hash
    if ($actual.ToLowerInvariant() -ne $file.Value) { throw ('Reviewed input changed: ' + $file.Name) }
}
$packages = @{}
foreach ($package in $gate.packages) {
    $packages[$package.id] = $package
    foreach ($pair in @(@($package.package, $package.sha256), @($package.symbols, $package.symbolSha256))) {
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $releaseRoot ('artifacts/packages/' + $pair[0]))).Hash
        if ($hash.ToLowerInvariant() -ne $pair[1]) { throw ('Package changed: ' + $pair[0]) }
    }
}
# Derive the order from inspected .nuspec dependencies, then cross-check the locked order.
$order = @()
while ($order.Count -lt $packages.Count) {
    $ready = @($packages.Keys | Where-Object {
        $id = $_
        $order -notcontains $id -and @($packages[$id].dependencies | Where-Object { $order -notcontains $_ }).Count -eq 0
    } | Sort-Object)
    if (!$ready.Count) { throw 'Cyclic or missing package dependency.' }
    $order += $ready
}
if (($order -join '|') -ne ($gate.publicationOrder -join '|')) { throw 'Publication order differs from reviewed package graph.' }
$keyPresent = ![string]::IsNullOrWhiteSpace($env:NUGET_API_KEY)
Write-Output ('Local checks passed. Process NUGET_API_KEY present: ' + $keyPresent)
Write-Output ('Dependency order: ' + ($order -join ' -> '))
if (!$Publish) { Write-Output 'No publication requested by this script invocation.'; return }
if (!$keyPresent) { throw 'NUGET_API_KEY is missing. Set it outside source control in this process. No package was pushed.' }

$pushResults = @()
foreach ($id in $order) {
    $package = $packages[$id]
    $packagePath = Join-Path $releaseRoot ('artifacts/packages/' + $package.package)
    # Adjacent .snupkg files follow the NuGet CLI supported automatic symbol flow.
    # Capture and redact before any output or persistent logging.
    $savedPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = & dotnet nuget push $packagePath --api-key $env:NUGET_API_KEY --source 'https://api.nuget.org/v3/index.json' --skip-duplicate 2>&1
    $pushExit = $LASTEXITCODE
    $ErrorActionPreference = $savedPreference
    $redacted = @($output | ForEach-Object { $_.ToString().Replace($env:NUGET_API_KEY, '[REDACTED]') })
    $pushResults += [pscustomobject]@{ id=$id; version=$gate.version; exitCode=$pushExit; output=$redacted }
    $pushResults | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $evidenceRoot 'push-results.json')
    $redacted | Write-Output
    if ($pushExit -ne 0) { throw ('Publication stopped at ' + $id + '. No dependent package will be pushed.') }
    # A skipped duplicate is not evidence that the existing public bytes are ours.
    if (($redacted -join ' ') -match '(?i)already exists|conflict|duplicate') {
        throw ('Stopped after duplicate response for ' + $id + '. Verify the public package before proceeding.')
    }
}
Write-Output 'Push commands completed. Publication is NOT complete until API/indexing and fresh nuget.org-only consumer checks pass.'
