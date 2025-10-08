using System;
using System.Net;
using System.Net.Sockets;

namespace AudioBridge.Windows.Net
{
  public sealed class UdpAudioSender : IDisposable
  {
    private readonly UdpClient _udp = new UdpClient();
    private IPEndPoint? _remote;
    public void Bind(int localPort = 0)
    {
      if (localPort != 0)
      {
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, localPort));
      }
    }

    public void Configure(IPEndPoint remote)
    {
      _remote = remote;
    }

    public void Send(ReadOnlySpan<byte> payload)
    {
      if (_remote == null) throw new InvalidOperationException("Remote not configured");
      byte[] buf = payload.ToArray();
      _udp.Send(buf, buf.Length, _remote);
    }

    public void Dispose()
    {
      _udp.Dispose();
    }
  }
}


