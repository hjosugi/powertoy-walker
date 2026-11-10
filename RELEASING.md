# リリース手順

`v` で始まるタグを push すると、[Release ワークフロー](.github/workflows/release.yml) が
テストからリリースの作成までを行います。手元でビルドした ZIP はアップロードしません。

## 手順

1. `src/PowerToyWalker/plugin.json` の `Version` を上げます (例: `0.1.2`)。
2. `CHANGELOG.md` の先頭に `## 0.1.2 - YYYY-MM-DD` の節を足し、変更点を書きます。
   この節がそのままリリースノートの冒頭になります。
3. コミットして `main` に push し、CI が通ることを確認します。
4. タグを付けて push します。

   ```sh
   git tag v0.1.2
   git push origin v0.1.2
   ```

5. Actions の **Release** の実行が終わったら、[Releases](https://github.com/hjosugi/powertoy-walker/releases)
   に `v0.1.2` が Latest として出ていることを確認します。

## ワークフローが行うこと

| ジョブ | 内容 |
| --- | --- |
| Check the version | タグが `v` + `plugin.json` の `Version` と一致し、`main` 上のコミットで、`CHANGELOG.md` に節があることを確認 |
| Test | CI と同じジョブ。Lint、単体テスト、x64 と ARM64 の実物の PowerToys Run での e2e テスト、1行インストールの e2e テスト |
| Publish | Test が作った ZIP をそのまま使い、`install.ps1` と `SHA256SUMS.txt` を添えて、artifact attestation を付け、リリースを作成 |
| Verify the download | README の1行インストールと同じ公開 URL から取得し、SHA-256、`install.ps1` の内容、attestation を確認 |

リリースに含まれるファイル:

- `powertoy-walker-<version>-x64.zip` / `powertoy-walker-<version>-ARM64.zip`
- `install.ps1` (`scripts/QuickInstall.ps1` と同じ内容)
- `SHA256SUMS.txt`

## 失敗したとき

- **Publish より前で失敗した場合**: リリースはまだ作られていません。修正をコミットしてから、
  タグを付け直して push します。

  ```sh
  git push origin :refs/tags/v0.1.2
  git tag -f v0.1.2
  git push origin v0.1.2
  ```

- **Publish 以降で失敗した場合**: 公開済みのリリースやタグは書き換えず、次のパッチバージョンで出し直します。

## 1行インストールの仕組み

`install.ps1` は `releases/latest/download/` から `SHA256SUMS.txt` を取得し、PowerToys のアーキテクチャに合う
ZIP の名前とハッシュを読みます。そのため ZIP の名前は `powertoy-walker-<version>-<x64|ARM64>.zip` の形を
保ってください。Windows PowerShell は charset の無いダウンロードを Latin-1 として読むので、
`scripts/QuickInstall.ps1` は ASCII だけで書きます (`scripts/Lint.ps1` が確認します)。

e2e テスト (`tests/e2e/Test-QuickInstall.ps1`) は環境変数 `POWERTOY_WALKER_DOWNLOAD_URL` で取得元を
ローカルの HTTP サーバーに向け、公開前のリリースを同じ手順で入れて確認します。
