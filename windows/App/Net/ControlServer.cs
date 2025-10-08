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

namespace AudioBridge.Windows.Net
{
  public sealed class ControlServer : IDisposable
  {
    private IHost? _host;
    private Func<string, int, Task>? _onCommand; // action,value
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new ConcurrentDictionary<Guid, WebSocket>();

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

    public async void Dispose()
    {
      if (_host != null)
      {
        try { await _host.StopAsync(); } catch { }
        _host.Dispose();
        _host = null;
      }
    }
  }
}


