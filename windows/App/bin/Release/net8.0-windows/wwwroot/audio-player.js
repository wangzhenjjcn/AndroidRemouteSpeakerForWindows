/**
 * AudioBridge Web Player
 * 实时音频流播放器 - 基于 WebSocket + Web Audio API
 */

class AudioPlayer {
  constructor() {
    this.ws = null;
    this.audioContext = null;
    this.gainNode = null;
    this.preGainNode = null;      // 前级衰减（输入预留余量）
    this.compressorNode = null;   // 动态压缩（软限幅）
    this.highpassNode = null;     // 直流阻断高通滤波
    this.isConnected = false;
    this.receivedPackets = 0;
    this.receivedBytes = 0;
    this.audioFormat = { sampleRate: 0, channels: 0, samplesPerCh: 0 };
    
    // 音频缓冲队列（改进的缓冲管理）
    this.audioQueue = [];
    this.isPlaying = false;
    this.nextPlayTime = 0;
    this.minBufferSize = 4;      // 提高起播门槛，减少欠载
    this.targetBufferSize = 8;   // 目标缓冲
    this.maxBufferSize = 20;     // 上限
    this.initialBufferDuration = 0.20; // 200ms 初始缓冲

    // 预增益与淡入淡出
    this.inputAttenuation = 0.84; // ≈ -1.5 dB，预留余量减少削波
    this.fadeMs = 2.0;            // 每个缓冲淡入/淡出时长
    
    // 性能监控
    this.lastPacketTime = 0;
    this.latencySum = 0;
    this.latencyCount = 0;
    this.lastDataTime = 0;
    
    // 心跳和重连
    this.heartbeatInterval = null;
    this.monitorInterval = null;
    this.reconnectTimer = null;
    this.reconnectAttempts = 0;
    this.manualDisconnect = false;
    
    this.initUI();
  }

  initUI() {
    this.statusText = document.getElementById('statusText');
    this.statusIndicator = document.getElementById('statusIndicator');
    this.serverUrl = document.getElementById('serverUrl');
    this.audioFormatText = document.getElementById('audioFormat');
    this.receivedPacketsText = document.getElementById('receivedPackets');
    this.receivedBytesText = document.getElementById('receivedBytes');
    this.latencyText = document.getElementById('latency');
    this.bufferSizeText = document.getElementById('bufferSize');
    this.connectBtn = document.getElementById('connectBtn');
    this.disconnectBtn = document.getElementById('disconnectBtn');
    this.errorBox = document.getElementById('errorBox');
    this.errorText = document.getElementById('errorText');
    
    // 启动 UI 更新定时器
    setInterval(() => this.updateUI(), 500);
  }

