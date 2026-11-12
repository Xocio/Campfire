# 第三方声明

## 依赖

| 项目 | 许可证 | 地址 |
| --- | --- | --- |
| Jetpack Compose、AndroidX | Apache-2.0 | https://developer.android.com/jetpack/androidx |
| Miuix | Apache-2.0 | https://github.com/compose-miuix-ui/miuix |
| Backdrop | Apache-2.0 | https://github.com/Kyant0/AndroidLiquidGlass |
| Shapes | Apache-2.0 | https://github.com/Kyant0/Shapes |
| MaterialKolor | MIT | https://github.com/jordond/MaterialKolor |
| AboutLibraries | Apache-2.0 | https://github.com/mikepenz/AboutLibraries |
| kotlinx.serialization、kotlinx.coroutines | Apache-2.0 | https://github.com/Kotlin |
| ZeroUI | Apache-2.0 | https://github.com/Xocio/ZeroUI |
| EasyTier 2.6.4 | LGPL-3.0 | https://github.com/EasyTier/EasyTier/tree/v2.6.4 |

## ZeroUI

ZeroUI 在构建时按 `zeroui-commit.txt` 中的提交克隆到 `zeroui` 目录，未作修改。其中包含的第三方代码见 ZeroUI 仓库的 `THIRD_PARTY_NOTICES.md`。

## 原生库

`easytier/src/main/jniLibs` 中的 `libeasytier_ffi.so` 与 `libeasytier_android_jni.so` 由 EasyTier 2.6.4 的源码编译而成，以动态库形式加载，未作修改。构建方式见 `tools/easytier/build-jni.sh`，版本与哈希见 `easytier/prebuilt.txt`。

## 资源

`app/src/main/res/drawable-nodpi/campfire_frames.png` 及由其制作的应用图标中的篝火图案，以及 `app/src/main/res/drawable-nodpi/ic_shield.webp` 中的盾牌图案，均来自 Minecraft，版权归 Mojang Studios 所有。正版玩家的皮肤头像经 Mojang 的公开服务获取。

许可证全文：

- Apache-2.0：https://www.apache.org/licenses/LICENSE-2.0
- LGPL-3.0：https://www.gnu.org/licenses/lgpl-3.0.html
