package com.audiobridge.app

import android.os.Bundle
import androidx.appcompat.app.AppCompatActivity
import android.widget.Button
import android.widget.EditText
import android.widget.TextView
import com.audiobridge.app.net.PcmUdpReceiver

class MainActivity : AppCompatActivity() {
  private var receiver: PcmUdpReceiver? = null
  override fun onCreate(savedInstanceState: Bundle?) {
    super.onCreate(savedInstanceState)
    setContentView(R.layout.activity_main)
    val portEdit = findViewById<EditText>(R.id.portEdit)
    val startBtn = findViewById<Button>(R.id.startBtn)
    val stopBtn = findViewById<Button>(R.id.stopBtn)
    val statusText = findViewById<TextView>(R.id.statusText)

    startBtn.setOnClickListener {
      try {
        val p = portEdit.text.toString().toInt()
        receiver?.stop()
        receiver = PcmUdpReceiver(p) { q -> runOnUiThread { statusText.text = "状态：接收中 (端口 $p，队列 $q)" } }
        receiver?.start()
        statusText.text = "状态：接收中 (端口 $p)"
      } catch (_: Throwable) {
        statusText.text = "状态：端口无效"
      }
    }
    stopBtn.setOnClickListener {
      receiver?.stop()
      receiver = null
      statusText.text = "状态：未接收"
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