  async connect(isReconnect = false) {
    try {
      this.hideError();
      this.manualDisconnect = false;
      
      // 获取当前页面的 host
      const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
      const host = window.location.host;
      const wsUrl = `${protocol}//${host}/audio`;
      
      this.updateStatus('connecting', isReconnect ? '重新连接中...' : '连接中...');
      this.serverUrl.textContent = host;
      
      // 初始化 Web Audio API
      if (!this.audioContext) {
        this.audioContext = new (window.AudioContext || window.webkitAudioContext)();
        // 输出音量
        this.gainNode = this.audioContext.createGain();
        this.gainNode.gain.value = 1.0;

        // 前级衰减（为后端峰值/跨样本峰值预留余量）
        this.preGainNode = this.audioContext.createGain();
        this.preGainNode.gain.value = this.inputAttenuation;

        // 动态压缩器（软限幅）
        this.compressorNode = this.audioContext.createDynamicsCompressor();
        try {
          this.compressorNode.threshold.setValueAtTime(-9, this.audioContext.currentTime); // dB
          this.compressorNode.knee.setValueAtTime(12, this.audioContext.currentTime);
          this.compressorNode.ratio.setValueAtTime(4, this.audioContext.currentTime);
          this.compressorNode.attack.setValueAtTime(0.003, this.audioContext.currentTime);
          this.compressorNode.release.setValueAtTime(0.25, this.audioContext.currentTime);
        } catch {}

        // 直流阻断（高通 20Hz @ Q≈0.707）
        this.highpassNode = this.audioContext.createBiquadFilter();
        this.highpassNode.type = 'highpass';
        try {
          this.highpassNode.frequency.setValueAtTime(20, this.audioContext.currentTime);
          this.highpassNode.Q.setValueAtTime(0.707, this.audioContext.currentTime);
        } catch {}

        // 连接图：source -> perBufferGain -> preGain -> compressor -> highpass -> gain -> destination
        this.compressorNode.connect(this.highpassNode);
        this.highpassNode.connect(this.gainNode);
        this.gainNode.connect(this.audioContext.destination);
      }
      
      // 恢复 AudioContext (浏览器要求用户交互后才能播放)
      if (this.audioContext.state === 'suspended') {
        await this.audioContext.resume();
      }
      
      // 建立 WebSocket 连接
      console.log(`[AudioPlayer] Connecting to ${wsUrl}... (attempt ${this.reconnectAttempts + 1})`);
      this.ws = new WebSocket(wsUrl);
      this.ws.binaryType = 'arraybuffer';
      
      this.ws.onopen = () => {
        this.isConnected = true;
        this.reconnectAttempts = 0; // 重置重连计数
        this.updateStatus('connected', '已连接');
        this.connectBtn.disabled = true;
        this.disconnectBtn.disabled = false;
        console.log('[AudioPlayer] Connected to server');
        
        // 启动心跳和监控
        this.startHeartbeat();
        this.startConnectionMonitor();
      };
      
      this.ws.onmessage = (event) => {
        // 处理 pong 响应（文本消息）
        if (typeof event.data === 'string') {
          try {
            const msg = JSON.parse(event.data);
            if (msg.type === 'pong') {
              console.log('[AudioPlayer] Received pong');
              return;
            }
          } catch (e) {
            // 不是 JSON，忽略
          }
        }
        // 处理音频数据（二进制消息）
        this.handleAudioData(event.data);
      };
      
      this.ws.onerror = (error) => {
        console.error('[AudioPlayer] WebSocket error:', error);
        console.error('[AudioPlayer] WebSocket URL:', wsUrl);
        console.error('[AudioPlayer] WebSocket readyState:', this.ws ? this.ws.readyState : 'N/A');
      };
      
      this.ws.onclose = (event) => {
        this.isConnected = false;
        this.isPlaying = false;
        this.updateStatus('disconnected', '未连接');
        this.connectBtn.disabled = false;
        this.disconnectBtn.disabled = true;
        console.log(`[AudioPlayer] Disconnected from server. Code: ${event.code}, Reason: ${event.reason}`);
        
        // 停止心跳和监控
        this.stopHeartbeat();
        this.stopConnectionMonitor();
        
        // 非正常关闭且非手动断开，尝试重连
        if (event.code !== 1000 && !this.manualDisconnect) {
          this.showError(`连接断开(${event.code})，正在重连...`);
          this.scheduleReconnect();
        } else if (event.code !== 1000) {
          this.showError(`连接已关闭 - 代码: ${event.code}, 原因: ${event.reason || '未知'}`);
        }
      };
      
    } catch (error) {
      console.error('[AudioPlayer] Connection error:', error);
      this.showError(`连接失败: ${error.message}`);
      this.updateStatus('disconnected', '连接失败');
      this.connectBtn.disabled = false;
      this.disconnectBtn.disabled = true;
    }
  }

  disconnect() {
    this.manualDisconnect = true; // 标记为手动断开
    clearTimeout(this.reconnectTimer);
    this.stopHeartbeat();
    this.stopConnectionMonitor();
    
    if (this.ws) {
      this.ws.close();
      this.ws = null;
    }
    this.isConnected = false;
    this.isPlaying = false;
    this.audioQueue = [];
    this.updateStatus('disconnected', '已断开');
    this.connectBtn.disabled = false;
    this.disconnectBtn.disabled = true;
  }

  startHeartbeat() {
    this.stopHeartbeat(); // 先清除旧的定时器
    this.heartbeatInterval = setInterval(() => {
      if (this.ws && this.ws.readyState === WebSocket.OPEN) {
        try {
          this.ws.send(JSON.stringify({ type: 'ping', timestamp: Date.now() }));
          console.log('[AudioPlayer] Sent ping');
        } catch (error) {
          console.error('[AudioPlayer] Error sending ping:', error);
        }
      }
    }, 30000); // 每30秒发送一次心跳
    console.log('[AudioPlayer] Heartbeat started');
  }

  stopHeartbeat() {
    if (this.heartbeatInterval) {
      clearInterval(this.heartbeatInterval);
      this.heartbeatInterval = null;
      console.log('[AudioPlayer] Heartbeat stopped');
    }
  }

