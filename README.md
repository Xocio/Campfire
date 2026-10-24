<p align="center">
  <img src="docs/images/logo.png" width="128" alt="Campfire" />
</p>

<h1 align="center">Campfire</h1>

<p align="center">
  先进的 Minecraft 工具箱
</p>

<p align="center">
  <b>简体中文</b> | <a href="README.en.md">English</a>
</p>

<p align="center">
  友情链接：<a href="https://linux.do">LINUX DO</a>
</p>

<p align="center">
  <a href="https://github.com/Xocio/Campfire/releases/latest"><img src="https://img.shields.io/github/v/release/Xocio/Campfire?label=%E4%B8%8B%E8%BD%BD&color=1370F3" alt="Release" /></a>
  <a href="https://github.com/Xocio/Campfire/releases"><img src="https://img.shields.io/github/downloads/Xocio/Campfire/total?label=%E4%B8%8B%E8%BD%BD%E9%87%8F&color=1370F3" alt="Downloads" /></a>
  <a href="https://github.com/Xocio/Campfire/actions/workflows/build.yml"><img src="https://img.shields.io/github/actions/workflow/status/Xocio/Campfire/build.yml?label=%E6%9E%84%E5%BB%BA" alt="Build" /></a>
  <img src="https://img.shields.io/badge/%E5%B9%B3%E5%8F%B0-Windows%207%20%7C%208%20%7C%2010%20%7C%2011-0B5BCB" alt="Platform" />
</p>

<p align="center">
  <img src="docs/images/screenshot-main.png" width="640" alt="Campfire 主界面" />
</p>

## 功能

Campfire 把多人联机、启动器修复与实用工具整合进一个约 1.3 MB 的单文件程序：免安装，从 Windows 7 SP1 到 Windows 11 均可运行。

### 多人联机

- **邀请码联机**：房主对局域网开放世界后，Campfire 自动识别并生成邀请码，好友粘贴即可加入。无需公网 IP、端口映射，也不必手动填写端口；复制邀请码后，主页会直接提示加入。
- **点对点直连**：优先建立直连，延迟接近局域网；直连失败时可启用中转，也可接入自建节点。
- **游戏内房间信息**：房间直接出现在多人游戏的局域网世界列表中。进入世界后，聊天栏显示欢迎消息，Tab 页眉页脚实时显示房主、在线人数与延迟，正版验证与加密连接下同样有效。
- **房间管理**：成员列表显示正版玩家的皮肤头像；房主可移出或拉黑访客，黑名单在联机设置中统一管理。
- **跨启动器互通**：与使用其他启动器的玩家共用同一套邀请码，进入同一个房间。
- **隐私优先**：可隐藏 IP 地址、限制房间仅本机访问，从不读取任何登录凭据；房间名称、端口、节点与等待时限均可自定义。

### 启动器修复

- **恢复下载**：恢复 PCL 正式版 2.99 的 Minecraft 版本列表、游戏与 Mod 加载器安装，以及 CurseForge、Modrinth、MCBBS、HMCL、MMC 等格式的整合包安装。
- **解除地区限制**：恢复 PCL 社区版 2.15.1 起按地区关闭的下载、安装与补全。
- **启用 JRE**：解除社区版对 JRE 的默认停用，让 JRE 与 JDK 一样可用于启动游戏。
- **精准且可逆**：按方法名与代码特征定位，不依赖固定偏移；修改在内存中逐项校验通过后才写入磁盘，原文件自动备份，一键即可还原。同时停用启动器的自动更新，避免修复结果被覆盖。

### 使用体验

- **个性化主题**：浅色与深色模式；莫奈取色可从桌面壁纸生成整套配色，并提供多种配色风格。
- **流畅动效**：页面切换、弹窗与控件均有细致的过渡动画，可随时关闭。
- **后台常驻**：通知区域图标实时显示联机状态，关闭窗口后房间仍可在后台保持。
- **安全更新**：正式版内置更新检查，新版本必须通过数字签名校验才会安装。
- **双语界面**：简体中文与 English，跟随系统语言，也可随时切换。

## 使用方法

### 多人联机

