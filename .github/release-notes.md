{{CHANGES}}

## Install

On Windows with PowerToys, open PowerShell and run this line. It does not need administrator rights.

```powershell
irm https://github.com/hjosugi/powertoy-walker/releases/latest/download/install.ps1 | iex
```

It checks the ZIP for your PowerToys architecture against `SHA256SUMS.txt`, installs it and restarts PowerToys. If PowerToys Run is off, as it is by default in recent PowerToys versions, it turns it on.
Open PowerToys Run (Alt+Space by default) and type `w:` to list your open windows.
The [README](https://github.com/hjosugi/powertoy-walker#readme) explains installing from the ZIP by hand and uninstalling.
日本語の説明は [README.ja.md](https://github.com/hjosugi/powertoy-walker/blob/main/README.ja.md) にあります。

## Tested on

This release's ZIPs were installed in PowerToys {{POWERTOYS}} on x64 (Windows Server 2025) and ARM64 (Windows 11), and tested for searching, switching, restoring, closing windows, moving between virtual desktops, applying settings, the plugin's text in English and Japanese, exiting, and the one-line install and uninstall.

## Verify

```text
{{CHECKSUMS}}
```

To check that this repository's Release workflow built a ZIP, use the GitHub CLI:

```sh
gh attestation verify powertoy-walker-{{VERSION}}-x64.zip --repo hjosugi/powertoy-walker
```
