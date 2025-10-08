using System;
using System.Drawing;
using System.Drawing.Imaging;
using QRCoder;

namespace AudioBridge.Windows.Net
{
  public static class QrHelper
  {
    public static string GeneratePairQrPng(string host, int ctrlPort, int audioPort, string? pskBase64Url)
    {
      string device = Environment.MachineName;
      string key = pskBase64Url ?? "";
      string content = $"abridge://pair?host={host}&ctrl={ctrlPort}&audio={audioPort}&key={key}&device={device}";
      using var qrGen = new QRCodeGenerator();
      using var data = qrGen.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
      using var qr = new QRCode(data);
      using Bitmap bmp = qr.GetGraphic(20);
      var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AudioBridge_Pair_QR.png");
      bmp.Save(path, ImageFormat.Png);
      return path;
    }
  }
}


