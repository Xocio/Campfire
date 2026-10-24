# Campfire Java Agent

在房主自己的 Minecraft 服务端里补一条欢迎消息和 Tab 页眉页脚。它解决的是代理方案绕不过去的问题：标准“对局域网开放”世界会在登录阶段发送加密请求，Campfire 的 TCP 代理看不到之后的游戏包。

Agent 不关闭正版验证，也不读取账号或令牌。它只做两件事：

1. 在 `MinecraftServer.tickServer` 开头插入一次 `CampfireHook.tick(this)` 调用；
2. 每 5 秒给服务端玩家列表中的每个人发送 `ClientboundTabListPacket`，并在玩家进入世界约 2 秒后发送一次 `ClientboundSystemChatPacket`。

因此房主自己也会看到信息，且不需要客户端装模组。欢迎消息按玩家 UUID 记录，重生或穿越维度不会重复发送；玩家离开后再进入会重新收到。

## 房间状态

Agent 注入后会一直留在游戏进程里，所以是否显示由 Campfire 决定：房间开着时，Campfire 每 3 秒重写一次 `%LocalAppData%\Campfire\Link\overlay-<游戏进程号>.txt`，内容是 `time=<Unix 毫秒>` 和当前界面语言的各段文字，房间结束时删除。

Hook 每 2 秒读一次这个文件。文件不存在或超过 15 秒没有更新时，就给所有玩家发一次空的页眉页脚并停止发送，Campfire 意外退出时也是如此。开始显示时，Hook 会写出 `overlay-<游戏进程号>.ready`，Campfire 看到它才关闭 TCP 代理那一侧的提示；版本不匹配时 Hook 不会写这个文件，代理提示照常保留。

同一次游戏里再开房间时，Campfire 会再附加一次；Agent 只更新状态目录，不会重复安装。

## 结构

| 文件 | 作用 |
|---|---|
| `src/CampfireHook.java` | 运行在服务端 tick 里，读取玩家、延迟、房主名，构造并发送消息；全部通过反射，编译时不需要 Minecraft |
| `src/CampfireTransformer.java` | 用 ASM 给 `tickServer` 开头插入调用 |
| `src/CampfireAgent.java` | Agent 入口；把只依赖 JDK 的 `CampfireHook` 追加到 bootstrap classpath，避免服务端自定义 classloader 找不到它 |
| `src/CampfireAttach.java` | 用游戏自己的 Java 附加到游戏进程并加载 Agent |
| `build.ps1` | 编译并打包 `CampfireAgent.jar`、`CampfireHook.jar`、`CampfireAttach.jar` |
| `dist/` | 已构建的 jar，C# 项目以 EmbeddedResource 嵌入 Campfire.exe |
| `lib/asm-9.11.1.jar` | ASM，构建时打进 Agent jar |

`CampfireHook` 会被 bootstrap classloader 加载，所以它只能依赖 JDK。Minecraft 的类通过 `server.getClass().getClassLoader()` 加载。

## 构建

需要 JDK 21+，在本目录运行：

```powershell
powershell -File build.ps1
```

然后按根目录 README 的“从源码构建”重新构建 Campfire。

## 兼容性

- 当前验证的是原版 26.3（Java 25）以及 26.4 的类结构。
- 旧的 26.1/26.2 大概率可用，但没有逐版本验证。
- 如果服务端类名被 Fabric/Forge 等加载器重映射，或者运行时没有 `jdk.attach`，Agent 会记录原因并退出，Campfire 自动回退到局域网条目，不影响游戏连接。
- 只改 UI 数据包，不改正版验证、加密、世界数据或玩家权限。
