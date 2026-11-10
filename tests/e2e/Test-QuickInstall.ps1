# End-to-end test of the one-line install from the README. Serves a release from a local
# folder the way GitHub serves release assets, as application/octet-stream, and runs the
# documented commands in Windows PowerShell, the shell every Windows machine has.
param(
    [Parameter(Mandatory)][string]$Zip,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$PowerToysDirectory = "$env:LOCALAPPDATA\PowerToys"
)
$ErrorActionPreference = 'Stop'
$Root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$RunData = "$env:LOCALAPPDATA\Microsoft\PowerToys\PowerToys Run"
$PluginDirectory = "$RunData\Plugins\powertoy-walker"
$BackupDirectory = "$env:LOCALAPPDATA\Microsoft\PowerToys\powertoy-walker-backups"
$RunnerPath = Join-Path $PowerToysDirectory 'PowerToys.exe'
$Failures = [System.Collections.Generic.List[string]]::new()

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$Zip = (Resolve-Path $Zip).Path
$ZipName = Split-Path $Zip -Leaf
if ($ZipName -notmatch '^powertoy-walker-(?<version>.+)-(?<architecture>x64|ARM64)\.zip$') { throw "Unexpected package name $ZipName." }
$Version = $Matches.version
$Architecture = $Matches.architecture

function Wait-Until([scriptblock]$Condition, [string]$What, [int]$Seconds = 20) {
    $Deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        $Value = & $Condition
        if ($Value) { return $Value }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $Deadline)
    throw "Timed out waiting for $What."
}

function Invoke-Check([string]$Name, [scriptblock]$Body) {
    try {
        & $Body
        Write-Host "PASS  $Name"
    }
    catch {
        $Failures.Add($Name)
        Write-Host "FAIL  $Name`n      $_"
    }
}

# Lays out a release folder as the Release workflow publishes it.
function Publish-TestRelease([string]$Name, [string]$Hash) {
    $Folder = Join-Path $OutputDirectory $Name
    New-Item -ItemType Directory -Force -Path $Folder | Out-Null
    Copy-Item $Zip $Folder
    Copy-Item "$Root/scripts/QuickInstall.ps1" "$Folder/install.ps1"
    Set-Content "$Folder/SHA256SUMS.txt" "$Hash  $ZipName" -Encoding ascii
}

# Serves $OutputDirectory over HTTP from a thread of this process.
function Open-ReleaseServer {
    $Probe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $Probe.Start()
    $Port = $Probe.LocalEndpoint.Port
    $Probe.Stop()
    $Listener = [System.Net.HttpListener]::new()
    $Listener.Prefixes.Add("http://127.0.0.1:$Port/")
    $Listener.Start()
    $Job = Start-ThreadJob -ScriptBlock {
        # A thread job shares this process, so the listener arrives as the same object.
        $Http = $using:Listener
        $Directory = $using:OutputDirectory
        while ($Http.IsListening) {
            try { $Context = $Http.GetContext() } catch { break }
            $Request = $Context.Request.Url.AbsolutePath
            $Path = Join-Path $Directory ([Uri]::UnescapeDataString($Request.TrimStart('/')))
            if (Test-Path -LiteralPath $Path -PathType Leaf) {
                $Bytes = [System.IO.File]::ReadAllBytes($Path)
                $Context.Response.ContentType = 'application/octet-stream'
                $Context.Response.ContentLength64 = $Bytes.Length
                $Context.Response.OutputStream.Write($Bytes, 0, $Bytes.Length)
            }
            else {
                $Context.Response.StatusCode = 404
            }
            "$($Context.Response.StatusCode) $Request"
            $Context.Response.Close()
        }
    }
    [pscustomobject]@{ Url = "http://127.0.0.1:$Port"; Listener = $Listener; Job = $Job }
}

# Runs a command in Windows PowerShell with the installer pointed at $BaseUrl.
function Invoke-WindowsPowerShell([string]$BaseUrl, [string]$Command) {
    $env:POWERTOY_WALKER_DOWNLOAD_URL = $BaseUrl
    Write-Host "      > $Command"
    $Output = @(& powershell.exe -NoProfile -NonInteractive -Command $Command 2>&1 | ForEach-Object { "$_" })
    $ExitCode = $LASTEXITCODE
    foreach ($Line in $Output) { Write-Host "      | $Line" }
    [pscustomobject]@{ ExitCode = $ExitCode; Output = $Output -join "`n" }
}

function Get-InitializeCount {
    $Text = (Get-ChildItem "$RunData\Logs" -Recurse -Filter '*.txt' -ErrorAction SilentlyContinue |
            ForEach-Object { Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue }) -join "`n"
    ([regex]::Matches($Text, 'Total initialize cost for <powertoy-walker>')).Count
}

function Get-RunnerId { @(Get-Process -Name 'PowerToys' -ErrorAction SilentlyContinue).Id }

# Waits for PowerToys to start PowerToys Run, and for the launcher to load the plugin again.
function Assert-PluginLoadedAfter([int]$Count) {
    try {
        Wait-Until { (Get-InitializeCount) -gt $Count } 'PowerToys Run to load the plugin' -Seconds 180 | Out-Null
    }
    catch {
        $Settings = "$env:LOCALAPPDATA\Microsoft\PowerToys\settings.json"
        if (Test-Path $Settings) { Write-Host "      settings.json: $(Get-Content $Settings -Raw)" }
        Write-Host "      PowerToys processes: $((Get-Process -Name 'PowerToys*' -ErrorAction SilentlyContinue).ProcessName -join ', ')"
        throw
    }
}

