package com.audiobridge.app.codec

class OpusBridge(sampleRate: Int, channels: Int) {
  private var handle: Long = nativeCreateDecoder(sampleRate, channels)
  fun close() { nativeDestroyDecoder(handle); handle = 0 }
  fun decode(packet: ByteArray, length: Int, outPcm: ShortArray): Int = nativeDecode(handle, packet, length, outPcm)

  companion object {
    init { System.loadLibrary("opusbridge") }
  }

  private external fun nativeCreateDecoder(sampleRate: Int, channels: Int): Long
  private external fun nativeDestroyDecoder(handle: Long)
  private external fun nativeDecode(handle: Long, inBytes: ByteArray, inLen: Int, outPcm: ShortArray): Int
}


