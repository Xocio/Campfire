# 更新日志

## v1.1.1

本版本带来联机大厅与完整的地图工作流：公开房间可在大厅中被发现与加入，CurseForge 地图可直接检索、下载并导入，随后即可创建房间。创建房间时可自动对局域网开放，游戏内房间信息扩展至 1.7.2 及以上的全部正式版。更新机制同步加固，全部联网行为亦已公开说明。

### 联机大厅

- 主页新增“联机大厅”入口，与创建房间、加入房间并列
- 大厅列出全部公开房间，支持按房间名称、房主或地图检索，并按游戏版本筛选；每个房间显示图标、房主、地图、游戏版本、标签、人数与开启时间
- 房间详情页显示游戏版本与大致地区，加入时自动领取邀请码；设有密码的房间需验证密码
- 房主可在房间页将房间公开至大厅，并设定房间名称、至多 3 个标签、密码与图标；图标可直接选用地图自带的图片，所在省份可选择是否显示
- 关闭房间时自动取消公开，亦可随时手动取消
- 支持举报名称或内容不当的房间，经多名玩家举报后自动自大厅隐藏

### 地图

- 新增“地图”标签页：检索 CurseForge 地图库，支持按分类与游戏版本筛选，按热门、下载量、更新时间、发布时间或名称排序
- 地图详情页呈现介绍、支持的游戏版本与文件列表，选定文件即可下载并导入；作者仅允许网站下载的地图可前往 CurseForge 获取，下载后拖入 Campfire 窗口即可导入
- 新增地图导入：支持 zip 压缩包、地图文件夹与拖放，识别地图的游戏版本、游戏模式与所用模组，并列出本机能够开启它的版本
- 导入前明示风险：跨越 1.13 升级可能导致命令与红石失效，缺少对应加载器时模组内容将会丢失；基岩版地图以及损坏或不完整的地图均有明确提示
- 地图自动置入所选版本的存档文件夹；游戏启动后若实际存档位置不同，将自动迁移
- 导入完成后即可创建房间，并在进入地图后自动对局域网开放

### 多人联机

- 创建房间时自动对局域网开放：进入单人世界后，Campfire 代为执行“对局域网开放”，并沿用世界原有的游戏模式与作弊设置；可在联机设置中关闭
- 游戏内欢迎消息与 Tab 信息由原版 26.3 / 26.4 扩展至 1.7.2 及以上的全部正式版：1.14.4 至 1.21.11 按需下载并校验 Mojang 官方映射表，更早的版本使用 Campfire 内置的名称表
- 同样支持沿用官方名称的加载器；Fabric 与 Quilt 仍需手动对局域网开放

### 安全

- 必须更新仅在签名正确的新版本已下载并通过校验后生效；下载或校验失败时 Campfire 照常可用，任何服务器消息均无法阻止其启动
- 更新签名支持多把公钥，发布密钥一旦泄露亦可平稳轮换
- 新增联网说明 NETWORK.md，文档站同步提供，逐项列出 Campfire 的联网行为、传输内容、关闭方式，以及每一项为何无法控制本机

## v1.1.0

PCL++ 更名为 Campfire，从启动器修复工具扩展为 Minecraft 工具箱：新增多人联机、PCL 社区版修复与 JRE 启用工具。设置、已下载的联机组件等数据会在首次启动时自动迁移。

### 多人联机

- 房主在游戏里对局域网开放后，Campfire 自动识别并生成邀请码；好友输入邀请码即可加入，复制了邀请码时主页会直接提示加入
- 点对点直连，不需要公网 IP 和端口映射；直连不上时可以选择走中转，也可以填写自己的节点
- 房间始终显示在本机多人游戏的局域网世界列表中，条目实时显示房主、在线人数、延迟与 McCampfire.top；离线账号从这里进入即可，不会再提示“无效会话”
- 进入世界后，聊天栏显示欢迎消息，Tab 页眉页脚显示房主、在线人数、延迟与 McCampfire.top；由房主游戏内的 Java Agent 在服务端直接发送，正版验证和加密连接下同样生效，房主自己也能看到
- Java Agent 不关闭正版验证、不读取登录凭据；当前面向原版 26.3 / 26.4，其他版本或改名后的模组环境会安全失败并回退到局域网条目，游戏连接不受影响
- 正版玩家在房间列表中显示皮肤头像
- 房主可在玩家列表中移出或拉黑访客，被拉黑的玩家无法再加入房间，可在联机设置的黑名单中解除
- 与使用其他启动器的玩家互通，同一个邀请码就能进同一个房间
- 隐私选项：隐藏 IP 地址、限制局域网访问、控制是否读取游戏账号与共享版本信息、重置设备标识
- 联机设置页：房间名称、本地端口、寻找房主时限与节点等均可自定义
- 输入错误与联机失败时以弹窗提示原因和解决办法；玩家名称含有不支持的字符时给出提醒
- 多人联机支持 Windows 7 SP1，需要安装 KB3063858 与 KB4474419 两个更新

