AudioBridge LAN
================

目标：在 Windows 10/11 捕获系统环回音频，经 Opus 编码通过局域网 UDP 发送到 Android 端低延迟播放，并支持 Android 端反向控制（播放/暂停/上一首/下一首/seek）。同时支持 Web 端浏览器实时播放音频流。

目录结构
---------

```
windows/        # Windows .NET 8 WPF 项目与解决方案
  └─ App/
     └─ wwwroot/   # Web 播放器静态文件 (HTML/CSS/JS)
android/        # Android Kotlin 应用（minSdk 24, targetSdk 34）
doc/            # API/协议文档（控制通道 JSON、UDP 包格式等）
  └─ web-player.md  # Web 播放器使用文档
scripts/        # 自动化安装依赖与一键构建脚本
```

快速开始
--------

### 构建与安装

1) 一键安装依赖（.NET 8 SDK、JDK 17、Android SDK、Gradle）并生成 Gradle Wrapper：

```powershell
pwsh -ExecutionPolicy Bypass -File .\scripts\setup.ps1
```

2) 一键构建（Windows 可执行与 Android APK）：

```powershell
pwsh -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

构建产物：

- Windows：`dist/Windows/AudioBridge.Windows.exe`（单文件发布目录）
- Android：`dist/Android/app-debug.apk`

### 使用说明

#### Windows 端

1. 启动 `AudioBridge.Windows.exe`
2. 选择音频源设备
3. 点击"开始推流"按钮
4. (可选) 勾选"启用 Web 服务"以支持浏览器播放

#### Android 端

1. 安装 `app-debug.apk`
2. 扫描 Windows 端显示的二维码配对
3. 或通过局域网自动发现并连接

#### Web 端

1. 确保 Windows 端已启用 Web 服务并开始推流
2. 在浏览器中访问 `http://<服务器IP>:29763/player.html`
3. 点击"连接"按钮开始播放
4. 详细说明见 `doc/web-player.md`

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
- `doc/web-player.md`：Web 播放器使用文档与技术细节

功能特性
--------

### Windows 端
- ✅ 系统音频环回捕获 (WASAPI Loopback)
- ✅ Opus 编码支持 (可选，可回退 PCM)
- ✅ UDP 音频推送到 Android
- ✅ WebSocket 控制服务
- ✅ 媒体控制集成 (SMTC)
- ✅ mDNS 服务发现
- ✅ 系统托盘常驻
- ✅ 配对二维码生成
- ✅ **Web 音频流服务** (新增)

### Android 端
- ✅ UDP 音频接收与解码
- ✅ 低延迟音频播放 (AudioTrack)
- ✅ 反向媒体控制
- ✅ 局域网设备发现
- ✅ 前台服务 + 通知栏控制
- ✅ 实时统计显示

### Web 端 (新增)
- ✅ **浏览器直接播放**，无需安装客户端
- ✅ WebSocket 实时音频流
- ✅ Web Audio API 低延迟播放
- ✅ 响应式 UI 设计
- ✅ 实时统计 (延迟、丢包、缓冲)
- ✅ 音量控制
- ✅ 支持桌面和移动浏览器

许可
----

本项目以 MIT 许可证发布，详见 `LICENSE`。


