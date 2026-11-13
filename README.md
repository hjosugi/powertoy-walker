# powertoy-walker

English | [日本語](README.ja.md)

A PowerToys Run plugin that switches to your open windows, shown with their app icons.
Each result's subtitle reads like **`WezTerm (powertoy-walker)`**, so windows stand apart from
results that start an app. The window title on the first line carries no plugin name.

| Type | What happens |
| --- | --- |
| `firefox` | Global search lists Firefox windows |
| `w: firefox` | Only this plugin searches, by app name, process name and title |
| `w: gmail` | Lists the windows with Gmail in their title |
| `w:` | Lists windows from the most recently used, with the window you came from last |

Enter switches to the window, and restores it if it is minimized. **Close window** in the context
menu, or Ctrl+W, asks the window to close, as its close button would. The app still asks about
unsaved work, and its process is never killed.

The plugin shows its text in English or Japanese; see [Language](#language).

## Quick start

On Windows with [PowerToys](https://learn.microsoft.com/en-us/windows/powertoys/install), open
PowerShell and run this line. It needs neither administrator rights nor a change to the execution policy.

```powershell
irm https://github.com/hjosugi/powertoy-walker/releases/latest/download/install.ps1 | iex
```

Open PowerToys Run (Alt+Space by default) and type `w:` to list your open windows.

The installer works in this order:

1. It finds PowerToys, installed for the user or for the machine, and its architecture (x64 / ARM64).
2. It downloads `SHA256SUMS.txt` and the matching ZIP from the latest release. If the SHA-256 does not match, it stops without changing anything.
3. It exits PowerToys and installs the plugin with the `Install.ps1` in the ZIP, backing up the previous version.
4. If PowerToys Run is off, it turns it on in the PowerToys settings, then starts PowerToys again.
   Recent PowerToys versions (checked with v0.102.3001) default to Command Palette and leave PowerToys Run off.

To pass options, run the installer as a script block:

```powershell
$Url = 'https://github.com/hjosugi/powertoy-walker/releases/latest/download/install.ps1'
& ([scriptblock]::Create((irm $Url))) -Version 0.1.1      # A specific version (0.1.1 or later)
& ([scriptblock]::Create((irm $Url))) -Architecture ARM64 # A specific architecture
& ([scriptblock]::Create((irm $Url))) -Uninstall          # Uninstall
```

If PowerToys is not installed yet, install it first, for example with
`winget install Microsoft.PowerToys --source winget`. If PowerToys runs as administrator, the
installer cannot exit it: exit it from its system tray menu, then run the installer.

To see what each step does as you go, follow [Install step by step](#install-step-by-step),
which splits the same work into separate commands.

## Install step by step

These PowerShell commands do what the one-line install does, one step at a time: they download
the latest GitHub release, extract it, install the plugin and start PowerToys. They need no
administrator rights. Paste the blocks in order.

<!-- step-by-step install: tests/e2e/Test-QuickInstall.ps1 runs the four blocks below as written. -->

1. Download the ZIP for your PowerToys architecture (x64 / ARM64) from the latest release, and
   check it against its hash in `SHA256SUMS.txt`. If they differ, the ZIP is deleted and the step stops.

   ```powershell
   $Url  = 'https://github.com/hjosugi/powertoy-walker/releases/latest/download'
   $Arch = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').PROCESSOR_ARCHITECTURE -replace 'AMD64', 'x64'
   $Work = (New-Item -ItemType Directory "$env:TEMP\powertoy-walker-$(Get-Date -Format yyyyMMdd-HHmmss)").FullName
   Invoke-WebRequest "$Url/SHA256SUMS.txt" -OutFile "$Work\SHA256SUMS.txt" -UseBasicParsing
   $Hash, $Name = -split (Select-String -LiteralPath "$Work\SHA256SUMS.txt" -Pattern "-$Arch\.zip$").Line
   Invoke-WebRequest "$Url/$Name" -OutFile "$Work\$Name" -UseBasicParsing
   if ((Get-FileHash "$Work\$Name").Hash -ne $Hash) { Remove-Item "$Work\$Name"; throw "SHA-256 mismatch: $Name" }
   ```

2. Extract the ZIP.

   ```powershell
   Expand-Archive "$Work\$Name" $Work
   ```

3. Exit PowerToys, and install the plugin with the extracted `Install.ps1`. The previous version
   is backed up to `%LOCALAPPDATA%\Microsoft\PowerToys\powertoy-walker-backups`.

   ```powershell
   Get-Process PowerToys* -ErrorAction SilentlyContinue | Stop-Process -Force -PassThru | Wait-Process
   powershell -NoProfile -ExecutionPolicy Bypass -File "$Work\powertoy-walker\Install.ps1"
   ```

4. Turn on PowerToys Run (recent PowerToys versions leave it off), and start PowerToys.

   ```powershell
   $Settings = "$env:LOCALAPPDATA\Microsoft\PowerToys\settings.json"
   if (Test-Path $Settings) { [IO.File]::WriteAllText($Settings, ([IO.File]::ReadAllText($Settings) -replace '("PowerToys Run"\s*:\s*)false', '${1}true')) }
   $PowerToys = "$env:LOCALAPPDATA\PowerToys", "$env:ProgramFiles\PowerToys" | Where-Object { Test-Path "$_\PowerToys.exe" } | Select-Object -First 1
   Start-Process "$PowerToys\PowerToys.exe" -WorkingDirectory $PowerToys
   ```

<!-- end of step-by-step install -->

Open PowerToys Run (Alt+Space by default) and type `w:`. When your windows are listed, you are
done. If they are not, check in the PowerToys settings that **PowerToys Run** is on, and that
powertoy-walker is on in its **Plugins**.

The plugin is installed in this folder. To uninstall, exit PowerToys and delete the folder, or run
the one-line install with `-Uninstall`.

```text
%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Plugins\powertoy-walker
```

If the same windows also appear from the built-in **Window Walker**, turn off its
**Include in global result**.

The plugin is for PowerToys Run on Windows 11, in PowerToys versions built on .NET 10. It was
developed against the official API of
[PowerToys v0.102.3001](https://github.com/microsoft/PowerToys/tree/v0.102.3001/src/modules/launcher/Wox.Plugin).
PowerToys Command Palette has a separate extension API, so it cannot load this DLL.

## Global search and the action keyword

In the plugin's PowerToys Run settings, you can change the **Direct activation command** from
`w:` to another, such as `win:`. It needs no trailing space.

Do not use a command of only letters, such as `w` or `win`. PowerToys Run compares the start of
what you type with the command, and when it matches, it searches no other plugin. With `w`,
everything you type that starts with `w`, such as `word` or `wezterm`, would reach only this
plugin, and you could not start other apps. Turn on **Include in global result** to search
windows both with and without the command, or turn it off to search them only with the command.

Options:

- **Prefer windows in global search**: on by default. Raises the score of matching windows, so
  they tend to rank above results that start an app. Other plugins' score adjustments and the
  usage history still change the order, so a window is not always first.
- **Match window titles in global search**: off by default. When on, titles are searched without the command too.
- **List the window you came from last**: on by default. `w:` + Enter then works like Alt+Tab.
- **Include minimized windows**: on by default.
- **Include windows on other desktops**: on by default. Switching desktops and moving the focus
  follow what Windows allows. Turn it off to search only the current desktop.
- **Enable fuzzy matching**: on by default. Also matches the initials of words
  (`vsc` → Visual Studio Code, `wt` → WezTerm) and letters in order within one word
  (`frfx` → Firefox). Letters scattered across a title do not match.

PowerToys Run's **Score adjustment** works with these too. The `(powertoy-walker)` in the
subtitle is not part of the text that queries match.

## Language

The plugin's text is in English or Japanese: its description and options in the settings, the
subtitles, the context menu and its messages. It follows the language picked in the PowerToys
settings, under **General** > **Language**. When that is the Windows default, it follows the
Windows display language. Other languages show English. Like PowerToys, the plugin applies a new
language after PowerToys restarts.

The translations are in [`src/Walker.Core/Strings.fs`](src/Walker.Core/Strings.fs), one record per
language. To add a language, add a record and its language code to `Strings.all`. A record that
leaves out a string does not compile, and the tests check that no string is empty or left in English.

## Build and test

Building needs the .NET 10 SDK, which includes the F# compiler. In PowerShell on Windows, run:

```powershell
./scripts/Build.ps1 -Architecture x64
# The ARM64 build
./scripts/Build.ps1 -Architecture ARM64
```

This creates `dist/powertoy-walker-<version>-<architecture>.zip`, with the version in `plugin.json`.
DLLs that the host owns, such as `Wox.Plugin.dll`, are not included in the ZIP. The API reference comes from
[Community.PowerToys.Run.Plugin.Dependencies](https://github.com/hlaueriksson/Community.PowerToys.Run.Plugin.Dependencies)
0.97.0.

Because the plugin is written in F#, its `plugin.json` differs from a C# plugin's in two ways:

- **`DynamicLoading` is `true`**: the ZIP includes the F# runtime, `FSharp.Core.dll` (MIT License).
  With `false`, the host's default load context cannot resolve it, and the plugin does not load.
- **`Language` stays `csharp`**: PowerToys Run identifies .NET plugins by this value, and rejects
  other values such as `fsharp`.

Searching, the state changes of window tracking and of the caches, and choosing the language are
pure functions in `Walker.Core`, independent of Windows, so the regression tests also run on Linux.
The side effects, through Win32 and WPF, are only in `PowerToyWalker`.

```sh
dotnet run --project tests/Walker.Core.Tests -c Release
dotnet run --project tests/Walker.Core.Tests -c Release -- --benchmark
dotnet build src/PowerToyWalker -c Release -p:Platform=x64
```

## Development environment

With Nix, `nix develop` provides the .NET SDK, PowerShell and nixfmt (`direnv allow` makes this automatic).
Without Nix, install the .NET 10 SDK and PowerShell 7. The Windows plugin also cross-builds on Linux.

When you open the folder in VS Code, it suggests the recommended extensions (`.vscode/extensions.json`).

| Action | What it does |
| --- | --- |
| F5 **Tests** / **Benchmark** | Runs the tests or the benchmark under the debugger |
| F5 **PowerToys Run: install and launch (Windows)** | Builds and installs the plugin, then starts the launcher under the debugger. Exit PowerToys first. PowerToys must be installed for the user |
| F5 **PowerToys Run: attach (Windows)** | Attaches to a running launcher |
| Tasks **test** / **format** / **lint** | Tests, formatting and static analysis |

Files are formatted when saved. [Fantomas](https://fsprojects.github.io/fantomas/) formats F#,
[FSharpLint](https://fsprojects.github.io/FSharpLint/) analyzes it, and PSScriptAnalyzer analyzes
PowerShell, at the versions pinned in `dotnet-tools.json` and `scripts/Lint.ps1`. From a terminal:

```sh
dotnet tool restore
dotnet fantomas src tests
pwsh scripts/Lint.ps1
```

The window list is refreshed in the background every 500 ms, and a WinEvent hook records changes
of the foreground window. A search while you type reads only an immutable snapshot.
Process names and executable details are cached by process ID and creation time, up to 512
entries, so the files of the same process are not read again every 500 ms.
Icons come from the window's own large icon first, then the executable, then the system.
At most two workers read and decode them, and the result list reads only the cache.
Once an icon has loaded, only the image on screen is updated, on the UI thread, without adding the results again.
The cache holds up to 256 icons, and the plugin's own icon shows while one loads for the first time.
When an app does not expose an icon, or reading it is not allowed, a fallback icon shows.
Actions on a window that has closed check its handle and process ID again.

What is tested: CI (`.github/workflows/ci.yml`) installs the ZIP in a real PowerToys Run v0.102.3001
on x64 (Windows Server 2025) and ARM64 (Windows 11), and uses it with keys: loading, searching,
switching, restoring from minimized, closing windows, switching between virtual desktops, applying
settings and exiting (`tests/e2e/Test-PowerToysRun.ps1`). The plugin's text is checked in English on
x64, and in Japanese on ARM64. Then CI serves the built ZIP locally the way a release serves it,
and in Windows PowerShell checks the one-line install, refusing a tampered ZIP, upgrading, turning
on PowerToys Run when it is off as by default, and uninstalling. It also runs the four blocks of
[Install step by step](#install-step-by-step) as written, and checks that the Japanese README has
the same commands (`tests/e2e/Test-QuickInstall.ps1`). Because this runs on CI runners, multiple
monitors and windows that run as administrator are not covered.

## Releases

Pushing a tag such as `v0.1.2` makes the Release workflow publish the ZIPs that passed the tests
above, with `install.ps1`, `SHA256SUMS.txt` and artifact attestations. See
[RELEASING.md](RELEASING.md) for the steps and [CHANGELOG.md](CHANGELOG.md) for the changes.

Official documentation: [PowerToys Run](https://learn.microsoft.com/en-us/windows/powertoys/run),
[the plugin checklist](https://github.com/microsoft/PowerToys/blob/main/doc/devdocs/modules/launcher/new-plugin-checklist.md).
