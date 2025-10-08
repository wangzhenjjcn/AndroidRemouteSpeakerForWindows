using System;
using System.IO;
using System.Text.Json;

namespace AudioBridge.Windows.Config
{
  public sealed class Settings
  {
    public string TargetIp { get; set; } = "127.0.0.1";
    public int TargetPort { get; set; } = 5004;
    public int BitrateKbps { get; set; } = 96;
    public bool UseOpus { get; set; } = false;
    public string? DeviceId { get; set; }
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
          if (loaded != null) return loaded;
        }
      }
      catch { }
      return new Settings();
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
  }
}


