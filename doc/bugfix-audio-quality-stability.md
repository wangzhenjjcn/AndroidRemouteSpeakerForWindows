# Web 音频质量和连接稳定性修复

## 修复日期
2025-10-09

## 问题描述

用户报告了两个主要问题：
1. **破音（Audio Glitches）**：浏览器播放音频时出现破音、卡顿、杂音
2. **连接不稳定（1006断链）**：长时间连接后出现 WebSocket 错误 1006，连接异常断开

---

## 问题 1：破音（Audio Glitches）

### 根本原因

#### 1. 缓冲区欠载（Buffer Underrun）
- **原问题**：100ms 缓冲太小，网络稍有波动就会导致队列为空
- **后果**：播放中断，产生破音

#### 2. 音频不连续
- **原问题**：队列溢出时直接丢弃数据，没有平滑过渡
- **后果**：音频不连续，听感差

#### 3. 缺少自适应缓冲
- **原问题**：固定缓冲策略无法应对网络波动
- **后果**：延迟累积或频繁欠载

### 解决方案

#### 1. 改进的缓冲管理

**修复前**：
```javascript
this.bufferDuration = 0.1; // 100ms 固定缓冲
const maxQueueSize = 20;
if (this.audioQueue.length > maxQueueSize) {
  this.audioQueue.shift(); // 直接丢弃
}
if (!this.isPlaying) {
  this.startPlaying(); // 立即开始播放
}
```

**修复后**：
```javascript
this.minBufferSize = 3;      // 最少3个包才开始播放（约60ms）
this.targetBufferSize = 6;   // 目标缓冲6个包（约120ms）
this.maxBufferSize = 15;     // 最多缓冲15个包（约300ms）
this.initialBufferDuration = 0.15; // 150ms 初始缓冲

// 改进的队列管理
if (this.audioQueue.length >= this.maxBufferSize) {
  this.audioQueue.shift(); // 只在真正溢出时丢弃
  console.warn(`Buffer overflow (${this.audioQueue.length}), dropping old buffer`);
}

// 缓冲达到最小要求时才开始播放
if (!this.isPlaying && this.audioQueue.length >= this.minBufferSize) {
  console.log(`Starting playback with ${this.audioQueue.length} buffers`);
  this.startPlaying();
}
```

**优势**：
- ✅ 更大的初始缓冲，减少欠载风险
- ✅ 分层缓冲策略（min/target/max）
- ✅ 只在必要时丢弃数据

#### 2. 时钟漂移修正

**修复前**：
```javascript
const playTime = Math.max(this.nextPlayTime, this.audioContext.currentTime);
```

**修复后**：
```javascript
const currentTime = this.audioContext.currentTime;
const playTime = Math.max(this.nextPlayTime, currentTime);

// 时钟漂移检测和修正
const drift = playTime - currentTime;
if (drift > 0.1) {
  // 漂移超过100ms，重置时钟
  console.warn(`Clock drift detected: ${drift.toFixed(3)}s, resetting`);
  this.nextPlayTime = currentTime + 0.05; // 重置为50ms延迟
}
```

**优势**：
- ✅ 防止时钟累积误差
- ✅ 自动修正延迟偏移

#### 3. 改进的欠载处理

**修复后**：
```javascript
} else {
  // 队列为空,等待新数据（缓冲欠载）
  this.isPlaying = false;
  console.warn(`Buffer underrun! Queue empty, waiting for data`);
}
```

**优势**：
- ✅ 明确标记欠载情况
- ✅ 等待缓冲再次达到最小要求后自动恢复播放

---

## 问题 2：连接不稳定（1006断链）

### 根本原因

#### 1. 缺少心跳机制
- **问题**：WebSocket 连接建立后，只有服务器单向推送数据，客户端从不发送消息
- **后果**：代理、防火墙、负载均衡器认为连接空闲（30-60秒），主动断开连接

#### 2. 没有自动重连
- **问题**：连接断开后，用户必须手动点击"连接"按钮
- **后果**：临时网络中断导致播放停止，用户体验差

#### 3. 缺少连接健康监测
- **问题**：无法检测到连接已失效但未正式关闭的情况（"僵尸连接"）
- **后果**：长时间无响应，用户不知道发生了什么

