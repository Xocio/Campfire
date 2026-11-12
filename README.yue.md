<p align="center">
  <img src="docs/images/logo.png" width="128" alt="Campfire" />
</p>

<h1 align="center">Campfire</h1>

<p align="center">
  先進嘅 Minecraft 工具箱
</p>

<p align="center">
  <a href="README.md">简体中文</a> | <a href="README.zh-Hant.md">繁體中文</a> | <b>粵語</b> | <a href="README.en.md">English</a>
</p>

<p align="center">
  交流：<a href="https://t.me/CampfireGroup">Telegram 羣聊</a> · <a href="https://t.me/McCampfire">Telegram 頻道</a>
</p>

<p align="center">
  <a href="https://github.com/Xocio/Campfire/releases/latest"><img src="https://img.shields.io/github/v/release/Xocio/Campfire?label=%E4%B8%8B%E8%BC%89&color=E2680C" alt="Release" /></a>
  <a href="https://github.com/Xocio/Campfire/releases"><img src="https://img.shields.io/github/downloads/Xocio/Campfire/total?label=%E4%B8%8B%E8%BC%89%E9%87%8F&color=E2680C" alt="Downloads" /></a>
  <a href="https://mccampfire.top/yue/"><img src="https://img.shields.io/badge/%E6%96%87%E6%AA%94-mccampfire.top-E2680C" alt="Docs" /></a>
  <img src="https://img.shields.io/badge/%E5%B9%B3%E5%8F%B0-Windows%207%20%7C%208%20%7C%2010%20%7C%2011-0B5BCB" alt="Platform" />
  <a href="NETWORK.yue.md"><img src="https://img.shields.io/badge/%E6%9C%8D%E5%8B%99%E8%88%87%E5%AE%89%E5%85%A8%E9%98%B2%E8%AD%B7-Cloudflare-F38020?logo=cloudflare&logoColor=white" alt="Cloudflare" /></a>
</p>

<p align="center">
  為 Minecraft Java 版而生嘅一體化工具箱。<br />
  跨越網絡嘅多人遊戲、開放嘅聯機大廳、完整嘅地圖工作流，以及精確可逆嘅啟動器修復。
</p>

<p align="center">
  <img src="docs/images/screenshot-main.png" width="640" alt="Campfire 主頁" />
</p>

<p align="center">
  <a href="https://github.com/Xocio/Campfire/releases/latest"><b>下載</b></a> · <a href="https://mccampfire.top/yue/guide/getting-started">快速上手</a> · <a href="https://mccampfire.top/yue/">文檔</a> · <a href="CHANGELOG.md">更新日誌</a>
</p>

## 概覽

<table>
  <tr>
    <td width="50%" valign="top">
      <img src="docs/images/screenshot-lobby.png" alt="聯機大廳" />
      <h3>聯機大廳</h3>
      房間可公開至大廳，供所有 Campfire 用戶發現與加入，並支持自定義名稱、標籤、密碼與圖標。
    </td>
    <td width="50%" valign="top">
      <img src="docs/images/screenshot-maps.png" alt="在線地圖" />
      <h3>在線地圖</h3>
      檢索 CurseForge 地圖庫，按分類、遊戲版本與熱度篩選排序。
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <img src="docs/images/screenshot-map.png" alt="地圖詳情" />
      <h3>下載即導入</h3>
      選定文件就可以完成下載與導入，本機已安裝嘅對應版本一併標註。
    </td>
    <td width="50%" valign="top">
      <img src="docs/images/screenshot-import.png" alt="導入地圖" />
      <h3>版本匹配</h3>
      識別地圖嘅遊戲版本，匹配可開啟佢嘅版本，並喺進入地圖後自動對局域網開放。
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <img src="docs/images/screenshot-fixed.png" alt="啟動器修復" />
      <h3>啟動器修復</h3>
      恢復 PCL 嘅下載、整合包安裝與 JRE 支持。每項修改均經校驗，原文件自動備份，隨時可還原。
    </td>
    <td width="50%" valign="top">
      <img src="docs/images/screenshot-dark.png" alt="深色模式" />
      <h3>主題與外觀</h3>
      淺色、深色與莫奈取色，多種配色風格，輔以細膩流暢嘅過渡動效。
    </td>
  </tr>
