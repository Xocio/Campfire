---
layout: page
title: Download
description: Download Campfire
navbar: false
footer: false
---

<script setup>
import rel from '../.vitepress/release.json'
</script>

<style>
/* No navbar on this page; hide the empty local nav that narrow screens would otherwise show */
.VPLocalNav { display: none !important; }
</style>

<a class="dl-home" href="/en/">
  <img src="/logo.gif" alt="" />
  <span>Campfire</span>
</a>

<div class="dl-wrap">
  <header class="dl-head">
    <div class="dl-logo-wrap"><img class="dl-logo" src="/logo.gif" alt="Campfire" /></div>
    <h1 class="dl-title">Download Campfire</h1>
    <p class="dl-sub">{{ rel.version || 'An advanced Minecraft toolbox' }}</p>
  </header>
  <DownloadCard />
  <p class="dl-foot"><a href="/en/guide/getting-started">Setup guide</a><span class="dl-dot">·</span><a href="/en/download">Files and checksums</a><span class="dl-dot">·</span><a href="/download-home">简体中文</a></p>
</div>
