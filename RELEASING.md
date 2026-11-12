# Releasing

Pushing a tag that starts with `v` runs the [Release workflow](.github/workflows/release.yml),
which tests the plugin and creates the release. ZIPs built on your own machine are never uploaded.

## Steps

1. Raise `Version` in `src/PowerToyWalker/plugin.json` (for example to `0.1.2`).
2. In `CHANGELOG.md`, rename the `## Unreleased` heading to `## 0.1.2 - YYYY-MM-DD`, and check
   the changes listed under it. This section becomes the start of the release notes.
3. Commit, push to `main`, and check that CI passes.
4. Tag the commit and push the tag.

   ```sh
   git tag v0.1.2
   git push origin v0.1.2
   ```

5. When the **Release** run in Actions has finished, check that `v0.1.2` is the Latest release in
   [Releases](https://github.com/hjosugi/powertoy-walker/releases).

After the release, add a new `## Unreleased` section at the top of `CHANGELOG.md` for the next changes.

## What the workflow does

| Job | What it does |
| --- | --- |
| Check the version | Checks that the tag is `v` + `Version` in `plugin.json`, that it points to a commit on `main`, and that `CHANGELOG.md` has a section for it |
| Test | The same jobs as CI: lint, unit tests, end-to-end tests in a real PowerToys Run on x64 (in English) and ARM64 (in Japanese), and end-to-end tests of the one-line install and the README's step-by-step install |
| Publish | Takes the ZIPs that Test built, as they are, adds `install.ps1` and `SHA256SUMS.txt`, attests them, and creates the release |
| Verify the download | Downloads from the same public URLs as the README's one-line install, and checks the SHA-256, the contents of `install.ps1`, and the attestations |

The files in a release:

- `powertoy-walker-<version>-x64.zip` / `powertoy-walker-<version>-ARM64.zip`
- `install.ps1` (the same as `scripts/QuickInstall.ps1`)
- `SHA256SUMS.txt`

## When it fails

- **Before Publish**: no release has been created yet. Commit the fix, then move the tag to it
  and push it again.

  ```sh
  git push origin :refs/tags/v0.1.2
  git tag -f v0.1.2
  git push origin v0.1.2
  ```

- **During or after Publish**: do not rewrite a published release or tag. Release the next patch version instead.

## How the one-line install works

`install.ps1` downloads `SHA256SUMS.txt` from `releases/latest/download/`, and reads from it the
name and hash of the ZIP for the PowerToys architecture. Keep the ZIP names in the form
`powertoy-walker-<version>-<x64|ARM64>.zip`. Windows PowerShell reads a download without a
charset as Latin-1, so `scripts/QuickInstall.ps1` is written in ASCII only (`scripts/Lint.ps1` checks this).

The end-to-end test (`tests/e2e/Test-QuickInstall.ps1`) points the installer at a local HTTP
server with the environment variable `POWERTOY_WALKER_DOWNLOAD_URL`, and installs the unreleased
ZIP the same way. It also runs the four blocks of the README's "Install step by step" as written,
replacing only the download URL. Keep the blocks between `<!-- step-by-step install: ... -->` and
`<!-- end of step-by-step install -->`. `README.ja.md` must have the same blocks, and the test checks it.

## Translations

The plugin's text is in `src/Walker.Core/Strings.fs`. When you change a string, change it in every
language. `tests/e2e/expected-text.json` holds the text that the end-to-end test expects on
screen, and the unit tests check that it matches `Strings.fs`.
