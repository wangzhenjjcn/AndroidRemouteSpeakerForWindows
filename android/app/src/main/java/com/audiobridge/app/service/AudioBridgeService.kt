package com.audiobridge.app.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import com.audiobridge.app.MainActivity
import com.audiobridge.app.R
import com.audiobridge.app.net.PcmUdpReceiver

class AudioBridgeService : Service() {
  companion object {
    const val EXTRA_PORT = "port"
    const val EXTRA_PSK = "psk"
    private const val CH_ID = "ab_foreground"
    private const val CH_NAME = "AudioBridge"
    private const val NOTI_ID = 1001
  }

  private var receiver: PcmUdpReceiver? = null

  override fun onBind(intent: Intent?): IBinder? = null

  override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
    val port = intent?.getIntExtra(EXTRA_PORT, 5004) ?: 5004
    val psk = intent?.getStringExtra(EXTRA_PSK)
    startForeground(NOTI_ID, buildNotification())
    receiver?.stop()
    receiver = PcmUdpReceiver(port, null, psk)
    receiver?.start()
    return START_STICKY
  }

  private fun buildNotification(): Notification {
    val nm = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
      val ch = NotificationChannel(CH_ID, CH_NAME, NotificationManager.IMPORTANCE_LOW)
      nm.createNotificationChannel(ch)
    }
    val pi = PendingIntent.getActivity(
      this, 0, Intent(this, MainActivity::class.java),
      PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
    )
    return NotificationCompat.Builder(this, CH_ID)
      .setSmallIcon(android.R.drawable.stat_sys_data_bluetooth)
      .setContentTitle("AudioBridge 正在接收")
      .setContentText("前台服务运行中")
      .setContentIntent(pi)
      .setOngoing(true)
      .build()
  }

  override fun onDestroy() {
    receiver?.stop()
    receiver = null
    super.onDestroy()
  }
}


