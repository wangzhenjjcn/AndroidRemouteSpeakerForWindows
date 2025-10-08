# 控制通道与音频帧协议

本文档描述 Android 与 Windows 之间的 WebSocket 控制通道消息规范与 UDP 音频帧封装格式，遵循面向稳定性的最小可用集合设计。

控制通道（WebSocket）
---------------------

- 端点：`/control`（ws 或 wss）
- 认证：预共享密钥（PSK）+ HMAC 校验，签名数据为 `clientId + nonce + timestamp`，算法 HMAC-SHA256，编码 Base64URL

消息格式（JSON）
----------------

客户端 -> 服务器（Android -> Windows）：

```json
{ "type": "hello", "clientId": "Android-Device", "nonce": "UUID", "timestamp": 1700000000000, "hmac": "base64url(HMAC_SHA256(clientId+nonce+timestamp, PSK))" }
```

```json
{ "type": "cmd", "action": "play|pause|next|prev|seek", "value": 0 }
```

服务器 -> 客户端（Windows -> Android）：

```json
{ "type": "nowPlaying", "title": "string", "artist": "string", "album": "string", "positionMs": 0, "durationMs": 0, "app": "Spotify" }
```

```json
{ "type": "telemetry", "latencyMs": 95, "pktLossPct": 1.2, "jitterMs": 8 }
```

音频通道（UDP + AES-GCM）
-------------------------

- 采样：默认 48 kHz 立体声（也支持系统实际采样率与声道数，PCM 回退）
- 编码：Opus 可选（Android 当前回退为 PCM，仅识别 OPUS 头并忽略；后续版本提供解码）
- 加密：AES-256-GCM（可选），12 字节随机 nonce，16 字节 tag

包格式
------

OPUS 头（12B）：
- magic: 4B = `0x4F505553`
- seq: 2B（循环计数）
- timestamp48k: 4B（以 48k 采样时钟递增）
- flags: 1B（声道/PLC/FEC 标志）
- keyId: 1B（密钥轮换）
  负载：Opus 压缩帧

PCM 头（8B）：
- sampleRate: 4B（LE）
- channels: 2B（LE）
- frameSamplesPerCh: 2B（LE）
  负载：PCM16 小端交错

加密负载（PCM）：
- 明文头（8B） + nonce(12B) + cipher(NB) + tag(16B)

错误处理与重放保护
--------------------

- 接收侧按 `seq` 做窗口校验与乱序重排（OPUS 时）
- 超窗即丢弃，防止重放

版本与兼容性
------------

- v1：当前文档版本（初始实现）
- 兼容策略：新增字段一律向后兼容，接收端忽略未知字段


