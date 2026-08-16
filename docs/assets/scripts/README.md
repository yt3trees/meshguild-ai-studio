# GIF generation scripts

このフォルダーには、README に掲載する操作 GIF の生成に使うファイルを置いています。

## ファイル

- `run-cursor-gifs-web.cmd`: E2E 用の Web UI を起動する
- `capture-cursor-gifs.py`: Playwright で操作を再現し、カーソル付きの PNG フレームを生成する
- `generate-gifs.cmd`: PNG フレームを GIF へ変換する

## 生成手順

リポジトリのルートから実行します。

```powershell
python "docs\assets\scripts\with_server.py" --server "docs\assets\scripts\run-cursor-gifs-web.cmd" --port 5049 --server "python docs\assets\scripts\mock-mission-api.py" --port 5050 -- python "docs\assets\scripts\capture-cursor-gifs.py"
docs\assets\scripts\generate-gifs.cmd
```

キャプチャは E2E 用の決定的なローカル環境を使い、LLM や外部 API へ接続しません。
PNG フレームは `C:\Users\<user>\AppData\Local\Temp\opencode\cursor-gifs-run` 配下に生成され、次回のキャプチャ開始時に削除されます。

必要な環境は Python Playwright、Playwright の Chromium、.NET 10 SDK、FFmpeg です。
`mock-mission-api.py` は E2E のミッション送信だけを受け付けるローカルモックで、外部 API へ接続しません。
