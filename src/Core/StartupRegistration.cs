using Microsoft.Win32;

namespace IslasEscritorio.Core;

public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppValueName = "IslasEscritorio";

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null) return;

            if (enabled)
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                    key.SetValue(AppValueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AppValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // No bloquear la app si una política impide escribir en el registro de inicio.
        }
    }
}
