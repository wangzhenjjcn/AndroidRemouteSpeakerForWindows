AudioBridge LAN
================

目标：在 Windows 10/11 捕获系统环回音频，经 Opus 编码通过局域网 UDP 发送到 Android 端低延迟播放，并支持 Android 端反向控制（播放/暂停/上一首/下一首/seek）。

目录结构
---------

```
windows/        # Windows .NET 8 WPF 项目与解决方案
android/        # Android Kotlin 应用（minSdk 24, targetSdk 34）
doc/            # API/协议文档（控制通道 JSON、UDP 包格式等）
scripts/        # 自动化安装依赖与一键构建脚本
```

快速开始
--------

1) 一键安装依赖（.NET 8 SDK、JDK 17、Android SDK、Gradle）并生成 Gradle Wrapper：

```powershell
pwsh -ExecutionPolicy Bypass -File .\scripts\setup.ps1
```

2) 一键构建（Windows 可执行与 Android APK）：

```powershell
pwsh -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

构建产物：

- Windows：`dist/Windows/AudioBridge.Windows`（单文件发布目录）
- Android：`dist/Android/app-debug.apk`

代码规范
--------

- C# 遵循《华为 C# 开发规范/阿里巴巴 C# 风格要点》：
  - 可读性优先、早返回、避免不必要 try/catch、明确命名、公共 API 注释。
  - 本仓库通过 `.editorconfig` 约束基础风格。
- Kotlin/Android 遵循阿里巴巴 Java 开发手册/安卓官方 Kotlin 风格：
  - 命名清晰、限制可变性、作用域最小化、UI 与逻辑解耦。

文档
----

- `doc/API.md`：控制通道与 UDP 音频帧协议说明
- `doc/control.json`：控制通道 JSON Schema
- `doc/audio-packet.md`：UDP 包头与加密格式

许可
----

本项目以 MIT 许可证发布，详见 `LICENSE`。


