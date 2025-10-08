using System;
using System.Runtime.InteropServices;

namespace AudioBridge.Windows.Media
{
  public static class MediaController
  {
    private const int KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const int KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
    private const byte VK_MEDIA_PREV_TRACK = 0xB1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);

    public static void PlayPause()
    {
      Tap(VK_MEDIA_PLAY_PAUSE);
    }

    public static void Next()
    {
      Tap(VK_MEDIA_NEXT_TRACK);
    }

    public static void Prev()
    {
      Tap(VK_MEDIA_PREV_TRACK);
    }

    private static void Tap(byte key)
    {
      try
      {
        keybd_event(key, 0, KEYEVENTF_EXTENDEDKEY, 0);
        keybd_event(key, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0);
      }
      catch { }
    }
  }
}
