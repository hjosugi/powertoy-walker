param([ValidateSet('x64', 'ARM64')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent
$Output = Join-Path $Root "dist/build/$Architecture"
$Stage = Join-Path $Root "dist/$Architecture/powertoy-walker"
$Version = (Get-Content "$Root/src/PowerToyWalker/plugin.json" -Raw | ConvertFrom-Json).Version

& dotnet run --project "$Root/tests/Walker.Core.Tests" --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Search tests failed.' }
& dotnet build "$Root/src/PowerToyWalker" --configuration Release "-p:Platform=$Architecture" "-p:Version=$Version" --output $Output
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }

# Start from an empty folder, so nothing left by an earlier build reaches the ZIP.
if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path "$Stage/Images" | Out-Null
# Include only our assemblies and assets. Host-owned PowerToys assemblies must
# never be shipped inside a third-party plugin's folder. Leaving out the .deps.json
# makes the host's load context resolve dependencies from this folder's DLLs alone.
foreach ($Assembly in 'Community.PowerToys.Run.Plugin.Walker', 'Walker.Core', 'FSharp.Core') {
    Copy-Item "$Output/$Assembly.dll" $Stage -Force
}
Copy-Item "$Root/src/PowerToyWalker/plugin.json" $Stage -Force
Copy-Item "$Root/src/PowerToyWalker/Images/walker.png" "$Stage/Images/" -Force
Copy-Item "$Root/README.md", "$Root/LICENSE", "$Root/THIRD-PARTY-NOTICES.txt" $Stage -Force
Copy-Item "$PSScriptRoot/Install.ps1" $Stage -Force
$Zip = Join-Path $Root "dist/powertoy-walker-$Version-$Architecture.zip"
Compress-Archive -Path $Stage -DestinationPath $Zip -Force
Write-Host "Built $Zip"
