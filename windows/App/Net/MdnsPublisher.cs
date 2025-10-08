using System;

namespace AudioBridge.Windows.Net
{
  public sealed class MdnsPublisher : IDisposable
  {
    private Mono.Zeroconf.RegisterService? _service;

    public void Start(string instanceName, int controlPort, int audioPort, string? pskBase64Url)
    {
      Stop();
      var svc = new Mono.Zeroconf.RegisterService();
      svc.Name = instanceName;
      svc.RegType = "_audiobridge._tcp";
      svc.ReplyDomain = "local.";
      svc.Port = unchecked((short)controlPort);
      var txt = new Mono.Zeroconf.TxtRecord();
      txt.Add("audio", audioPort.ToString());
      if (!string.IsNullOrWhiteSpace(pskBase64Url)) txt.Add("key", pskBase64Url);
      svc.TxtRecord = txt;
      svc.Register();
      _service = svc;
    }

    public void Stop()
    {
      try { _service?.Dispose(); } catch { }
      _service = null;
    }

    public void Dispose() { Stop(); }
  }
}

// placeholder removed


