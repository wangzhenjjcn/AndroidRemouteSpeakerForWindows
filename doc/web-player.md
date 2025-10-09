# Web 播放器使用说明

## 概述

AudioBridge Web 播放器允许用户通过浏览器直接播放 Windows 端推送的实时音频流，无需安装任何客户端软件。

## 功能特性

- ✅ **实时音频播放**: 通过 WebSocket 接收并播放 PCM 音频流
- ✅ **低延迟**: 优化的缓冲策略,延迟通常在 100-200ms
- ✅ **自动重采样**: 支持任意采样率和声道数
- ✅ **音量控制**: 客户端音量调节 (0-100%)
- ✅ **实时统计**: 显示接收包数、数据量、延迟和缓冲状态
- ✅ **响应式设计**: 支持桌面和移动设备浏览器
- ✅ **优雅的 UI**: 现代化的渐变设计和流畅动画

## 使用步骤

### 1. 启用 Web 服务

在 Windows 端主界面:

1. 勾选 **"启用 Web 服务"** 复选框
2. 可选择修改端口 (默认 29763)
3. 点击 **"开始推流"** 按钮
4. Web 服务会自动启动

### 2. 访问播放器

有两种方式访问播放器:

**方式一: 通过程序打开**
- 点击主界面的 **"打开网页"** 按钮
- 程序会自动在默认浏览器中打开播放器页面

**方式二: 手动访问**
- 打开浏览器
- 访问 `http://<服务器IP>:<端口>/player.html`
- 例如: `http://192.168.1.100:29763/player.html`

### 3. 连接并播放

1. 在播放器页面点击 **"连接"** 按钮
2. 等待连接状态变为 **"已连接"** (绿色指示灯)
3. 音频会自动开始播放
4. 可以通过音量滑块调节音量 (0-100%)

### 4. 查看统计信息

播放器底部会显示实时统计:
- **已接收**: 收到的音频包数量
- **数据量**: 累计接收的数据量 (KB)
- **延迟**: 平均包间隔时间 (ms)
- **缓冲**: 当前音频队列中的缓冲数量

## 技术架构

### 传输协议

**WebSocket 音频流** (`ws://<host>:<port>/audio`)
- 二进制传输模式
- 每个消息包含一个音频帧

### 音频格式

**数据包格式**:
```
[Header 8 字节] + [PCM16 数据]

Header 结构 (Little-Endian):
  - sampleRate: 4 字节 (采样率, 如 48000)
  - channels: 2 字节 (声道数, 1=单声道, 2=立体声)
  - samplesPerCh: 2 字节 (每声道样本数)

PCM16 数据:
  - 16-bit signed integer, Little-Endian
  - 交错格式 (Interleaved): L0 R0 L1 R1 L2 R2 ...
```

### 音频处理流程

```
WebSocket 接收
    ↓
解析数据包头
    ↓
提取 PCM16 数据
    ↓
转换为 Float32 (-1.0 ~ 1.0)
    ↓
创建 AudioBuffer
    ↓
加入播放队列
    ↓
Web Audio API 播放
```

### 浏览器兼容性

支持所有实现了 **Web Audio API** 的现代浏览器:

- ✅ Chrome 34+
- ✅ Edge 79+
- ✅ Firefox 25+
- ✅ Safari 14.1+
- ✅ Opera 21+

**移动端**:
- ✅ Chrome for Android
- ✅ Safari for iOS 14.5+

## 配置选项

### Windows 端配置

**Settings.json** 位置: `%AppData%\AudioBridge\settings.json`

```json
{
  "WebEnabled": true,
  "WebPort": 29763,
  ...
}
```

- `WebEnabled`: 是否启用 Web 服务
- `WebPort`: Web 服务监听端口

### 客户端配置

播放器参数可在 `audio-player.js` 中调整:

```javascript
// 音频缓冲时间 (秒)
this.bufferDuration = 0.1;  // 100ms

// 最大队列大小 (防止延迟累积)
const maxQueueSize = 20;
```

## 性能优化