</table>

## 特性

<table>
  <tr>
    <td width="33%" valign="top">
      <h3>邀請碼聯機</h3>
      進入單人世界後自動對局域網開放並生成邀請碼，無需公網地址或端口映射。
    </td>
    <td width="33%" valign="top">
      <h3>點對點優先</h3>
      優先建立直接連接，延遲接近局域網；必要時經中轉節點或自建節點傳輸。
    </td>
    <td width="33%" valign="top">
      <h3>遊戲內房間信息</h3>
      歡迎消息與 Tab 列表實時呈現房主、在線人數與延遲，覆蓋 1.7.2 起嘅全部正式版。
    </td>
  </tr>
  <tr>
    <td width="33%" valign="top">
      <h3>隱私為先</h3>
      支持隱藏 IP 地址，從唔讀取登錄憑據；使用統計僅喺授權後發送，內容隨時可查。
    </td>
    <td width="33%" valign="top">
      <h3>簽名更新</h3>
      新版本須通過數字簽名校驗方可安裝，任何服務器消息均冇辦法阻止 Campfire 運行。
    </td>
    <td width="33%" valign="top">
      <h3>唔限啟動器</h3>
      作用於喺度運行嘅遊戲本身，使用不同啟動器嘅玩家亦可進入同一房間。
    </td>
  </tr>
</table>

## 服務與安全

<p>
  <img src="https://img.shields.io/badge/Cloudflare-%E6%8F%90%E4%BE%9B%E6%9C%8D%E5%8B%99%E8%88%87%E5%AE%89%E5%85%A8%E9%98%B2%E8%AD%B7-F38020?logo=cloudflare&logoColor=white" alt="由 Cloudflare 提供服務與安全防護" />
</p>

Campfire 嘅更新、公告、聯機大廳與在線地圖服務由 **Cloudflare** 提供服務與安全防護，全部通信均經 HTTPS 加密。遊戲數據唔經由 Campfire 嘅服務器傳輸。

服務器下發嘅內容僅限文字與列表數據，冇辦法更改設置、執行命令或啟動程序；只有通過簽名校驗嘅更新與哈希一致嘅聯機組件先會被運行。完整說明見[聯網說明](NETWORK.yue.md)。

## 快速上手

