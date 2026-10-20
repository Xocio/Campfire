# 更新日志

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
