# End-to-end test against a real PowerToys Run on a Windows desktop session. Installs the
# packaged plugin with its own Install.ps1, then uses the launcher as a person would:
# summon it, type a query, read the results, and press keys. -Language picks the language
# that PowerToys is set to, and the plugin's text is checked in that language.
param(
    [Parameter(Mandatory)][string]$Zip,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('en-US', 'ja-JP')][string]$Language = 'en-US',
    [string]$LauncherPath = "$env:LOCALAPPDATA\PowerToys\PowerToys.PowerLauncher.exe"
)
$ErrorActionPreference = 'Stop'
$InvokeEvent = 'Local\PowerToysRunInvokeEvent-30f26ad7-d36d-4c0e-ab02-68bb5ff3c4ab'
$ExitEvent = 'Local\PowerToysRunExitEvent-3e38e49d-a762-4ef1-88f2-fd4bc7481516'
$PluginTag = '(powertoy-walker)'
$RunData = "$env:LOCALAPPDATA\Microsoft\PowerToys\PowerToys Run"
# The text the plugin shows. It lives in a UTF-8 JSON file, so this script stays ASCII.
$Text = (Get-Content "$PSScriptRoot/expected-text.json" -Raw -Encoding utf8 | ConvertFrom-Json).$Language
$CloseItem = "$($Text.closeWindow) $PluginTag"
$Failures = [System.Collections.Generic.List[string]]::new()

Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type -Namespace E2E -Name Win32 -MemberDefinition @'
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
[DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
'@

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function Save-Screenshot([string]$Name) {
    try {
        $Bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
        $Bitmap = [System.Drawing.Bitmap]::new($Bounds.Width, $Bounds.Height)
        $Graphics = [System.Drawing.Graphics]::FromImage($Bitmap)
        $Graphics.CopyFromScreen($Bounds.Location, [System.Drawing.Point]::Empty, $Bounds.Size)
        $Bitmap.Save((Join-Path $OutputDirectory "$Name.png"))
        $Graphics.Dispose()
        $Bitmap.Dispose()
    }
    catch {
        Write-Warning "Screenshot $Name failed: $_"
    }
}

function Wait-Until([scriptblock]$Condition, [string]$What, [int]$Seconds = 20) {
    $Deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        $Value = & $Condition
        if ($Value) { return $Value }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $Deadline)
    throw "Timed out waiting for $What."
}

# Runs one check; a failure is recorded, and the remaining checks still run.
function Invoke-Check([string]$Name, [scriptblock]$Body) {
    $Slug = $Name -replace '[^A-Za-z0-9]+', '-'
    try {
        & $Body
        Write-Host "PASS  $Name"
    }
    catch {
        $Failures.Add($Name)
        Write-Host "FAIL  $Name`n      $_"
    }
    Save-Screenshot $Slug
}

# Builds a console-free GUI program. A console would open in Windows Terminal, whose
# window belongs to another process and could not stand in for an app's own window.
function Build-TestWindowProgram {
    $Program = Join-Path ([IO.Path]::GetTempPath()) 'WalkerE2EWindow.exe'
    $Source = 'public static class Program { [System.STAThread] public static void Main(string[] args) { System.Windows.Forms.Application.Run(new System.Windows.Forms.Form { Text = args[0] }); } }'
    & powershell.exe -NoProfile -Command "Add-Type -TypeDefinition '$Source' -OutputAssembly '$Program' -OutputType WindowsApplication -ReferencedAssemblies System.Windows.Forms"
    if (-not (Test-Path $Program)) { throw 'Could not build the test window program.' }
    $Program
}

function Open-TestWindow([string]$Title) {
    $Process = Start-Process $TestWindowProgram -ArgumentList $Title -PassThru
    $Handle = Wait-Until {
        $Process.Refresh()
        $Process.MainWindowHandle.ToInt64()
    } "the window $Title"
    [pscustomobject]@{ Title = $Title; Process = $Process; Handle = $Handle }
}

function Invoke-NamedEvent([string]$Name) {
    $NamedEvent = [System.Threading.EventWaitHandle]::OpenExisting($Name)
    try { $NamedEvent.Set() | Out-Null } finally { $NamedEvent.Dispose() }
}