### 减少延迟

1. **减少缓冲时间**:
   ```javascript
   this.bufferDuration = 0.05;  // 改为 50ms
   ```

2. **启用低延迟模式** (Chrome):
   ```javascript
   this.audioContext = new AudioContext({
     latencyHint: 'interactive'
   });
   ```

### 提升稳定性

1. **增加缓冲时间**:
   ```javascript
   this.bufferDuration = 0.15;  // 改为 150ms
   ```

2. **增大队列大小**:
   ```javascript
   const maxQueueSize = 30;
   ```

## 故障排查

### 问题 1: 无法连接

**症状**: 点击连接后显示连接失败

**解决方案**:
1. 确认 Windows 端已启用 Web 服务
2. 确认 Windows 端正在推流
3. 检查防火墙是否阻止了端口 (默认 29763)
4. 尝试使用服务器的实际 IP 地址而非 localhost

### 问题 2: 音频断断续续

**症状**: 播放中出现卡顿、爆音

**解决方案**:
1. 检查网络连接质量
2. 增加缓冲时间 (修改 `bufferDuration`)
3. 关闭其他占用带宽的应用
4. 检查浏览器控制台是否有错误

### 问题 3: 延迟过高

**症状**: 音频明显落后于实时

**解决方案**:
1. 减少缓冲时间
2. 检查网络延迟
3. 清空浏览器缓存并重新加载
4. 查看统计中的缓冲数量,如果持续增长说明播放速度慢于接收速度

### 问题 4: 浏览器无法自动播放

**症状**: 连接后无声音

**解决方案**:
- 大多数浏览器需要用户交互才能播放音频
- 点击"连接"按钮即为用户交互,通常可以触发播放
- 如果仍无声音,检查浏览器控制台是否有 AudioContext 相关错误

## 安全注意事项

### HTTP vs HTTPS

- 当前实现使用 **HTTP + WebSocket**
- 音频数据以**明文**传输 (Web 客户端不使用加密)
- 仅适用于**受信任的局域网**环境

### 推荐做法

1. **局域网使用**: 仅在家庭或办公室局域网内使用
2. **防火墙**: 不要将 Web 端口暴露到公网
3. **路由器**: 不要配置端口转发
4. **HTTPS (可选)**: 如需公网访问,建议配置反向代理 (Nginx) + SSL 证书

## 扩展开发

### 自定义播放器 UI

修改 `player.html` 和内嵌的 CSS 样式:

```html
<style>
  /* 自定义颜色方案 */
  body {
    background: linear-gradient(135deg, #your-color-1, #your-color-2);
  }
</style>
```

### 添加新功能

在 `audio-player.js` 中扩展 `AudioPlayer` 类:

```javascript
class AudioPlayer {
  // 添加音频可视化
  addVisualizer() {
    const analyser = this.audioContext.createAnalyser();
    this.gainNode.connect(analyser);
    // ... 实现可视化逻辑
  }
}
```

### 支持 Opus 解码

如需支持 Opus 编码流,可集成 **opus.js**:

```html
<script src="https://cdn.jsdelivr.net/npm/opus.js"></script>
```

然后在 `handleAudioData` 中解码 Opus 帧。

## API 参考

### WebSocket 端点

**端点**: `ws://<host>:<port>/audio`

**消息类型**: Binary (ArrayBuffer)

**消息格式**: 见 "音频格式" 章节

### HTTP 端点

- `GET /`: 重定向到 `/player.html`
- `GET /player.html`: 播放器主页面
- `GET /audio-player.js`: 播放器 JavaScript
- `WS /audio`: WebSocket 音频流

## 更新日志

### v1.0 (2025-01)
- ✅ 初始版本
- ✅ PCM16 音频流播放
- ✅ 实时统计显示
- ✅ 响应式 UI 设计
- ✅ 音量控制
- ✅ 自动重连机制

## 许可证

本项目遵循 MIT 许可证。详见 `LICENSE` 文件。

## 技术支持

如有问题或建议,请提交 Issue 或 Pull Request。

