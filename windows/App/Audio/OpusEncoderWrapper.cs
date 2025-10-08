using System;
using Concentus.Common;
using Concentus.Enums;
using Concentus.Structs;

namespace AudioBridge.Windows.Audio
{
  public sealed class OpusEncoderWrapper : IDisposable
  {
    private readonly OpusEncoder _encoder;
    private readonly int _channels;
    private readonly int _frameSizePerChannel; // samples per channel per frame

    public OpusEncoderWrapper(int sampleRate = 48000, int channels = 2, int bitrateKb = 96, bool vbr = true, bool fec = false, int frameMs = 20)
    {
      _channels = channels;
      _frameSizePerChannel = sampleRate / 1000 * frameMs; // e.g., 48k -> 960
      _encoder = OpusEncoder.Create(sampleRate, channels, OpusApplication.OPUS_APPLICATION_AUDIO);
      _encoder.Bitrate = bitrateKb * 1000;
      _encoder.ForceChannels = channels;
      _encoder.UseVBR = vbr;
      _encoder.EnableAnalysis = false;
      _encoder.SignalType = OpusSignal.OPUS_SIGNAL_MUSIC;
      _encoder.UseInbandFEC = fec;
    }

    public int Encode(ReadOnlySpan<short> pcm16Interleaved, Span<byte> opusOut)
    {
      if (pcm16Interleaved.Length != _frameSizePerChannel * _channels)
      {
        throw new ArgumentException("PCM length must equal one frame (frameMs * sampleRate)");
      }
      // Concentus expects short[]
      short[] buf = pcm16Interleaved.ToArray();
      byte[] outBuf = opusOut.ToArray();
      int len = _encoder.Encode(buf, 0, _frameSizePerChannel, outBuf, 0, outBuf.Length);
      outBuf.AsSpan(0, len).CopyTo(opusOut);
      return len;
    }

    public void Dispose()
    {
      // no unmanaged resources in Concentus encoder
    }
  }
}