function Get-ForegroundHandle { [E2E.Win32]::GetForegroundWindow().ToInt64() }

# Compares processes, because the launcher's window is owned and so is never a MainWindowHandle.
function Test-LauncherForeground {
    $ProcessId = [uint32]0
    [E2E.Win32]::GetWindowThreadProcessId([E2E.Win32]::GetForegroundWindow(), [ref]$ProcessId) | Out-Null
    $ProcessId -eq $Launcher.Id
}

function Send-KeyChord([byte[]]$Keys) {
    $KeyUp = 2
    foreach ($Key in $Keys) { [E2E.Win32]::keybd_event($Key, 0, 0, [UIntPtr]::Zero) }
    [array]::Reverse($Keys)
    foreach ($Key in $Keys) { [E2E.Win32]::keybd_event($Key, 0, $KeyUp, [UIntPtr]::Zero) }
}

function Test-NamedEvent([string]$Name) {
    $NamedEvent = $null
    $Exists = [System.Threading.EventWaitHandle]::TryOpenExisting($Name, [ref]$NamedEvent)
    if ($NamedEvent) { $NamedEvent.Dispose() }
    $Exists
}

# The names of the launcher's list items: results, and the context menu of the selected one.
function Get-ListItemName {
    $Root = [System.Windows.Automation.AutomationElement]::RootElement
    $IsLauncher = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $Launcher.Id)
    $IsItem = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    foreach ($Window in $Root.FindAll([System.Windows.Automation.TreeScope]::Children, $IsLauncher)) {
        foreach ($Item in $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $IsItem)) {
            $Item.Current.Name
        }
    }
}

function Get-ResultName { Get-ListItemName | Where-Object { $_ -ne $CloseItem } }

# Summons the launcher if it is hidden and replaces its query. The launcher is left open.
function Send-Query([string]$Query) {
    # Enter moves the focus before the launcher hides, so a hotkey sent right after it
    # only finishes hiding the launcher. Keep pressing until the launcher is in front.
    Wait-Until {
        if (-not (Test-LauncherForeground)) {
            Send-KeyChord 0x12, 0x20 # Alt+Space
            Start-Sleep -Seconds 2
        }
        Test-LauncherForeground
    } 'the launcher to take the foreground' | Out-Null
    [System.Windows.Forms.SendKeys]::SendWait('^a{DEL}')
    [System.Windows.Forms.SendKeys]::SendWait($Query)
}

# Sends a query, then waits until $IsDone holds for the results. Another window can take the
# foreground while the keys are typed (on the runners, a WSL update prompt or the runner's own
# console), which hides the launcher and loses the query, so it is sent again every 5 seconds.
# Script blocks see the variables of their callers, so these names must not repeat Wait-Until's.
function Wait-QueryResult([string]$Query, [scriptblock]$IsDone, [string]$Expected) {
    $Retry = @{ At = [DateTime]::UtcNow.AddSeconds(5) }
    Send-Query $Query
    Wait-Until {
        if (& $IsDone) { return $true }
        if ([DateTime]::UtcNow -gt $Retry.At) {
            Send-Query $Query
            $Retry.At = [DateTime]::UtcNow.AddSeconds(5)
        }
        $false
    } $Expected -Seconds 30
}

# Sends a query, then returns once a result satisfies $Matcher.
function Search-Launcher([string]$Query, [scriptblock]$Matcher) {
    try {
        $Found = { @(Get-ResultName | Where-Object $Matcher).Count -gt 0 }
        Wait-QueryResult -Query $Query -IsDone $Found -Expected "a matching result for '$Query'" | Out-Null
    }
    finally {
        Write-Host "      '$Query' shows: $(@(Get-ResultName) -join ' | ')"
    }
}

function Assert-Foreground($Window) {
    Wait-Until { (Get-ForegroundHandle) -eq $Window.Handle } "$($Window.Title) to take the foreground" | Out-Null
}

$Package = Join-Path $OutputDirectory 'package'
Expand-Archive -LiteralPath $Zip -DestinationPath $Package -Force
$Extracted = Join-Path $Package 'powertoy-walker'

