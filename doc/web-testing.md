# Web 播放器测试指南

## 测试前准备

### 1. 构建项目

```powershell
# 在项目根目录执行
pwsh -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

### 2. 确认文件存在

检查以下文件是否存在:
- `windows/App/bin/Release/net8.0-windows/AudioBridge.Windows.exe`
- `windows/App/bin/Release/net8.0-windows/wwwroot/player.html`
- `windows/App/bin/Release/net8.0-windows/wwwroot/audio-player.js`

如果 wwwroot 文件不存在,手动复制:

```powershell
Copy-Item -Path "windows/App/wwwroot" -Destination "windows/App/bin/Release/net8.0-windows/" -Recurse
```

## 功能测试清单

### 基础功能测试

#### ✅ Test 1: Web 服务启动

**步骤**:
1. 启动 `AudioBridge.Windows.exe`
2. 勾选"启用 Web 服务"
3. 点击"开始推流"

**预期结果**:
- 状态栏显示 "Web 服务已启动，访问 http://localhost:29763"
- "打开网页"按钮变为可用

#### ✅ Test 2: 播放器页面加载

**步骤**:
1. 点击"打开网页"按钮
2. 或手动访问 `http://localhost:29763/player.html`

**预期结果**:
- 浏览器成功打开播放器页面
- 页面显示完整的 UI (标题、状态卡片、控制按钮、统计信息)
- 连接状态显示"未连接" (红色指示灯)

#### ✅ Test 3: WebSocket 连接

**步骤**:
1. 在播放器页面点击"连接"按钮
2. 观察连接状态变化

**预期结果**:
- 连接状态变为"连接中..." (黄色指示灯)
- 然后变为"已连接" (绿色指示灯)
- 服务器地址显示为 `localhost:29763`
- 浏览器控制台显示 `[AudioPlayer] Connected to server`

#### ✅ Test 4: 音频播放

**步骤**:
1. 确保 Windows 端正在推流
2. 确保 Windows 端有音频输出 (播放音乐或视频)
3. 在播放器页面连接后等待几秒

**预期结果**:
- 浏览器播放出音频
- 音频格式显示正确 (如 "48000Hz 2ch")
- 已接收包数持续增长
- 数据量持续增长
- 延迟显示合理数值 (50-200ms)
- 缓冲显示正常范围 (0-5)

#### ✅ Test 5: 音量控制

**步骤**:
1. 确保音频正在播放
2. 拖动音量滑块到不同位置 (0%, 50%, 100%)

**预期结果**:
- 音量立即响应
- 音量值显示正确
- 50% 时音量明显降低
- 0% 时完全静音

#### ✅ Test 6: 断开连接

**步骤**:
1. 点击"断开"按钮

**预期结果**:
- 连接状态变为"已断开" (红色指示灯)
- 音频停止播放
- "连接"按钮变为可用
- "断开"按钮变为禁用

#### ✅ Test 7: 重新连接

**步骤**:
1. 断开后再次点击"连接"

**预期结果**:
- 能够成功重新连接
- 音频恢复播放
- 统计数据重置或继续累积

#### ✅ Test 8: 端口配置

**步骤**:
1. 停止推流
2. 修改 Web 端口为 8080
3. 重新启动推流
4. 访问 `http://localhost:8080/player.html`

**预期结果**:
- Web 服务在新端口启动
- 能够正常访问和连接
- 配置被保存到 settings.json

### 兼容性测试

#### ✅ Test 9: 不同浏览器

**测试浏览器**:
- Chrome
- Edge
- Firefox
- Safari (macOS)

**预期结果**:
- 所有浏览器都能正常加载页面
- 所有浏览器都能正常连接和播放音频

#### ✅ Test 10: 移动端浏览器

**测试设备**:
- Android Chrome
- iOS Safari

**步骤**:
1. 确保移动设备与 Windows 在同一局域网
2. 访问 `http://<Windows_IP>:29763/player.html`

**预期结果**:
- 页面响应式布局正确
- 能够正常连接和播放
- 触摸交互正常

### 性能测试

#### ✅ Test 11: 延迟测试

**步骤**:
1. 播放一个有明显节拍的音乐
2. 同时在 Windows 端和 Web 端监听
3. 观察延迟情况

**预期结果**:
- 延迟 < 300ms (可接受)
- 延迟 < 200ms (良好)
- 延迟 < 100ms (优秀)

#### ✅ Test 12: 长时间运行

**步骤**:
1. 连接并播放 30 分钟以上

**预期结果**:
- 音频持续稳定播放
- 没有明显的内存泄漏
- 缓冲队列保持稳定
- 浏览器不卡顿

#### ✅ Test 13: 多客户端连接

**步骤**:
1. 在多个浏览器窗口/标签页中同时打开播放器
2. 同时连接

**预期结果**:
- 所有客户端都能同时播放
- 音频同步性较好 (差异 < 1秒)
- Windows 端 CPU 使用率合理

### 稳定性测试

#### ✅ Test 14: 网络中断恢复

