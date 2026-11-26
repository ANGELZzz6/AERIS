using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace IslasEscritorio.Core;

/// <summary>
/// Gestor del servidor local NihilNox (Python / FastAPI / ngrok).
/// </summary>
public static class NihilNoxServerManager
{
    private static Process? _serverProcess;
    private static bool _isRunning;
    public static event Action<bool>? StatusChanged;

    public static bool IsRunning => _isRunning;

    public static string GetConfiguredDomain()
    {
        try
        {
            string configFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nihilnox_config.json");
            if (File.Exists(configFile))
            {
                string json = File.ReadAllText(configFile);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("ngrok_domain", out var el) && el.GetString() is { } domain && !string.IsNullOrWhiteSpace(domain))
                {
                    return domain;
                }
            }
        }
        catch { }
        return "tudominio.ngrok-free.dev";
    }

    public static void StartServer()
    {
        if (_isRunning) return;
        try
        {
            string pyScript = @"C:\Users\elang\Desktop\app_diesño_escritorio\NihilNox.py";
            if (!File.Exists(pyScript))
            {
                pyScript = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NihilNox.py");
            }

            if (File.Exists(pyScript))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"\"{pyScript}\"",
                    UseShellExecute = true,
                    CreateNoWindow = false
                };
                _serverProcess = Process.Start(psi);
                _isRunning = true;
                StatusChanged?.Invoke(true);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error iniciando NihilNox: {ex.Message}");
        }
    }

    public static void StopServer()
    {
        try
        {
            if (_serverProcess is { HasExited: false })
            {
                _serverProcess.Kill();
                _serverProcess.Dispose();
            }
        }
        catch { }
        _serverProcess = null;
        _isRunning = false;
        StatusChanged?.Invoke(false);
    }

    public static void ToggleServer()
    {
        if (_isRunning) StopServer();
        else StartServer();
    }
}
