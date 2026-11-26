using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace IslasEscritorio.Core;

/// <summary>
/// Gestor avanzado del servidor local NihilNox (Python / FastAPI / ngrok), con captura de logs y configuración persistente.
/// </summary>
public static class NihilNoxServerManager
{
    private static Process? _serverProcess;
    private static bool _isRunning;
    private static readonly List<string> _logs = new();

    public static event Action<bool>? StatusChanged;
    public static event Action<string>? LogAdded;

    public static bool IsRunning => _isRunning;
    public static IReadOnlyList<string> Logs => _logs;

    public static string ConfigFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nihilnox_config.json");

    public static Dictionary<string, string> LoadConfig()
    {
        var dict = new Dictionary<string, string>
        {
            ["ngrok_token"] = "",
            ["ngrok_domain"] = "tudominio.ngrok-free.dev",
            ["api_key"] = "MI_KEY_SECRETA",
            ["ffmpeg_path"] = @"C:\Users\elang\.spotdl\ffmpeg.exe"
        };

        try
        {
            if (File.Exists(ConfigFilePath))
            {
                string json = File.ReadAllText(ConfigFilePath);
                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.GetString() ?? "";
                }
            }
        }
        catch { }
        return dict;
    }

    public static void SaveConfig(string token, string domain, string apiKey, string ffmpegPath)
    {
        try
        {
            var dict = new Dictionary<string, string>
            {
                ["ngrok_token"] = token,
                ["ngrok_domain"] = domain,
                ["api_key"] = apiKey,
                ["ffmpeg_path"] = ffmpegPath
            };
            string json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFilePath, json);
            AddLog("💾 Configuración de NihilNox guardada correctamente.");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error guardando config NihilNox: {ex.Message}");
        }
    }

    public static string GetConfiguredDomain()
    {
        var cfg = LoadConfig();
        return cfg.TryGetValue("ngrok_domain", out var d) && !string.IsNullOrWhiteSpace(d) ? d : "tudominio.ngrok-free.dev";
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
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _serverProcess = new Process { StartInfo = psi };
                _serverProcess.OutputDataReceived += (_, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) AddLog(e.Data);
                };
                _serverProcess.ErrorDataReceived += (_, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) AddLog($"[WARN/ERR] {e.Data}");
                };

                _serverProcess.Start();
                _serverProcess.BeginOutputReadLine();
                _serverProcess.BeginErrorReadLine();

                _isRunning = true;
                AddLog("🚀 Servidor NihilNox iniciado desde AERIS.");
                StatusChanged?.Invoke(true);
            }
            else
            {
                AddLog("❌ No se encontró el script NihilNox.py");
            }
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error iniciando NihilNox: {ex.Message}");
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
        AddLog("⏹ Servidor NihilNox detenido.");
        StatusChanged?.Invoke(false);
    }

    public static void ToggleServer()
    {
        if (_isRunning) StopServer();
        else StartServer();
    }

    private static void AddLog(string msg)
    {
        lock (_logs)
        {
            _logs.Add(msg);
            if (_logs.Count > 120) _logs.RemoveAt(0);
        }
        LogAdded?.Invoke(msg);
    }
}
