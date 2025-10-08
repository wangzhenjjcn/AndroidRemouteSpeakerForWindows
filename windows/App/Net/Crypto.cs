using System;
using System.Security.Cryptography;

namespace AudioBridge.Windows.Net
{
  public static class Crypto
  {
    public static int EncryptAesGcm(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext, Span<byte> tag)
    {
      using var gcm = new AesGcm(key);
      gcm.Encrypt(nonce, plaintext, ciphertext, tag, ReadOnlySpan<byte>.Empty);
      return plaintext.Length;
    }

    public static int DecryptAesGcm(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag, Span<byte> plaintext)
    {
      using var gcm = new AesGcm(key);
      gcm.Decrypt(nonce, ciphertext, tag, plaintext, ReadOnlySpan<byte>.Empty);
      return ciphertext.Length;
    }
  }
}