| | 多人聯機 | 啟動器修復 |
| --- | --- | --- |
| **1** | 自 [Releases](https://github.com/Xocio/Campfire/releases/latest) 下載並運行 `Campfire.exe` | 將 `Campfire.exe` 置於啟動器所在嘅文件夾 |
| **2** | 房主按 **創建房間**，隨後進入單人世界 | 關閉 PCL，打開 **工具 › 啟動器修復工具** |
| **3** | 好友輸入邀請碼，或喺 **聯機大廳** 中選擇房間 | 按 **一鍵修復**，完成後照常啟動 PCL |

**運行環境**：Windows 7 SP1 及以上版本，.NET Framework 4.8。喺 Windows 7 上使用多人聯機，需預先安裝 KB3063858 與 KB4474419。

完整指南請參閲[文檔](https://mccampfire.top/yue/)。

<details>
<summary><b>支持嘅啟動器版本</b></summary>

| 啟動器 | 結果 |
| --- | --- |
| PCL 正式版 2.99.0.0 | ✅ 可修復 |
| PCL 正式版 2.99 之前嘅版本 | 下載功能正常，無需修復 |
| [PCL 社區版](https://github.com/PCL-Community/PCL-CE) 2.15.1 及以上 | ✅ 可解除地區限制 |
| PCL 社區版 2.15.0 及之前嘅版本 | 下載功能正常，無需修復 |

Campfire 依據方法名稱與代碼特徵定位修改位置，唔依賴固定偏移，因此通常可直接適配後續嘅小版本。遇到冇辦法識別嘅版本時，Campfire 會提示「暫唔支持」，**唔會修改任何文件**。

社區版為 .NET 單文件包。Campfire 先喺其中定位 `PCL.Core` 程序集，再原地改寫地區判定方法，文件大小與打包佈局均保持唔變，無需重新打包。

</details>

<details>
<summary><b>常見問題</b></summary>

**聯機時，數據經由誰嘅服務器傳輸？**
遊戲數據唔經由 Campfire 嘅服務器。房主與好友之間優先點對點直連，公共節點僅用於雙方互相發現；只有喺容許中轉且直連失敗時，遊戲數據先會經中轉節點傳輸。

**加入時提示「無效會話」。**
房主以正版賬號開放局域網時，遊戲會校驗每位加入者嘅身份。請從多人遊戲列表底部嘅局域網世界進入，或請房主改用離線賬號開放局域網。

**冇辦法加入房間。**
請先確認邀請碼無誤，且房主嘅房間仍處於開啟狀態。雙方網絡限制均較嚴時可能冇辦法直連，可喺聯機設置中容許中轉，或由網絡條件更好嘅一方擔任房主。失敗頁面中嘅 **查看聯機日誌** 記錄咗詳細原因。

**遊戲內未顯示 Tab 信息。**
Campfire 會將一個小型 Java Agent 附加到房主嘅遊戲進程，支持 1.7.2 及以上嘅原版，以及沿用官方名稱嘅加載器。若遊戲所用嘅 JRE 唔提供 `jdk.attach`，或類名經 Fabric、Quilt 等加載器重映射，Campfire 將退回僅喺局域網條目中顯示信息，遊戲連接唔受影響。原因記錄於 `%LocalAppData%\Campfire\agent.log`。

**啟動器修復是否安全？**
Campfire 僅修改啟動器中與下載相關嘅方法：正式版 4 處，社區版 1 處地區判定方法，且唔添加任何聯網行為。修復結果先喺內存中生成，重新加載並逐項校驗後方才寫入磁盤；任一步驟失敗，原文件均保持唔變。

**殺毒軟件報告風險。**
修改其他程序文件嘅工具偶有誤報。Campfire 完全開源，可自行審閲源代碼並從源碼構建。

**修復後，PCL 能否繼續更新？**
為避免更新覆蓋修復結果，Campfire 會停用 PCL 嘅自動更新。如需升級，請手動下載新版 PCL，再運行 Campfire 檢測是否需要修復。

**點樣撤銷修復？**
喺 Campfire 中按 **還原**；或刪除 `Plain Craft Launcher 2.exe`，再將 `Plain Craft Launcher 2.exe.bak` 恢復為原名。

</details>

<details>
<summary><b>修復原理</b></summary>

| 修復項 | 修改嘅方法 | 說明 |
| --- | --- | --- |
| Minecraft 版本列表 | `PageDownloadInstall.LoaderInit` | 下載頁重新綁定版本列表加載器 `ModDownload.DlClientListLoader` |
| 手動安裝整合包 | `ModModpack.ModpackInstall()` | 恢復文件選擇，並喺後台線程安裝 |
| 整合包安裝 | `ModModpack.ModpackInstall(File, …)` | 按識別出嘅整合包格式調用對應嘅安裝方法 |
| 停用自動更新 | `ModSecret.UpdateStart` | 更新入口直接返回 |
| 社區版地區限制 | `RegionAssessment.get_IsRestricted` | 判定方法直接返回 `false`；下載、安裝、補全、修復等入口均讀取呢一屬性 |

</details>

<details>
<summary><b>從源碼構建</b></summary>

需要 [.NET SDK](https://dotnet.microsoft.com/download) 8 或更高版本，或 Visual Studio 2022 及以上版本。

```bash
git clone https://github.com/Xocio/Campfire.git
cd Campfire
dotnet build src/Campfire/Campfire.csproj -c Release
```

亦可使用 Visual Studio 打開 `Campfire.slnx`。

附加到遊戲嘅 Java Agent 已預先構建於 `src/java-agent/dist`，構建 Campfire 無需 JDK。修改 Agent 源碼後，需使用 JDK 21 或更高版本運行 `powershell -File src/java-agent/build.ps1`，再構建 Campfire。

從源碼構建嘅版本唔連接 Campfire 嘅在線服務，都唔發送使用統計；邀請碼聯機唔受影響。

Android 版位於 `src/android`，需要 JDK 21 同 Android SDK 37。佢使用 ZeroUI 設計系統，構建前要按 `src/android/zeroui-commit.txt` 中記錄嘅提交將佢克隆到 `src/android/zeroui`：

```bash
cd src/android
git clone https://github.com/Xocio/ZeroUI.git zeroui
git -C zeroui checkout $(cat zeroui-commit.txt)
./gradlew :app:assembleDebug
```

詳見 [src/android/README.md](src/android/README.md)。

</details>

<details>
<summary><b>項目結構</b></summary>

```
Campfire/
├─ docs/                         文檔站與圖片
├─ src/Campfire/
│  ├─ Patching/                  補丁框架
│  │  ├─ Patch.cs                補丁基類
│  │  ├─ PatchCatalog.cs         全部補丁及執行順序
│  │  ├─ ModuleQuery.cs          按名稱與代碼特徵定位
│  │  ├─ IlEditor.cs             安全嘅 IL 編輯
│  │  ├─ BundleScanner.cs        喺 .NET 單文件包中定位程序集
│  │  └─ Patches/                每個修復項一個類
│  ├─ Launcher/                  啟動器檢測、修復、還原、進程檢查
│  ├─ Link/                      多人聯機：房間、網絡組件與遊戲連接代理
│  ├─ Online/                    更新檢查、公告與匿名使用統計
│  ├─ Worlds/                    地圖瀏覽、下載與導入
│  ├─ Tools/                     JRE 啟用工具
│  ├─ Pages/                     各頁面與設置
│  ├─ Controls/                  動畫與自繪控件
│  ├─ Localization/              簡體、繁體、粵語同英文字符串表
│  ├─ Themes/                    控件樣式、配色生成與壁紙取色
│  ├─ Assets/                    圖標
│  ├─ MainWindow.xaml            主窗口與頁面切換
│  ├─ TrayIcon.cs                通知區域圖標
│  └─ ILRepack.targets           構建後將 dnlib 合併進 Campfire.exe
├─ src/java-agent/               附加到房主遊戲嘅 Java Agent
├─ src/android/                  Android 應用
├─ tools/                        發佈簽名腳本
└─ Campfire.slnx
```

新增修復項：繼承 `Patch`，實現 `TextKey` / `FindTarget` / `GetState` / `Apply`，加入 `PatchCatalog.All`，並喺 `Localization` 嘅四個字符串表中添加 `Patch.<TextKey>.Title` / `Description`。社區版經由獨立嘅 `CommunityLauncher` 處理：補丁點位於單文件包內嘅 `PCL.Core` 程序集，只能原地改寫方法體。

</details>

## Android 版

Campfire Android 版同桌面版使用同一套聯機協議，手機同電腦上嘅玩家可以進入同一個房間。佢支持憑邀請碼創建同加入房間、瀏覽聯機大廳並公開自己嘅房間、房間設置、白名單、語音、正版玩家皮膚頭像、聯機診斷同節點測速，界面基於 [ZeroUI](https://github.com/Xocio/ZeroUI) 設計系統，運行於 Android 7.0 及以上版本。

源代碼位於 [`src/android`](src/android)，聯網行為見[聯網說明](NETWORK.yue.md#android-版)。

## 依賴

| 組件 | 用途 | 許可 |
| --- | --- | --- |
| [dnlib](https://github.com/0xd4d/dnlib) | 讀取與寫入 .NET 程序集 | MIT |
| [EasyTier](https://github.com/EasyTier/EasyTier) | 多人聯機嘅網絡組件，首次聯機時按需下載 | LGPL-3.0 |
| [ASM](https://asm.ow2.io) | Java 字節碼庫，隨遊戲內組件一同打包 | BSD-3-Clause |

各組件嘅許可原文見 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

Android 版嘅依賴同許可見 [src/android/THIRD_PARTY_NOTICES.md](src/android/THIRD_PARTY_NOTICES.md)。

## 說明

- Campfire 為獨立嘅社區項目，與 PCL 及其作者無關。
- 聯機大廳截圖中嘅房間為示例數據。
- 問題與建議請提交至 [Issues](https://github.com/Xocio/Campfire/issues)。

## Star History

<a href="https://www.star-history.com/?repos=xocio%2Fcampfire&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=xocio/campfire&type=date&theme=dark&legend=top-left" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=xocio/campfire&type=date&legend=top-left" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=xocio/campfire&type=date&legend=top-left" />
 </picture>
</a>
