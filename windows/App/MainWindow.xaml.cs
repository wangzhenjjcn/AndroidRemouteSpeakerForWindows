using System;
using System.Linq;
using System.Windows;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;
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
    private Audio.AudioResampler? _resampler;
    private const int TARGET_SAMPLE_RATE = 44100; // 改用 44.1kHz，兼容性更好
    private const float WEB_PRE_ATTENUATION = 0.84f; // 约 -1.5 dB：Web PCM 量化预衰减，减少削波
    private WinForms.NotifyIcon? _tray;
    private WinForms.ToolStripMenuItem? _trayStartStopItem;
    private WinForms.ToolStripMenuItem? _trayAutostartItem;
    private bool _allowExit = false;
    private AudioBridge.Windows.Net.MdnsPublisher? _mdns;
    private AudioBridge.Windows.Net.WebAudioStreamer? _webStreamer;

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
      var m = this.FindName("MenuUseEncryption") as System.Windows.Controls.MenuItem;
      if (m != null) m.IsChecked = s.UseEncryption;
      var ma = this.FindName("MenuAutostart") as System.Windows.Controls.MenuItem;
      if (ma != null) ma.IsChecked = s.Autostart;
      WebEnabledCheck.IsChecked = s.WebEnabled;
      WebPortBox.Text = s.WebPort.ToString();
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
      InitTray();
      this.StateChanged += (_, __) => UpdateTrayTexts();
      this.Closing += (o, args) =>
      {
        if (_allowExit) return;
        bool shouldMinimizeToTray = _isStreaming || _webServiceStarted;
        if (shouldMinimizeToTray)
        {
          args.Cancel = true;
          this.Hide();
          if (_tray != null)
          {
            try { _tray.BalloonTipTitle = "AudioBridge"; _tray.BalloonTipText = "程序已最小化到托盘"; _tray.ShowBalloonTip(1000); } catch { }
          }
        }
        // 未推流且未开启 Web 服务时，允许直接退出（不取消 Closing）
      };
    }

    private void InitTray()
    {
      if (_tray != null) return;
      _tray = new WinForms.NotifyIcon();
      _tray.Icon = Drawing.SystemIcons.Application;
      _tray.Text = "AudioBridge LAN";
      _tray.Visible = true;
      _tray.MouseClick += (_, e) =>
      {
        if (e.Button == WinForms.MouseButtons.Left)
        {
          ShowWindowFromTray();
        }
      };

      var menu = new WinForms.ContextMenuStrip();
      var miShow = new WinForms.ToolStripMenuItem("打开主界面");
      miShow.Click += (_, __) => ShowWindowFromTray();
      _trayStartStopItem = new WinForms.ToolStripMenuItem("开始推流");
      _trayStartStopItem.Click += (_, __) =>
      {
        if (_isStreaming) StopStreaming(); else StartStreaming();
        UpdateTrayTexts();
      };
      _trayAutostartItem = new WinForms.ToolStripMenuItem("开机自启");
      _trayAutostartItem.CheckOnClick = true;
      _trayAutostartItem.Checked = Settings.Load().Autostart;
      _trayAutostartItem.CheckedChanged += (_, __) =>
      {
        SetAutostart(_trayAutostartItem.Checked);
      };
      var miExit = new WinForms.ToolStripMenuItem("退出");
      miExit.Click += async (_, __) => { await ExitAndCleanupAsync(); };

      menu.Items.Add(miShow);
      menu.Items.Add(_trayStartStopItem);
      menu.Items.Add(new WinForms.ToolStripSeparator());
      menu.Items.Add(_trayAutostartItem);
      menu.Items.Add(new WinForms.ToolStripSeparator());
      menu.Items.Add(miExit);
      _tray.ContextMenuStrip = menu;
      UpdateTrayTexts();
    }

    private void ShowWindowFromTray()
    {
      this.Show();
      if (this.WindowState == WindowState.Minimized) this.WindowState = WindowState.Normal;
      this.Activate();
      UpdateTrayTexts();
    }

    private void UpdateTrayTexts()
    {
      try
      {
        if (_tray != null)
        {
          _tray.Text = _isStreaming ? "AudioBridge LAN - 推流中" : "AudioBridge LAN - 未推流";
        }
        if (_trayStartStopItem != null)
        {
          _trayStartStopItem.Text = _isStreaming ? "停止推流" : "开始推流";
        }
        if (_trayAutostartItem != null)
        {
          _trayAutostartItem.Checked = Settings.Load().Autostart;
        }
      }
      catch { }
    }

    private void Menu_GenerateQr_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        var s = Settings.Load();
        string host = System.Net.Dns.GetHostName();
        int ctrlPort = 8181;
        int audioPort = int.TryParse(TargetPortBox.Text, out var tp) ? tp : 5004;
        var img = AudioBridge.Windows.Net.QrHelper.GeneratePairQrImageSource(host, ctrlPort, audioPort, s.PskBase64Url);
        var w = new System.Windows.Window
        {
          Title = "配对二维码",
          Width = 360,
          Height = 420,
          Content = new System.Windows.Controls.Border
          {
            Padding = new Thickness(12),
            Child = new System.Windows.Controls.Image { Source = img, Stretch = System.Windows.Media.Stretch.Uniform }
          }
        };
        w.ShowDialog();
        StatusText.Text = "已显示二维码弹窗";
      }
      catch (Exception ex)
      {
        StatusText.Text = "生成二维码失败：" + ex.Message;
      }
    }

    private async void Menu_Exit_Click(object sender, RoutedEventArgs e)
    {
      await ExitAndCleanupAsync();
    }

    private void Menu_UseEncryption_Click(object sender, RoutedEventArgs e)
    {
      var mi = sender as System.Windows.Controls.MenuItem;
      var s = Settings.Load();
      s.UseEncryption = mi?.IsChecked == true;
      s.Save();
      StatusText.Text = s.UseEncryption ? "已启用加密（需客户端 PSK 一致）" : "已关闭加密";
    }

    private void Menu_CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
      // 简单提示：Android 端在 控制端口/download/app-debug.apk 下载
      StatusText.Text = "更新包路由：/download/app-debug.apk";
    }

    private void Menu_About_Click(object sender, RoutedEventArgs e)
    {
      System.Windows.MessageBox.Show("AudioBridge LAN\nWindows-Android 局域网音频桥接\n支持 Android/Web 客户端\n© 2025", "关于");
    }

    private void WebEnabledCheck_Click(object sender, RoutedEventArgs e)
    {
      var s = Settings.Load();
      s.WebEnabled = WebEnabledCheck.IsChecked == true;
      s.Save();
      
      if (s.WebEnabled)
      {
        // 立即启动 Web 服务(不需要等待推流)
        _ = StartWebAudioServiceAsync();
      }
      else
      {
        _ = StopWebAudioServiceAsync();
      }
    }

    private void OpenWebButton_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        int port = int.TryParse(WebPortBox.Text, out var p) ? p : 29763;
        string url = $"http://localhost:{port}/player.html";
        var psi = new System.Diagnostics.ProcessStartInfo
        {
          FileName = url,
          UseShellExecute = true
        };
        System.Diagnostics.Process.Start(psi);
        StatusText.Text = "已在浏览器中打开 Web 播放器";
      }
      catch (Exception ex)
      {
        StatusText.Text = "打开网页失败：" + ex.Message;
      }
    }

    private void Menu_Autostart_Click(object sender, RoutedEventArgs e)
    {
      var mi = sender as System.Windows.Controls.MenuItem;
      bool enable = mi?.IsChecked == true;
      SetAutostart(enable);
    }

    private void SetAutostart(bool enable)
    {
      try
      {
        var s = Settings.Load();
        s.Autostart = enable;
        s.Save();
        const string runKey = "HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        string name = "AudioBridgeLAN";
        if (enable)
        {
          string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
          Microsoft.Win32.Registry.SetValue(runKey, name, '"' + exe + '"');
          StatusText.Text = "已启用开机自启";
        }
        else
        {
          using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true);
          key?.DeleteValue("AudioBridgeLAN", false);
          StatusText.Text = "已关闭开机自启";
        }
        UpdateTrayTexts();
      }
      catch (Exception ex)
      {
        StatusText.Text = "开机自启设置失败：" + ex.Message;
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
      // start web audio service if enabled
      if (WebEnabledCheck.IsChecked == true)
      {
        _ = StartWebAudioServiceAsync();
      }
      // 生成配对二维码
      try
      {
        var s = Settings.Load();
        string host = System.Net.Dns.GetHostName();
        int ctrlPort = 8181;
        int audioPort = int.TryParse(TargetPortBox.Text, out var tp) ? tp : 5004;
        var img = AudioBridge.Windows.Net.QrHelper.GeneratePairQrImageSource(host, ctrlPort, audioPort, s.PskBase64Url);
        var w = new System.Windows.Window
        {
          Title = "配对二维码",
          Width = 360,
          Height = 420,
          Content = new System.Windows.Controls.Border
          {
            Padding = new Thickness(12),
            Child = new System.Windows.Controls.Image { Source = img, Stretch = System.Windows.Media.Stretch.Uniform }
          }
        };
        w.Show();
        StatusText.Text = "状态：推流中，已弹出二维码";
      }
      catch { }
      UpdateTrayTexts();
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
      try { _mdns?.Stop(); } catch { }
      _ = StopWebAudioServiceAsync();
      UpdateTrayTexts();
    }

    private bool _webServiceStarted = false;
    
    private async System.Threading.Tasks.Task StartWebAudioServiceAsync()
    {
      try
      {
        // 避免重复启动
        if (_webServiceStarted) return;
        
        int port = int.TryParse(WebPortBox.Text, out var p) ? p : 29763;
        
        if (_webStreamer == null)
        {
          _webStreamer = new AudioBridge.Windows.Net.WebAudioStreamer();
          _webStreamer.StartStreaming();
        }
        
        // 确保控制服务器已启动(Web 服务依赖它)
        await EnsureControlServerStarted();
        
        if (_ctrl != null)
        {
          await _ctrl.StartWebAudioAsync(port, _webStreamer);
          
          // 保存配置
          var s = Settings.Load();
          s.WebPort = port;
          s.Save();
          
          _webServiceStarted = true;
          StatusText.Text = $"Web 服务已启动，访问 http://localhost:{port}";
          OpenWebButton.IsEnabled = true;
          
          // 自动复制 wwwroot 中的静态文件(如果不存在)
          EnsureWebFilesExist();
        }
      }
      catch (Exception ex)
      {
        StatusText.Text = "启动 Web 服务失败：" + ex.Message;
        WebEnabledCheck.IsChecked = false;
      }
    }

    private async System.Threading.Tasks.Task StopWebAudioServiceAsync()
    {
      try
      {
        if (_ctrl != null)
        {
          await _ctrl.StopWebAudioAsync();
        }
        _webStreamer?.StopStreaming();
        _webStreamer = null;
        _webServiceStarted = false;
        OpenWebButton.IsEnabled = false;
        StatusText.Text = "Web 服务已停止";
      }
      catch (Exception ex)
      {
        StatusText.Text = "停止 Web 服务失败：" + ex.Message;
      }
    }

    private async System.Threading.Tasks.Task ExitAndCleanupAsync()
    {
      try
      {
        if (_isStreaming)
        {
          StopStreaming();
        }
      }
      catch { }

      try { await StopWebAudioServiceAsync(); } catch { }
      try { _ctrl?.Dispose(); _ctrl = null; } catch { }
      try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; } } catch { }

      _allowExit = true;
      Close();
    }

    private void EnsureWebFilesExist()
    {
      try
      {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var wwwroot = System.IO.Path.Combine(baseDir, "wwwroot");
        
        // 检查是否存在 player.html
        var playerHtml = System.IO.Path.Combine(wwwroot, "player.html");
        if (!System.IO.File.Exists(playerHtml))
        {
          System.Diagnostics.Debug.WriteLine("[MainWindow] Warning: wwwroot/player.html not found");
        }
      }
      catch { }
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
      // 启动 mDNS 广播
      try
      {
        var s = Settings.Load();
        int ctrlPort = 8181;
        int audioPort = int.TryParse(TargetPortBox.Text, out var tp) ? tp : 5004;
        string name = System.Net.Dns.GetHostName();
        _mdns ??= new AudioBridge.Windows.Net.MdnsPublisher();
        _mdns.Start(name, ctrlPort, audioPort, s.PskBase64Url);
      }
      catch { }
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
        
        // 在状态栏显示当前采样率（便于调试）
        this.Dispatcher.Invoke(() =>
        {
          if (StatusText.Text.Contains("推流中") && !StatusText.Text.Contains("Hz"))
          {
            StatusText.Text += $" ({sampleRate}Hz {channels}ch)";
          }
        });
        
        // reset accumulator when format changes
        if (sampleRate != _lastSampleRate || channels != _lastChannels)
        {
          _accum = Array.Empty<float>();
          _accumCount = 0;
          _lastSampleRate = sampleRate;
          _lastChannels = channels;
          
          // Web 端：改为直接使用源采样率推送，避免 48k -> 44.1k 下采样造成的高频折叠失真
          _resampler = null;
          System.Diagnostics.Debug.WriteLine($"[MainWindow] Web stream using source sample rate: {sampleRate}Hz");
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
            bool doEncrypt = settings.UseEncryption && psk != null && (psk.Length == 16 || psk.Length == 32);
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
              
              // 同时发送到 Web 客户端（使用 44.1kHz）
              if (_webStreamer != null && _webStreamer.IsStreaming)
              {
                SendToWebClients(frameSamplesPcm);
              }
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
              
              // Web 客户端使用未加密的版本并重采样到 44.1kHz
              if (_webStreamer != null && _webStreamer.IsStreaming)
              {
                SendToWebClients(frameSamplesPcm);
              }
            }
            // shift remaining samples left
            _accumCount -= frameSamplesPcm;
            if (_accumCount > 0)
            {
              Array.Copy(_accum, frameSamplesPcm, _accum, 0, _accumCount);
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
          var settings = Settings.Load();
          var psk = settings.GetPskBytes();
          bool doEncrypt = settings.UseEncryption && psk != null && (psk.Length == 16 || psk.Length == 32);
          if (!doEncrypt)
          {
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
            
            // 同步到 Web 客户端：使用浮点源直接量化（避免沿用已可能裁剪的 pcm16）
            if (_webStreamer != null && _webStreamer.IsStreaming)
            {
              SendToWebClients(frameSamples);
            }
          }
          else
          {
            // 12(header) + 12(nonce) + cipher + 16(tag)
            byte[] packet = new byte[12 + 12 + len + 16];
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
            var nonce = new byte[12];
            System.Security.Cryptography.RandomNumberGenerator.Fill(nonce);
            Buffer.BlockCopy(nonce, 0, packet, 12, 12);
            var plain = opusBuf.Slice(0, len).ToArray();
            var cipher = new byte[len];
            var tag = new byte[16];
            Crypto.EncryptAesGcm(psk!, nonce, plain, cipher, tag);
            Buffer.BlockCopy(cipher, 0, packet, 24, len);
            Buffer.BlockCopy(tag, 0, packet, 24 + len, 16);
            _udp.Send(packet);
            
            // 同步到 Web 客户端：使用浮点源直接量化（避免沿用已可能裁剪的 pcm16）
            if (_webStreamer != null && _webStreamer.IsStreaming)
            {
              SendToWebClients(frameSamples);
            }
          }
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

    private void SendToWebClients(int frameSampleCount)
    {
      try
      {
        if (_webStreamer == null || !_webStreamer.IsStreaming) return;
        
        int channels = _lastChannels;
        int sampleRate = _lastSampleRate;

        // 直接使用源采样率与当前帧样本，避免在服务端做重采样
        int outputSampleCount = frameSampleCount;
        int outputFrameCount = outputSampleCount / channels;

        // 封包并转换为 PCM16（加入预衰减，降低削波概率）
        int headerLen = 8;
        byte[] webPayload = new byte[headerLen + outputSampleCount * 2];
        
        // 写入头信息
        unchecked
        {
          webPayload[0] = (byte)(sampleRate & 0xFF);
          webPayload[1] = (byte)((sampleRate >> 8) & 0xFF);
          webPayload[2] = (byte)((sampleRate >> 16) & 0xFF);
          webPayload[3] = (byte)((sampleRate >> 24) & 0xFF);
          webPayload[4] = (byte)(channels & 0xFF);
          webPayload[5] = (byte)((channels >> 8) & 0xFF);
          webPayload[6] = (byte)(outputFrameCount & 0xFF);
          webPayload[7] = (byte)((outputFrameCount >> 8) & 0xFF);
        }
        
        // 转换为 PCM16（直接读取累积缓冲区的当前帧样本）
        int outIdx = headerLen;
        for (int i = 0; i < outputSampleCount; i++)
        {
          float fs = _accum[i] * WEB_PRE_ATTENUATION;
          short s = (short)Math.Clamp(fs * 32767f, short.MinValue, short.MaxValue);
          webPayload[outIdx++] = (byte)(s & 0xFF);
          webPayload[outIdx++] = (byte)((s >> 8) & 0xFF);
        }
        
        _ = _ctrl?.BroadcastWebAudioAsync(new ReadOnlyMemory<byte>(webPayload));
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[MainWindow] SendToWebClients error: {ex.Message}");
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