  startConnectionMonitor() {
    this.stopConnectionMonitor(); // 先清除旧的定时器
    this.lastDataTime = Date.now();
    
    this.monitorInterval = setInterval(() => {
      const timeSinceLastData = Date.now() - this.lastDataTime;
      
      // 如果10秒没收到数据，认为连接可能已死
      if (timeSinceLastData > 10000 && this.isConnected) {
        console.warn(`[AudioPlayer] No data received for ${timeSinceLastData}ms, connection may be dead`);
        this.showError('连接似乎已断开，正在重连...');
        if (this.ws) {
          this.ws.close(); // 触发 onclose，进而触发重连
        }
      }
    }, 5000); // 每5秒检查一次
    console.log('[AudioPlayer] Connection monitor started');
  }

  stopConnectionMonitor() {
    if (this.monitorInterval) {
      clearInterval(this.monitorInterval);
      this.monitorInterval = null;
      console.log('[AudioPlayer] Connection monitor stopped');
    }
  }

  scheduleReconnect() {
    // 取消之前的重连计划
    clearTimeout(this.reconnectTimer);
    
    this.reconnectAttempts++;
    // 指数退避：1s, 2s, 4s, 8s, 16s, 最多30s
    const delay = Math.min(1000 * Math.pow(2, this.reconnectAttempts - 1), 30000);
    console.log(`[AudioPlayer] Reconnecting in ${delay}ms (attempt ${this.reconnectAttempts})`);
    
    this.reconnectTimer = setTimeout(() => {
      console.log('[AudioPlayer] Attempting reconnect...');
      this.connect(true);
    }, delay);
  }

  handleAudioData(data) {
    try {
      // 更新最后接收数据时间（用于连接监控）
      this.lastDataTime = Date.now();
      
      const now = performance.now();
      if (this.lastPacketTime > 0) {
        const packetInterval = now - this.lastPacketTime;
        this.latencySum += packetInterval;
        this.latencyCount++;
      }
      this.lastPacketTime = now;
      
      this.receivedPackets++;
      this.receivedBytes += data.byteLength;
      
      // 解析音频数据头 (8 字节)
      // [sampleRate(4B LE) | channels(2B LE) | samplesPerCh(2B LE)]
      const header = new DataView(data, 0, 8);
      const sampleRate = header.getUint32(0, true); // little-endian
      const channels = header.getUint16(4, true);
      const samplesPerCh = header.getUint16(6, true);
      
      // 更新音频格式信息
      if (this.audioFormat.sampleRate !== sampleRate || 
          this.audioFormat.channels !== channels ||
          this.audioFormat.samplesPerCh !== samplesPerCh) {
        this.audioFormat = { sampleRate, channels, samplesPerCh };
        this.audioFormatText.textContent = `${sampleRate}Hz ${channels}ch`;
        console.log(`[AudioPlayer] Audio format: ${sampleRate}Hz, ${channels}ch, ${samplesPerCh} samples/ch`);
      }
      
      // 解码 PCM16 数据 (从第 8 字节开始)
      const pcmData = new Int16Array(data, 8);
      const totalSamples = samplesPerCh * channels;
      
      if (pcmData.length !== totalSamples) {
        console.warn(`[AudioPlayer] PCM data length mismatch: expected ${totalSamples}, got ${pcmData.length}`);
      }
      
      // 转换为 Float32 并创建 AudioBuffer
      const audioBuffer = this.audioContext.createBuffer(
        channels,
        samplesPerCh,
        sampleRate
      );
      
      // 填充音频数据 (交错 -> 平面)
      for (let ch = 0; ch < channels; ch++) {
        const channelData = audioBuffer.getChannelData(ch);
        for (let i = 0; i < samplesPerCh; i++) {
          const pcmIndex = i * channels + ch;
          if (pcmIndex < pcmData.length) {
            // Int16 -> Float32 对称映射：-32768 -> -1.0，其余除以 32767
            const v = pcmData[pcmIndex];
            const f = (v === -32768) ? -1.0 : (v / 32767.0);
            channelData[i] = f * this.inputAttenuation;
          }
        }
      }
      
      // 改进的队列管理：根据缓冲深度决定操作
      if (this.audioQueue.length >= this.maxBufferSize) {
        // 缓冲过多，丢弃最旧的包
        this.audioQueue.shift();
        console.warn(`[AudioPlayer] Buffer overflow (${this.audioQueue.length}), dropping old buffer`);
      }
      
      // 加入播放队列
      this.audioQueue.push(audioBuffer);
      
      // 开始播放：缓冲达到最小要求时开始
      if (!this.isPlaying && this.audioQueue.length >= this.minBufferSize) {
        console.log(`[AudioPlayer] Starting playback with ${this.audioQueue.length} buffers`);
        this.startPlaying();
      }
      
    } catch (error) {
      console.error('[AudioPlayer] Error handling audio data:', error);
    }
  }

