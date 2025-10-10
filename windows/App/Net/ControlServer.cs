using System;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;

namespace AudioBridge.Windows.Net
{
  public sealed class ControlServer : IDisposable
  {
    private IHost? _host;
    private IHost? _webHost;
    private Func<string, int, Task>? _onCommand; // action,value
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new ConcurrentDictionary<Guid, WebSocket>();
    private WebAudioStreamer? _webAudioStreamer;

    public Task StartAsync(int port = 8181, Func<string, int, Task>? onCommand = null)
    {
      _onCommand = onCommand;
      _host = Host.CreateDefaultBuilder()
        .ConfigureWebHostDefaults(webBuilder =>
        {
          webBuilder.UseKestrel(options =>
          {
            options.Listen(IPAddress.Any, port);
          });
          webBuilder.Configure(app =>
          {
            app.UseWebSockets();
            app.Run(HandleRequestAsync);
          });
        })
        .Build();
      return _host.StartAsync();
    }

    private async Task HandleRequestAsync(HttpContext context)
    {
      var path = context.Request.Path.Value ?? "/";
      if (path.StartsWith("/control") && context.WebSockets.IsWebSocketRequest)
      {
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var id = Guid.NewGuid();
        _clients.TryAdd(id, socket);
        try
        {
          await EchoLoopAsync(socket);
        }
        finally
        {
          _clients.TryRemove(id, out _);
        }
        return;
      }
      if (path.StartsWith("/download/"))
      {
        var file = path.Substring("/download/".Length);
        var apkPath = ResolveApkPath(file);
        if (!string.IsNullOrEmpty(apkPath) && System.IO.File.Exists(apkPath))
        {
          context.Response.ContentType = "application/vnd.android.package-archive";
          await context.Response.SendFileAsync(apkPath);
          return;
        }
        context.Response.StatusCode = 404;
        await context.Response.WriteAsync("Not Found");
        return;
      }
      context.Response.StatusCode = 200;
      await context.Response.WriteAsync("AudioBridge ControlServer");
    }

    private static string? ResolveApkPath(string file)
    {
      try
      {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        // try ../Android/app-debug.apk relative to dist/*
        var distDir = System.IO.Directory.GetParent(baseDir)?.Parent?.FullName;
        if (!string.IsNullOrEmpty(distDir))
        {
          var p = System.IO.Path.Combine(distDir, "Android", file);
          if (System.IO.File.Exists(p)) return p;
        }
      }
      catch { }
      return null;
    }

    private async Task EchoLoopAsync(WebSocket ws)
    {
      var buffer = new byte[4096];
      while (ws.State == WebSocketState.Open)
      {
        var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        if (result.MessageType == WebSocketMessageType.Close) break;
        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        try
        {
          using var doc = JsonDocument.Parse(json);
          var root = doc.RootElement;
          // optional handshake auth
          if (root.TryGetProperty("type", out var tp0) && tp0.GetString() == "hello")
          {
            // { type:"hello", clientId, nonce, timestamp, hmac }
            if (root.TryGetProperty("clientId", out var cid) && root.TryGetProperty("nonce", out var nn) && root.TryGetProperty("timestamp", out var ts) && root.TryGetProperty("hmac", out var hm))
            {
              var settings = AudioBridge.Windows.Config.Settings.Load();
              var key = settings.GetPskBytes();
              if (key != null)
              {
                var data = (cid.GetString() ?? "") + (nn.GetString() ?? "") + ts.GetInt64().ToString();
                var mac = new System.Security.Cryptography.HMACSHA256(key);
                var calc = mac.ComputeHash(Encoding.UTF8.GetBytes(data));
                string calcB64 = Convert.ToBase64String(calc).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                if (calcB64 != hm.GetString())
                {
                  await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "auth failed", CancellationToken.None);
                  return;
                }
              }
            }
            continue;
          }
          if (root.TryGetProperty("type", out var tp) && tp.GetString() == "cmd")
          {
            var action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
            var value = root.TryGetProperty("value", out var v) ? v.GetInt32() : 0;
            if (_onCommand != null) await _onCommand.Invoke(action, value);
          }
        }
        catch { }
      }
      try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
    }

