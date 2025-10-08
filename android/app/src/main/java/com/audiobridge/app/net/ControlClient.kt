package com.audiobridge.app.net

import okhttp3.*
import okio.ByteString
import java.util.concurrent.TimeUnit

class ControlClient(private val onState: (String) -> Unit) {
  private var ws: WebSocket? = null
  private var client: OkHttpClient = OkHttpClient.Builder()
    .pingInterval(20, TimeUnit.SECONDS)
    .retryOnConnectionFailure(true)
    .build()

  fun connect(host: String, port: Int) {
    close()
    val req = Request.Builder()
      .url("ws://$host:$port/control")
      .build()
    ws = client.newWebSocket(req, object : WebSocketListener() {
      override fun onOpen(webSocket: WebSocket, response: Response) {
        onState("控制通道: 已连接")
        // send hello with HMAC if PSK available
        val hello = buildHello()
        if (hello != null) webSocket.send(hello)
      }
      override fun onMessage(webSocket: WebSocket, text: String) {
        // 解析 telemetry 简单展示
        if (text.contains("\"type\":\"telemetry\"")) {
          onState("控制通道: telemetry $text")
        }
      }
      override fun onMessage(webSocket: WebSocket, bytes: ByteString) {}
      override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
        onState("控制通道: 已关闭")
      }
      override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
        onState("控制通道: 失败 ${t.message}")
      }
    })
  }

  private fun buildHello(): String? {
    val psk = lastPsk ?: return null
    val clientId = android.os.Build.MODEL + "-" + android.os.Build.DEVICE
    val nonce = java.util.UUID.randomUUID().toString()
    val ts = System.currentTimeMillis()
    val data = clientId + nonce + ts.toString()
    val mac = javax.crypto.Mac.getInstance("HmacSHA256")
    mac.init(javax.crypto.spec.SecretKeySpec(psk, "HmacSHA256"))
    val sig = mac.doFinal(data.toByteArray(Charsets.UTF_8))
    val h = android.util.Base64.encodeToString(sig, android.util.Base64.NO_WRAP)
      .trimEnd('=').replace('+','-').replace('/','_')
    return "{\"type\":\"hello\",\"clientId\":\"$clientId\",\"nonce\":\"$nonce\",\"timestamp\":$ts,\"hmac\":\"$h\"}"
  }

  private var lastPsk: ByteArray? = null
  fun setPskBase64Url(pskBase64Url: String?) {
    lastPsk = try {
      if (pskBase64Url.isNullOrBlank()) null else decodeBase64Url(pskBase64Url)
    } catch (_: Throwable) { null }
  }

  fun sendCmd(action: String, value: Int = 0) {
    val json = "{" +
      "\"type\":\"cmd\"," +
      "\"action\":\"$action\"," +
      "\"value\":$value" +
      "}"
    ws?.send(json)
  }

  fun close() {
    try { ws?.close(1000, "bye") } catch (_: Throwable) {}
    ws = null
  }
}


