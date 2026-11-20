# 下載

<script setup>
import rel from '../.vitepress/release.json'
const GH = 'https://github.com/Xocio/Campfire/releases'
const app = rel.app || {}
const mirror = app.drive || ''
const android = rel.android || {}
const androidMirror = android.drive || ''
const androidGithub = android.tag ? GH + '/tag/' + android.tag : GH
</script>

::: tip 網盤下載
由於用戶基數較大，國內直鏈嘅用量已經超出 Free 計劃，而家改為經**網盤**下載，中國大陸用戶唔使代理。網盤入面嘅文件以版本號命名，與 GitHub Releases 分發嘅係同一份經過數字簽名嘅文件，可使用下方嘅 SHA-256 自行核對。
:::

<DownloadRow />

## 文件信息

<table class="dl-table">
  <thead>
    <tr><th>文件</th><th>版本</th><th>下載</th></tr>
  </thead>
  <tbody>
    <tr>
      <td><code>Campfire.exe</code></td>
      <td>{{ rel.version || '—' }}</td>
      <td><template v-if="mirror"><a :href="mirror" target="_blank" rel="noopener">網盤</a> · </template><a :href="GH" target="_blank" rel="noopener">GitHub</a></td>
    </tr>
  </tbody>
</table>

<div v-if="app.sha256" class="dl-sum">

**SHA-256**

<code>{{ app.sha256 }}</code>

</div>

請僅從本站、本站提供嘅網盤連結或 GitHub Releases 獲取 Campfire，切勿使用來源不明嘅二次分發版本。核對方法：喺文件所在目錄打開終端，執行

```powershell
certutil -hashfile Campfire.exe SHA256
```

輸出嘅值應與上方完全一致。

## 系統要求

| 項目 | 要求 |
| --- | --- |
| 操作系統 | Windows 7 SP1、8、8.1、10、11 |
| 運行庫 | .NET Framework 4.8；Windows 10 與 11 已內置 |
| 多人聯機 | Windows 7 需另行安裝 KB3063858 與 KB4474419 更新 |
| 磁盤空間 | 首次聯機時需約 14 MB 存放聯機組件 |

Windows 7 與 8 如尚未安裝 .NET Framework 4.8，請從[微軟官方網站](https://dotnet.microsoft.com/download/dotnet-framework/net48)下載。

## 安裝與更新

Campfire 係免安裝嘅單文件程序，下載後可直接運行，亦可以放喺任意文件夾中長期使用。個人數據保存在 `%LocalAppData%\Campfire`，與程序所在位置無關，移動或替換程序文件唔會丟失設置。

正式版喺啟動時檢查更新。發現新版本後可以揀兩種方式：**通用更新**喺瀏覽器打開網盤，下載後替換程序文件；**GitHub** 由 Campfire 下載更新包並校驗其數字簽名，校驗通過後自動替換並重新啟動。若程序位於不可寫入嘅文件夾，**GitHub** 將改為打開 GitHub 下載頁面。

完整嘅首次使用說明見[安裝與上手](/yue/guide/getting-started)。

## Android 版

<p v-if="!rel.android">Android 版發佈後，下載地址同校驗值會顯示喺呢度。</p>

<template v-if="rel.android">

<table class="dl-table">
  <thead>
    <tr><th>文件</th><th>版本</th><th>下載</th></tr>
  </thead>
  <tbody>
    <tr>
      <td><code>Campfire.apk</code></td>
      <td>{{ android.version }}</td>
      <td><template v-if="androidMirror"><a :href="androidMirror" target="_blank" rel="noopener">網盤</a> · </template><a :href="androidGithub" target="_blank" rel="noopener">GitHub</a></td>
    </tr>
  </tbody>
</table>

<div v-if="android.sha256" class="dl-sum">

**SHA-256**

<code>{{ android.sha256 }}</code>

</div>

</template>

| 項目 | 要求 |
| --- | --- |
| 系統 | Android 7.0 及以上 |
| 處理器 | 64 位 ARM |
| 遊戲 | 一款可以運行 Minecraft Java 版嘅 Android 啟動器 |

安裝包已使用 Campfire 嘅發佈證書簽名。首次安裝時，系統可能要求容許瀏覽器或文件管理器安裝未知來源應用。更新時直接安裝新版本即可，應用數據會被保留。完整說明見[Android 版](/yue/guide/android)。

## 下載遇到問題

- **網盤下載緩慢或中斷**：未登入或者唔係會員嗰陣網盤可能限速，可以登入後再試，或者改用 GitHub 渠道。
- **瀏覽器或系統提示文件唔常見**：新發布嘅版本下載量尚少，可能觸發 SmartScreen 等信譽提示。核對 SHA-256 一致後，選擇「保留」或「仍要運行」就可以。
- **殺毒軟件攔截**：修改其他程序文件嘅工具偶爾會被誤判。Campfire 完全開源，可審閲源代碼或[自行構建](https://github.com/Xocio/Campfire#從源碼構建)。
- **冇辦法啟動**：請確認系統已安裝 .NET Framework 4.8，其他問題見[常見問題](/yue/guide/faq)。
