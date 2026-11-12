# Campfire Android

Campfire 的 Android 应用。界面基于 ZeroUI 设计系统，联机协议与桌面端一致，可与桌面端互相加入房间。

## 模块

| 模块 | 内容 |
| --- | --- |
| `zeroui` | ZeroUI 设计系统的克隆，提交记录在 `zeroui-commit.txt`，通过组合构建引用其 `kit` 模块 |
| `:core` | 纯 Kotlin：邀请码、房间协议、会话状态机、联机大厅客户端 |
| `:easytier` | EasyTier 原生库及其接口 |
| `:app` | Campfire 应用 |
| `:fakegame` | 调试用测试应用，在设备上模拟游戏的局域网行为，不随应用发布 |
| `:baselineprofile` | 基线配置文件生成与启动性能测试，不随应用发布 |

## 环境

- JDK 21
- Android SDK 37
- Android 7.0（API 24）及以上

## 常用命令

构建前按 `zeroui-commit.txt` 中的提交克隆 ZeroUI：

```bash
git clone https://github.com/Xocio/ZeroUI.git zeroui
git -C zeroui checkout $(cat zeroui-commit.txt)
```

更新 ZeroUI 时，将 `zeroui-commit.txt` 改为新的提交，再在 `zeroui` 中检出该提交。CI 按同一文件检出 ZeroUI。

```bash
./gradlew :core:test
./gradlew :app:assembleDebug
bash tools/ci.sh
```

带 Campfire 服务地址的构建：

```bash
./gradlew :app:assembleRelease -Pcampfire.serviceUrl=https://api.example.com
```

未传入服务地址时，联机大厅、更新检查等依赖服务的功能不出现，凭邀请码联机不受影响。

在连接的设备上重新生成基线配置文件（ Android 9 及以上，结果写入 `app/src/release/generated/baselineProfiles`）：

```bash
./gradlew :app:generateBaselineProfile
```

## EasyTier 原生库

`easytier/src/main/jniLibs` 中的库由 `tools/easytier/build-jni.sh` 在 Linux 上编译，需要 rustup、cargo-ndk、protoc、libclang 与 Android NDK。构建时校验 `easytier/prebuilt.txt` 中记录的 SHA-256。

## 文档

- [架构说明](docs/ARCHITECTURE.md)
- [兼容矩阵](docs/compat-matrix.md)
- [第三方声明](THIRD_PARTY_NOTICES.md)