Publish-TestRelease 'release' (Get-FileHash $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
Publish-TestRelease 'tampered' ('0' * 64)
$Server = Open-ReleaseServer
$Release = "$($Server.Url)/release"
$Tampered = "$($Server.Url)/tampered"
$OneLine = "irm $Release/install.ps1 | iex"
$WithOptions = "& ([scriptblock]::Create((irm $Release/install.ps1)))"

try {
    Invoke-Check 'PowerToys starts PowerToys Run with the plugin already installed' {
        $Count = Get-InitializeCount
        Start-Process $RunnerPath -WorkingDirectory $PowerToysDirectory
        Assert-PluginLoadedAfter $Count
    }

    Invoke-Check 'A package that does not match SHA256SUMS.txt is refused before PowerToys is touched' {
        $Runner = Get-RunnerId
        $Before = (Get-Item "$PluginDirectory\plugin.json").LastWriteTimeUtc
        $Result = Invoke-WindowsPowerShell $Tampered "irm $Tampered/install.ps1 | iex"
        if ($Result.ExitCode -eq 0) { throw 'The install succeeded.' }
        if ($Result.Output -notmatch 'does not match its SHA-256') { throw 'It failed for another reason.' }
        if ((Get-Item "$PluginDirectory\plugin.json").LastWriteTimeUtc -ne $Before) { throw 'The plugin changed.' }
        if ("$(Get-RunnerId)" -ne "$Runner") { throw 'PowerToys was restarted.' }
    }

    Invoke-Check 'The one-line install replaces the running plugin and restarts PowerToys' {
        $Runner = Get-RunnerId
        $Count = Get-InitializeCount
        $Backups = @(Get-ChildItem $BackupDirectory -Directory -ErrorAction SilentlyContinue).Count
        $Result = Invoke-WindowsPowerShell $Release $OneLine
        if ($Result.ExitCode -ne 0) { throw "It exited with $($Result.ExitCode)." }
        if ($Result.Output -notmatch "Found PowerToys \($Architecture\)") { throw "It did not pick $Architecture." }
        $Installed = (Get-Content "$PluginDirectory\plugin.json" -Raw | ConvertFrom-Json).Version
        if ($Installed -ne $Version) { throw "Installed $Installed instead of $Version." }
        foreach ($File in 'Community.PowerToys.Run.Plugin.Walker.dll', 'Walker.Core.dll', 'FSharp.Core.dll', 'Images\walker.png') {
            if (-not (Test-Path "$PluginDirectory\$File")) { throw "$File is missing." }
        }
        if (@(Get-ChildItem $BackupDirectory -Directory).Count -ne $Backups + 1) { throw 'The previous version was not backed up.' }
        $NewRunner = Wait-Until { Get-RunnerId } 'PowerToys to start again'
        if ("$NewRunner" -eq "$Runner") { throw 'PowerToys was not restarted.' }
        Assert-PluginLoadedAfter $Count
    }

    Invoke-Check 'The installer asks for the release named by -Version, with a v prefix or without' {
        Remove-Item Env:\POWERTOY_WALKER_DOWNLOAD_URL -ErrorAction SilentlyContinue
        $Text = Get-Content "$Root/scripts/QuickInstall.ps1" -Raw
        foreach ($Requested in '0.1.0', 'v0.1.0') {
            # A missing release fails at the download, before PowerToys is touched, and names the URL it tried.
            $Result = Invoke-WindowsPowerShell '' "& ([scriptblock]::Create([IO.File]::ReadAllText('$Root/scripts/QuickInstall.ps1'))) -Version $Requested-missing"
            if ($Result.ExitCode -eq 0) { throw 'A missing release installed.' }
            if ($Result.Output -notmatch [regex]::Escape("releases/download/v0.1.0-missing/SHA256SUMS.txt")) { throw 'It asked for another URL.' }
        }
        if ($Text -notmatch 'releases/latest/download') { throw 'The default is not the latest release.' }
    }

    Invoke-Check '-Uninstall removes the plugin and its backups, and restarts PowerToys' {
        $Runner = Get-RunnerId
        $Result = Invoke-WindowsPowerShell $Release "$WithOptions -Uninstall"
        if ($Result.ExitCode -ne 0) { throw "It exited with $($Result.ExitCode)." }
        if (Test-Path $PluginDirectory) { throw 'The plugin is still installed.' }
        if (Test-Path $BackupDirectory) { throw 'The backups are still there.' }
        $NewRunner = Wait-Until { Get-RunnerId } 'PowerToys to start again'
        if ("$NewRunner" -eq "$Runner") { throw 'PowerToys was not restarted.' }
    }

    Invoke-Check 'The one-line install works on a machine without the plugin' {
        $Count = Get-InitializeCount
        $Result = Invoke-WindowsPowerShell $Release $OneLine
        if ($Result.ExitCode -ne 0) { throw "It exited with $($Result.ExitCode)." }
        if ($Result.Output -notmatch "powertoy-walker $([regex]::Escape($Version)) is ready") { throw 'It did not report the version.' }
        Assert-PluginLoadedAfter $Count
    }
}
finally {
    $Server.Listener.Stop()
    $Server.Job | Wait-Job -Timeout 10 | Receive-Job | Set-Content (Join-Path $OutputDirectory 'requests.txt')
    Remove-Item Env:\POWERTOY_WALKER_DOWNLOAD_URL -ErrorAction SilentlyContinue
    Get-Process -Name 'PowerToys*' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Copy-Item "$RunData\Logs" (Join-Path $OutputDirectory 'logs') -Recurse -Force -ErrorAction SilentlyContinue
}

if ($Failures.Count -gt 0) {
    throw "$($Failures.Count) quick install check(s) failed: $($Failures -join '; ')"
}
Write-Host 'ALL QUICK INSTALL CHECKS PASSED'
