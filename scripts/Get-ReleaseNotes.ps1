# Writes the notes for a release: its section of CHANGELOG.md, filled into
# .github/release-notes.md. Without -Checksums it only checks that the section exists.
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Checksums,
    [string]$OutFile
)
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent

$Lines = @(Get-Content "$Root/CHANGELOG.md" -Encoding utf8)
$Heading = "^## \[?$([regex]::Escape($Version))\]?( |$)"
$Start = [array]::FindIndex($Lines, [Predicate[string]] { param($Line) $Line -match $Heading })
if ($Start -lt 0) { throw "CHANGELOG.md has no section for $Version. Add one headed '## $Version - YYYY-MM-DD'." }
$End = [array]::FindIndex($Lines, $Start + 1, [Predicate[string]] { param($Line) $Line -match '^## ' })
if ($End -lt 0) { $End = $Lines.Count }
$Changes = if ($End -gt $Start + 1) { ($Lines[($Start + 1)..($End - 1)] -join "`n").Trim() } else { '' }
if (-not $Changes) { throw "The CHANGELOG.md section for $Version is empty." }
if (-not $Checksums) { return }

# The PowerToys release that CI tests against.
$PowerToysVersion = (Select-String -Path "$Root/.github/workflows/ci.yml" -Pattern '^\s*POWERTOYS_TAG:\s*(\S+)').Matches[0].Groups[1].Value

$Notes = (Get-Content "$Root/.github/release-notes.md" -Raw -Encoding utf8).
Replace('{{CHANGES}}', $Changes).
Replace('{{VERSION}}', $Version).
Replace('{{POWERTOYS}}', $PowerToysVersion).
Replace('{{CHECKSUMS}}', (Get-Content $Checksums -Raw).Trim())
if ($Notes -match '\{\{\w+\}\}') { throw "Unfilled placeholder $($Matches[0]) in the release notes." }
if ($OutFile) { [System.IO.File]::WriteAllText($OutFile, $Notes) } else { $Notes }
