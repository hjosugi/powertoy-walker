<#
.SYNOPSIS
Downloads powertoy-walker from GitHub Releases and installs it into PowerToys Run.

.DESCRIPTION
Each release publishes this file as install.ps1, so it runs straight from the web:

    irm https://github.com/hjosugi/powertoy-walker/releases/latest/download/install.ps1 | iex

It finds PowerToys and the architecture it was built for, downloads the matching ZIP,
checks it against the release's SHA256SUMS.txt, closes PowerToys, installs the plugin
with the Install.ps1 inside the ZIP, and starts PowerToys again. To pass options, run
it as a script block instead:

    $Url = 'https://github.com/hjosugi/powertoy-walker/releases/latest/download/install.ps1'
    & ([scriptblock]::Create((irm $Url))) -Version 0.1.1
    & ([scriptblock]::Create((irm $Url))) -Uninstall

Keep this file ASCII. GitHub serves it as application/octet-stream, and Windows
PowerShell decodes a download without a charset as Latin-1.
#>
param(
    # A release such as 0.1.1, or latest.
    [string]$Version = 'latest',
    # The package to install. Auto picks the architecture of the installed PowerToys.
    [ValidateSet('Auto', 'x64', 'ARM64')][string]$Architecture = 'Auto',
    # Removes the plugin instead of installing it.
    [switch]$Uninstall
)

