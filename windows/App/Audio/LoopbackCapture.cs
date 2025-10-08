using System;
using System.Buffers;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioBridge.Windows.Audio
{
  public sealed class LoopbackCapture : IDisposable
  {
    public event Action<ReadOnlyMemory<float>>? OnPcm;

    private WasapiLoopbackCapture? _capture;
    private readonly object _gate = new object();
    private float[] _buffer = Array.Empty<float>();
    public WaveFormat? Format { get; private set; }

    public void Start(MMDevice? device = null)
    {
      lock (_gate)
      {
        if (_capture != null) return;
        var enumerator = new MMDeviceEnumerator();
        var dev = device ?? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _capture = new WasapiLoopbackCapture(dev);
        Format = _capture.WaveFormat;
        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += OnStopped;
        _capture.StartRecording();
      }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
      // Convert 32-bit float interleaved PCM
      int samples = e.BytesRecorded / sizeof(float);
      if (_buffer.Length < samples) _buffer = ArrayPool<float>.Shared.Rent(samples);
      Buffer.BlockCopy(e.Buffer!, 0, _buffer, 0, e.BytesRecorded);
      OnPcm?.Invoke(new ReadOnlyMemory<float>(_buffer, 0, samples));
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
      // no-op
    }

    public void Stop()
    {
      lock (_gate)
      {
        var c = _capture;
        _capture = null;
        if (c != null)
        {
          c.DataAvailable -= OnData;
          c.RecordingStopped -= OnStopped;
          c.StopRecording();
          c.Dispose();
        }
      }
    }

    public void Dispose()
    {
      Stop();
      if (_buffer.Length > 0)
      {
        ArrayPool<float>.Shared.Return(_buffer);
        _buffer = Array.Empty<float>();
      }
    }
  }
}