  startPlaying() {
    this.isPlaying = true;
    this.nextPlayTime = this.audioContext.currentTime + this.initialBufferDuration;
    this.scheduleNextBuffer();
  }

  scheduleNextBuffer() {
    if (!this.isPlaying || !this.isConnected) {
      return;
    }
    
    // 从队列中取出音频缓冲
    if (this.audioQueue.length > 0) {
      const audioBuffer = this.audioQueue.shift();
      
      // 创建音频源节点
      const source = this.audioContext.createBufferSource();
      source.buffer = audioBuffer;

      // 为单个缓冲创建包络增益，做短淡入/淡出降低拼接爆音
      const srcGain = this.audioContext.createGain();
      try { srcGain.gain.setValueAtTime(1.0, this.audioContext.currentTime); } catch {}

      source.connect(srcGain);
      // 完整链路：perBufferGain -> preGain -> compressor -> highpass -> gain -> destination
      if (this.preGainNode && this.compressorNode) {
        srcGain.connect(this.preGainNode);
        this.preGainNode.connect(this.compressorNode);
      } else {
        // 兜底：直接连到输出增益
        srcGain.connect(this.gainNode);
      }
      
      // 调度播放
      const currentTime = this.audioContext.currentTime;
      const playTime = Math.max(this.nextPlayTime, currentTime);
      
      // 时钟漂移检测和修正
      const drift = playTime - currentTime;
      if (drift > 0.1) {
        // 漂移超过100ms，重置时钟
        console.warn(`[AudioPlayer] Clock drift detected: ${drift.toFixed(3)}s, resetting`);
        this.nextPlayTime = currentTime + 0.05; // 重置为50ms延迟
      }
      
      // 安排淡入/淡出包络
      const fade = Math.min(this.fadeMs / 1000, Math.max(0.001, audioBuffer.duration / 4));
      const t0 = playTime;
      const t1 = playTime + audioBuffer.duration;
      try {
        // 避免到 0 的指数斜坡，使用很小的底值
        srcGain.gain.setValueAtTime(0.0001, t0);
        srcGain.gain.exponentialRampToValueAtTime(1.0, t0 + fade);
        srcGain.gain.setValueAtTime(1.0, Math.max(t0, t1 - fade));
        srcGain.gain.exponentialRampToValueAtTime(0.0001, t1);
      } catch {}

      source.start(playTime);
      
      // 更新下次播放时间
      this.nextPlayTime = playTime + audioBuffer.duration;
      
      // 在音频播放完成时调度下一个缓冲
      source.onended = () => {
        this.scheduleNextBuffer();
      };
    } else {
      // 队列为空,等待新数据（缓冲欠载）
      this.isPlaying = false;
      console.warn(`[AudioPlayer] Buffer underrun! Queue empty, waiting for data`);
    }
  }

  setVolume(value) {
    const volume = Math.max(0, Math.min(100, value)) / 100;
    if (this.gainNode) {
      this.gainNode.gain.value = volume;
    }
  }

  updateStatus(status, text) {
    this.statusText.textContent = text;
    this.statusIndicator.className = `status-indicator ${status}`;
  }

  updateUI() {
    this.receivedPacketsText.textContent = this.receivedPackets.toLocaleString();
    this.receivedBytesText.textContent = (this.receivedBytes / 1024).toFixed(1) + ' KB';
    this.bufferSizeText.textContent = this.audioQueue.length;
    
    if (this.latencyCount > 0) {
      const avgLatency = this.latencySum / this.latencyCount;
      this.latencyText.textContent = avgLatency.toFixed(0) + ' ms';
    }
  }

  showError(message) {
    this.errorText.textContent = `❌ ${message}`;
    this.errorBox.style.display = 'block';
  }

  hideError() {
    this.errorBox.style.display = 'none';
  }
}

// 全局实例
const player = new AudioPlayer();

// 全局函数供 HTML 调用
function connect() {
  player.connect();
}

function disconnect() {
  player.disconnect();
}

function updateVolume(value) {
  player.setVolume(value);
  document.getElementById('volumeValue').textContent = value + '%';
}

// 页面加载完成
console.log('[AudioPlayer] Web Audio Player initialized');