### 启动器修复

- 新增 PCL 社区版修复：解除 2.15.1 起按地区关闭的 Minecraft 下载、安装与补全
- 社区版是 .NET 单文件包，Campfire 在包内定位 `PCL.Core` 程序集，原地改写判定方法 `RegionAssessment.IsRestricted`，文件大小与打包布局保持不变，无需重新打包
- 自动区分官方版与社区版：官方版仍按原有 4 个修复项处理，社区版只改 1 个判定方法；社区版 2.15.0 及更早版本没有该限制，会提示“无需修复”
- 社区版修复同样备份为 `.bak`，可一键还原

### JRE 启用工具

- 工具箱新增“JRE 启用工具”，独立于启动器修复：PCL 社区版会把没有 `javac.exe` 的 Java 即 JRE 默认停用，Java 9 及以上的 JRE 一律不可选，导致无法用 JRE 启动游戏；处理后 JRE 与 JDK 一样默认可选
- 原地改写 `PCL.Core` 中的 `JavaManager._ShouldEnableByDefault`，只去掉 JRE 的两条限制，保留“系统位数必须与 Java 位数一致”的检查；改动 21 个字节，文件大小与打包布局不变
- 备份保存为启动器旁的 `.jre.bak`，与启动器修复的 `.bak` 互不影响，可单独还原
- 官方版没有此限制，选择官方版或更早的社区版时会提示无需处理；处理前已被启动器记录为“停用”的 JRE，需要在启动器的 Java 列表中手动启用一次

### 界面与体验

- 界面改为“主页 / 工具 / 设置”三个标签页：主页以多人联机为主，可直接创建或加入房间并查看房间状态，附有联机步骤说明；启动器修复工具与 JRE 启用工具位于工具中
- 首次启动时提供新手引导，介绍主要功能
- 默认采用与篝火图标相配的橙色主题，标题栏图标为动态篝火
- 设置标签页：浅色与深色主题，莫奈取色可从桌面壁纸提取颜色自动生成整套配色，并提供多种配色风格；关于与开放源代码许可也位于设置中
- 页面切换、弹窗、选项与按钮均有过渡动画，可在设置中关闭
- 通知区域图标显示联机状态，可复制邀请码；可设置关闭窗口时保留在后台
- 界面文案统一为书面表述：选项的补充说明改为选项旁的“?”图标，鼠标移上去显示提示

### 更新与隐私

- 新增更新检查与公告：发现新版本时提示，下载后必须通过数字签名校验才会安装
- 匿名使用统计首次启动时询问，同意后每日至多发送一次，不含 IP 地址、游戏账号、玩家名称及房间信息；发送内容可在 设置 › 常规 中查看，也可随时关闭
- 从源码构建的版本不连接更新与公告服务，也不发送统计

### 使用说明

1. 下载下方的 `Campfire.exe` 并打开，在主页点击“多人联机”。
2. 房主点击“创建房间”，在游戏里对局域网开放；好友点击“加入房间”，粘贴邀请码。
3. 修复启动器：把 `Campfire.exe` 放到启动器所在文件夹，关闭 PCL 后在工具中打开“启动器修复工具”，点击“一键修复”。

第一次联机会下载约 14 MB 的联机组件。Windows 7 使用多人联机前，请先通过 Windows 更新安装 KB3063858 与 KB4474419。

### English

