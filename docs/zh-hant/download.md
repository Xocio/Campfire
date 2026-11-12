# 下載

<script setup>
import rel from '../.vitepress/release.json'
const GH = 'https://github.com/Xocio/Campfire/releases'
const app = rel.app || {}
const mirror = rel.mirror && app.file ? rel.mirror.replace(/\/?$/, '/') + app.file : ''
const android = rel.android || {}
const androidMirror = rel.mirror && android.file ? rel.mirror.replace(/\/?$/, '/') + android.file : ''
const androidGithub = android.tag ? GH + '/tag/' + android.tag : GH
</script>

::: tip 國內直鏈
本站為中國大陸使用者提供**國內直鏈**，無需代理即可高速下載。直鏈與 GitHub Releases 分發的是同一份經過數字簽名的檔案，可使用下方的 SHA-256 自行核對。
:::

<DownloadRow />

## 檔案資訊

<table class="dl-table">
  <thead>
    <tr><th>檔案</th><th>版本</th><th>下載</th></tr>
  </thead>
  <tbody>
    <tr>
      <td><code>Campfire.exe</code></td>
      <td>{{ rel.version || '—' }}</td>
      <td><template v-if="mirror"><a :href="mirror">國內直鏈</a> · </template><a :href="GH" target="_blank" rel="noopener">GitHub</a></td>
    </tr>
  </tbody>
</table>

<div v-if="app.sha256" class="dl-sum">

**SHA-256**

<code>{{ app.sha256 }}</code>

</div>

請僅從本站或 GitHub Releases 獲取 Campfire，切勿使用來源不明的二次分發版本。核對方法：在檔案所在目錄開啟終端，執行

```powershell
certutil -hashfile Campfire.exe SHA256
```

輸出的值應與上方完全一致。

## 系統要求

| 專案 | 要求 |
| --- | --- |
| 作業系統 | Windows 7 SP1、8、8.1、10、11 |
| 執行庫 | .NET Framework 4.8；Windows 10 與 11 已內建 |
| 多人連線 | Windows 7 需另行安裝 KB3063858 與 KB4474419 更新 |
| 磁碟空間 | 首次連線時需約 14 MB 存放連線元件 |

Windows 7 與 8 如尚未安裝 .NET Framework 4.8，請從[微軟官方網站](https://dotnet.microsoft.com/download/dotnet-framework/net48)下載。

## 安裝與更新

Campfire 是免安裝的單檔案程式，下載後可直接執行，也可以放在任意資料夾中長期使用。個人資料儲存在 `%LocalAppData%\Campfire`，與程式所在位置無關，移動或替換程式檔案不會丟失設定。

正式版在啟動時檢查更新。發現新版本後，Campfire 會先徵求同意，再下載更新包並校驗其數字簽名，校驗通過後自動替換並重新啟動。若程式位於不可寫入的資料夾，將改為開啟本頁供手動下載。

完整的首次使用說明見[安裝與上手](/zh-hant/guide/getting-started)。

## Android 版

<p v-if="!rel.android">Android 版發布後，下載位址與校驗值將顯示於此。</p>

<template v-if="rel.android">

<table class="dl-table">
  <thead>
    <tr><th>檔案</th><th>版本</th><th>下載</th></tr>
  </thead>
  <tbody>
    <tr>
      <td><code>Campfire.apk</code></td>
      <td>{{ android.version }}</td>
      <td><template v-if="androidMirror"><a :href="androidMirror">國內直鏈</a> · </template><a :href="androidGithub" target="_blank" rel="noopener">GitHub</a></td>
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
| 處理器 | 64 位元 ARM |
| 遊戲 | 一款能夠執行 Minecraft Java 版的 Android 啟動器 |

安裝套件已使用 Campfire 的發布憑證簽署。首次安裝時，系統可能要求允許瀏覽器或檔案管理器安裝未知來源應用程式。更新時直接安裝新版本即可，應用程式資料會被保留。完整說明見[Android 版](/zh-hant/guide/android)。

## 下載遇到問題

- **直鏈下載緩慢或中斷**：更換網路後重試，或改用 GitHub 渠道。
- **瀏覽器或系統提示檔案不常見**：新發布的版本下載量尚少，可能觸發 SmartScreen 等信譽提示。核對 SHA-256 一致後，選擇「保留」或「仍要執行」即可。
- **防毒軟體攔截**：修改其他程式檔案的工具偶爾會被誤判。Campfire 完全開源，可審閱原始碼或[自行構建](https://github.com/Xocio/Campfire#從原始碼構建)。
- **無法啟動**：請確認系統已安裝 .NET Framework 4.8，其他問題見[常見問題](/zh-hant/guide/faq)。