Invoke-Check 'The ZIP holds exactly the expected files' {
    $Expected = 'Community.PowerToys.Run.Plugin.Walker.dll', 'FSharp.Core.dll', 'Images\walker.png', 'Install.ps1',
        'LICENSE', 'README.ja.md', 'README.md', 'THIRD-PARTY-NOTICES.txt', 'Walker.Core.dll', 'plugin.json' | Sort-Object
    $Actual = Get-ChildItem $Extracted -Recurse -File |
        ForEach-Object { $_.FullName.Substring($Extracted.Length + 1) } | Sort-Object
    if (($Expected -join ',') -ne ($Actual -join ',')) { throw "Found: $($Actual -join ', ')" }
}

# The setup may have started PowerToys, and Install.ps1 rightly refuses while it runs.
Get-Process -Name 'PowerToys*' -ErrorAction SilentlyContinue | Stop-Process -Force
Wait-Until { -not (Get-Process -Name 'PowerToys*' -ErrorAction SilentlyContinue) } 'PowerToys to exit' | Out-Null

Invoke-Check 'Install.ps1 installs the package' {
    # The command from the README, which has to work under Windows PowerShell's default policy.
    & powershell.exe -ExecutionPolicy Bypass -File (Join-Path $Extracted 'Install.ps1')
    if ($LASTEXITCODE -ne 0) { throw "Install.ps1 exited with $LASTEXITCODE." }
    if (-not (Test-Path "$RunData\Plugins\powertoy-walker\FSharp.Core.dll")) { throw 'FSharp.Core.dll was not installed.' }
}

# A fresh Windows 11 image may still show its first-run screens, which cover the whole desktop.
Get-Process | Select-Object -ExpandProperty ProcessName -Unique | Sort-Object |
    Set-Content (Join-Path $OutputDirectory 'processes.txt')
Stop-Process -Name 'WWAHost', 'CloudExperienceHost', 'CloudExperienceHostBroker', 'UserOOBEBroker', 'msoobe' -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3
Send-KeyChord 0x1B # Escape, for the Start menu that can open behind them

$TestWindowProgram = Build-TestWindowProgram
$Alpha = Open-TestWindow 'walker-e2e-alpha'
$Beta = Open-TestWindow 'walker-e2e-beta'

# PowerToys keeps the language picked in its settings in this file. Without it, PowerToys
# follows the Windows display language, which is English on the runners.
$LanguageFile = "$env:LOCALAPPDATA\Microsoft\PowerToys\language.json"
$SavedLanguage = if (Test-Path $LanguageFile) { Get-Content $LanguageFile -Raw }
if ($Language -eq 'en-US') { Remove-Item $LanguageFile -ErrorAction SilentlyContinue }
else { Set-Content $LanguageFile (@{ language = $Language } | ConvertTo-Json -Compress) }

$Launcher = Start-Process $LauncherPath -PassThru

function Get-LogText {
    (Get-ChildItem "$RunData\Logs" -Recurse -Filter '*.txt' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue }) -join "`n"
}