- PCL++ is now called Campfire and grows from a launcher repair tool into a Minecraft toolbox with multiplayer, Community Edition repair and a JRE tool. Settings and the downloaded networking component are moved over on first start.
- New multiplayer: open a world to LAN and Campfire creates an invite code; friends join with the code, and a copied code can be joined right from the Home tab.
- Direct peer-to-peer connections with no public IP or port forwarding; optional relay and custom nodes.
- The room always appears under LAN worlds in this computer's multiplayer list and shows the host, player count, latency and McCampfire.top. Offline accounts enter from there without the invalid session error.
- Once in the world, the chat shows a welcome message and the Tab header/footer shows the host, player count, latency and McCampfire.top. A Java agent inside the host's game sends them from the server, so authenticated and encrypted LAN worlds work too, including the host's own client.
- The agent never disables authentication or reads credentials. It targets vanilla 26.3 / 26.4 for now; other versions or remapped modded runtimes fail safely, fall back to the LAN entry and never affect the game connection.
- Premium players show their skin face in the room.
- The host can remove or block guests from the player list; blocked players cannot join again until unblocked in the Multiplayer Settings blocklist.
- Works with players on other launchers: the same invite code leads to the same room.
- Privacy options: hide your IP address, keep the room off the local network, choose whether the game account is read and the version is shared, and reset the device identity.
- A Multiplayer Settings page for the room name, local port, host search limit, nodes and more.
- Input mistakes and multiplayer failures are explained in a dialog; a warning appears when the player name contains characters Minecraft rejects.
- Multiplayer runs on Windows 7 SP1 with the updates KB3063858 and KB4474419.
- Adds repair support for PCL Community Edition 2.15.1 and later: Campfire locates the bundled `PCL.Core` assembly and rewrites `RegionAssessment.IsRestricted` in place, keeping the file size and bundle layout. Official and Community editions are detected automatically, and the original is backed up as `.bak`.
- Adds a "JRE Tool": PCL Community Edition switches off every JRE, a Java without `javac.exe`, by default, and any JRE newer than Java 8 can never be selected. The tool rewrites `JavaManager._ShouldEnableByDefault` in place, removing only the two JRE conditions while the architecture check stays; 21 bytes change. The backup is saved as `.jre.bak`, independent of the launcher repair. A JRE the launcher already saved as disabled must be enabled once in its Java list.
- The window now has Home, Tools and Settings tabs. Home is built around multiplayer; the Launcher Repair Tool and the JRE Tool are under Tools. A short tour introduces the main features on first start.
- An orange theme by default that matches the campfire logo, which is animated in the title bar.
- A new Settings tab: light and dark themes, Monet colors from your desktop wallpaper in several palette styles, About and the open source licenses.
- Animated page changes, dialogs, options and buttons, which can be turned off in Settings.
- A notification area icon shows the multiplayer status and can copy the invite code; Campfire can keep running there when the window is closed.
- Interface copy is unified in a formal register; option explanations become a "?" mark that shows the text on hover.
- Update check and notices: a new version is installed only if its digital signature checks out.
- Anonymous usage statistics are asked for on first start and sent at most once a day without IP address, game account, player name or room details. You can see what is sent and turn it off under Settings › General. Builds from source never contact the service.

Multiplayer downloads a networking component of about 14 MB the first time. On Windows 7, install KB3063858 and KB4474419 through Windows Update first.

## v1.0.1

### 新功能

- 新增英文界面：跟随系统语言自动选择，也可点击标题栏的语言按钮切换，选择会被记住
- 支持 Windows 7 SP1、Windows 8 与 Windows 8.1

### 优化

- 在没有“微软雅黑 UI”字体的系统上自动改用“微软雅黑”显示界面

### 使用说明

1. 下载下方的 `PCL++.exe`，放到 `Plain Craft Launcher 2.exe` 所在的文件夹。
2. 关闭 PCL，打开 PCL++，点击“一键修复”。

运行环境：Windows 7 SP1 / 8 / 8.1 / 10 / 11，.NET Framework 4.8（与 PCL 2.99 相同）。Windows 7 / 8 如未安装 .NET Framework 4.8，请先从微软官网下载安装。

### English

- New English interface. It follows the system language and can be switched from the title bar; the choice is remembered.
- Supports Windows 7 SP1, Windows 8 and Windows 8.1.
- Falls back to "Microsoft YaHei" on systems without "Microsoft YaHei UI".

Usage: put `PCL++.exe` next to `Plain Craft Launcher 2.exe`, close PCL, open PCL++ and click **Repair**. Requires .NET Framework 4.8 (the same as PCL 2.99).

## v1.0.0

PCL++ 的第一个版本，提供 PCL 正式版 2.99.0.0 的 Minecraft 下载功能修复。

### 新功能

- 一键修复：恢复“下载”页的 Minecraft 版本列表，以及游戏与 Mod 加载器的安装
- 恢复“安装整合包”按钮的文件选择功能
- 恢复 CurseForge、Modrinth、MCBBS、HMCL、MMC 等格式整合包的安装
- 停用启动器自动更新，避免修复结果被覆盖
- 修复前自动备份原启动器，可随时一键还原
- 自动查找同一文件夹中的启动器，也支持手动选择或拖入窗口
- 识别 PCL 社区版和 2.99 之前的版本，提示无需修复

### 使用说明

1. 下载下方的 `PCL++.exe`，放到 `Plain Craft Launcher 2.exe` 所在的文件夹。
2. 关闭 PCL，打开 PCL++，点击“一键修复”。

运行环境：Windows 10 / 11，.NET Framework 4.8（系统自带）。

### 注意事项

- 修复后 PCL 不再自动更新。升级 PCL 后，请重新运行 PCL++ 检测是否需要修复。
- 修改程序文件的工具可能被杀毒软件误报，PCL++ 的全部源码均已公开。