# A child scope, so that nothing below changes the session that ran "irm | iex".
& {
    param([string]$Version, [string]$Architecture, [bool]$Uninstall)
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'
    if ($env:OS -ne 'Windows_NT') { throw 'powertoy-walker is a plugin for PowerToys on Windows.' }

    $Repository = 'hjosugi/powertoy-walker'
    $PluginData = Join-Path $env:LOCALAPPDATA 'Microsoft\PowerToys'
    $PluginDirectory = Join-Path $PluginData 'PowerToys Run\Plugins\powertoy-walker'
    $BackupDirectory = Join-Path $PluginData 'powertoy-walker-backups'

    # Only this sign-in session's processes: on a shared PC, other people run their own PowerToys.
    $Session = (Get-Process -Id $PID).SessionId
    function Get-PowerToysProcess([string[]]$Name = @('PowerToys', 'PowerToys.*')) {
        Get-Process -Name $Name -ErrorAction SilentlyContinue | Where-Object SessionId -EQ $Session
    }

    # Returns the folder that holds PowerToys.exe, for a per-user or a machine-wide install.
    function Find-PowerToysDirectory {
        $Candidates = [System.Collections.Generic.List[string]]::new()
        foreach ($Process in Get-PowerToysProcess 'PowerToys') {
            # The path is unreadable when PowerToys runs as administrator and this shell does not.
            try { if ($Process.Path) { $Candidates.Add((Split-Path $Process.Path -Parent)) } } catch { $null = $_ }
        }
        $Uninstallers = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*'
        foreach ($Entry in Get-ItemProperty $Uninstallers -ErrorAction SilentlyContinue) {
            if ($Entry.DisplayName -like 'PowerToys*' -and $Entry.InstallLocation) { $Candidates.Add($Entry.InstallLocation) }
        }
        $Candidates.Add((Join-Path $env:LOCALAPPDATA 'PowerToys'))
        $Candidates.Add((Join-Path $env:ProgramFiles 'PowerToys'))
        foreach ($Directory in $Candidates) {
            if (Test-Path -LiteralPath (Join-Path $Directory 'PowerToys.exe')) { return $Directory }
        }
        $null
    }

    # Reads the machine type from the PE header. PowerToys.exe is native code, built
    # separately for x64 and ARM64, and the plugin has to match the host's architecture.
    function Get-ExecutableArchitecture([string]$Path) {
        $Stream = [System.IO.File]::OpenRead($Path)
        try {
            $Reader = [System.IO.BinaryReader]::new($Stream)
            $Stream.Position = 0x3C
            $Stream.Position = $Reader.ReadInt32() + 4
            switch ($Reader.ReadUInt16()) {
                0x8664 { 'x64' }
                0xAA64 { 'ARM64' }
                default { $null }
            }
        }
        finally {
            $Stream.Dispose()
        }
    }

    # Closes every PowerToys process, and reports whether any was running.
    function Close-PowerToysApp {
        if (-not (Get-PowerToysProcess)) { return $false }
        Write-Host 'Closing PowerToys...'
        Get-PowerToysProcess | Stop-Process -Force -ErrorAction SilentlyContinue
        $Deadline = [DateTime]::UtcNow.AddSeconds(20)
        while (Get-PowerToysProcess) {
            if ([DateTime]::UtcNow -gt $Deadline) {
                throw 'Could not close PowerToys. If it runs as administrator, exit it from its system tray menu, then run this again.'
            }
            Start-Sleep -Milliseconds 250
        }
        $true
    }

    # PowerToys turns PowerToys Run off by default since Command Palette arrived, and the
    # plugin runs inside it. Call this only while PowerToys is closed: it saves this file.
    function Enable-PowerToysRun {
        $Path = Join-Path $PluginData 'settings.json'
        $Text = if (Test-Path -LiteralPath $Path) { [System.IO.File]::ReadAllText($Path) } else { '' }
        $Settings = if ($Text.Trim()) { $Text | ConvertFrom-Json } else { [pscustomobject]@{} }
        if ($Settings.enabled -and $Settings.enabled.'PowerToys Run' -eq $true) { return }
        if ($Text -match '"PowerToys Run"\s*:\s*false') {
            # Change just this value, and leave the rest of the file as PowerToys wrote it.
            $Text = $Text -replace '("PowerToys Run"\s*:\s*)false', '${1}true'
        }
        else {
            if (-not $Settings.enabled) { $Settings | Add-Member -NotePropertyName enabled -NotePropertyValue ([pscustomobject]@{}) -Force }
            $Settings.enabled | Add-Member -NotePropertyName 'PowerToys Run' -NotePropertyValue $true -Force
            $Text = $Settings | ConvertTo-Json -Depth 32 -Compress
        }
        if (($Text | ConvertFrom-Json).enabled.'PowerToys Run' -ne $true) { throw "Could not turn on PowerToys Run in $Path." }
        New-Item -ItemType Directory -Force -Path $PluginData | Out-Null
        [System.IO.File]::WriteAllText($Path, $Text)
        Write-Host 'Turned on PowerToys Run, which recent PowerToys versions leave off.'
    }

    function Open-PowerToysApp([string]$Directory) {
        Write-Host 'Starting PowerToys...'
        Start-Process -FilePath (Join-Path $Directory 'PowerToys.exe') -WorkingDirectory $Directory
    }

    function Save-Download([string]$Url, [string]$Path) {
        try {
            Invoke-WebRequest -Uri $Url -OutFile $Path -UseBasicParsing
        }
        catch {
            throw "Could not download $Url ($($_.Exception.Message)). Check that release '$Version' exists at https://github.com/$Repository/releases."
        }
    }

    $PowerToys = Find-PowerToysDirectory

    if ($Uninstall) {
        if (-not (Test-Path -LiteralPath $PluginDirectory)) {
            Write-Host 'powertoy-walker is not installed.'
            return
        }
        $WasRunning = Close-PowerToysApp
        Remove-Item -LiteralPath $PluginDirectory -Recurse -Force
        Remove-Item -LiteralPath $BackupDirectory -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "Removed $PluginDirectory"
        if ($WasRunning -and $PowerToys) { Open-PowerToysApp $PowerToys }
        return
    }

    if (-not $PowerToys) {
        throw 'PowerToys is not installed. Install it first (for example: winget install Microsoft.PowerToys --source winget), then run this again.'
    }
    if ($Architecture -eq 'Auto') {
        $Architecture = Get-ExecutableArchitecture (Join-Path $PowerToys 'PowerToys.exe')
        if (-not $Architecture) { throw 'Only the x64 and ARM64 builds of PowerToys are supported.' }
    }
    Write-Host "Found PowerToys ($Architecture) in $PowerToys"

    $BaseUrl = if ($env:POWERTOY_WALKER_DOWNLOAD_URL) {
        # Lets the tests serve a release that is not published yet.
        $env:POWERTOY_WALKER_DOWNLOAD_URL.TrimEnd('/')
    }
    elseif ($Version -eq 'latest') {
        "https://github.com/$Repository/releases/latest/download"
    }
    else {
        "https://github.com/$Repository/releases/download/v$($Version.TrimStart('v'))"
    }

    $Work = Join-Path ([System.IO.Path]::GetTempPath()) ('powertoy-walker-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $Work | Out-Null
    try {
        $Sums = Join-Path $Work 'SHA256SUMS.txt'
        Save-Download "$BaseUrl/SHA256SUMS.txt" $Sums
        $Pattern = "^(?<hash>[0-9a-f]{64}) [ *](?<name>powertoy-walker-[0-9][0-9A-Za-z.+-]*-$Architecture\.zip)$"
        $Name = $null
        foreach ($Line in Get-Content -LiteralPath $Sums) {
            if ($Line -match $Pattern) {
                $Hash = $Matches.hash
                $Name = $Matches.name
                break
            }
        }
        if (-not $Name) { throw "The release has no $Architecture package." }

        Write-Host "Downloading $Name..."
        $Zip = Join-Path $Work $Name
        Save-Download "$BaseUrl/$Name" $Zip
        if ((Get-FileHash -LiteralPath $Zip -Algorithm SHA256).Hash -ne $Hash) {
            throw "$Name does not match its SHA-256 in SHA256SUMS.txt. Nothing was installed."
        }
        Expand-Archive -LiteralPath $Zip -DestinationPath $Work -Force
        $Package = Join-Path $Work 'powertoy-walker'
        $Installed = (Get-Content -LiteralPath (Join-Path $Package 'plugin.json') -Raw | ConvertFrom-Json).Version

        Close-PowerToysApp | Out-Null
        try {
            # A script block runs whatever the execution policy, which would refuse the file itself.
            $Installer = [System.IO.File]::ReadAllText((Join-Path $Package 'Install.ps1'))
            & ([scriptblock]::Create($Installer)) -PackageDirectory $Package
            Enable-PowerToysRun
        }
        finally {
            # Bring PowerToys back even if the install failed, rather than leave it closed.
            Open-PowerToysApp $PowerToys
        }
    }
    finally {
        Remove-Item -LiteralPath $Work -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host ''
    Write-Host "powertoy-walker $Installed is ready. Open PowerToys Run (Alt+Space by default) and type w: to list your windows."
} $Version $Architecture $Uninstall.IsPresent
