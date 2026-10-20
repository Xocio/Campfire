using System.Collections.Generic;

namespace PCLPlus.Localization
{
    public static partial class Text
    {
        private static readonly Dictionary<string, string> Chinese = Table(
            // Title bar
            "App.Tag", "修复工具",
            "App.Minimize", "最小化",
            "App.Close", "关闭",
            "App.LanguageButton", "EN",
            "App.LanguageTooltip", "Switch to English",

            // Main page
            "Main.Launcher", "启动器",
            "Main.NoLauncher", "未选择启动器",
            "Main.PickHint", "点击右侧按钮选择 Plain Craft Launcher 2.exe，或直接把它拖到本窗口",
            "Main.Browse", "选择文件",
            "Main.OpenFolder", "打开所在文件夹",
            "Main.LaunchPcl", "启动 PCL",
            "Main.Patches", "修复内容",
            "Main.About", "关于",
            "Main.AboutTooltip", "版本、作者与开源仓库",
            "Main.Restore", "还原",
            "Main.RestoreTooltip", "用备份文件恢复修复前的启动器",
            "Main.Fix", "一键修复",
            "Main.Fixing", "正在修复……",
            "Main.Fixed", "已修复",

            // Status messages
            "Status.Searching", "正在查找启动器……",
            "Status.NotFoundNearby", "没有在 PCL++ 所在的文件夹中找到启动器，请点击“选择文件”手动选择。",
            "Status.Inspecting", "正在检测启动器……",
            "Status.InspectFailed", "检测失败：{0}",
            "Status.Fixing", "正在修复，请稍候……",
            "Status.FixDone", "修复完成！现在可以启动 PCL 下载游戏了。原文件已备份为 {0}。",
            "Status.FixUnexpected", "出现意外错误，启动器未被修改：{0}",
            "Status.Restoring", "正在还原……",
            "Status.Restored", "已还原为修复前的启动器。",
            "Status.Unexpected", "出现意外错误：{0}",
            "Status.LaunchFailed", "启动失败：{0}",

            // Dialogs
            "Dialog.SelectTitle", "选择 PCL 启动器",
            "Dialog.SelectFilter", "PCL 启动器 (*.exe)|*.exe",
            "Dialog.BrowserFailed", "无法打开浏览器，请手动访问：\n{0}\n\n{1}",

            // About page
            "About.Header", "关于",
            "About.Summary", "PCL 正式版的增强与修复工具，开源免费。",
            "About.AuthorRole", "作者 · github.com/Xocio",
            "About.Homepage", "主页",
            "About.RepoSummary", "源代码、问题反馈与新版本下载",
            "About.Repository", "仓库",
            "About.Notes", "说明",
            "About.Note1", "· PCL++ 是社区项目，与 PCL 及其作者无关。",
            "About.Note2", "· 修复只在本机进行，不上传、不下载任何文件；原启动器会自动备份，可随时还原。",
            "About.Note3", "· 修复后启动器不再自动更新，升级 PCL 后请重新运行 PCL++。",
            "About.Back", "返回",

            // Patch states
            "PatchState.Pending", "待修复",
            "PatchState.Restored", "已恢复",
            "PatchState.Disabled", "已停用",
            "PatchState.Normal", "正常",
            "PatchState.NotNeeded", "无需修复",
            "PatchState.NotFound", "未找到",

            // Patches
            "Patch.VersionList.Title", "Minecraft 版本列表",
            "Patch.VersionList.Description", "恢复“下载”页的游戏版本列表与安装功能",
            "Patch.ModpackPicker.Title", "手动安装整合包",
            "Patch.ModpackPicker.Description", "恢复“安装整合包”按钮的文件选择功能",
            "Patch.ModpackDispatch.Title", "整合包安装",
            "Patch.ModpackDispatch.Description", "恢复 CurseForge、Modrinth、MCBBS 等整合包的安装",
            "Patch.UpdateBlock.Title", "停用自动更新",
            "Patch.UpdateBlock.Description", "防止启动器自动更新后再次关闭下载功能",

            // Launcher inspection
            "Inspect.FileMissing", "文件不存在。",
            "Inspect.NotOfficial", "这不是 PCL 正式版。社区版等其他启动器的下载功能可以正常使用，无需修复。",
            "Inspect.ReadFailed", "无法读取文件：{0}",
            "Inspect.AccessDenied", "没有读取该文件的权限。",
            "Inspect.Patched", "下载功能已由 PCL++ 恢复，可以正常使用。",
            "Inspect.NotBlocked", "此版本的下载功能正常，无需修复。",
            "Inspect.Blocked", "此版本已关闭下载功能，可以一键修复。",
            "Inspect.Unsupported", "此版本已关闭下载功能，但代码结构与已知版本不同，暂不支持修复。",

            // Repair and restore
            "Fix.PatchFailed", "修复“{0}”失败：{1}",
            "Fix.WriteFailed", "写入文件失败：{0}\n如果启动器位于 Program Files 等受保护的文件夹，请以管理员身份运行 PCL++。",
            "Fix.NoBackup", "没有找到备份文件，无法还原。",
            "Fix.BadBackup", "备份文件不是 PCL 启动器，已取消还原。",
            "Fix.RestoreFailed", "还原失败：{0}",
            "Fix.ReadFailed", "无法读取启动器：{0}",
            "Fix.Running", "PCL 正在运行，请先关闭 PCL 再试。",
            "Fix.BuildFailed", "生成修复后的启动器失败：{0}",
            "Fix.VerifyFailed", "修复结果校验失败，原文件未做任何改动。");
    }
}
