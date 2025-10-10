# WebSocket 错误 1006 修复

## 问题描述

用户报告 WebSocket 连接后立即关闭，浏览器显示关闭代码 **1006** (Abnormal Closure - 异常关闭)。

## 错误分析

### WebSocket 关闭代码 1006 含义

关闭代码 `1006` 表示连接异常关闭，通常发生在以下情况：
- 连接突然断开（没有正常的关闭握手）
- 服务器在接受连接后立即关闭
- 网络层面的连接中断

### 根本原因

问题出在 `WebAudioStreamer.cs` 的 `AddClientAsync` 方法：

**错误的代码（修复前）**：

```csharp
public async Task AddClientAsync(WebSocket socket)
{
  var client = new WebSocketClient(socket);
  if (_clients.TryAdd(client.Id, client))
  {
    Interlocked.Increment(ref _clientCount);
    System.Diagnostics.Debug.WriteLine($"[WebAudioStreamer] Client {client.Id} connected. Total: {_clientCount}");

    // ❌ 问题：使用 Task.Run 启动后台任务，然后立即返回
    _ = Task.Run(async () => await ClientReceiveLoopAsync(client));
  }
  // ❌ 方法返回，导致 ASP.NET Core 中间件认为请求处理完成，关闭连接
}
```

**执行流程**：

1. WebSocket 请求到达 `/audio` 端点
2. 调用 `AcceptWebSocketAsync()` 升级连接
3. 调用 `AddClientAsync(socket)` 添加客户端
4. `AddClientAsync` 启动后台任务 `Task.Run(...)` 
5. ❌ `AddClientAsync` **立即返回**
6. ❌ 中间件认为请求已处理完成，**关闭 WebSocket 连接**
7. 浏览器收到连接关闭，显示错误码 1006

### 技术细节

在 ASP.NET Core 中，WebSocket 连接的生命周期由中间件方法控制：

```csharp
app.Use(async (context, next) =>
{
  if (context.Request.Path.Equals("/audio", StringComparison.OrdinalIgnoreCase))
  {
    if (context.WebSockets.IsWebSocketRequest)
    {
      var socket = await context.WebSockets.AcceptWebSocketAsync();
      await _webAudioStreamer.AddClientAsync(socket);
      // ⬆️ 当这个 await 返回时，中间件认为请求完成，会清理资源（包括关闭 WebSocket）
      return;
    }
  }
  await next();
});
```

如果 `AddClientAsync` 立即返回（不等待），连接就会立即被关闭。

## 解决方案

### 正确的代码（修复后）

```csharp
public async Task AddClientAsync(WebSocket socket)
{
  var client = new WebSocketClient(socket);
  if (_clients.TryAdd(client.Id, client))
  {
    Interlocked.Increment(ref _clientCount);
    System.Diagnostics.Debug.WriteLine($"[WebAudioStreamer] Client {client.Id} connected. Total: {_clientCount}");

    // ✅ 修复：直接等待接收循环完成，保持连接打开
    await ClientReceiveLoopAsync(client);
  }
}
```

**执行流程（修复后）**：

1. WebSocket 请求到达 `/audio` 端点
2. 调用 `AcceptWebSocketAsync()` 升级连接
3. 调用 `AddClientAsync(socket)` 添加客户端
4. ✅ `AddClientAsync` **等待** `ClientReceiveLoopAsync` 完成
5. ✅ `ClientReceiveLoopAsync` 进入接收循环，持续监听客户端消息
6. ✅ 连接保持打开状态
7. ✅ 只有在客户端主动断开或发生错误时，循环才会退出，连接才会正常关闭

### ClientReceiveLoopAsync 的作用

```csharp
private async Task ClientReceiveLoopAsync(WebSocketClient client)
{
  try
  {
    var buffer = new byte[1024];
    // 循环接收消息，保持连接打开
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
```

此方法会：
1. 持续监听客户端发送的消息（保持连接活跃）
2. 检测客户端主动关闭连接
3. 在连接断开时清理资源

## 副作用修复

修复前，编译器警告：
```
warning CS1998: 此异步方法缺少 "await" 运算符，将以同步方式运行
```

修复后，`AddClientAsync` 正确使用了 `await`，警告消失。

## 测试验证

修复后，应验证：

1. ✅ WebSocket 连接成功建立（状态码 101）
2. ✅ 连接保持打开状态（不再显示 1006 错误）
3. ✅ 开始推流后，网页端能接收音频数据
4. ✅ 关闭网页时，连接正常断开（代码 1000）
5. ✅ 服务器端日志显示客户端连接和断开信息

## 测试步骤

1. 运行 `dist/Windows/AudioBridge.Windows.exe`
2. 勾选 "启用 Web 服务"
3. 点击 "打开网页"
4. 按 F12 打开浏览器开发者工具
5. 点击 "连接" 按钮
6. 确认 Console 显示：
   ```
   [AudioPlayer] Connecting to ws://localhost:29763/audio...
   [AudioPlayer] Connected to server
   ```
7. 确认 Network 标签显示 WebSocket 状态为 "101 Switching Protocols"
8. 点击 "开始推流" 按钮
9. 确认能听到音频播放

## 相关文件

- `windows/App/Net/WebAudioStreamer.cs` - WebSocket 客户端管理
- `windows/App/Net/ControlServer.cs` - Web 服务器配置

## 修复日期

2025-10-09

## 修复者

AI Assistant (Claude)

## 参考资料

- [WebSocket 关闭代码规范](https://www.rfc-editor.org/rfc/rfc6455.html#section-7.4)
- [ASP.NET Core WebSocket 支持](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/websockets)

