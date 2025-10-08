using System;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
      if (context.WebSockets.IsWebSocketRequest)
      {
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await EchoLoopAsync(socket);
        return;
      }
      context.Response.StatusCode = 200;
      await context.Response.WriteAsync("AudioBridge ControlServer");
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


