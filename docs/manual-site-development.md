# マニュアルサイト(manual/)の開発環境構築

利用者向けマニュアルサイトは [manual/](../manual/) 配下の Jekyll サイトとして管理している。ローカルでビルド・表示確認するには Ruby と Bundler が必要になる。

## 1. Ruby のインストール(Windows)

winget で RubyInstaller(DevKit 同梱版)を導入する。DevKit がないと `github-pages` gem が依存するネイティブ拡張(nokogiri など)のビルドに失敗するため、`RubyWithDevKit` 系を選ぶこと。

```powershell
winget install --id RubyInstallerTeam.RubyWithDevKit.3.2 -e --source winget
```

インストール後、新しいターミナルを開くか `PATH` を再読み込みしてから、バージョンを確認する。

```powershell
ruby -v
gem -v
```

`github-pages` gem は GitHub Pages 本番環境が対応する Ruby バージョンに追従するため、3.2系を推奨する(新しすぎるRubyでは依存gemの解決に失敗する場合がある)。

## 2. 依存gemのインストール

```powershell
cd manual
bundle install
```

## 3. ビルド検証

Front Matter の不備やLiquidエラーを検知するため、`--strict` オプション付きでビルドする。

```powershell
bundle exec jekyll build --strict
```

エラーなく完了すると `manual/_site/` に静的HTMLが生成される。

## 4. ローカルサーバーでの確認

```powershell
bundle exec jekyll serve
```

`http://127.0.0.1:4000` をブラウザで開いて表示を確認する。

## 5. ページの追加・更新

新しいページの追加手順やナビゲーションへの登録方法は [manual/README.md](../manual/README.md) を参照する。

## 6. 公開の仕組み

`main` ブランチへの `manual/` 配下の変更は [.github/workflows/manual-pages.yml](../.github/workflows/manual-pages.yml) によって自動的にビルド・GitHub Pagesへデプロイされる。リポジトリの Settings > Pages で Source を「GitHub Actions」に設定しておくこと。
