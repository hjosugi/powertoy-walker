param([ValidateSet('x64', 'ARM64')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent
$Output = Join-Path $Root "dist/build/$Architecture"
$Stage = Join-Path $Root "dist/$Architecture/powertoy-walker"

& dotnet run --project "$Root/tests/Walker.Core.Tests" --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Search tests failed.' }
& dotnet build "$Root/src/PowerToyWalker" --configuration Release "-p:Platform=$Architecture" --output $Output
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }

New-Item -ItemType Directory -Force -Path "$Stage/Images" | Out-Null
# Include only our assembly and assets. Host-owned PowerToys assemblies must
# never be shipped inside a third-party plugin's folder.
Copy-Item "$Output/Community.PowerToys.Run.Plugin.Walker.dll" $Stage -Force
Copy-Item "$Root/src/PowerToyWalker/plugin.json" $Stage -Force
Copy-Item "$Root/src/PowerToyWalker/Images/walker.png" "$Stage/Images/" -Force
Copy-Item "$Root/README.md", "$Root/LICENSE" $Stage -Force
Copy-Item "$PSScriptRoot/Install.ps1" $Stage -Force
$Zip = Join-Path $Root "dist/powertoy-walker-0.1.0-$Architecture.zip"
Compress-Archive -Path $Stage -DestinationPath $Zip -Force
Write-Host "Built $Zip"
