using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IslasEscritorio.Core;

/// <summary>
/// Gestor avanzado del servidor local NihilNox (FastAPI), con soporte para servidor local IP, túnel ngrok y túnel Cloudflare (cloudflared).
/// </summary>
public static class NihilNoxServerManager
{
    private static Process? _serverProcess;
    private static Process? _cloudflareProcess;
    private static bool _isRunning;
    private static string _cloudflareUrl = "";
    private static readonly List<string> _logs = new();

    public static event Action<bool>? StatusChanged;
    public static event Action<string>? LogAdded;

    public static bool IsRunning => _isRunning;
    public static IReadOnlyList<string> Logs => _logs;
    public static string CloudflareUrl => _cloudflareUrl;

    public static string ConfigFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nihilnox_config.json");

    public static Dictionary<string, string> LoadConfig()
    {
        var dict = new Dictionary<string, string>
        {
            ["ngrok_token"] = "",
            ["ngrok_domain"] = "tudominio.ngrok-free.dev",
            ["api_key"] = "MI_KEY_SECRETA",
            ["ffmpeg_path"] = @"C:\Users\elang\.spotdl\ffmpeg.exe",
            ["enable_local"] = "true",
            ["enable_ngrok"] = "true",
            ["enable_cloudflare"] = "false"
        };

        try
        {
            if (File.Exists(ConfigFilePath))
            {
                string json = File.ReadAllText(ConfigFilePath);
                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.True || prop.Value.ValueKind == JsonValueKind.False)
                        dict[prop.Name] = prop.Value.GetBoolean().ToString().ToLowerInvariant();
                    else
                        dict[prop.Name] = prop.Value.GetString() ?? "";
                }
            }
        }
        catch { }
        return dict;
    }

    public static void SaveConfig(string token, string domain, string apiKey, string ffmpegPath, bool enableLocal, bool enableNgrok, bool enableCloudflare)
    {
        try
        {
            var dict = new Dictionary<string, object>
            {
                ["ngrok_token"] = token,
                ["ngrok_domain"] = domain,
                ["api_key"] = apiKey,
                ["ffmpeg_path"] = ffmpegPath,
                ["enable_local"] = enableLocal,
                ["enable_ngrok"] = enableNgrok,
                ["enable_cloudflare"] = enableCloudflare
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

    public static string GetLocalIpAddress()
    {
        try
        {
            string fallback = "127.0.0.1";
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    !ni.Name.Contains("VPN", StringComparison.OrdinalIgnoreCase) &&
                    !ni.Name.Contains("Radmin", StringComparison.OrdinalIgnoreCase) &&
                    !ni.Name.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) &&
                    !ni.Name.Contains("ZeroTier", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string ip = addr.Address.ToString();
                            if (ip.StartsWith("192.168.") || ip.StartsWith("10.") || ip.StartsWith("172."))
                            {
                                return ip;
                            }
                            if (!ip.StartsWith("169.254") && fallback == "127.0.0.1")
                            {
                                fallback = ip;
                            }
                        }
                    }
                }
            }
            return fallback;
        }
        catch { }
        return "127.0.0.1";
    }

    public static void StartServer()
    {
        StopServer();

        var cfg = LoadConfig();
        bool enableLocal = cfg.TryGetValue("enable_local", out var el) && bool.TryParse(el, out bool bl) && bl;
        bool enableCloudflare = cfg.TryGetValue("enable_cloudflare", out var ec) && bool.TryParse(ec, out bool bc) && bc;

        try
        {
            if (enableLocal)
            {
                string pyScript = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NihilNox_Cloud.py");
                if (!File.Exists(pyScript)) pyScript = @"C:\Users\elang\Desktop\app_diesño_escritorio\NihilNox_Cloud.py";
                if (!File.Exists(pyScript)) pyScript = Path.Combine(Directory.GetCurrentDirectory(), "NihilNox_Cloud.py");

                if (File.Exists(pyScript))
                {
                    var (fileName, arguments) = GetPythonCommand(pyScript);
                    var psi = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    psi.Environment["PYTHONUNBUFFERED"] = "1";

                    _serverProcess = new Process { StartInfo = psi };
                    _serverProcess.OutputDataReceived += (_, e) => {
                        if (!string.IsNullOrEmpty(e.Data)) AddLog(e.Data);
                    };
                    _serverProcess.ErrorDataReceived += (_, e) => {
                        if (!string.IsNullOrEmpty(e.Data)) AddLog($"[ERROR] {e.Data}");
                    };

                    _serverProcess.Start();
                    _serverProcess.BeginOutputReadLine();
                    _serverProcess.BeginErrorReadLine();
                    AddLog($"🚀 Servidor local activo en http://{GetLocalIpAddress()}:8000 (usando {fileName})");
                }
                else
                {
                    AddLog("❌ No se encontró NihilNox_Cloud.py en la raíz del proyecto");
                }
            }

            if (enableCloudflare)
            {
                StartCloudflareTunnel();
            }

            _isRunning = true;
            StatusChanged?.Invoke(true);
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error iniciando NihilNox: {ex.Message}");
        }
    }

    private static (string fileName, string arguments) GetPythonCommand(string pyScript)
    {
        try
        {
            var testPsi = new ProcessStartInfo
            {
                FileName = "py",
                Arguments = "-3.12 --version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var p = Process.Start(testPsi);
            p?.WaitForExit(1000);
            if (p?.ExitCode == 0)
            {
                return ("py", $"-3.12 \"{pyScript}\"");
            }
        }
        catch { }

        try
        {
            var testPsi = new ProcessStartInfo
            {
                FileName = "py",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var p = Process.Start(testPsi);
            p?.WaitForExit(1000);
            if (p?.ExitCode == 0)
            {
                return ("py", $"\"{pyScript}\"");
            }
        }
        catch { }

        return ("python", $"\"{pyScript}\"");
    }

    private static void StartCloudflareTunnel()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cloudflared",
                Arguments = "tunnel --url http://localhost:8000",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            _cloudflareProcess = new Process { StartInfo = psi };
            _cloudflareProcess.OutputDataReceived += (_, e) => {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    AddLog(e.Data);
                    Match match = Regex.Match(e.Data, @"https://[a-zA-Z0-9\-]+\.trycloudflare\.com");
                    if (match.Success)
                    {
                        _cloudflareUrl = match.Value;
                        AddLog($"☁️ Cloudflare URL: {_cloudflareUrl}");
                    }
                }
            };
            _cloudflareProcess.ErrorDataReceived += (_, e) => {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    AddLog($"[CF-ERR] {e.Data}");
                    Match match = Regex.Match(e.Data, @"https://[a-zA-Z0-9\-]+\.trycloudflare\.com");
                    if (match.Success)
                    {
                        _cloudflareUrl = match.Value;
                        AddLog($"☁️ Cloudflare URL: {_cloudflareUrl}");
                    }
                }
            };
            _cloudflareProcess.Start();
            _cloudflareProcess.BeginOutputReadLine();
            _cloudflareProcess.BeginErrorReadLine();
            AddLog("☁️ Iniciando túnel Cloudflare...");
        }
        catch (Exception ex)
        {
            AddLog($"⚠️ No se pudo iniciar cloudflared (¿Está instalado?): {ex.Message}");
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

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c for /f \"tokens=5\" %a in ('netstat -a -n -o ^| findstr :8000') do taskkill /f /pid %a",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi)?.WaitForExit(1500);
        }
        catch { }

        try
        {
            if (_cloudflareProcess is { HasExited: false })
            {
                _cloudflareProcess.Kill();
                _cloudflareProcess.Dispose();
            }
        }
        catch { }

        _serverProcess = null;
        _cloudflareProcess = null;
        _cloudflareUrl = "";
        _isRunning = false;
        AddLog("⏹ Servidores detenidos.");
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
            if (_logs.Count > 150) _logs.RemoveAt(0);
        }
        LogAdded?.Invoke(msg);
    }
}