    public async Task BroadcastAsync(object payload, CancellationToken ct = default)
    {
      var json = JsonSerializer.Serialize(payload);
      var msg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(json));
      foreach (var kv in _clients)
      {
        var ws = kv.Value;
        if (ws.State == WebSocketState.Open)
        {
          try { await ws.SendAsync(msg, WebSocketMessageType.Text, true, ct); } catch { }
        }
      }
    }

    /// <summary>
    /// 启动 Web 音频服务
    /// </summary>
    public async Task StartWebAudioAsync(int port, WebAudioStreamer streamer)
    {
      _webAudioStreamer = streamer;
      
      System.Diagnostics.Debug.WriteLine($"[ControlServer] Starting Web Audio service on port {port}");
      
      _webHost = Host.CreateDefaultBuilder()
        .ConfigureWebHostDefaults(webBuilder =>
        {
          webBuilder.UseKestrel(options =>
          {
            options.Listen(IPAddress.Any, port);
            System.Diagnostics.Debug.WriteLine($"[ControlServer] Kestrel listening on 0.0.0.0:{port}");
          });
          webBuilder.Configure(app =>
          {
            // 启用 WebSocket 支持
            app.UseWebSockets();
            System.Diagnostics.Debug.WriteLine("[ControlServer] WebSocket middleware enabled");
            
            // WebSocket 音频流端点 (必须在静态文件之前)
            app.Use(async (context, next) =>
            {
              System.Diagnostics.Debug.WriteLine($"[ControlServer] Request: {context.Request.Method} {context.Request.Path}");
              
              if (context.Request.Path.Equals("/audio", StringComparison.OrdinalIgnoreCase))
              {
                if (context.WebSockets.IsWebSocketRequest)
                {
                  System.Diagnostics.Debug.WriteLine("[ControlServer] Accepting WebSocket connection to /audio");
                  var socket = await context.WebSockets.AcceptWebSocketAsync();
                  System.Diagnostics.Debug.WriteLine("[ControlServer] WebSocket connected");
                  if (_webAudioStreamer != null)
                  {
                    await _webAudioStreamer.AddClientAsync(socket);
                  }
                  else
                  {
                    System.Diagnostics.Debug.WriteLine("[ControlServer] WARNING: _webAudioStreamer is null!");
                  }
                  return;
                }
                else
                {
                  System.Diagnostics.Debug.WriteLine("[ControlServer] Non-WebSocket request to /audio - returning 400");
                  context.Response.StatusCode = 400; // Bad Request
                  return;
                }
              }
              await next();
            });
            
            // 静态文件服务
            var webRoot = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
            if (!System.IO.Directory.Exists(webRoot))
            {
              System.IO.Directory.CreateDirectory(webRoot);
            }
            
            app.UseStaticFiles(new StaticFileOptions
            {
              FileProvider = new PhysicalFileProvider(webRoot),
              RequestPath = ""
            });
            
            // 默认路由
            app.Run(async context =>
            {
              if (context.Request.Path == "/" || context.Request.Path == "/index.html")
              {
                context.Response.Redirect("/player.html");
                return;
              }
              
              context.Response.StatusCode = 404;
              await context.Response.WriteAsync("Not Found");
            });
          });
        })
        .Build();
      
      try
      {
        await _webHost.StartAsync();
        System.Diagnostics.Debug.WriteLine($"[ControlServer] Web Audio service started successfully on port {port}");
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[ControlServer] ERROR starting Web Audio service: {ex.Message}");
        System.Diagnostics.Debug.WriteLine($"[ControlServer] Stack trace: {ex.StackTrace}");
        throw;
      }
    }


    /// <summary>
    /// 停止 Web 音频服务
    /// </summary>
    public async Task StopWebAudioAsync()
    {
      if (_webHost != null)
      {
        try { await _webHost.StopAsync(); } catch { }
        _webHost.Dispose();
        _webHost = null;
      }
      _webAudioStreamer = null;
    }

    /// <summary>
    /// 广播音频数据到 Web 客户端
    /// </summary>
    public Task BroadcastWebAudioAsync(ReadOnlyMemory<byte> audioData, CancellationToken ct = default)
    {
      if (_webAudioStreamer != null && _webAudioStreamer.IsStreaming)
      {
        return _webAudioStreamer.BroadcastAudioAsync(audioData, ct);
      }
      return Task.CompletedTask;
    }

    public async void Dispose()
    {
      if (_host != null)
      {
        try { await _host.StopAsync(); } catch { }
        _host.Dispose();
        _host = null;
      }
      
      if (_webHost != null)
      {
        try { await _webHost.StopAsync(); } catch { }
        _webHost.Dispose();
        _webHost = null;
      }
      
      _webAudioStreamer?.Dispose();
    }
  }
}