**步骤**:
1. 正常连接播放
2. 暂时禁用网络连接
3. 重新启用网络
4. 点击重新连接

**预期结果**:
- 网络恢复后能够重新连接
- 音频恢复播放

#### ✅ Test 15: Windows 端停止推流

**步骤**:
1. Web 端正在播放
2. Windows 端停止推流

**预期结果**:
- Web 端停止接收数据
- 已接收包数停止增长
- 播放完缓冲后停止

#### ✅ Test 16: Windows 端关闭 Web 服务

**步骤**:
1. Web 端正在播放
2. Windows 端取消勾选"启用 Web 服务"

**预期结果**:
- WebSocket 连接断开
- Web 端显示断开状态

### 边界条件测试

#### ✅ Test 17: 快速切换连接/断开

**步骤**:
1. 快速连续点击"连接"和"断开"按钮 10 次

**预期结果**:
- 不应崩溃或卡死
- 最终状态与最后一次操作一致

#### ✅ Test 18: 不同采样率

**步骤**:
1. 测试不同采样率的音频源
   - 44.1kHz
   - 48kHz
   - 96kHz

**预期结果**:
- 所有采样率都能正确显示和播放
- 音频质量正常

#### ✅ Test 19: 单声道音频

**步骤**:
1. 播放单声道音频源

**预期结果**:
- 音频格式显示 "xxxHz 1ch"
- 音频正常播放

## 性能优化建议

### 如果延迟过高

1. **减少缓冲时间** (`audio-player.js`):
   ```javascript
   this.bufferDuration = 0.05;  // 从 0.1 改为 0.05
   ```

2. **启用低延迟模式**:
   ```javascript
   this.audioContext = new AudioContext({
     latencyHint: 'interactive',
     sampleRate: 48000
   });
   ```

### 如果音频断断续续

1. **增加缓冲时间**:
   ```javascript
   this.bufferDuration = 0.15;  // 从 0.1 改为 0.15
   ```

2. **增大队列大小**:
   ```javascript
   const maxQueueSize = 30;  // 从 20 改为 30
   ```

3. **检查网络质量**:
   - 使用有线连接而非 Wi-Fi
   - 关闭其他占用带宽的应用

## 调试技巧

### 浏览器控制台

打开浏览器开发者工具 (F12) 查看:

- **Console**: 查看日志和错误信息
- **Network**: 查看 WebSocket 连接状态
- **Performance**: 分析性能问题

### 关键日志

```javascript
// 连接成功
[AudioPlayer] Connected to server

// 音频格式变化
[AudioPlayer] Audio format: 48000Hz, 2ch, 960 samples/ch

// 队列溢出警告
[AudioPlayer] Audio queue overflow, dropping old buffer

// 队列为空
[AudioPlayer] Audio queue empty, waiting for data
```

### Windows 端日志

在 Visual Studio 的输出窗口查看:

```
[WebAudioStreamer] Client {GUID} connected. Total: 1
[WebAudioStreamer] Client {GUID} disconnected. Total: 0
```

## 常见问题

### Q1: 页面显示 404 Not Found

**解决**: 
- 确认 wwwroot 文件已复制到输出目录
- 检查 App.csproj 中的 Content 配置

### Q2: 连接失败 "WebSocket connection error"

**解决**:
- 确认 Windows 端已启用 Web 服务
- 检查防火墙设置
- 尝试使用 IP 地址而非 localhost

### Q3: 没有声音

**解决**:
- 检查浏览器是否允许自动播放
- 查看浏览器控制台错误
- 确认 Windows 端正在推流且有音频输出

### Q4: 音频延迟过高 (> 1 秒)

**解决**:
- 清空浏览器缓存
- 减少 bufferDuration
- 检查网络连接
- 关闭其他占用 CPU 的进程

## 测试报告模板

```markdown
## Web 播放器测试报告

**测试日期**: YYYY-MM-DD
**测试人员**: [姓名]
**测试环境**: 
- Windows: [版本]
- 浏览器: [名称和版本]
- 网络: [有线/Wi-Fi]

### 测试结果

| 测试项 | 结果 | 备注 |
|--------|------|------|
| Test 1: Web 服务启动 | ✅ Pass | |
| Test 2: 播放器页面加载 | ✅ Pass | |
| Test 3: WebSocket 连接 | ✅ Pass | |
| Test 4: 音频播放 | ✅ Pass | 延迟约 150ms |
| Test 5: 音量控制 | ✅ Pass | |
| ... | | |

### 性能指标

- 平均延迟: [数值] ms
- CPU 使用率: [数值]%
- 内存使用: [数值] MB
- 长时间稳定性: [良好/一般/差]

### 发现的问题

1. [问题描述]
   - 重现步骤: [...]
   - 预期结果: [...]
   - 实际结果: [...]
   - 优先级: [高/中/低]

### 建议

[改进建议...]
```

## 结论

完成以上测试后,确认:
- ✅ 所有基础功能正常
- ✅ 性能符合预期
- ✅ 主流浏览器兼容
- ✅ 稳定性良好

即可认为 Web 播放器功能开发完成并可以发布。

