# Helpers shared by the end-to-end tests. Dot-source this file at the start of a test:
#
#     . "$PSScriptRoot/Common.ps1"
#
# The test must have an $OutputDirectory, where every check saves a screenshot.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing

# PowerToys Run keeps its settings, plugins and logs here.
$RunData = "$env:LOCALAPPDATA\Microsoft\PowerToys\PowerToys Run"
$Failures = [System.Collections.Generic.List[string]]::new()

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

# Returns the first value of $Condition that is true.
# Script blocks see the variables of their callers, so a caller's names must not repeat these.
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
# The screen afterwards is saved, named after the check.
function Invoke-Check([string]$Name, [scriptblock]$Body) {
    try {
        & $Body
        Write-Host "PASS  $Name"
    }
    catch {
        $Failures.Add($Name)
        Write-Host "FAIL  $Name`n      $_"
    }
    Save-Screenshot ($Name -replace '[^A-Za-z0-9]+', '-')
}

# Fails the test if any check failed, naming them all.
function Complete-CheckRun([string]$What) {
    if ($Failures.Count -gt 0) {
        throw "$($Failures.Count) $What check(s) failed: $($Failures -join '; ')"
    }
    Write-Host "ALL $($What.ToUpperInvariant()) CHECKS PASSED"
}

function Get-LogText {
    (Get-ChildItem "$RunData\Logs" -Recurse -Filter '*.txt' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue }) -join "`n"
}

# Ends every PowerToys process, and waits until they are gone.
function Close-PowerToysProcess {
    Get-Process -Name 'PowerToys*' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Wait-Until { -not (Get-Process -Name 'PowerToys*' -ErrorAction SilentlyContinue) } 'PowerToys to exit' | Out-Null
}