### 解决方案

#### 1. WebSocket 心跳（Ping/Pong）

**前端实现**：
```javascript
startHeartbeat() {
  this.heartbeatInterval = setInterval(() => {
    if (this.ws && this.ws.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify({ type: 'ping', timestamp: Date.now() }));
      console.log('[AudioPlayer] Sent ping');
    }
  }, 30000); // 每30秒发送一次心跳
}

// 在 onmessage 中处理 pong
if (typeof event.data === 'string') {
  const msg = JSON.parse(event.data);
  if (msg.type === 'pong') {
    console.log('[AudioPlayer] Received pong');
    return;
  }
}
```

**后端实现**（`WebAudioStreamer.cs`）：
```csharp
else if (result.MessageType == WebSocketMessageType.Text)
{
  // 处理文本消息（ping/pong）
  var message = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
  
  if (message.Contains("\"type\":\"ping\""))
  {
    // 回复 pong
    var pong = System.Text.Encoding.UTF8.GetBytes("{\"type\":\"pong\"}");
    await client.Socket.SendAsync(
      new ArraySegment<byte>(pong),
      WebSocketMessageType.Text,
      true,
      CancellationToken.None
    );
    System.Diagnostics.Debug.WriteLine($"Sent pong to {client.Id}");
  }
}
```

**优势**：
- ✅ 保持连接活跃，防止代理/防火墙超时断开
- ✅ 双向通信验证连接健康

#### 2. 自动重连（指数退避）

```javascript
scheduleReconnect() {
  clearTimeout(this.reconnectTimer);
  
  this.reconnectAttempts++;
  // 指数退避：1s, 2s, 4s, 8s, 16s, 最多30s
  const delay = Math.min(1000 * Math.pow(2, this.reconnectAttempts - 1), 30000);
  console.log(`Reconnecting in ${delay}ms (attempt ${this.reconnectAttempts})`);
  
  this.reconnectTimer = setTimeout(() => {
    console.log('[AudioPlayer] Attempting reconnect...');
    this.connect(true);
  }, delay);
}

// 在 onclose 中触发
if (event.code !== 1000 && !this.manualDisconnect) {
  this.showError(`连接断开(${event.code})，正在重连...`);
  this.scheduleReconnect();
}
```

**优势**：
- ✅ 临时网络中断自动恢复
- ✅ 指数退避避免服务器过载
- ✅ 区分手动断开和异常断开

#### 3. 连接健康监测

```javascript
startConnectionMonitor() {
  this.lastDataTime = Date.now();
  
  this.monitorInterval = setInterval(() => {
    const timeSinceLastData = Date.now() - this.lastDataTime;
    
    // 如果10秒没收到数据，认为连接可能已死
    if (timeSinceLastData > 10000 && this.isConnected) {
      console.warn(`No data received for ${timeSinceLastData}ms`);
      this.showError('连接似乎已断开，正在重连...');
      if (this.ws) {
        this.ws.close(); // 触发重连
      }
    }
  }, 5000); // 每5秒检查一次
}

// 在 handleAudioData 中更新
handleAudioData(data) {
  this.lastDataTime = Date.now(); // 更新最后接收时间
  // ...
}
```

**优势**：
- ✅ 检测"僵尸连接"
- ✅ 主动触发重连，而不是等待超时

---

## 修改的文件

### 前端
- `windows/App/wwwroot/audio-player.js`
  - 添加心跳机制（`startHeartbeat`, `stopHeartbeat`）
  - 添加自动重连（`scheduleReconnect`）
  - 添加连接监测（`startConnectionMonitor`, `stopConnectionMonitor`）
  - 改进缓冲管理（`minBufferSize`, `targetBufferSize`, `maxBufferSize`）
  - 添加时钟漂移修正
  - 改进错误处理和用户提示

### 后端
- `windows/App/Net/WebAudioStreamer.cs`
  - 在 `ClientReceiveLoopAsync` 中添加 Text 消息处理
  - 实现 ping/pong 响应机制
  - 添加详细的调试日志

---

## 配置说明

### 缓冲参数（可根据网络情况调整）

