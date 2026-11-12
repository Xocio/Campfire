---
layout: page
title: 下載
description: 下載 Campfire
navbar: false
footer: false
---

<script setup>
import rel from '../.vitepress/release.json'
</script>

<style>
/* 隱藏窄屏本地導航欄 */
.VPLocalNav { display: none !important; }
</style>

<a class="dl-home" href="/yue/">
  <img src="/logo.gif" alt="" />
  <span>Campfire</span>
</a>

<div class="dl-wrap">
  <header class="dl-head">
    <div class="dl-logo-wrap"><img class="dl-logo" src="/logo.gif" alt="Campfire" /></div>
    <h1 class="dl-title">下載 Campfire</h1>
    <p class="dl-sub">{{ rel.version || '先進嘅 Minecraft 工具箱' }}</p>
  </header>
  <DownloadCard />
  <DownloadCard android />
  <p class="dl-foot"><a href="/yue/guide/getting-started">安裝說明</a><span class="dl-dot">·</span><a href="/yue/download">文件與校驗</a><span class="dl-dot">·</span><a href="/download-home">简体中文</a><span class="dl-dot">·</span><a href="/zh-hant/download-home">繁體中文</a><span class="dl-dot">·</span><a href="/en/download-home">English</a></p>
</div>
