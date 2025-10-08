using System;
using System.Linq;
using System.Windows;
using NAudio.CoreAudioApi;
using AudioBridge.Windows.Audio;
using AudioBridge.Windows.Net;
using System.Net;
using AudioBridge.Windows.Config;
using AudioBridge.Windows.Media;

namespace AudioBridge.Windows
{
  public partial class MainWindow : Window
  {
    private readonly MMDeviceEnumerator _deviceEnumerator = new MMDeviceEnumerator();
    private LoopbackCapture? _capture;
    private bool _isStreaming;
    private int _bitrateKbps = 96;
    private readonly UdpAudioSender _udp = new UdpAudioSender();
    private OpusEncoderWrapper? _opus;
    private ushort _seq;
    private uint _ts48k;
    // Accumulator to assemble exact frame sizes across DataAvailable events
    private float[] _accum = Array.Empty<float>();
    private int _accumCount = 0;
    private int _lastSampleRate = 0;
    private int _lastChannels = 0;

    public MainWindow()
    {
      InitializeComponent();
      Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
      RefreshDevices();
      UpdateBitrateLabel();
      // Load settings
      var s = Settings.Load();
      TargetIpBox.Text = s.TargetIp;
      TargetPortBox.Text = s.TargetPort.ToString();
      BitrateSlider.Value = s.BitrateKbps;
      UseOpusCheck.IsChecked = s.UseOpus;
      if (!string.IsNullOrWhiteSpace(s.DeviceId))
      {
        var devices = DeviceCombo.ItemsSource as System.Collections.IEnumerable;
        if (devices != null)
        {
          foreach (var d in devices)
          {
            if (d is MMDevice md && md.ID == s.DeviceId) { DeviceCombo.SelectedItem = d; break; }
          }
        }
      }
    }

    private void RefreshDevicesButton_Click(object sender, RoutedEventArgs e)
    {
      RefreshDevices();
    }

    private void RefreshDevices()
    {
      var devices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
        .OrderByDescending(d => d.ID == _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID)
        .ToList();
      DeviceCombo.ItemsSource = devices;
      DeviceCombo.DisplayMemberPath = nameof(MMDevice.FriendlyName);
      DeviceCombo.SelectedItem = devices.FirstOrDefault();
    }

    private void BitrateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      _bitrateKbps = (int)Math.Round(e.NewValue);
      UpdateBitrateLabel();
    }