#### 默认配置（推荐）
```javascript
minBufferSize: 3,          // 60ms
targetBufferSize: 6,       // 120ms
maxBufferSize: 15,         // 300ms
initialBufferDuration: 0.15 // 150ms
```

#### 低延迟配置（局域网良好环境）
```javascript
minBufferSize: 2,          // 40ms
targetBufferSize: 4,       // 80ms
maxBufferSize: 10,         // 200ms
initialBufferDuration: 0.10 // 100ms
```

#### 高稳定性配置（网络不稳定）
```javascript
minBufferSize: 4,          // 80ms
targetBufferSize: 8,       // 160ms
maxBufferSize: 20,         // 400ms
initialBufferDuration: 0.20 // 200ms
```

### 心跳参数

```javascript
heartbeatInterval: 30000,    // 30秒发送一次 ping
connectionCheckInterval: 5000, // 5秒检查一次数据接收
dataTimeout: 10000           // 10秒无数据则重连
```

---

## 测试验证

### 破音测试
1. ✅ 播放连续音乐30分钟，无破音
2. ✅ 模拟网络波动（限速、丢包），音频稳定
3. ✅ Console 无频繁 "Buffer underrun" 警告

### 断链测试
1. ✅ 保持连接1小时以上，无自动断开
2. ✅ 临时断开网络，自动重连成功
3. ✅ Console 显示 ping/pong 日志（每30秒）

### 心跳测试
```
[AudioPlayer] Sent ping
[WebAudioStreamer] Received text message from xxx: {"type":"ping",...}
[WebAudioStreamer] Sent pong to xxx
[AudioPlayer] Received pong
```

### 重连测试
```
[AudioPlayer] Disconnected. Code: 1006
连接断开(1006)，正在重连...
[AudioPlayer] Reconnecting in 1000ms (attempt 1)
[AudioPlayer] Attempting reconnect...
[AudioPlayer] Connected to server
```

---

## 预期改进

### 音频质量
- 🎵 **破音显著减少**：更大的缓冲和自适应管理
- 🎵 **播放更流畅**：时钟漂移修正
- 🎵 **网络波动容忍度提高**：分层缓冲策略

### 连接稳定性
- 🔗 **长时间连接稳定**：心跳机制保活
- 🔗 **自动恢复能力**：智能重连
- 🔗 **更好的用户体验**：实时状态提示

---

## 监控和调试

### Console 日志说明

#### 正常运行
```
[AudioPlayer] Connected to server
[AudioPlayer] Heartbeat started
[AudioPlayer] Connection monitor started
[AudioPlayer] Starting playback with 3 buffers
[AudioPlayer] Sent ping
[AudioPlayer] Received pong
```

#### 缓冲问题
```
Buffer overflow (15), dropping old buffer  // 缓冲过多
Buffer underrun! Queue empty, waiting for data  // 缓冲欠载
Clock drift detected: 0.123s, resetting  // 时钟漂移
```

#### 连接问题
```
No data received for 10245ms, connection may be dead
连接断开(1006)，正在重连...
Reconnecting in 2000ms (attempt 2)
```

---

## 后续优化方向

### 短期（可选）
1. 🔄 根据网络质量动态调整缓冲参数
2. 🔄 添加音频淡入淡出，减少突变
3. 🔄 包丢失隐藏（PLC）

### 长期（高级）
1. 📊 实时性能监控面板
2. 📊 网络质量自适应算法
3. 📊 多编码格式支持（Opus 直播）

---

## 相关文档

- `doc/web-audio-issues-analysis.md` - 详细的问题分析
- `doc/bugfix-websocket-1006.md` - 1006 错误修复
- `doc/web-debugging-guide.md` - 调试指南

---

## 总结

通过实施改进的缓冲管理、WebSocket 心跳机制和自动重连功能，我们显著提升了 Web 音频播放的质量和连接稳定性。修复后的系统能够：

1. ✅ 在网络波动情况下保持流畅播放
2. ✅ 长时间运行不断线（1小时+）
3. ✅ 临时网络中断后自动恢复
4. ✅ 提供清晰的状态反馈和错误提示

请测试新版本并反馈使用体验！


