---
layout: page
title: 下载
description: 下载 Campfire
navbar: false
footer: false
---

<script setup>
import rel from './.vitepress/release.json'
</script>

<style>
/* 本页没有导航栏，窄屏下默认会露出一个空的“回到顶部”按钮；隐藏它，改用返回首页的链接 */
.VPLocalNav { display: none !important; }
</style>

<a class="dl-home" href="/">
  <img src="/logo.gif" alt="" />
  <span>Campfire</span>
</a>

<div class="dl-wrap">
  <header class="dl-head">
    <div class="dl-logo-wrap"><img class="dl-logo" src="/logo.gif" alt="Campfire" /></div>
    <h1 class="dl-title">下载 Campfire</h1>
    <p class="dl-sub">{{ rel.version || '先进的 Minecraft 工具箱' }}</p>
  </header>
  <DownloadCard />
  <p class="dl-foot"><a href="/guide/getting-started">安装说明</a><span class="dl-dot">·</span><a href="/download">文件与校验</a><span class="dl-dot">·</span><a href="/en/download-home">English</a></p>
</div>
