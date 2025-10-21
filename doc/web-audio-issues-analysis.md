# Web 音频播放问题分析与解决方案

## 问题 1：破音（Audio Glitches）

### 问题表现
浏览器播放音频时出现破音、卡顿、杂音。

### 根本原因

#### 1. 缓冲区欠载（Buffer Underrun）
**当前代码问题**：
```javascript
this.bufferDuration = 0.1; // 100ms 缓冲
```
- 100ms 缓冲太小，网络稍有波动就会导致欠载
- 欠载时队列为空，播放中断，产生破音

#### 2. 音频不连续
**当前代码问题**：
```javascript
const maxQueueSize = 20;
if (this.audioQueue.length > maxQueueSize) {
  this.audioQueue.shift(); // 直接丢弃旧数据
  console.warn('[AudioPlayer] Audio queue overflow, dropping old buffer');
}
```
- 队列溢出时丢弃数据，导致音频不连续
- 没有平滑过渡处理

#### 3. 时钟同步问题
**当前代码问题**：
```javascript
const playTime = Math.max(this.nextPlayTime, this.audioContext.currentTime);
```
- 时钟漂移处理不完善
- 播放时间累积误差

#### 4. 缺少抖动缓冲（Jitter Buffer）
- 没有处理网络延迟波动
- 没有包到达时间补偿

### 解决方案

#### 方案 1：自适应缓冲
```javascript
// 动态调整缓冲深度
minBufferSize: 3,  // 最少3个包才开始播放
targetBufferSize: 5, // 目标缓冲5个包
maxBufferSize: 15,   // 最多缓冲15个包

// 根据队列状态调整播放速度
if (this.audioQueue.length < this.minBufferSize) {
  // 缓冲不足，等待
  this.isPlaying = false;
} else if (this.audioQueue.length > this.maxBufferSize) {
  // 缓冲过多，丢弃最旧的包
  this.audioQueue.shift();
}
```

#### 方案 2：平滑播放
```javascript
// 使用更大的初始缓冲
this.initialBufferDuration = 0.2; // 200ms 初始缓冲

// 时钟同步
if (Math.abs(drift) > 0.05) { // 超过50ms偏移
  this.nextPlayTime = this.audioContext.currentTime + 0.1;
}
```

#### 方案 3：错误隐藏（Packet Loss Concealment）
```javascript
// 检测丢包
if (this.audioQueue.length === 0 && this.isPlaying) {
  // 插入静音或重复上一帧
  console.warn('[AudioPlayer] Buffer underrun, inserting silence');
}
```

---

## 问题 2：连接不稳定（1006 断链）

### 问题表现
长时间连接后出现 WebSocket 错误 1006，连接异常断开。

### 根本原因

#### 1. 缺少心跳机制
**当前实现**：
- WebSocket 连接建立后，只有服务器单向推送数据
- 客户端从不向服务器发送消息
- 某些代理、防火墙、负载均衡器会关闭"空闲"连接

**典型超时时间**：
- 浏览器：通常无限期保持（除非明确关闭）
- 代理/防火墙：30-60秒无活动就断开
- 云服务负载均衡器：60-300秒

#### 2. 没有自动重连
**当前实现**：
- 连接断开后，用户必须手动点击"连接"按钮
- 临时网络中断会导致播放停止

#### 3. 缓冲区内存泄漏风险
**当前实现**：
```javascript
this.audioQueue.push(audioBuffer);
```
- 如果播放速度慢于接收速度，队列会无限增长
- 长时间运行可能导致内存溢出

### 解决方案

#### 方案 1：WebSocket 心跳（Ping/Pong）
```javascript
// 客户端定期发送 ping
startHeartbeat() {
  this.heartbeatInterval = setInterval(() => {
    if (this.ws && this.ws.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify({ type: 'ping' }));
      console.log('[AudioPlayer] Sent ping');
    }
  }, 30000); // 每30秒发送一次
}

// 处理 pong 响应
ws.onmessage = (event) => {
  if (typeof event.data === 'string') {
    const msg = JSON.parse(event.data);
    if (msg.type === 'pong') {
      console.log('[AudioPlayer] Received pong');
      return;
    }
  }
  this.handleAudioData(event.data);
};
```

