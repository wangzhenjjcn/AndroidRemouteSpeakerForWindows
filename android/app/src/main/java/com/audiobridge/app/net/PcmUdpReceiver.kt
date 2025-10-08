package com.audiobridge.app.net

import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioTrack
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import kotlin.concurrent.thread

class PcmUdpReceiver(private val listenPort: Int, private val onQueueStat: ((Int) -> Unit)? = null, private val pskBase64Url: String? = null) {
  @Volatile private var running = false
  private var th: Thread? = null
  @Volatile private var writerRunning = false
  private var writerThread: Thread? = null

  fun start() {
    if (running) return
    running = true
    th = thread(name = "pcm-udp-recv") {
      var socket: DatagramSocket? = null
      var track: AudioTrack? = null
      try {
        socket = DatagramSocket(null).apply {
          reuseAddress = true
          soTimeout = 0
          bind(InetSocketAddress(listenPort))
        }
        // 使用大缓冲以兼容高采样率/多声道大帧
        val buf = ByteArray(65535)
        val pkt = DatagramPacket(buf, buf.size)
        var configured = false
        var sampleRate = 48000
        var channels = 2
        var curSr = -1
        var curCh = -1
        var isOpus = false
        var opus: Any? = null // OPUS 暂不解码
        // Jitter buffer (very simple): queue of PCM16 frames (short[]), target 3 frames
        val queue = java.util.ArrayDeque<ShortArray>()
        val lock = Object()
        fun startWriterIfNeeded() {
          if (writerRunning) return
          writerRunning = true
          writerThread = thread(name = "pcm-writer") {
            try {
              var localTrack: AudioTrack?
              while (running) {
                var frame: ShortArray? = null
                synchronized(lock) {
                  frame = if (!queue.isEmpty()) queue.removeFirst() else null
                  onQueueStat?.invoke(queue.size)
                }
                localTrack = track
                if (frame != null && localTrack != null) {
                  localTrack.write(frame, 0, frame.size)
                } else {
                  Thread.sleep(10)
                }
              }
            } catch (_: Throwable) {
            } finally {
              writerRunning = false
            }
          }
        }
        while (running) {
          socket.receive(pkt)
          val data = pkt.data
          val off = pkt.offset
          val len = pkt.length
          if (len < 8) continue
          if (data[off + 0].toInt() == 'O'.code && data[off + 1].toInt() == 'P'.code && len >= 12) {
            // OPUS
            isOpus = true
            sampleRate = 48000
            channels = 2
            // OPUS 暂不解码
          } else {
            // PCM header: 8B [sr(4) ch(2) frame(2)] little-endian
            isOpus = false
            sampleRate = ((data[off + 3].toInt() and 0xFF) shl 24) or ((data[off + 2].toInt() and 0xFF) shl 16) or ((data[off + 1].toInt() and 0xFF) shl 8) or (data[off + 0].toInt() and 0xFF)
            channels = ((data[off + 5].toInt() and 0xFF) shl 8) or (data[off + 4].toInt() and 0xFF)
            if (sampleRate <= 0) { sampleRate = 48000 }
            if (channels != 1 && channels != 2) { channels = 2 }
          }
          val audioFormat = if (channels == 1) AudioFormat.CHANNEL_OUT_MONO else AudioFormat.CHANNEL_OUT_STEREO
          if (!configured || curSr != sampleRate || curCh != channels) {
            try { track?.stop(); track?.release() } catch (_: Throwable) {}
            val minBuf = AudioTrack.getMinBufferSize(sampleRate, audioFormat, AudioFormat.ENCODING_PCM_16BIT)
            track = AudioTrack.Builder()
              .setAudioAttributes(
                AudioAttributes.Builder()
                  .setUsage(AudioAttributes.USAGE_MEDIA)
                  .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC)
                  .build()
              )
              .setAudioFormat(
                AudioFormat.Builder()
                  .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                  .setSampleRate(sampleRate)
                  .setChannelMask(audioFormat)
                  .build()
              )
              .setTransferMode(AudioTrack.MODE_STREAM)
              .setBufferSizeInBytes(maxOf(minBuf, 16384))
              .build()
            track?.play()
            configured = true
            curSr = sampleRate
            curCh = channels
            startWriterIfNeeded()
          }
          if (isOpus) {
            // 暂不处理 OPUS
          } else {
            val frameSamplesPerCh = ((data[off + 7].toInt() and 0xFF) shl 8) or (data[off + 6].toInt() and 0xFF)
            val pcmLen = frameSamplesPerCh * channels * 2
            // 先尝试加密载荷
            if (len >= 8 + 12 + 16) {
              val key = decodeBase64Url(pskBase64Url)
              if (key != null && (key.size == 16 || key.size == 32)) {
                try {
                  val nonce = data.copyOfRange(off + 8, off + 20)
                  val cipher = data.copyOfRange(off + 20, off + len - 16)
                  val tag = data.copyOfRange(off + len - 16, off + len)
                  val plain = aesGcmDecrypt(key, nonce, cipher, tag)
                  if (plain != null && plain.size % 2 == 0) {
                    val shorts = ShortArray(plain.size / 2)
                    var si = 0
                    var bi = 0
                    while (bi + 1 < plain.size) {
                      val lo = (plain[bi].toInt() and 0xFF)
                      val hi = (plain[bi + 1].toInt() and 0xFF)
                      shorts[si++] = ((hi shl 8) or lo).toShort()
                      bi += 2
                    }
                    synchronized(lock) {
                      queue.addLast(shorts)
                      while (queue.size > 20) queue.removeFirst()
                      onQueueStat?.invoke(queue.size)
                    }
                    continue
                  }
                } catch (_: Throwable) {}
              }
            }
            // 再尝试明文
            if (len >= 8 + pcmLen) {
              // enqueue
              val shorts = ShortArray(frameSamplesPerCh * channels)
              var si = 0
              var bi = off + 8
              val end = off + 8 + pcmLen
              while (bi + 1 < end) {
                val lo = (data[bi].toInt() and 0xFF)
                val hi = (data[bi + 1].toInt() and 0xFF)
                shorts[si++] = ((hi shl 8) or lo).toShort()
                bi += 2
              }
              synchronized(lock) {
                queue.addLast(shorts)
                // limit queue to 20 frames (~400ms upper bound)
                while (queue.size > 20) queue.removeFirst()
                onQueueStat?.invoke(queue.size)
              }
            }
          }
        }
      } catch (_: Throwable) {
      } finally {
        try { track?.stop(); track?.release() } catch (_: Throwable) {}
        try { socket?.close() } catch (_: Throwable) {}
        writerRunning = false
      }
    }
  }

  fun stop() {
    running = false
    th?.interrupt()
    writerRunning = false
    writerThread?.interrupt()
  }
}


