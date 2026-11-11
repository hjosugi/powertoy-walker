{{CHANGES}}

## インストール

PowerToys を入れた Windows で PowerShell を開き、次の1行を実行します。管理者権限は不要です。

```powershell
irm https://github.com/hjosugi/powertoy-walker/releases/latest/download/install.ps1 | iex
```

PowerToys のアーキテクチャに合う ZIP を `SHA256SUMS.txt` で検証してから入れ、PowerToys を起動し直します。PowerToys Run がオフ (最近の PowerToys の既定) ならオンにします。
PowerToys Run (既定は Alt+Space) で `w:` と入力すると、開いている窓の一覧が出ます。
ZIP から手動で入れる手順とアンインストールは [README](https://github.com/hjosugi/powertoy-walker#readme) にあります。

## 確認した環境

PowerToys {{POWERTOYS}} に x64 (Windows Server 2025) と ARM64 (Windows 11) でこのリリースの ZIP を入れ、検索、切り替え、復元、ウィンドウを閉じる操作、仮想デスクトップ間の移動、設定の反映、終了、1行インストールとアンインストールまでを確認しています。

## 検証

```text
{{CHECKSUMS}}
```

このリポジトリの Release ワークフローがビルドしたことは、GitHub CLI で確認できます。

```sh
gh attestation verify powertoy-walker-{{VERSION}}-x64.zip --repo hjosugi/powertoy-walker
```
