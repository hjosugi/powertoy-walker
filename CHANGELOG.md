# 変更履歴

リリースノートは、このファイルの該当するバージョンの節から作られます ([RELEASING.md](RELEASING.md))。

## 0.1.1 - 2026-11-10

- PowerShell の1行でインストールできるようにしました。PowerToys の場所とアーキテクチャ (x64 / ARM64) を調べ、ZIP を `SHA256SUMS.txt` で検証してから入れ、PowerToys を起動し直します。`-Uninstall` で削除もできます。
- リリースを GitHub Actions で作るようにしました。x64 と ARM64 の実物の PowerToys Run でテストした ZIP だけを公開し、SHA-256 とビルドの来歴 (artifact attestation) を添えます。

## 0.1.0 - 2026-11-06

- 最初のリリースです。
