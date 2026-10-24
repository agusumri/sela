using System.Runtime.InteropServices;

namespace Sela.Services;

/// <summary>Detects a full-screen app / presentation / busy state via SHQueryUserNotificationState.</summary>
public static class FullscreenDetector
{
    private const int QunsBusy = 2;
    private const int QunsRunningD3DFullScreen = 3;
    private const int QunsPresentationMode = 4;

    public static bool IsFullscreenActive()
    {
        try
        {
            return SHQueryUserNotificationState(out int state) == 0
                   && state is QunsBusy or QunsRunningD3DFullScreen or QunsPresentationMode;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
}
