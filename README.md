# Token Checker for Windows

タスクバーに Claude Code と Codex の使用率を常時表示する Windows アプリケーション。

## 概要

ターミナルで `claude login` / `codex login` を完了済みのアカウントに対し、Anthropic の OAuth エンドポイントおよび `codex app-server` の JSON-RPC を経由してレート制限情報を取得する。取得結果はタスクバー上のウィジェットにバーグラフで表示され、クリックでポップアップに 5 時間ウィンドウと週次ウィンドウの詳細を展開する。

macOS 版 [Token Checker](https://github.com/satonico-bit/Token-Checker) の Windows 移植版。

## 動作要件

| 項目 | 値 |
|------|----|
| OS | Windows 10 / 11 |
| .NET | 8.0 以上（SDK または Runtime） |
| Claude Code CLI | `claude login` 済み |
| Codex CLI | `npm i -g @openai/codex` でインストール後 `codex login` 済み |

Claude Code と Codex のいずれかが欠けていても、もう一方は動作する。

## インストール

```powershell
dotnet build TokenChecker.sln -c Release
```

出力先: `TokenChecker\bin\Release\net8.0-windows\TokenChecker.exe`

### 単一ファイルに発行する場合

```powershell
dotnet publish TokenChecker\TokenChecker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish\
```

## 使用方法

事前にターミナルで以下を実行し、両サービスにログインしておく。

```powershell
claude login
codex login
```

いずれもブラウザの OAuth フローを経て、Windows 資格情報マネージャーまたは `%USERPROFILE%\.claude\credentials.json` にトークンが保存される。アプリは保存されたトークンを参照するため、ログインは CLI 側で 1 度行えばよい。

`TokenChecker.exe` を起動するとタスクバー上にウィジェットが表示される。クリックで展開するポップアップには、5 時間ウィンドウと週次ウィンドウの使用率、リセットまでの残時間、更新間隔（2 分〜10 分、既定 2 分）、ログイン時の自動起動トグルが含まれる。

## データ取得経路

- **Claude**: Windows 資格情報マネージャー（`Claude Code-credentials`）から OAuth アクセストークンを取得し、`https://api.anthropic.com/api/oauth/usage` に対して `anthropic-beta: oauth-2025-04-20` ヘッダー付きで GET する。資格情報マネージャーに見つからない場合は `%USERPROFILE%\.claude\credentials.json` にフォールバック。
- **Codex**: `codex app-server` を子プロセスとして起動し、行区切り JSON-RPC 経由で `account/rateLimits/read` を呼ぶ。

## アンインストール

アプリを終了後、以下を実行する。

```powershell
# 自動起動の登録を削除
reg delete "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v TokenChecker /f

# 設定・キャッシュファイルを削除
Remove-Item "$env:APPDATA\TokenChecker" -Recurse -Force
```

## 再配布・商標利用について

本リポジトリには明示的なライセンスを設定していない。個人利用・改変は自由だが、再配布・フォーク公開を行う場合は事前に作者まで連絡すること。

## 免責事項

本ソフトウェアは現状有姿 (as-is) で提供されるものであり、動作・安全性・正確性について一切の保証を行わない。本ソフトウェアの利用に起因して発生したいかなる損害（データ損失、アカウント停止、トークン漏洩、セキュリティインシデント等を含むがこれに限らない）についても、作者は一切の責任を負わない。利用者自身の責任において使用すること。
