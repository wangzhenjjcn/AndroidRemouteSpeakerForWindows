package com.audiobridge.app.net

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo

class NsdHelper(private val context: Context) {
  private val nsdManager = context.getSystemService(Context.NSD_SERVICE) as NsdManager
  private var discoveryListener: NsdManager.DiscoveryListener? = null

  fun discover(serviceType: String, onFound: (host: String, port: Int, psk: String?) -> Unit, onStatus: (String) -> Unit) {
    stop()
    discoveryListener = object : NsdManager.DiscoveryListener {
      override fun onDiscoveryStarted(regType: String) { onStatus("NSD: 开始发现") }
      override fun onDiscoveryStopped(serviceType: String) { onStatus("NSD: 已停止") }
      override fun onStartDiscoveryFailed(serviceType: String, errorCode: Int) { onStatus("NSD: 启动失败 $errorCode") }
      override fun onStopDiscoveryFailed(serviceType: String, errorCode: Int) { onStatus("NSD: 停止失败 $errorCode") }
      override fun onServiceLost(serviceInfo: NsdServiceInfo) { }
      override fun onServiceFound(serviceInfo: NsdServiceInfo) {
        if (serviceInfo.serviceType.contains(serviceType)) {
          nsdManager.resolveService(serviceInfo, object : NsdManager.ResolveListener {
            override fun onResolveFailed(serviceInfo: NsdServiceInfo, errorCode: Int) { onStatus("NSD: 解析失败 $errorCode") }
            override fun onServiceResolved(resolved: NsdServiceInfo) {
              val host = resolved.host?.hostAddress ?: resolved.host?.canonicalHostName ?: return
              val port = resolved.port
              // Android NSD 无标准 TXT API; 仅填充 host/port，PSK 留给扫码或手动
              onFound(host, port, null)
              stop()
            }
          })
        }
      }
    }
    nsdManager.discoverServices(serviceType, NsdManager.PROTOCOL_DNS_SD, discoveryListener)
  }

  fun stop() {
    try { discoveryListener?.let { nsdManager.stopServiceDiscovery(it) } } catch (_: Throwable) {}
    discoveryListener = null
  }
}


