# powertoy-walker

PowerToys Run から、起動中のウィンドウへアプリのアイコン付きで切り替えるプラグインです。
候補の下段は **`WezTerm (powertoy-walker)`** のように表示し、アプリを新しく起動する候補と
区別できます。上段のウィンドウタイトルにはプラグイン名を付けません。

| 入力 | 動作 |
| --- | --- |
| `firefox` | グローバル検索で Firefox のウィンドウを表示 |
| `w firefox` | このプラグインだけでアプリ名・プロセス名・タイトルを検索 |
| `w gmail` | Gmail をタイトルに含むウィンドウを検索 |
| `w ` | 直近に使った順に一覧表示。元いたウィンドウは最後 |

Enter で切り替え、最小化中なら復元します。コンテキストメニューの **Close window** または
Ctrl+W でウィンドウに通常の終了要求を送ります。未保存の確認は対象アプリが行います。
プロセスの強制終了は行いません。

## Windows へのインストール

Windows 11、.NET 10 対応の PowerToys Run を対象にしています。開発時に確認した公式APIは
[PowerToys v0.102.3001](https://github.com/microsoft/PowerToys/tree/v0.102.3001/src/modules/launcher/Wox.Plugin) です。
PowerToys Command Palette は別の拡張APIなので、このDLLの読み込み先にはできません。

1. 使用している PowerToys と同じアーキテクチャの ZIP (`x64` または `ARM64`) を展開します。
2. タスクトレイの PowerToys メニューから PowerToys を終了します。
3. 展開した `powertoy-walker` フォルダーで PowerShell を開き、`./Install.ps1` を実行します。
4. PowerToys を起動し、**PowerToys Run → プラグイン → powertoy-walker** を有効にします。

手動の場合は、DLL・`plugin.json`・`Images` を含むフォルダーを次へコピーします。

```text
%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Plugins\powertoy-walker
```

組み込みの **Window Walker** と同じ窓が重複して出る場合は、組み込み側のグローバル検索への
参加をオフにしてください。アンインストールは PowerToys 終了後、このフォルダーを削除します。
インストーラーは更新前のプラグインを `Microsoft\PowerToys\powertoy-walker-backups` に保存します。

## グローバル検索と任意の prefix

PowerToys Run のプラグイン設定で、**直接アクティブ化コマンド (Direct activation command)** を
`w` から `win` などへ変更できます。末尾に空白を登録する必要はありません。
**グローバルの結果に含める (Include in global result)** をオンにすれば prefix と併用でき、
オフにすれば prefix 専用になります。

追加設定:

- **Prefer windows in global search**: 既定オン。一致する窓のスコアを加算して、
  アプリを新規起動する候補より上位に出やすくします。他プラグインのスコア調整や利用履歴に
  よって順位は変わるため、必ず先頭になる保証ではありません。
- **Match window titles in global search**: 既定オフ。オンなら prefix 無しでもタイトルを検索します。
- **List the window you came from last**: 既定オン。`w ` + Enter が Alt+Tab のように動きます。
- **Include minimized windows**: 既定オン。
- **Include windows on other desktops**: 既定オン。別デスクトップへの切り替え・フォーカスの許可は
  Windows に従います。現在のデスクトップだけを検索する場合はオフにします。
- **Enable fuzzy matching**: 既定オン。`frfx` のような省略形でも一致します。

PowerToys Run 側の **スコア調整 (Score adjustment)** も併用できます。
照合する文字列には表示用の `(powertoy-walker)` を含めません。

## ビルドとテスト

.NET 10 SDK が必要です。Windows の PowerShell で次を実行します。

```powershell
./scripts/Build.ps1 -Architecture x64
# ARM64 版
./scripts/Build.ps1 -Architecture ARM64
```

`dist/powertoy-walker-0.1.0-<architecture>.zip` が生成されます。ホストが所有する
`Wox.Plugin.dll` などの DLL は ZIP に同梱しません。API参照には
[Community.PowerToys.Run.Plugin.Dependencies](https://github.com/hlaueriksson/Community.PowerToys.Run.Plugin.Dependencies)
0.97.0 を使用します。

検索処理は Windows に依存しないため、Linux でも次で回帰テストを実行できます。

```sh
dotnet run --project tests/Walker.Core.Tests -c Release
dotnet run --project tests/Walker.Core.Tests -c Release -- --benchmark
dotnet build src/PowerToyWalker -c Release -p:Platform=x64
```

ウィンドウ一覧はバックグラウンドで 500 ms ごとに更新し、前面ウィンドウの変化は WinEvent
フックで記録します。入力中の検索では不変のスナップショットだけを参照します。
プロセス名・実行ファイル情報はプロセスIDと作成時刻をキーに最大512件キャッシュし、
同じプロセスの情報を500msごとにファイルから読み直しません。
アイコンはウィンドウ固有の大きい画像を優先し、実行ファイル、システムのアイコンの順に代替します。
取得・デコードは最大2ワーカーで行い、表示側はキャッシュだけを参照します。
ロード完了後は表示中の画像だけをUIスレッドで更新し、検索結果を追加し直しません。
キャッシュは最大256件で、初回取得中はプラグインの画像を表示します。
アプリがアイコンを公開しない場合や取得が許可されない場合は、代替アイコンを表示します。
終了した窓へのアクションでは、ハンドルとプロセスIDを再確認します。

検証範囲: Linux 上の検索回帰テストと Windows 向けクロスビルド。
Windows 実機での PowerToys Run による読み込み、フォーカス移動、アイコン表示、
仮想デスクトップ間の切り替えは、Windows 環境での確認が必要です。

公式資料: [PowerToys Run](https://learn.microsoft.com/en-us/windows/powertoys/run)、
[プラグイン作成チェックリスト](https://github.com/microsoft/PowerToys/blob/main/doc/devdocs/modules/launcher/new-plugin-checklist.md)。
