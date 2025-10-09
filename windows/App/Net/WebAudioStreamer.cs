using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace AudioBridge.Windows.Net
{
  /// <summary>
  /// 管理 Web 客户端的音频流传输
  /// 通过 WebSocket 将音频数据推送到浏览器端
  /// </summary>
  public sealed class WebAudioStreamer : IDisposable
  {
    private readonly ConcurrentDictionary<Guid, WebSocketClient> _clients = new();
    private bool _isStreaming;
    private int _totalBytesSent;
    private int _clientCount;

    private sealed class WebSocketClient
    {
      public WebSocket Socket { get; init; }
      public Guid Id { get; init; }
      public DateTime ConnectedAt { get; init; }
      public CancellationTokenSource Cts { get; init; }

      public WebSocketClient(WebSocket socket)
      {
        Socket = socket;
        Id = Guid.NewGuid();
        ConnectedAt = DateTime.UtcNow;
        Cts = new CancellationTokenSource();
      }
    }

    /// <summary>
    /// 添加新的 Web 客户端连接
    /// </summary>
    public async Task AddClientAsync(WebSocket socket)
    {
      var client = new WebSocketClient(socket);
      if (_clients.TryAdd(client.Id, client))
      {
        Interlocked.Increment(ref _clientCount);
        System.Diagnostics.Debug.WriteLine($"[WebAudioStreamer] Client {client.Id} connected. Total: {_clientCount}");

        // 启动接收循环（处理客户端断开）
        _ = Task.Run(async () => await ClientReceiveLoopAsync(client));
      }
    }

    /// <summary>
    /// 客户端接收循环，用于检测断开连接
    /// </summary>
    private async Task ClientReceiveLoopAsync(WebSocketClient client)
    {
      try
      {
        var buffer = new byte[1024];
        while (client.Socket.State == WebSocketState.Open && !client.Cts.Token.IsCancellationRequested)
        {
          var result = await client.Socket.ReceiveAsync(
            new ArraySegment<byte>(buffer), 
            client.Cts.Token
          );

          if (result.MessageType == WebSocketMessageType.Close)
          {
            await client.Socket.CloseAsync(
              WebSocketCloseStatus.NormalClosure, 
              "Client closed", 
              CancellationToken.None
            );
            break;
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[WebAudioStreamer] Client {client.Id} receive error: {ex.Message}");
      }
      finally
      {
        RemoveClient(client.Id);
      }
    }

    /// <summary>
    /// 移除客户端连接
    /// </summary>
    private void RemoveClient(Guid id)
    {
      if (_clients.TryRemove(id, out var client))
      {
        Interlocked.Decrement(ref _clientCount);
        client.Cts.Cancel();
        try { client.Socket.Dispose(); } catch { }
        System.Diagnostics.Debug.WriteLine($"[WebAudioStreamer] Client {id} disconnected. Total: {_clientCount}");
      }
    }

    /// <summary>
    /// 开始推流
    /// </summary>
    public void StartStreaming()
    {
      _isStreaming = true;
      _totalBytesSent = 0;
      System.Diagnostics.Debug.WriteLine("[WebAudioStreamer] Streaming started");
    }

    /// <summary>
    /// 停止推流
    /// </summary>
    public void StopStreaming()
    {
      _isStreaming = false;
      System.Diagnostics.Debug.WriteLine("[WebAudioStreamer] Streaming stopped");
    }

    /// <summary>
    /// 广播音频数据到所有连接的 Web 客户端
    /// 数据格式：[header(8B: sampleRate(4) + channels(2) + samplesPerCh(2))] + [PCM16 data]
    /// </summary>
    public async Task BroadcastAudioAsync(ReadOnlyMemory<byte> audioData, CancellationToken ct = default)
    {
      if (!_isStreaming || _clientCount == 0) return;

      var deadClients = new System.Collections.Generic.List<Guid>();
      
      foreach (var kv in _clients)
      {
        var client = kv.Value;
        if (client.Socket.State != WebSocketState.Open)
        {
          deadClients.Add(client.Id);
          continue;
        }

        try
        {
          await client.Socket.SendAsync(
            audioData,
            WebSocketMessageType.Binary,
            endOfMessage: true,
            ct
          );
          
          Interlocked.Add(ref _totalBytesSent, audioData.Length);
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"[WebAudioStreamer] Send error to {client.Id}: {ex.Message}");
          deadClients.Add(client.Id);
        }
      }

      // 清理断开的客户端
      foreach (var id in deadClients)
      {
        RemoveClient(id);
      }
    }

    /// <summary>
    /// 获取当前连接的客户端数量
    /// </summary>
    public int ClientCount => _clientCount;

    /// <summary>
    /// 获取总发送字节数
    /// </summary>
    public int TotalBytesSent => _totalBytesSent;

    /// <summary>
    /// 是否正在推流
    /// </summary>
    public bool IsStreaming => _isStreaming;

    /// <summary>
    /// 释放所有资源
    /// </summary>
    public void Dispose()
    {
      _isStreaming = false;
      
      foreach (var kv in _clients)
      {
        try
        {
          kv.Value.Cts.Cancel();
          if (kv.Value.Socket.State == WebSocketState.Open)
          {
            kv.Value.Socket.CloseAsync(
              WebSocketCloseStatus.NormalClosure,
              "Server shutting down",
              CancellationToken.None
            ).Wait(1000);
          }
          kv.Value.Socket.Dispose();
        }
        catch { }
      }
      
      _clients.Clear();
    }
  }
}

