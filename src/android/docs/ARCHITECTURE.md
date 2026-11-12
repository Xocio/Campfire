# 架构说明

## 模块

```text
:app  ──▶  ZeroUI kit  ──▶  Miuix / Material 3 / Compose
  │
  ├──▶  :easytier  ──▶  :core
  └──▶  :core
```

ZeroUI 克隆在 `zeroui` 目录，使用的提交记录在 `zeroui-commit.txt`，通过 Gradle 组合构建引用其 `kit` 模块，依赖坐标为 `com.zeroui:zeroui-kit`。更新 ZeroUI 时修改 `zeroui-commit.txt` 并在 `zeroui` 中检出对应提交；目录缺失时构建会提示克隆命令。

通用组件使用 ZeroUI kit，功能专属组件放在 `:app` 对应的 `feature` 包。Campfire 自有的颜色（如 `okContainerColor`、`neutralContainerColor`）定义在 `feature/settings/OkColors.kt`；ZeroUI kit 中文字的繁体与粤语版本放在 `:app` 的 `kit_strings.xml` 中。

## ZeroUI kit 目录

| 包 | 内容 |
| --- | --- |
| `theme` | `ZeroTheme`、`ThemeSettings`、语义颜色和字体 |
| `components` | 文本、按钮、卡片、列表行、输入、窗口、页面、图标和拖动排序 |
| `navigation` | `ZeroNavHost`、`page`、`pushUnique`、`DeferUntilSettled` |
| `motion` | 转场与动效减弱 |
| `glass` | 模糊、液态玻璃控件和页面背景 |
| `effect` | 流动背景和涟漪着色器 |

## `:app` 目录

| 包 | 内容 |
| --- | --- |
| `core` | `AppContainer` 与 `appViewModel` |
| `data` | 设置模型与存储、外观分享码、背景图、更新检查、公告、使用统计、语言和日志 |
| `navigation` | `AppRoute` |
| `feature/welcome` | 起始页 |
| `feature/main` | 主框架和底栏 |
| `feature/home` | 主页和缩略预览 |
| `feature/room` | 创建房间、加入房间、房间页、房间设置与语音 |
| `feature/lobby` | 联机大厅、房间详情与公开到大厅 |
| `feature/linksettings` | 联机设置、节点、白名单、联机诊断、游戏文件夹与联机日志 |
| `feature/notice` | 公告横幅与公告弹窗 |
| `feature/support` | 支持项目页 |
| `link` | 联机会话、前台服务、局域网世界发现、节点测速、联机诊断、语音、皮肤头像与联机日志 |
| `game` | 各启动器的游戏文件夹、版本识别与语音模组安装 |
| `lobby` | 联机大厅数据与房间公开 |
| `feature/settings` | 应用设置、主题设置、底栏设置、背景设置和关于页 |
| `feature/update` | 更新弹窗 |
| `ui` | 应用标志 |

## 其他模块

| 模块 | 内容 |
| --- | --- |
| `:core` | `link` 为邀请码、房间协议、会话状态机、节点与端口转发，`lobby` 为联机大厅客户端；纯 Kotlin，不依赖 Android |
| `:easytier` | `EasyTierJNI` 原生接口与 `JniNetworkRuntime` |
| `:fakegame` | 模拟游戏对局域网开放的测试应用 |
| `:baselineprofile` | 基线配置文件生成与启动性能测试 |

## 主题

`ZeroTheme` 接收一个 `ThemeSettings`，并下发颜色、字体、缩放、动效和外观选项。


## 组件

组件在 ZeroUI kit 内部按当前风格分发：

- `Card`：卡片，内边距由调用方指定。
- `ListCard` / `ListSection`：设置行分组。
- `CardSlice`：懒加载列表中的分组行。
- `ButtonVariant.Primary` / `Secondary`：按钮层级。
- `ZeroPage` / `ZeroPageFrame`：带折叠顶栏和模糊效果的标准页面。

## 页面与状态

- 需要加载数据的页面使用 `ViewModel` 和不可变 `UiState`，示例见 `WelcomeViewModel`；联机状态由 `LinkController.state` 提供。
- 依赖在 `AppContainer` 中创建，页面通过 `appViewModel { ... }` 获取 ViewModel。
- 输入内容、弹窗显隐等界面状态使用 `rememberSaveable`。
- 需要确认的流程可以使用 `ConfirmDialogState.confirm()`。

## 导航

路由定义在 `AppRoute`，需要标注 `@Serializable`：

```kotlin
@Serializable
data class LobbyRoom(val id: String) : AppRoute
```

在 `AppRoot.kt` 的 `AppNavGraph` 注册页面，使用 `controller.pushUnique(route)` 跳转。

重内容可用 `DeferUntilSettled` 包裹，在进入转场结束后组合。

## 设置

`SettingsRepository` 保存 `AppSettings`，设置页通过 `LocalAppController` 读写。新增设置项的步骤：

1. 在 `AppSettings` 或 `AppearanceExtras` 增加字段。
2. 需要作用于主题时，在 `toThemeSettings` 中传给 `ThemeSettings`。
3. 在设置页添加对应的行。

## 起始页

- `WelcomeViewModel` 负责检测流程和完成状态。
- `SetupChecks` 定义应用自己的检测项。
- `WelcomeScreen` 负责翻页、权限请求和页面组合。
- `WelcomeFx` 负责主视觉和页面动效。

`SetupItemId.required` 决定某一项是否阻止进入下一步。

## 测试

| 测试 | 覆盖内容 |
| --- | --- |
| `SettingsCodecTest` | 设置编解码、容错和恢复默认 |
| `SettingsRepositoryTest` | 状态发布与持久化 |
| `AppRouteTest` | 路由序列化与去重 |
| `WelcomeViewModelTest` | 检测流程与翻页范围 |
| `StringResourcesTest` | 四种语言的 key 与占位符，以及 ZeroUI kit 文字的覆盖 |
| `NoticesTest` | 公告解析与筛选 |
| `GameFoldersTest` | 游戏文件夹与版本识别 |
| `:core` 的测试 | 邀请码、房间协议、会话状态机、节点测速、联机大厅客户端，以及与桌面端一致性的对照数据 |
