using System;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;

namespace AudioBridge.Windows.Config
{
  public sealed class Settings
  {
    public string TargetIp { get; set; } = "127.0.0.1";
    public int TargetPort { get; set; } = 5004;
    public int BitrateKbps { get; set; } = 96;
    public bool UseOpus { get; set; } = false;
    public string? DeviceId { get; set; }
    public string? PskBase64Url { get; set; }
    private static string GetFolder()
    {
      var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
      var dir = Path.Combine(appData, "AudioBridge");
      Directory.CreateDirectory(dir);
      return dir;
    }

    private static string GetFilePath() => Path.Combine(GetFolder(), "settings.json");

    public static Settings Load()
    {
      try
      {
        var path = GetFilePath();
        if (File.Exists(path))
        {
          var json = File.ReadAllText(path);
          var loaded = JsonSerializer.Deserialize<Settings>(json);
          if (loaded != null)
          {
            // ensure PSK exists
            if (string.IsNullOrWhiteSpace(loaded.PskBase64Url))
            {
              loaded.PskBase64Url = GenerateNewPskBase64Url();
              try { File.WriteAllText(path, JsonSerializer.Serialize(loaded, new JsonSerializerOptions { WriteIndented = true })); } catch { }
            }
            return loaded;
          }
        }
      }
      catch { }
      var fresh = new Settings();
      fresh.PskBase64Url = GenerateNewPskBase64Url();
      try { File.WriteAllText(GetFilePath(), JsonSerializer.Serialize(fresh, new JsonSerializerOptions { WriteIndented = true })); } catch { }
      return fresh;
    }

    public void Save()
    {
      try
      {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(GetFilePath(), json);
      }
      catch { }
    }
    public byte[]? GetPskBytes()
    {
      if (string.IsNullOrWhiteSpace(PskBase64Url)) return null;
      try
      {
        string s = PskBase64Url!;
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
          case 2: s += "=="; break;
          case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
      }
      catch { return null; }
    }

    private static string GenerateNewPskBase64Url()
    {
      var key = new byte[32];
      RandomNumberGenerator.Fill(key);
      string s = Convert.ToBase64String(key)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
      return s;
    }
  }
}


