/**
 * AudioBridge Web Player
 * 实时音频流播放器 - 基于 WebSocket + Web Audio API
 */

class AudioPlayer {
  constructor() {
    this.ws = null;
    this.audioContext = null;
    this.gainNode = null;
    this.isConnected = false;
    this.receivedPackets = 0;
    this.receivedBytes = 0;
    this.audioFormat = { sampleRate: 0, channels: 0, samplesPerCh: 0 };
    
    // 音频缓冲队列
    this.audioQueue = [];
    this.isPlaying = false;
    this.nextPlayTime = 0;
    this.bufferDuration = 0.1; // 100ms 缓冲
    
    // 性能监控
    this.lastPacketTime = 0;
    this.latencySum = 0;
    this.latencyCount = 0;
    
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

  async connect() {
    try {
      this.hideError();
      
      // 获取当前页面的 host
      const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
      const host = window.location.host;
      const wsUrl = `${protocol}//${host}/audio`;
      
      this.updateStatus('connecting', '连接中...');
      this.serverUrl.textContent = host;
      
      // 初始化 Web Audio API
      if (!this.audioContext) {
        this.audioContext = new (window.AudioContext || window.webkitAudioContext)();
        this.gainNode = this.audioContext.createGain();
        this.gainNode.connect(this.audioContext.destination);
        this.gainNode.gain.value = 1.0;
      }
      
      // 恢复 AudioContext (浏览器要求用户交互后才能播放)
      if (this.audioContext.state === 'suspended') {
        await this.audioContext.resume();
      }
      
      // 建立 WebSocket 连接
      console.log(`[AudioPlayer] Connecting to ${wsUrl}...`);
      this.ws = new WebSocket(wsUrl);
      this.ws.binaryType = 'arraybuffer';
      
      this.ws.onopen = () => {
        this.isConnected = true;
        this.updateStatus('connected', '已连接');
        this.connectBtn.disabled = true;
        this.disconnectBtn.disabled = false;
        console.log('[AudioPlayer] Connected to server');
      };
      
      this.ws.onmessage = (event) => {
        this.handleAudioData(event.data);
      };
      
      this.ws.onerror = (error) => {
        console.error('[AudioPlayer] WebSocket error:', error);
        console.error('[AudioPlayer] WebSocket URL:', wsUrl);
        console.error('[AudioPlayer] WebSocket readyState:', this.ws ? this.ws.readyState : 'N/A');
        this.showError(`WebSocket 连接错误 - URL: ${wsUrl}`);
      };
      
      this.ws.onclose = (event) => {
        this.isConnected = false;
        this.updateStatus('disconnected', '未连接');
        this.connectBtn.disabled = false;
        this.disconnectBtn.disabled = true;
        console.log(`[AudioPlayer] Disconnected from server. Code: ${event.code}, Reason: ${event.reason}`);
        
        if (event.code !== 1000) { // 1000 = Normal closure
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

  handleAudioData(data) {
    try {
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
            channelData[i] = pcmData[pcmIndex] / 32768.0; // Int16 -> Float32
          }
        }
      }
      
      // 加入播放队列
      this.audioQueue.push(audioBuffer);
      
      // 限制队列长度,防止延迟累积
      const maxQueueSize = 20;
      if (this.audioQueue.length > maxQueueSize) {
        this.audioQueue.shift();
        console.warn('[AudioPlayer] Audio queue overflow, dropping old buffer');
      }
      
      // 开始播放
      if (!this.isPlaying) {
        this.startPlaying();
      }
      
    } catch (error) {
      console.error('[AudioPlayer] Error handling audio data:', error);
    }
  }

  startPlaying() {
    this.isPlaying = true;
    this.nextPlayTime = this.audioContext.currentTime + this.bufferDuration;
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
      source.connect(this.gainNode);
      
      // 调度播放
      const playTime = Math.max(this.nextPlayTime, this.audioContext.currentTime);
      source.start(playTime);
      
      // 更新下次播放时间
      this.nextPlayTime = playTime + audioBuffer.duration;
      
      // 在音频播放完成时调度下一个缓冲
      source.onended = () => {
        this.scheduleNextBuffer();
      };
    } else {
      // 队列为空,等待新数据
      this.isPlaying = false;
      console.log('[AudioPlayer] Audio queue empty, waiting for data');
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

