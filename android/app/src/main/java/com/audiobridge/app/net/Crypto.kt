package com.audiobridge.app.net

import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec
import android.util.Base64

fun decodeBase64Url(s: String?): ByteArray? {
  if (s.isNullOrBlank()) return null
  return try {
    val pad = when (s.length % 4) { 2 -> "=="; 3 -> "="; else -> "" }
    Base64.decode(s.replace('-', '+').replace('_', '/') + pad, Base64.DEFAULT)
  } catch (e: Throwable) { null }
}

fun aesGcmDecrypt(key: ByteArray, nonce: ByteArray, cipher: ByteArray, tag: ByteArray): ByteArray? {
  return try {
    val combined = ByteArray(cipher.size + tag.size)
    System.arraycopy(cipher, 0, combined, 0, cipher.size)
    System.arraycopy(tag, 0, combined, cipher.size, tag.size)
    val sk = SecretKeySpec(key, "AES")
    val spec = GCMParameterSpec(128, nonce)
    val cp = Cipher.getInstance("AES/GCM/NoPadding")
    cp.init(Cipher.DECRYPT_MODE, sk, spec)
    cp.doFinal(combined)
  } catch (e: Throwable) { null }
}