try {
    Invoke-Check 'PowerToys Run loads and initializes the plugin' {
        Wait-Until -Condition { (Get-LogText) -match 'Total initialize cost for <powertoy-walker>' } -What 'the plugin to initialize' -Seconds 90 | Out-Null
    }

    # The host blocks startup until every plugin has initialized, and its Program
    # plugin takes minutes to index a CI image. Its hotkey event appears afterwards.
    Invoke-Check 'PowerToys Run finishes starting with the plugin installed' {
        $Started = [DateTime]::UtcNow
        Wait-Until -Condition { Test-NamedEvent $InvokeEvent } -What 'the launcher to finish starting' -Seconds 420 | Out-Null
        Write-Host "      ready after $([int]([DateTime]::UtcNow - $Started).TotalSeconds) s"
    }

    Invoke-Check "The host publishes the six settings with their defaults, described in $Language" {
        $Settings = Get-Content "$RunData\settings.json" -Raw -Encoding utf8 | ConvertFrom-Json
        $Plugin = $Settings.plugins | Where-Object Name -EQ 'powertoy-walker'
        $Options = $Plugin.AdditionalOptions
        $Actual = ($Options | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ','
        $Expected = 'PreferWindowsInGlobalSearch=True,MatchTitlesInGlobalSearch=False,CurrentWindowLast=True,IncludeMinimized=True,AllDesktops=True,FuzzyMatching=True'
        if ($Actual -ne $Expected) { throw "Found: $Actual" }
        if ($Plugin.Description -ne $Text.description) { throw "The description is: $($Plugin.Description)" }
        $Label = ($Options | Where-Object Key -EQ 'FuzzyMatching').DisplayLabel
        if ($Label -ne $Text.fuzzyMatchingLabel) { throw "The FuzzyMatching label is: $Label" }
    }

    Invoke-Check 'A keyword query lists only the windows that match it' {
        # Letters in order anywhere in a title used to match too: e, 2, e in C:\Windows\system32\wsl.exe.
        try {
            $OnlyMatches = {
                $Names = @(Get-ResultName)
                @($Names -match '^walker-e2e-(alpha|beta) : ').Count -eq 2 -and @($Names -notmatch 'e2e').Count -eq 0
            }
            Wait-QueryResult -Query 'w: e2e' -IsDone $OnlyMatches -Expected 'both test windows, and no window without e2e in its name' | Out-Null
        }
        finally {
            Write-Host "      'w: e2e' shows: $(@(Get-ResultName) -join ' | ')"
        }
    }

    Invoke-Check 'A keyword query lists a window by title, tagged in the subtitle' {
        Search-Launcher 'w: walker-e2e-alpha' { $_ -match 'walker-e2e-alpha' -and $_ -match [regex]::Escape($PluginTag) }
    }

    Invoke-Check 'Enter switches to the window' {
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        Assert-Foreground $Alpha
    }

    Invoke-Check 'An empty keyword query lists the window you came from last, and Enter acts like Alt+Tab' {
        # Visit both in turn, so beta is the most recent window other than the one we come from.
        foreach ($Window in $Beta, $Alpha) {
            $Title = $Window.Title
            Search-Launcher "w: $Title" { $_ -match $Title }
            [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
            Assert-Foreground $Window
        }
        Search-Launcher 'w:' { $_ -match 'walker-e2e-beta' }
        $Names = @(Get-ResultName)
        if ($Names[0] -notmatch 'walker-e2e-beta') { throw "The previous window is not first: $($Names -join ' | ')" }
        $AlphaIndex = [array]::FindIndex($Names, [Predicate[string]] { param($Name) $Name -match 'walker-e2e-alpha' })
        if ($AlphaIndex -ge 0 -and $AlphaIndex -ne $Names.Count - 1) { throw "The window we came from is not last: $($Names -join ' | ')" }
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        Assert-Foreground $Beta
    }

    Invoke-Check "Enter restores a minimized window and marks it in the subtitle, in $Language" {
        [E2E.Win32]::ShowWindow([IntPtr]$Alpha.Handle, 6) | Out-Null # SW_MINIMIZE
        Wait-Until { [E2E.Win32]::IsIconic([IntPtr]$Alpha.Handle) } 'the window to minimize' | Out-Null
        Search-Launcher 'w: walker-e2e-alpha' { $_ -match 'walker-e2e-alpha' -and $_.Contains($Text.minimized) }
        Save-Screenshot 'minimized-result'
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        Assert-Foreground $Alpha
        if ([E2E.Win32]::IsIconic([IntPtr]$Alpha.Handle)) { throw 'The window stayed minimized.' }
    }

    Invoke-Check 'A global query finds windows by app name without the keyword' {
        Search-Launcher 'WalkerE2EWindow' { $_ -match 'walker-e2e' -and $_ -match [regex]::Escape($PluginTag) }
    }

    # The host matches an action keyword as a prefix and then skips all global plugins, so a
    # keyword of just "w" would take over everything typed that starts with that letter.
    Invoke-Check 'Typing that starts like the keyword still reaches the other plugins' {
        Search-Launcher 'walker-e2e-beta' { $_ -match 'walker-e2e-beta' -and $_ -notmatch [regex]::Escape($PluginTag) }
    }

    Invoke-Check 'A setting changed in settings.json applies while the launcher runs' {
        $IsOurs = { $_ -match 'walker-e2e-beta' -and $_ -match [regex]::Escape($PluginTag) }
        Send-Query 'walker-e2e-beta'
        Start-Sleep -Seconds 3
        if (@(Get-ResultName | Where-Object $IsOurs).Count -gt 0) {
            throw 'A global query matched a title while MatchTitlesInGlobalSearch was off.'
        }
        $Settings = Get-Content "$RunData\settings.json" -Raw | ConvertFrom-Json
        $Plugin = $Settings.plugins | Where-Object Name -EQ 'powertoy-walker'
        ($Plugin.AdditionalOptions | Where-Object Key -EQ 'MatchTitlesInGlobalSearch').Value = $true
        $Settings | ConvertTo-Json -Depth 20 | Set-Content "$RunData\settings.json"
        Wait-Until {
            Send-Query 'walker-e2e-beta'
            Start-Sleep -Seconds 1
            @(Get-ResultName | Where-Object $IsOurs).Count -gt 0
        } 'the setting to take effect' | Out-Null
    }

    Invoke-Check "The selected window offers a Close window button, in $Language" {
        Search-Launcher 'w: walker-e2e-alpha' { $_ -match 'walker-e2e-alpha' }
        try {
            Wait-Until { @(Get-ListItemName) -contains $CloseItem } "the button '$CloseItem'" | Out-Null
        }
        finally {
            Write-Host "      The list items are: $(@(Get-ListItemName) -join ' | ')"
        }
    }

    Invoke-Check 'Ctrl+W asks the selected window to close' {
        Search-Launcher 'w: walker-e2e-alpha' { $_ -match 'walker-e2e-alpha' }
        [System.Windows.Forms.SendKeys]::SendWait('^w')
        Wait-Until { $Alpha.Process.HasExited } 'the window to close' | Out-Null
    }

    Invoke-Check "A window on another virtual desktop is marked, in $Language, and Enter switches to it" {
        Send-KeyChord 0x5B, 0x11, 0x44 # Win+Ctrl+D creates and enters a new desktop
        Start-Sleep -Seconds 2
        Search-Launcher 'w: walker-e2e-beta' { $_ -match 'walker-e2e-beta' -and $_.Contains($Text.otherDesktop) }
        Save-Screenshot 'other-desktop-result'
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        Assert-Foreground $Beta
    }

    Invoke-Check 'PowerToys Run keeps running after all of the above' {
        if ($Launcher.HasExited) { throw "It exited with $($Launcher.ExitCode)." }
    }

    Invoke-Check 'PowerToys Run exits cleanly, disposing the plugin' {
        Invoke-NamedEvent $ExitEvent
        Wait-Until -Condition { $Launcher.HasExited } -What 'the launcher to exit' -Seconds 30 | Out-Null
    }

    Invoke-Check 'The host logged no errors for the plugin' {
        $Entries = (Get-LogText) -split '(?m)^(?=\[\d{4}-\d{2}-\d{2} )'
        $Errors = $Entries | Where-Object {
            $_ -match '^\[[^\]]+\] \[(ERROR|FATAL)\]' -and $_ -match 'powertoy-walker|PowerToyWalker|Plugin\.Walker|FSharp'
        }
        if ($Errors) { throw ($Errors -join "`n") }
    }
}
finally {
    foreach ($Window in $Alpha, $Beta) {
        if (-not $Window.Process.HasExited) { $Window.Process.Kill() }
    }
    if (-not $Launcher.HasExited) { $Launcher.Kill() }
    if ($null -eq $SavedLanguage) { Remove-Item $LanguageFile -ErrorAction SilentlyContinue }
    else { Set-Content $LanguageFile $SavedLanguage -NoNewline }
    Copy-Item "$RunData\Logs" (Join-Path $OutputDirectory 'logs') -Recurse -Force -ErrorAction SilentlyContinue
    Get-ChildItem $RunData -File -Filter '*.json' -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\Plugins\\' } |
        Copy-Item -Destination $OutputDirectory -Force -ErrorAction SilentlyContinue
}

if ($Failures.Count -gt 0) {
    throw "$($Failures.Count) end-to-end check(s) failed: $($Failures -join '; ')"
}
Write-Host 'ALL END-TO-END CHECKS PASSED'
