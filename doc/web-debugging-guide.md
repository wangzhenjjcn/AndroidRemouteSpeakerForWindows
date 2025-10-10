# Web 播放器调试指南

## 问题现象

用户报告网页端显示 WebSocket 连接错误，无法建立连接。

## 调试步骤

### 1. 启动应用并启用 Web 服务

1. 运行 `dist/Windows/AudioBridge.Windows.exe`
2. 勾选 "启用 Web 服务"
3. 确认状态栏显示 "Web 服务已启动，访问 http://localhost:29763"
4. 如果显示错误，记录错误信息

### 2. 打开浏览器开发者工具

1. 点击 "打开网页" 按钮（或手动访问 `http://localhost:29763/player.html`）
2. 按 `F12` 打开浏览器开发者工具
3. 切换到 **Console** 标签页

### 3. 测试 WebSocket 连接

1. 在网页中点击 "连接" 按钮
2. 观察 Console 中的输出信息

#### 正常情况下应看到：

```
[AudioPlayer] Connecting to ws://localhost:29763/audio...
[AudioPlayer] Connected to server
```

#### 异常情况可能看到：

```
[AudioPlayer] WebSocket error: <error object>
[AudioPlayer] WebSocket URL: ws://localhost:29763/audio
[AudioPlayer] WebSocket readyState: <number>
连接已关闭 - 代码: <code>, 原因: <reason>
```

### 4. 检查网络请求

在开发者工具中切换到 **Network** 标签页：

1. 确认是否有到 `/audio` 的 WebSocket 连接请求
2. 查看请求的状态码：
   - `101 Switching Protocols` = 成功
   - `400 Bad Request` = WebSocket 握手失败
   - `404 Not Found` = 路由配置错误
   - `连接被拒绝` = 服务器未启动或端口被占用

### 5. 检查服务器端日志（使用 DebugView）

由于 Windows 应用使用 `System.Diagnostics.Debug.WriteLine`，需要使用 DebugView 工具查看日志：

1. 下载 [DebugView](https://learn.microsoft.com/en-us/sysinternals/downloads/debugview) (Sysinternals 工具)
2. 以管理员身份运行 DebugView
3. 点击 `Capture > Capture Win32` 和 `Capture > Capture Global Win32`
4. 重新启动 AudioBridge 应用

#### 正常情况下应看到：

```
[ControlServer] Starting Web Audio service on port 29763
[ControlServer] Kestrel listening on 0.0.0.0:29763
[ControlServer] WebSocket middleware enabled
[ControlServer] Web Audio service started successfully on port 29763
[ControlServer] Request: GET /player.html
[ControlServer] Request: GET /audio-player.js
[ControlServer] Request: GET /audio
[ControlServer] Accepting WebSocket connection to /audio
[ControlServer] WebSocket connected
[WebAudioStreamer] Client <guid> connected. Total: 1
```

#### 异常情况可能看到：

```
[ControlServer] ERROR starting Web Audio service: <error message>
[ControlServer] Stack trace: <stack trace>
```

或者

```
[ControlServer] WARNING: _webAudioStreamer is null!
```

### 6. 常见问题排查

#### 问题 1: 端口被占用

**症状**: 应用显示 "启动 Web 服务失败：地址已在使用"

**解决方法**:
```powershell
# 检查端口 29763 是否被占用
netstat -ano | findstr :29763

# 如果被占用，终止占用进程（替换 <PID> 为实际进程 ID）
taskkill /F /PID <PID>
```

或者在应用中修改 Web 端口号。

#### 问题 2: 防火墙阻止

**症状**: 浏览器显示 "连接被拒绝" 或 "无法访问"

**解决方法**:
1. 临时关闭 Windows 防火墙测试
2. 或添加防火墙规则允许端口 29763

```powershell
# 添加防火墙规则（以管理员身份运行 PowerShell）
New-NetFirewallRule -DisplayName "AudioBridge Web" -Direction Inbound -Protocol TCP -LocalPort 29763 -Action Allow
```

#### 问题 3: 静态文件找不到

**症状**: 网页无法加载，显示 404

**解决方法**:
检查 `dist/Windows/wwwroot/` 目录是否存在以下文件：
- `player.html`
- `audio-player.js`

如果缺失，重新编译并发布应用。

#### 问题 4: WebSocket 升级失败

**症状**: Network 标签显示请求返回 400 或其他非 101 状态码

**解决方法**:
- 确认浏览器支持 WebSocket（所有现代浏览器都支持）
- 检查是否有代理或防病毒软件拦截 WebSocket 连接
- 尝试使用不同的浏览器（Chrome, Firefox, Edge）

#### 问题 5: 连接成功但无声音

**症状**: WebSocket 连接成功，但听不到声音

**排查方法**:
1. 在 Windows 应用中点击 "开始推流"
2. 检查 Console 是否有音频数据包到达：
   ```
   收到音频包: 格式 48000Hz / 2ch / 960 samples
   ```
3. 检查浏览器音量是否静音
4. 检查网页播放器的音量滑块

### 7. 手动测试 WebSocket 连接

可以使用以下 JavaScript 代码在浏览器 Console 中手动测试：

```javascript
// 创建 WebSocket 连接
const ws = new WebSocket('ws://localhost:29763/audio');
ws.binaryType = 'arraybuffer';

ws.onopen = () => console.log('✓ WebSocket 连接成功');
ws.onerror = (err) => console.error('✗ WebSocket 错误:', err);
ws.onclose = (e) => console.log(`WebSocket 关闭 - 代码: ${e.code}, 原因: ${e.reason}`);
ws.onmessage = (e) => console.log(`收到数据: ${e.data.byteLength} 字节`);
```

### 8. 报告问题

如果问题仍未解决，请提供以下信息：

1. **浏览器 Console 输出**（完整的错误信息）
2. **Network 标签中的请求详情**（状态码、响应头）
3. **DebugView 日志**（如果可用）
4. **Windows 应用状态栏的提示信息**
5. **系统环境**：
   - Windows 版本
   - 浏览器版本
   - 是否使用代理或 VPN
   - 防病毒软件

## 最新修复

### 修复日期: 2025-10-09

**修复内容**:

1. **中间件顺序优化**: 将 WebSocket 端点处理移到静态文件之前，确保 `/audio` 路径优先被处理
2. **增强错误处理**: 添加详细的日志输出和异常捕获
3. **前端错误提示**: 在 WebSocket 错误和关闭事件中添加详细的错误码和原因显示
4. **异步启动**: 将 `StartWebAudioAsync` 方法改为真正的异步方法，确保服务正确启动

**关键代码变更**:

- `windows/App/Net/ControlServer.cs`: 重构中间件配置，使用 `app.Use` 代替 `app.Run` 处理 WebSocket
- `windows/App/wwwroot/audio-player.js`: 增强错误日志输出
- `windows/App/Net/WebAudioStreamer.cs`: 已经包含详细的日志输出

## 下一步

请运行最新编译的应用，按照上述步骤进行测试，并将观察到的日志信息反馈，以便进一步诊断问题。

