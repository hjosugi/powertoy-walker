# Changelog

The notes of each release are made from its section in this file (see [RELEASING.md](RELEASING.md)).

## Unreleased

## 0.2.0 - 2026-11-13

- The plugin shows its text in English or Japanese: its description and options in the settings, the subtitles, the context menu and its messages. It follows the language picked in the PowerToys settings, or the Windows display language when that is the Windows default. Other languages show English.
- The README is now in English, with a Japanese translation in `README.ja.md`. The ZIP includes both.

## 0.1.1 - 2026-11-12

- Fixed filtering with `w:`. Fuzzy matching also matched letters scattered across a whole title, so a short query such as `w: note` listed unrelated windows. Abbreviations now match only the initials of words (`vsc` → Visual Studio Code) or letters in order within one word (`frfx` → Firefox). Windows that match at the start of a word also rank above those that match inside a word.
- Added a one-line install in PowerShell. It finds PowerToys and its architecture (x64 / ARM64), checks the ZIP against `SHA256SUMS.txt` before installing it, and restarts PowerToys. It also turns on PowerToys Run, which recent PowerToys versions leave off. `-Uninstall` removes the plugin.
- The README's install steps now download the latest GitHub release, check its SHA-256, extract it, install it and start PowerToys, all with PowerShell commands. CI runs these steps as written.
- Releases are now made by GitHub Actions. Only ZIPs tested in a real PowerToys Run on x64 and ARM64 are published, with their SHA-256 and build provenance (artifact attestations).

## 0.1.0 - 2026-11-06

- First release.