    private void UpdateBitrateLabel()
    {
      if (BitrateLabel != null)
      {
        BitrateLabel.Text = _bitrateKbps + " kbps";
      }
    }

    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
      if (_isStreaming)
      {
        StopStreaming();
      }
      else
      {
        StartStreaming();
      }
    }

    private void StartStreaming()
    {
      var selected = DeviceCombo?.SelectedItem as MMDevice;
      ConfigureUdpFromUi();
      _capture = new LoopbackCapture();
      _capture.OnPcm += OnPcm;
      _capture.Start(selected);
      _seq = 0;
      _ts48k = 0;
      _opus = UseOpusCheck.IsChecked == true ? new Audio.OpusEncoderWrapper(48000, 2, _bitrateKbps, vbr: true, fec: false, frameMs: 20) : null;
      _isStreaming = true;
      StartStopButton.Content = "停止推流";
      StatusText.Text = "状态：推流中  码率=" + _bitrateKbps + " kbps";
      // start control server for basic commands
      _ = EnsureControlServerStarted();
      // 生成配对二维码
      try
      {
        var s = Settings.Load();
        string host = System.Net.Dns.GetHostName();
        int ctrlPort = 8181;
        int audioPort = int.TryParse(TargetPortBox.Text, out var tp) ? tp : 5004;
        string path = AudioBridge.Windows.Net.QrHelper.GeneratePairQrPng(host, ctrlPort, audioPort, s.PskBase64Url);
        StatusText.Text = "状态：推流中，已生成二维码：" + path;
      }
      catch { }
    }

    private void StopStreaming()
    {
      _capture?.Dispose();
      _capture = null;
      _opus = null;
      _accum = Array.Empty<float>();
      _accumCount = 0;
      _lastSampleRate = 0;
      _lastChannels = 0;
      _isStreaming = false;
      StartStopButton.Content = "开始推流";
      StatusText.Text = "状态：未推流";
    }

    private static AudioBridge.Windows.Net.ControlServer? _ctrl;
    private async System.Threading.Tasks.Task EnsureControlServerStarted()
    {
      if (_ctrl != null) return;
      _ctrl = new AudioBridge.Windows.Net.ControlServer();
      await _ctrl.StartAsync(8181, async (action, value) =>
      {
        switch (action)
        {
          case "play": MediaController.PlayPause(); break; // toggle
          case "pause": MediaController.PlayPause(); break; // toggle
          case "next": MediaController.Next(); break;
          case "prev": MediaController.Prev(); break;
          case "seek": /* not supported via media keys */ break;
        }
        await System.Threading.Tasks.Task.CompletedTask;
      });
      // start simple telemetry timer
      var timer = new System.Timers.Timer(1000);
      timer.Elapsed += async (_, __) =>
      {
        try
        {
          if (_ctrl != null)
          {
            await _ctrl.BroadcastAsync(new { type = "telemetry", latencyMs = 0, pktLossPct = 0.0, jitterMs = 0 });
          }
        }
        catch { }
      };
      timer.AutoReset = true;
      timer.Start();
      // mDNS 暂不启用，改为二维码配对
    }


    private void OnPcm(ReadOnlyMemory<float> pcm)
    {
      // 将 float PCM -> Opus 帧（20ms, 48kHz, 立体声）并封包发送
      try
      {
        var format = _capture?.Format;
        if (format == null) return;
        int sampleRate = format.SampleRate;
        int channels = format.Channels;
        // reset accumulator when format changes
        if (sampleRate != _lastSampleRate || channels != _lastChannels)
        {
          _accum = Array.Empty<float>();
          _accumCount = 0;
          _lastSampleRate = sampleRate;
          _lastChannels = channels;
        }
        // append incoming samples into accumulator
        if (_accum.Length < _accumCount + pcm.Length)
        {
          int newCap = Math.Max(_accum.Length * 2, _accumCount + pcm.Length);
          if (newCap == 0) newCap = pcm.Length;
          Array.Resize(ref _accum, newCap);
        }
        pcm.Span.CopyTo(new Span<float>(_accum, _accumCount, pcm.Length));
        _accumCount += pcm.Length;
        // 当未启用 Opus 或当前不是 48k/立体声时，使用 PCM 回退路径
        if (_opus == null || sampleRate != 48000 || channels != 2)
        {
          // 回退：发送 PCM（与之前相同的 8B 头格式）
          int frameSamplesPerChPcm = Math.Max(1, sampleRate / 50); // 每 20ms 帧
          int frameSamplesPcm = frameSamplesPerChPcm * channels;
          while (_accumCount >= frameSamplesPcm)
          {
            int headerLen = 8;
            var settings = Settings.Load();
            var psk = settings.GetPskBytes();
            bool doEncrypt = psk != null && (psk.Length == 16 || psk.Length == 32);
            int frameBytes = frameSamplesPcm * 2;
            if (!doEncrypt)
            {
              Span<byte> payload = stackalloc byte[headerLen + frameBytes];
              unchecked
              {
                payload[0] = (byte)(sampleRate & 0xFF);
                payload[1] = (byte)((sampleRate >> 8) & 0xFF);
                payload[2] = (byte)((sampleRate >> 16) & 0xFF);
                payload[3] = (byte)((sampleRate >> 24) & 0xFF);
                payload[4] = (byte)(channels & 0xFF);
                payload[5] = (byte)((channels >> 8) & 0xFF);
                payload[6] = (byte)(frameSamplesPerChPcm & 0xFF);
                payload[7] = (byte)((frameSamplesPerChPcm >> 8) & 0xFF);
              }
              int outIdx = headerLen;
              for (int i = 0; i < frameSamplesPcm; i++)
              {
                float f = _accum[i];
                short s = (short)Math.Clamp(f * 32767f, short.MinValue, short.MaxValue);
                payload[outIdx++] = (byte)(s & 0xFF);
                payload[outIdx++] = (byte)((s >> 8) & 0xFF);
              }
              _udp.Send(payload);
            }
            else
            {
              byte[] packet = new byte[headerLen + 12 + frameBytes + 16];
              unchecked
              {
                packet[0] = (byte)(sampleRate & 0xFF);
                packet[1] = (byte)((sampleRate >> 8) & 0xFF);
                packet[2] = (byte)((sampleRate >> 16) & 0xFF);
                packet[3] = (byte)((sampleRate >> 24) & 0xFF);
                packet[4] = (byte)(channels & 0xFF);
                packet[5] = (byte)((channels >> 8) & 0xFF);
                packet[6] = (byte)(frameSamplesPerChPcm & 0xFF);
                packet[7] = (byte)((frameSamplesPerChPcm >> 8) & 0xFF);
              }
              // plaintext
              byte[] plain = new byte[frameBytes];
              int pi = 0;
              for (int i = 0; i < frameSamplesPcm; i++)
              {
                short s = (short)Math.Clamp(_accum[i] * 32767f, short.MinValue, short.MaxValue);
                plain[pi++] = (byte)(s & 0xFF);
                plain[pi++] = (byte)((s >> 8) & 0xFF);
              }
              var nonce = new byte[12];
              System.Security.Cryptography.RandomNumberGenerator.Fill(nonce);
              Buffer.BlockCopy(nonce, 0, packet, headerLen, 12);
              var cipher = new byte[frameBytes];
              var tag = new byte[16];
              Crypto.EncryptAesGcm(psk!, nonce, plain, cipher, tag);
              Buffer.BlockCopy(cipher, 0, packet, headerLen + 12, frameBytes);
              Buffer.BlockCopy(tag, 0, packet, headerLen + 12 + frameBytes, 16);
              _udp.Send(packet);
            }
            // shift remaining samples left
            _accumCount -= frameSamplesPcm;
            if (_accumCount > 0)
            {
              Array.Copy(_accum, frameSamplesPerChPcm, _accum, 0, _accumCount);
            }
          }
          return;
        }
        int frameSamplesPerCh = 48000 / 50; // 20ms -> 960
        int frameSamples = frameSamplesPerCh * 2;
        while (_accumCount >= frameSamples)
        {
          // float -> short16 interleaved
          short[] pcm16 = new short[frameSamples];
          for (int i = 0; i < frameSamples; i++)
          {
            pcm16[i] = (short)Math.Clamp(_accum[i] * 32767f, short.MinValue, short.MaxValue);
          }
          Span<byte> opusBuf = stackalloc byte[400]; // 20ms 96kbps 立体声足够
          int len = _opus.Encode(pcm16, opusBuf);

          // Header: 12B [ 'O''P''U''S'(4) | seq(2 LE) | ts48k(4 LE) | flags(1) | rsv(1) ]
          Span<byte> packet = stackalloc byte[12 + len];
          packet[0] = (byte)'O'; packet[1] = (byte)'P'; packet[2] = (byte)'U'; packet[3] = (byte)'S';
          unchecked { packet[4] = (byte)(_seq & 0xFF); packet[5] = (byte)((_seq >> 8) & 0xFF); }
          unchecked {
            packet[6] = (byte)(_ts48k & 0xFF);
            packet[7] = (byte)((_ts48k >> 8) & 0xFF);
            packet[8] = (byte)((_ts48k >> 16) & 0xFF);
            packet[9] = (byte)((_ts48k >> 24) & 0xFF);
          }
          packet[10] = 0; // flags
          packet[11] = 0; // rsv
          opusBuf.Slice(0, len).CopyTo(packet.Slice(12));
          _udp.Send(packet);
          _seq++;
          _ts48k += (uint)frameSamplesPerCh;

          // shift remaining samples left
          _accumCount -= frameSamples;
          if (_accumCount > 0)
          {
            Array.Copy(_accum, frameSamples, _accum, 0, _accumCount);
          }
        }
      }
      catch
      {
        // swallow send errors in prototype
      }
    }

    private void ConfigureUdpFromUi()
    {
      try
      {
        string ip = TargetIpBox.Text?.Trim() ?? "127.0.0.1";
        int port = 5004;
        int.TryParse(TargetPortBox.Text, out port);
        _udp.Bind();
        _udp.Configure(new IPEndPoint(IPAddress.Parse(ip), port));
        // save settings
        var s = new Settings
        {
          TargetIp = ip,
          TargetPort = port,
          BitrateKbps = _bitrateKbps,
          UseOpus = UseOpusCheck.IsChecked == true,
          DeviceId = (DeviceCombo?.SelectedItem as MMDevice)?.ID
        };
        s.Save();
      }
      catch
      {
        // ignore invalid input
      }
    }
  }
}


