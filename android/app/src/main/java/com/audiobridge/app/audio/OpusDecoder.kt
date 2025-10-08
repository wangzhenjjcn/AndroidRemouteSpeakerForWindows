package com.audiobridge.app.audio

// 占位实现：当前工程未引入 Concentus 依赖，保留空解码器以保证编译通过。
class SimpleOpusDecoder(
  private val sampleRate: Int = 48000,
  private val channels: Int = 2
) {
  fun decode(input: ByteArray, offset: Int, length: Int, outPcm16: ShortArray): Int {
    // 未启用 Opus 解码，返回 0 表示忽略
    return 0
  }
}


