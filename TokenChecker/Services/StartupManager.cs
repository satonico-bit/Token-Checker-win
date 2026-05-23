using Microsoft.Win32;

namespace TokenChecker.Services;

/// <summary>
/// Windows レジストリの Run キーを使ってログイン時自動起動を管理する。
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "TokenChecker";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(AppName) is not null;
        }
        set
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)!;
            if (value)
            {
                var exe = Environment.ProcessPath;
                if (exe is not null)
                    key.SetValue(AppName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
    }
}
