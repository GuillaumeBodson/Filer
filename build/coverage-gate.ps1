# Coverage gate (12-testing-strategy.md): fails when any gated assembly is below
# 80% line / 70% branch. Gated per assembly on every run - deterministic and
# reproducible locally, unlike changed-module diff detection. Runs on Windows
# PowerShell 5.1 and pwsh (CI uses pwsh on ubuntu).
#
# Local usage (after a coverage run):
#   dotnet test Filer.slnx -c Release --collect:"XPlat Code Coverage" --results-directory ./TestResults
#   dotnet tool run reportgenerator -reports:./TestResults/**/coverage.cobertura.xml `
#     -targetdir:./CoverageReport "-reporttypes:JsonSummary" "-classfilters:-*.Migrations.*" `
#     "-filefilters:-**/Program.cs;-**/*.g.cs"
#   ./build/coverage-gate.ps1
[CmdletBinding()]
param(
    [string]$SummaryPath = "./CoverageReport/Summary.json",
    [double]$MinLine = 80.0,
    [double]$MinBranch = 70.0,
    # De-minimis rule (12-testing-strategy.md: "DTOs/records with no logic" are
    # excluded): an assembly with fewer coverable lines than this is reported but
    # not gated - a percentage over a handful of lines is noise, and an
    # implementation module this small is essentially empty anyway.
    [int]$MinCoverableLines = 10
)

$ErrorActionPreference = 'Stop'

# Gate scope: module implementations + their Contracts + the kernels + the shared
# UI library. Not gated (by not matching): Filer.Api (thin host), Filer.ApiClient
# (generated), Filer.Web (WASM bootstrap).
$gatedPattern = '^Filer\.(Modules\..+|SharedKernel|WebKernel|Ui)$'

if (-not (Test-Path $SummaryPath)) {
    Write-Host "Coverage summary not found at '$SummaryPath'. Run the coverage report first (see header)."
    exit 1
}

$summary = Get-Content -Raw $SummaryPath | ConvertFrom-Json
$failures = @()

foreach ($assembly in $summary.coverage.assemblies) {
    if ($assembly.name -notmatch $gatedPattern) { continue }

    $line = $assembly.coverage
    $branch = $assembly.branchcoverage

    if ($assembly.coverablelines -lt $MinCoverableLines) {
        Write-Host ('{0,-45} {1,3} coverable line(s)          skip (de minimis)' -f $assembly.name, $assembly.coverablelines)
        continue
    }

    # No coverable lines/branches (e.g. a contracts assembly of pure records)
    # reports null - nothing to gate there.
    $lineOk = ($null -eq $line) -or ($line -ge $MinLine)
    $branchOk = ($null -eq $branch) -or ($branch -ge $MinBranch)

    $lineText = if ($null -eq $line) { '   n/a' } else { '{0,5:N1}%' -f $line }
    $branchText = if ($null -eq $branch) { '   n/a' } else { '{0,5:N1}%' -f $branch }
    $status = if ($lineOk -and $branchOk) { 'ok' } else { 'FAIL' }
    Write-Host ('{0,-45} line {1}  branch {2}  {3}' -f $assembly.name, $lineText, $branchText, $status)

    if (-not ($lineOk -and $branchOk)) { $failures += $assembly.name }
}

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host ("FAIL: coverage below the {0}% line / {1}% branch gate (12-testing-strategy.md): {2}" `
        -f $MinLine, $MinBranch, ($failures -join ', '))
    exit 1
}

Write-Host ''
Write-Host 'All gated assemblies meet the coverage thresholds.'
