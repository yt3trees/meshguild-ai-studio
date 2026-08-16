---
title: MeshGuild AI Studio マニュアル
description: MeshGuild AI Studio(WorkAgents)の基本的な使い方をまとめたマニュアルサイトです。
layout: default
---
<article class="wa-panel">
  <h1 class="wa-page-title">MeshGuild AI Studio マニュアル</h1>
  <p class="wa-page-desc">MeshGuild AI Studio の基本的な使い方、機能、設定方法を紹介します。上から順に「はじめる」を読み、必要になったら作成、運用、困ったときのページへ進んでください。</p>

  {% for section in site.data.nav %}
    <section class="wa-home-section">
      <h2>{{ section.title }}</h2>
      {% if section.children %}
        <div class="wa-home-links">
          {% for child in section.children %}
            <a class="wa-home-link" href="{{ child.path | relative_url }}">
              <span>{{ child.title }}</span>
              <span aria-hidden="true">→</span>
            </a>
          {% endfor %}
        </div>
      {% elsif section.path %}
        <a class="wa-home-link" href="{{ section.path | relative_url }}">
          <span>{{ section.title }}</span>
          <span aria-hidden="true">→</span>
        </a>
      {% endif %}
    </section>
  {% endfor %}

</article>
