# Checks formatting and lints everything, without changing any file.
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent
$AnalyzerVersion = '1.25.0'
$Failures = [System.Collections.Generic.List[string]]::new()

Push-Location $Root
try {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore the .NET tools.' }

    & dotnet fantomas --check src tests
    if ($LASTEXITCODE -ne 0) { $Failures.Add('Formatting: run "dotnet fantomas src tests".') }

    foreach ($Project in 'src/Walker.Core', 'src/PowerToyWalker', 'tests/Walker.Core.Tests') {
        & dotnet dotnet-fsharplint lint "$Project/$(Split-Path $Project -Leaf).fsproj"
        if ($LASTEXITCODE -ne 0) { $Failures.Add("FSharpLint: $Project") }
    }

    # Windows PowerShell decodes an "irm" download without a charset as Latin-1.
    if ((Get-Content scripts/QuickInstall.ps1 -Raw) -match '[^\x00-\x7F]') {
        $Failures.Add('scripts/QuickInstall.ps1 must stay ASCII: it runs as "irm ... | iex".')
    }

    if (-not (Get-Module -ListAvailable PSScriptAnalyzer | Where-Object Version -EQ $AnalyzerVersion)) {
        Install-Module PSScriptAnalyzer -RequiredVersion $AnalyzerVersion -Scope CurrentUser -Force
    }
    Import-Module PSScriptAnalyzer -RequiredVersion $AnalyzerVersion
    $Findings = Get-ChildItem scripts, tests -Recurse -Filter '*.ps1' |
        ForEach-Object { Invoke-ScriptAnalyzer -Path $_.FullName -Settings ./PSScriptAnalyzerSettings.psd1 }
    if ($Findings) {
        $Findings | Format-Table RuleName, ScriptName, Line, Message -Wrap | Out-Host
        $Failures.Add('PSScriptAnalyzer')
    }
}
finally {
    Pop-Location
}

if ($Failures.Count -gt 0) { throw "Lint failed:`n$($Failures -join "`n")" }
Write-Host 'Lint passed.'
