# 変更履歴

リリースノートは、このファイルの該当するバージョンの節から作られます ([RELEASING.md](RELEASING.md))。

## 0.1.1 - 2026-11-10

- `w:` の絞り込みを直しました。あいまい一致がタイトル全体に散らばった文字にも一致していたため、`w: note` のような短い入力で関係の無い窓まで並んでいました。省略形は単語の頭文字 (`vsc` → Visual Studio Code) か、1つの単語の中で順に並ぶ文字 (`frfx` → Firefox) にだけ一致します。また、単語の先頭で一致した窓を、単語の途中で一致した窓より上に並べます。
- PowerShell の1行でインストールできるようにしました。PowerToys の場所とアーキテクチャ (x64 / ARM64) を調べ、ZIP を `SHA256SUMS.txt` で検証してから入れ、PowerToys を起動し直します。最近の PowerToys が既定でオフにしている PowerToys Run もオンにします。`-Uninstall` で削除もできます。
- リリースを GitHub Actions で作るようにしました。x64 と ARM64 の実物の PowerToys Run でテストした ZIP だけを公開し、SHA-256 とビルドの来歴 (artifact attestation) を添えます。

## 0.1.0 - 2026-11-06

- 最初のリリースです。