**服务器端也需要响应**：
```csharp
// 在 ClientReceiveLoopAsync 中处理 ping
var result = await client.Socket.ReceiveAsync(
  new ArraySegment<byte>(buffer), 
  client.Cts.Token
);

if (result.MessageType == WebSocketMessageType.Text)
{
  // 处理 ping 消息
  var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
  if (msg.Contains("ping"))
  {
    var pong = Encoding.UTF8.GetBytes("{\"type\":\"pong\"}");
    await client.Socket.SendAsync(
      new ArraySegment<byte>(pong),
      WebSocketMessageType.Text,
      true,
      CancellationToken.None
    );
  }
}
```

#### 方案 2：自动重连
```javascript
connect(isReconnect = false) {
  // ... 连接逻辑 ...
  
  this.ws.onclose = (event) => {
    this.isConnected = false;
    console.log(`[AudioPlayer] Disconnected. Code: ${event.code}`);
    
    if (event.code !== 1000 && !this.manualDisconnect) {
      // 非正常关闭且非手动断开，尝试重连
      this.scheduleReconnect();
    }
  };
}

scheduleReconnect() {
  this.reconnectAttempts++;
  const delay = Math.min(1000 * Math.pow(2, this.reconnectAttempts), 30000);
  console.log(`[AudioPlayer] Reconnecting in ${delay}ms (attempt ${this.reconnectAttempts})`);
  
  this.reconnectTimer = setTimeout(() => {
    console.log('[AudioPlayer] Attempting reconnect...');
    this.connect(true);
  }, delay);
}

disconnect() {
  this.manualDisconnect = true; // 标记为手动断开
  clearTimeout(this.reconnectTimer);
  // ... 断开逻辑 ...
}
```

#### 方案 3：连接健康监测
```javascript
startConnectionMonitor() {
  this.lastDataTime = Date.now();
  
  this.monitorInterval = setInterval(() => {
    const timeSinceLastData = Date.now() - this.lastDataTime;
    
    if (timeSinceLastData > 10000) { // 10秒没收到数据
      console.warn('[AudioPlayer] No data received for 10s, connection may be dead');
      this.showError('连接似乎已断开，正在重连...');
      this.ws.close();
    }
  }, 5000); // 每5秒检查一次
}

handleAudioData(data) {
  this.lastDataTime = Date.now(); // 更新最后接收时间
  // ... 处理逻辑 ...
}
```

---

## 综合解决方案优先级

### 高优先级（立即修复）
1. ✅ 增加初始缓冲（200-300ms）
2. ✅ 添加心跳机制（30秒ping）
3. ✅ 添加自动重连（指数退避）

### 中优先级（推荐）
4. 🔄 自适应缓冲管理
5. 🔄 连接健康监测
6. 🔄 改进时钟同步

### 低优先级（可选优化）
7. 📝 包丢失隐藏（PLC）
8. 📝 音频淡入淡出
9. 📝 性能监控面板

---

## 实施计划

### 阶段 1：基础修复（解决破音和断链）
1. 修改 `audio-player.js`：
   - 增加 `minBufferSize`、`targetBufferSize`
   - 添加 `startHeartbeat()` 方法
   - 添加 `scheduleReconnect()` 方法
   - 改进 `handleAudioData()` 缓冲逻辑

2. 修改 `WebAudioStreamer.cs`：
   - 在 `ClientReceiveLoopAsync` 中处理 Text 类型消息（ping/pong）

### 阶段 2：高级优化（提升体验）
1. 动态缓冲调整
2. 丢包检测和补偿
3. 更精确的时钟同步

---

## 测试验证

### 破音测试
1. 播放连续音乐30分钟，检查是否有破音
2. 模拟网络波动（限速、丢包），观察播放稳定性
3. 检查浏览器 Console 是否有 "Buffer underrun" 警告

### 断链测试
1. 保持连接1小时以上，检查是否自动断开
2. 临时断开网络，检查是否自动重连
3. 检查 WebSocket 是否定期发送 ping

---

## 配置建议

### 开发/测试环境
```javascript
minBufferSize: 2,
targetBufferSize: 4,
maxBufferSize: 10,
heartbeatInterval: 10000, // 10秒
```

### 生产环境
```javascript
minBufferSize: 3,
targetBufferSize: 6,
maxBufferSize: 15,
heartbeatInterval: 30000, // 30秒
```

### 低延迟环境（局域网）
```javascript
minBufferSize: 1,
targetBufferSize: 3,
maxBufferSize: 8,
heartbeatInterval: 30000,
```


