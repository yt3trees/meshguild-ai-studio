---
title: インストールと起動
description: MeshGuild AI Studio をローカルで起動し、初回のモデル登録までを説明します。
layout: page
---

MeshGuild AI Studio(リポジトリ名: WorkAgents)は、Windows 上で Web 画面と Host を起動して使います。
このページでは、ソースから開発用に起動する手順と、初回実行前に必要な設定だけを扱います。

## 起動前チェック

- [ ] Windows 環境である
- [ ] .NET SDK 10.x がインストールされている。`dotnet --version` で確認する
- [ ] Git がインストールされている
- [ ] 利用する LLM プロバイダーの接続情報(API キーなど)を用意できる
- [ ] リポジトリを取得し、リポジトリのルートでコマンドを実行できる
- [ ] `src/WorkAgents.Web/appsettings.Development.json` を用意する
- [ ] `src/WorkAgents.Host/appsettings.Development.json` を用意する
- [ ] Web と Host の `Runs:DatabasePath` が同じ SQLite ファイルを指している
- [ ] API キーや秘密鍵を `appsettings`、定義ファイル、`.env` に書いていない

`dotnet --version` がエラーになる場合は、[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) をインストールしてから、PowerShell を開き直して確認してください。
Runtime ではなく SDK が必要です。

## リポジトリを取得してビルドする

任意の作業フォルダーで clone し、以降のコマンドはすべてリポジトリのルートで実行します。

```powershell
git clone https://github.com/yt3trees/meshguild-ai-studio.git
cd meshguild-ai-studio
dotnet restore WorkAgents.sln
dotnet build WorkAgents.sln
```

初回の `restore` と `build` は NuGet パッケージの取得を伴うため、数分かかることがあります。
ここでエラーが出た場合は、先へ進んでも起動しません。[トラブルシューティング]({{ '/pages/troubleshooting/' | relative_url }})の「ビルドや起動でつまずく」を参照してください。

## appsettings を用意する

それぞれのプロジェクトにあるサンプルをコピーします。
PowerShell ではリポジトリのルートから次を実行できます。

```powershell
Copy-Item src\WorkAgents.Web\appsettings.example.json src\WorkAgents.Web\appsettings.Development.json
Copy-Item src\WorkAgents.Host\appsettings.example.json src\WorkAgents.Host\appsettings.Development.json
```

2つのファイルで `Runs:DatabasePath` と `Workspace:Root` を同じ値にします。
Web は表示用、Host は実行用ですが、履歴と承認を同じ SQLite から読む必要があります。
設定の意味は[設定]({{ '/pages/configuration/' | relative_url }})を参照してください。

## 起動する

リポジトリのルートで次のコマンドを実行すると、Web と Host が別ウィンドウで起動し、ブラウザで Web 画面が開きます。

```powershell
start-workagents.cmd
```

起動後に次の2つを確認します。

| 確認対象 | URL | 期待される状態 |
| --- | --- | --- |
| Web 画面 | `http://localhost:5049/` | Mission Control が表示される |
| Host | `http://localhost:5160/` | `WorkAgents.Host running` と表示される |

起動直後はブラウザーを開いてもまだ画面が表示されないことがあります。
`dotnet watch` がビルドを終えるまで待ってから、もう一度読み込んでください。

ログを同じターミナルで見たい場合は、2つのコマンドを別々のターミナルで実行します。

```powershell
dotnet run --project src\WorkAgents.Host\WorkAgents.Host.csproj --launch-profile http
dotnet run --project src\WorkAgents.Web\WorkAgents.Web.csproj --launch-profile http
```

## 初回実行前にモデルを登録する

Web 画面の `/models` を開き、エージェントが使う LLM を1つ登録します。

![Models の画面。モデル設定パネル、入力フォーム、保存ボタンに番号付きの注釈があります。]({{ '/assets/images/models.png' | relative_url }})

1. モデル設定パネル
2. Provider、endpoint、モデル名、認証情報の入力欄
3. `Save model` と既定モデルの指定

次の項目を確認してください。

- [ ] Provider を選び、モデル名または Deployment / model name を入力した
- [ ] 選んだ Provider に必要な endpoint、region、API key などを入力した
- [ ] 「Use as default model」を選んだ
- [ ] 保存後、モデル一覧に `Default` が表示された

Provider は Microsoft Foundry、OpenAI、Amazon Bedrock、OpenRouter、Anthropic (Claude)、GitHub Models (Copilot) から選べます。
どれを選ぶかで入力欄が変わるため、Provider ごとの入力内容は[設定]({{ '/pages/configuration/' | relative_url }})の「LLMモデルを登録する」を参照してください。

API キーやクライアントシークレットは Local secret store に保存され、画面には再表示されません。
登録がないままミッションを開始すると、Host はモデル未設定として受け付けを拒否します。

## 費用について

アプリ自体はローカルで動きますが、エージェントの発言やツール判断はすべて登録した LLM プロバイダーの API を呼び出します。
ミッションを1回実行するだけでも、そのプロバイダーの利用料金が発生します。
チームは複数のエージェントが会話と委譲を繰り返すため、単発の問い合わせより呼び出し回数が多くなります。

最初は次のようにすると、想定外の費用を避けやすくなります。

- 短い目標で試し、Team Room 右側のコストと反復回数を実行のたびに確認する
- 安価なモデルを既定モデルにして動作を確認してから、必要な役割だけ上位モデルへ切り替える
- プロバイダー側の利用上限やアラートを設定しておく

## 停止する

`start-workagents.cmd` で起動した場合は、開いている Host と Web の2つのコマンドウィンドウを閉じるとアプリが停止します。
個別に `dotnet run` で起動した場合は、それぞれのターミナルで `Ctrl+C` を押します。
実行中のミッションは停止と同時に中断されるため、終端状態になってから停止すると履歴を追いやすくなります。

## 配布物を起動する場合

ソースを開発せずに使う場合は、リポジトリのルートで `publish-workagents.cmd` を実行します。
生成された `dist` フォルダーをまとめて配置し、`dist\WorkAgents.Tray\WorkAgents.Tray.exe` を起動してください。
トレイメニューの「開く」で Web 画面、「更新」で定義変更後の再起動、「終了」で全プロセスの終了を行えます。
配布版のパスや追加定義ソースは[設定]({{ '/pages/configuration/' | relative_url }})を参照してください。

## 次に進む

- [概念を絵で理解する]({{ '/pages/concepts/' | relative_url }})で Web、Host、実行エンジンの関係を確認する
- [はじめてのミッション(15分)]({{ '/pages/first-mission/' | relative_url }})で `demo-team` を実行する
- [はじめてのチームを作る(画面版)]({{ '/pages/first-team/' | relative_url }})で定義エディタを試す
- [トラブルシューティング]({{ '/pages/troubleshooting/' | relative_url }})で起動できない場合を確認する