1. 从 [Releases](https://github.com/Xocio/Campfire/releases/latest) 下载并打开 `Campfire.exe`，在 **主页** 点击 **多人联机**。
2. 房主：点击 **创建房间**，然后在游戏里进入单人世界，按 Esc →“对局域网开放”→“创建局域网世界”。房间建好后邀请码会自动复制，发给好友即可。
3. 好友：点击 **加入房间**，粘贴邀请码。加入后在游戏的多人游戏列表底部找到“Campfire 联机房间”，双击进入。

第一次联机时会下载约 14 MB 的联机组件，每个文件都会校验。Windows 可能会询问是否允许联机组件访问网络，选择“允许”可以提高直连成功率。联机期间请保持 Campfire 开着。

### 启动器修复

1. 从 [Releases](https://github.com/Xocio/Campfire/releases/latest) 下载 `Campfire.exe`。
2. 把它放到启动器所在的文件夹（正式版是 `Plain Craft Launcher 2.exe`，社区版是 `PCL2_CE_Release_x64.exe` 等；也可以运行后手动选择，或把启动器拖进窗口）。
3. **关闭 PCL**，打开 Campfire，在 **工具** 中点击 **启动器修复工具**，再点击 **一键修复**。

<p align="center">
  <img src="docs/images/screenshot-fixed.png" width="640" alt="修复完成" />
</p>

修复完成后直接启动 PCL 即可。修复前的启动器保存在同一文件夹下的 `Plain Craft Launcher 2.exe.bak`，需要时点击 **还原**。

**运行环境**：Windows 7 SP1 / 8 / 8.1 / 10 / 11，.NET Framework 4.8。PCL 2.99 本身同样需要 .NET Framework 4.8，能运行 PCL 的电脑就能运行 Campfire。Windows 10 / 11 已自带，Windows 7 / 8 如未安装请从[微软官网](https://dotnet.microsoft.com/download/dotnet-framework/net48)下载。Windows 7 使用多人联机前，需要通过 Windows 更新安装 KB3063858 与 KB4474419。

## 支持的版本

| 启动器 | 结果 |
| --- | --- |
| PCL 正式版 2.99.0.0 | ✅ 可修复 |
| PCL 正式版 2.99 之前的版本 | 下载功能正常，无需修复 |
| [PCL 社区版](https://github.com/PCL-Community/PCL-CE) 2.15.1 及以上 | ✅ 可修复（地区限制） |
| PCL 社区版 2.15.0 及之前的版本 | 下载功能正常，无需修复 |

Campfire 按方法名和代码特征定位修改位置，不依赖固定偏移，后续小版本大概率可以直接支持。遇到无法识别的版本时，Campfire 会提示“暂不支持”，**不会修改任何文件**。

社区版是 .NET 单文件包，Campfire 会先在其中定位 `PCL.Core` 程序集，再把地区判定方法原地改写。文件大小与打包布局都保持不变，不会重新打包。

## 常见问题

**联机时数据经过谁的服务器？**
游戏数据不经过 Campfire 的服务器。房主和好友之间优先点对点直连，公共节点只用来帮双方找到对方；只有在你开启中转、且直连失败时，游戏数据才会经过中转节点。

**加入时提示“无效会话”？**
房主用正版账号开局域网时，游戏会校验加入者的正版身份。让房主改用离线账号开局域网即可。

**加入不了房间怎么办？**
先确认邀请码正确、房主的房间还开着。双方网络都比较严格（如 NAT4）时可能无法直连，可以在联机设置中开启中转，或换一位网络更好的玩家当房主。失败页面的“查看联机日志”可以看到详细原因。

**游戏内 Tab 信息没有显示？**
Campfire 会尝试把一个很小的 Java Agent 附加到房主的游戏进程，由服务端直接发送欢迎消息和 Tab 页眉页脚。当前支持原版 26.3 / 26.4；如果游戏使用不支持 `jdk.attach` 的 JRE，或类名被模组加载器重映射，会自动回退到局域网条目（多人游戏列表底部），不影响游戏连接。原因会写入 `%LocalAppData%\Campfire\agent.log`。

**修复安全吗？**
Campfire 只修改启动器里与下载有关的方法：正式版 4 个，社区版 1 个地区判定方法（`RegionAssessment.IsRestricted`），不添加任何联网行为。修复结果会先在内存中生成，重新加载并逐项校验通过后才写入磁盘；任一步失败都不会改动原文件。

**杀毒软件报毒怎么办？**
修改其他程序文件的工具偶尔会被误报。Campfire 完全开源，可以自行审阅源码并[从源码构建](#从源码构建)。

**修复后 PCL 能更新吗？**
为防止更新覆盖修复结果，Campfire 会停用 PCL 的自动更新。需要升级时，请手动下载新版 PCL，再运行一次 Campfire 检测是否需要修复。

**怎么撤销修复？**
打开 Campfire，点击 **还原**；或者删除 `Plain Craft Launcher 2.exe`，再把 `Plain Craft Launcher 2.exe.bak` 改回原名。

## 修复原理

| 修复项 | 修改的方法 | 说明 |
| --- | --- | --- |
| Minecraft 版本列表 | `PageDownloadInstall.LoaderInit` | 下载页重新绑定版本列表加载器 `ModDownload.DlClientListLoader` |
| 手动安装整合包 | `ModModpack.ModpackInstall()` | 恢复文件选择，并在后台线程安装 |
| 整合包安装 | `ModModpack.ModpackInstall(File, …)` | 按识别出的整合包格式调用对应的安装方法 |
| 停用自动更新 | `ModSecret.UpdateStart` | 更新入口直接返回 |
| 社区版地区限制 | `RegionAssessment.get_IsRestricted` | 判定方法直接返回 `false`；下载、安装、补全、修复等入口都读这一个属性 |

## 从源码构建

需要 [.NET SDK](https://dotnet.microsoft.com/download) 8 或更高版本，或 Visual Studio 2022 及以上版本。

```bash
git clone https://github.com/Xocio/Campfire.git
cd Campfire
dotnet build src/Campfire/Campfire.csproj -c Release
```

也可以用 Visual Studio 打开 `Campfire.slnx`。

联机时附加到游戏的 Java Agent 已预先构建在 `src/java-agent/dist`，构建 Campfire 不需要 JDK。修改 Agent 源码后，需要 JDK 21 或更高版本，先运行 `powershell -File src/java-agent/build.ps1`，再构建 Campfire。

从源码构建的版本不会连接 Campfire 的更新与公告服务，也不会发送使用统计。

## 项目结构

```
Campfire/
├─ docs/                         文档与图片
├─ src/Campfire/
│  ├─ Patching/                  补丁框架
│  │  ├─ Patch.cs                补丁基类
│  │  ├─ PatchCatalog.cs         全部补丁及执行顺序
│  │  ├─ ModuleQuery.cs          按名称与代码特征定位
│  │  ├─ IlEditor.cs             安全的 IL 编辑
│  │  ├─ BundleScanner.cs        在 .NET 单文件包中定位程序集
│  │  └─ Patches/                每个修复项一个类
│  ├─ Launcher/                  启动器检测、修复、还原、进程检查
│  ├─ Link/                      多人联机：房间、网络组件与游戏连接代理
│  ├─ Online/                    更新检查、公告与匿名使用统计
│  ├─ Tools/                     JRE 启用工具
│  ├─ Pages/                     各页面与设置
│  ├─ Controls/                  动画与自绘控件
│  ├─ Localization/              中英文字符串表
│  ├─ Themes/                    控件样式、配色生成与壁纸取色
│  ├─ Assets/                    图标
│  ├─ MainWindow.xaml            主窗口与页面切换
│  ├─ TrayIcon.cs                通知区域图标
│  └─ EmbeddedAssemblyLoader.cs  加载嵌入的依赖库
├─ src/java-agent/               附加到房主游戏、发送欢迎消息与 Tab 信息的 Java Agent
├─ tools/                        发布签名脚本
└─ Campfire.slnx
```

新增修复项：继承 `Patch`，实现 `TextKey` / `FindTarget` / `GetState` / `Apply`，加入 `PatchCatalog.All`，并在 `Localization/Text.Chinese.cs` 与 `Text.English.cs` 中添加 `Patch.<TextKey>.Title` / `Description`。社区版走单独的 `CommunityLauncher`：补丁点在单文件包内的 `PCL.Core` 程序集里，只能原地改写方法体。

## 依赖

- [dnlib](https://github.com/0xd4d/dnlib)：读取与写入 .NET 程序集，MIT 许可
- [EasyTier](https://github.com/EasyTier/EasyTier)：多人联机使用的网络组件，首次联机时按需下载，LGPL-3.0 许可
- [ASM](https://asm.ow2.io)：Java 字节码库，打包在联机时附加到游戏的组件中，BSD-3-Clause 许可

各组件的许可原文见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

## 说明

- Campfire 是社区项目，与 PCL 及其作者无关。
- 修复只在本机进行，不上传、不下载任何文件。
- 正式发布版会检查更新与公告；匿名使用统计只在同意后发送，发送内容可在 设置 › 常规 中查看，也可随时关闭。
- 问题反馈请提交 [Issue](https://github.com/Xocio/Campfire/issues)。