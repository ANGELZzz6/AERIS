using System.IO;
using System.Text;
using System.Text.Json;

namespace IslasEscritorio.Core;

public sealed class ConfigService : IDisposable
{
    public static readonly string DefaultConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "IslasEscritorio");

    public static readonly string DefaultConfigPath = Path.Combine(DefaultConfigDirectory, "config.json");

    private readonly string _configPath;
    private readonly FileSystemWatcher _watcher;
    private readonly object _sync = new();
    private CancellationTokenSource? _debounceCts;
    private DateTime _ignoreEventsUntilUtc = DateTime.MinValue;

    public AppConfig Current { get; private set; }
    public string ConfigPath => _configPath;
    public string? LastError { get; private set; }

    public event Action<AppConfig>? ConfigChanged;
    public event Action<string>? ConfigError;

    public ConfigService(string? configPath = null)
    {
        _configPath = configPath ?? DefaultConfigPath;
        Current = LoadOrCreateInitial();

        string dir = Path.GetDirectoryName(_configPath)!;
        string file = Path.GetFileName(_configPath);
        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Renamed += OnFileEvent;
    }

    private AppConfig LoadOrCreateInitial()
    {
        string dir = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(dir);

        if (!File.Exists(_configPath))
        {
            AppConfig initial = LoadTemplateOrFallback();
            WriteConfigFile(initial);
            return initial;
        }

        try
        {
            string json = File.ReadAllText(_configPath, Encoding.UTF8);
            ConfigValidationResult result = ConfigValidator.ParseAndValidate(json);
            if (result.IsValid && result.Config is not null)
            {
                LastError = null;
                return result.Config;
            }

            LastError = string.Join(Environment.NewLine, result.Errors);
        }
        catch (Exception ex)
        {
            LastError = $"No se pudo leer config.json: {ex.Message}";
        }

        return LoadTemplateOrFallback();
    }

    public static AppConfig LoadTemplateOrFallback()
    {
        string bundled = Path.Combine(AppContext.BaseDirectory, "config", "config.default.json");
        if (File.Exists(bundled))
        {
            try
            {
                string json = File.ReadAllText(bundled, Encoding.UTF8);
                ConfigValidationResult result = ConfigValidator.ParseAndValidate(json);
                if (result.IsValid && result.Config is not null)
                    return result.Config;
            }
            catch { }
        }
        return AppConfig.CreateDefault();
    }

    public bool TrySave(AppConfig newConfig, out IReadOnlyList<string> errors)
    {
        ConfigValidationResult result = ConfigValidator.Validate(newConfig);
        if (!result.IsValid || result.Config is null)
        {
            errors = result.Errors;
            LastError = string.Join(Environment.NewLine, errors);
            ConfigError?.Invoke(LastError);
            return false;
        }

        AppConfig cloned = result.Config.Clone();
        WriteConfigFile(cloned);
        Current = cloned;
        LastError = null;
        errors = [];
        StartupRegistration.SetEnabled(cloned.Global.IniciarConWindows);
        ConfigChanged?.Invoke(cloned);
        return true;
    }

    public void ResetToDefaults()
    {
        AppConfig defaults = LoadTemplateOrFallback();
        TrySave(defaults, out _);
    }

    public bool TryImportFromFile(string sourcePath, out string errorMessage)
    {
        try
        {
            string json = File.ReadAllText(sourcePath, Encoding.UTF8);
            ConfigValidationResult result = ConfigValidator.ParseAndValidate(json);
            if (!result.IsValid || result.Config is null)
            {
                errorMessage = string.Join(Environment.NewLine, result.Errors);
                return false;
            }

            TrySave(result.Config, out _);
            errorMessage = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public void ExportToFile(string destinationPath)
    {
        string json = JsonSerializer.Serialize(Current, ConfigSerializerOptions.Default);
        File.WriteAllText(destinationPath, json, Encoding.UTF8);
    }

    private void WriteConfigFile(AppConfig config)
    {
        string json = JsonSerializer.Serialize(config, ConfigSerializerOptions.Default);
        string dir = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(dir);
        string temp = Path.Combine(dir, $"{Path.GetFileName(_configPath)}.tmp");

        lock (_sync)
        {
            _ignoreEventsUntilUtc = DateTime.UtcNow.AddMilliseconds(400);
            File.WriteAllText(temp, json, Encoding.UTF8);
            File.Move(temp, _configPath, overwrite: true);
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (DateTime.UtcNow < _ignoreEventsUntilUtc)
            return;

        lock (_sync)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            CancellationToken token = _debounceCts.Token;
            _ = Task.Delay(180, token).ContinueWith(t =>
            {
                if (!t.IsCanceled) ReloadFromDisk();
            }, TaskScheduler.Default);
        }
    }

    public void ReloadFromDisk()
    {
        try
        {
            if (!File.Exists(_configPath))
                return;

            string json;
            using (var stream = new FileStream(_configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                json = reader.ReadToEnd();
            }

            ConfigValidationResult result = ConfigValidator.ParseAndValidate(json);
            if (!result.IsValid || result.Config is null)
            {
                string msg = "Configuración JSON inválida; se mantiene la configuración válida anterior:\n" +
                             string.Join(Environment.NewLine, result.Errors);
                LastError = msg;
                ConfigError?.Invoke(msg);
                return;
            }

            Current = result.Config;
            LastError = null;
            StartupRegistration.SetEnabled(Current.Global.IniciarConWindows);
            ConfigChanged?.Invoke(Current);
        }
        catch (Exception ex)
        {
            LastError = $"Error al recargar config.json: {ex.Message}";
            ConfigError?.Invoke(LastError);
        }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        lock (_sync)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
        }
    }
}
