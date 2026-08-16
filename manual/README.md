# マニュアルサイトの運用方法

このディレクトリは GitHub Pages(Jekyll)で公開するマニュアルサイトのソースです。`main` ブランチへの変更が [.github/workflows/manual-pages.yml](../.github/workflows/manual-pages.yml) によって自動的にビルド・公開されます。

## ローカルでの確認

```bash
cd manual
bundle install
bundle exec jekyll build --strict
bundle exec jekyll serve
```

`http://127.0.0.1:4000` でサイトを確認できます。

## 新しいページを追加する

1. `manual/_pages/` に Markdown ファイルを追加する。Front Matter は以下の形式に従う([../specs/003-github-pages-manual/contracts/page-front-matter.md](../specs/003-github-pages-manual/contracts/page-front-matter.md) を参照)。

   ```yaml
   ---
   title: ページタイトル
   description: ページの概要(任意)
   layout: page
   ---
   ```

2. `manual/_data/nav.yml` に対応するエントリを追加する([../specs/003-github-pages-manual/contracts/navigation-data.md](../specs/003-github-pages-manual/contracts/navigation-data.md) を参照)。既存のセクションへ追加する場合は `children` に入れる。

   ```yaml
   - title: ページタイトル
     path: /pages/<ファイル名>/
     order: <表示順>
   ```

3. `bundle exec jekyll build --strict` でエラーがないことを確認してからコミットする。

追加のビルド設定変更は不要で、Markdownファイルの追加と `nav.yml` の更新だけでサイトに反映される。

## ファイル名・URLの注意

URL(`path`)は英数字とハイフンで構成することを推奨する。日本語ファイル名や日本語スラッグは環境によってリンク解決やURLエンコードの挙動が異なる場合があるため、ファイル名・`path` は ASCII 表記とし、日本語はページタイトルや本文側で表現する。
