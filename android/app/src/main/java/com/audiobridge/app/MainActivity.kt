package com.audiobridge.app

import android.os.Bundle
import android.content.Intent
import androidx.appcompat.app.AppCompatActivity
import android.widget.Button
import android.widget.EditText
import android.widget.TextView
import com.audiobridge.app.net.PcmUdpReceiver
import com.audiobridge.app.net.ControlClient
import com.audiobridge.app.service.AudioBridgeService
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions

class MainActivity : AppCompatActivity() {
  private var receiver: PcmUdpReceiver? = null
  private var control: ControlClient? = null
  private var nsd: com.audiobridge.app.net.NsdHelper? = null
  override fun onCreate(savedInstanceState: Bundle?) {
    super.onCreate(savedInstanceState)
    setContentView(R.layout.activity_main)
    // 权限请求（相机/通知）
    val needCamera = androidx.core.content.ContextCompat.checkSelfPermission(this, android.Manifest.permission.CAMERA) != android.content.pm.PackageManager.PERMISSION_GRANTED
    if (needCamera) {
      androidx.core.app.ActivityCompat.requestPermissions(this, arrayOf(android.Manifest.permission.CAMERA), 200)
    }
    if (android.os.Build.VERSION.SDK_INT >= 33) {
      if (!androidx.core.app.NotificationManagerCompat.from(this).areNotificationsEnabled()) {
        androidx.core.app.ActivityCompat.requestPermissions(this, arrayOf(android.Manifest.permission.POST_NOTIFICATIONS), 201)
      }
    }
    val portEdit = findViewById<EditText>(R.id.portEdit)
    val startBtn = findViewById<Button>(R.id.startBtn)
    val stopBtn = findViewById<Button>(R.id.stopBtn)
    val statusText = findViewById<TextView>(R.id.statusText)
    val hostEdit = findViewById<EditText>(R.id.hostEdit)
    val ctrlPortEdit = findViewById<EditText>(R.id.ctrlPortEdit)
    val btnDiscover = findViewById<Button>(R.id.btnDiscover)
    val btnPrev = findViewById<Button>(R.id.btnPrev)
    val btnPlayPause = findViewById<Button>(R.id.btnPlayPause)
    val btnNext = findViewById<Button>(R.id.btnNext)
    val pskEdit = findViewById<EditText>(R.id.pskEdit)
    val btnScan = findViewById<Button>(R.id.btnScan)
    val btnCheckUpdate = findViewById<Button>(R.id.btnCheckUpdate)

    startBtn.setOnClickListener {
      try {
        val p = portEdit.text.toString().toInt()
        val psk = pskEdit.text?.toString()
        // 启动前台服务
        val it = Intent(this, AudioBridgeService::class.java)
        it.putExtra(AudioBridgeService.EXTRA_PORT, p)
        it.putExtra(AudioBridgeService.EXTRA_PSK, psk)
        if (android.os.Build.VERSION.SDK_INT >= 26) {
          startForegroundService(it)
        } else {
          startService(it)
        }
        statusText.text = "状态：服务已启动 (端口 $p)"
      } catch (_: Throwable) {
        statusText.text = "状态：端口无效"
      }
    }
    stopBtn.setOnClickListener {
      stopService(Intent(this, AudioBridgeService::class.java))
      statusText.text = "状态：未接收"
    }

    fun ensureControlConnected() {
      val host = hostEdit.text?.toString()?.trim().orEmpty()
      val cp = ctrlPortEdit.text?.toString()?.toIntOrNull() ?: 8181
      val psk = pskEdit.text?.toString()
      if (host.isNotEmpty()) {
        if (control == null) control = ControlClient { s -> runOnUiThread { statusText.text = s } }
        control?.setPskBase64Url(psk)
        control?.connect(host, cp)
      }
    }
    btnPrev.setOnClickListener { ensureControlConnected(); control?.sendCmd("prev") }
    btnPlayPause.setOnClickListener { ensureControlConnected(); control?.sendCmd("play") }
    btnNext.setOnClickListener { ensureControlConnected(); control?.sendCmd("next") }

    val scanLauncher = registerForActivityResult(ScanContract()) { result ->
      if (result != null && result.contents != null) {
        val uri = android.net.Uri.parse(result.contents)
        if (uri.scheme == "abridge" && uri.host == "pair") {
          val host = uri.getQueryParameter("host")
          val ctrl = uri.getQueryParameter("ctrl")
          val audio = uri.getQueryParameter("audio")
          val key = uri.getQueryParameter("key")
          if (!host.isNullOrBlank()) hostEdit.setText(host)
          if (!ctrl.isNullOrBlank()) ctrlPortEdit.setText(ctrl)
          if (!audio.isNullOrBlank()) portEdit.setText(audio)
          if (!key.isNullOrBlank()) pskEdit.setText(key)
          statusText.text = "已填充配对信息"
        }
      }
    }
    btnScan.setOnClickListener {
      val options = ScanOptions().setDesiredBarcodeFormats(ScanOptions.QR_CODE).setPrompt("扫描配对二维码")
      scanLauncher.launch(options)
    }

    btnCheckUpdate.setOnClickListener {
      val host = hostEdit.text?.toString()?.trim().orEmpty()
      val cp = ctrlPortEdit.text?.toString()?.toIntOrNull() ?: 8181
      if (host.isNotEmpty()) {
        ControlClient.init(this)
        control?.openUpdateUrl(host, cp)
      }
    }

    btnDiscover.setOnClickListener {
      if (nsd == null) nsd = com.audiobridge.app.net.NsdHelper(this)
      nsd?.discover("_audiobridge._tcp.", onFound = { host, port, _ ->
        runOnUiThread {
          hostEdit.setText(host)
          ctrlPortEdit.setText(port.toString())
          statusText.text = "NSD: 已解析 $host:$port"
        }
      }, onStatus = { s -> runOnUiThread { statusText.text = s } })
    }
  }

  override fun onStart() {
    super.onStart()
    // 保持空，使用按钮启动
  }

  override fun onStop() {
    receiver?.stop()
    receiver = null
    super.onStop()
  }
}


