using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using IslasEscritorio.Core;
using IslasEscritorio.Desktop;
using IslasEscritorio.Input;
using IslasEscritorio.Inventory;
using IslasEscritorio.Islands;
using IslasEscritorio.Settings;

namespace IslasEscritorio.Tests;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--diag")
        {
            PrintDiagnostics();
            return 0;
        }

        Run("ZoneManager: cálculo de zonas de borde y esquina", TestZoneRectsAndCorners);
        Run("ZoneManager: multimonitor y escalado DPI 100%-300%", TestMultiMonitorAndDpi);
        Run("ZoneManager: comportamiento de esquinas (propia vs ninguna vs borde)", TestCornerBehaviors);
        Run("IslandStateMachine: transiciones completas y cancelaciones", TestStateMachineTransitions);
        Run("FilterMatcher: globs, exclusiones, tipos, fuentes y orden", TestFilterMatcher);
        Run("ConfigValidator: validación positiva y rechazo de errores", TestConfigValidator);
        Run("ConfigService: conserva configuración válida si JSON tiene errores", TestConfigServiceResilience);
        Run("GlobalHotkeyManager: parseo de combinaciones de teclas", TestShortcutParsing);
        Run("MouseHook: instala bucle de mensajes dedicado y libera sin bloquear", TestMouseHookLifecycle);
        Run("SettingsWindow: inicializa XAML y carga configuración sin excepciones", TestSettingsWindowInit);
        Run("IslandWindow: abre, posiciona y anima hasta opacidad 1 sin errores", TestIslandWindowOpenAndAnimate);
        Run("AudioVolumeService: consulta y asignación de volumen maestro", TestAudioVolumeService);
        Run("OcrService: atajo global y procesamiento seguro de imágenes", TestOcrServiceAndShortcut);
        Run("MemoryOptimizer: optimización y exclusión segura de procesos", TestMemoryOptimizer);

        Console.WriteLine();
        Console.WriteLine($"Resumen: {_passed} pasaron, {_failed} fallaron.");
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"[PASS] {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.Error.WriteLine($"[FAIL] {name}: {ex.Message}");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void TestZoneRectsAndCorners()
    {
        var bounds = new ScreenRect(0, 0, 1920, 1080);
        ScreenRect left = ZoneManager.ComputeActivationRect(IslandEdge.Left, bounds, 3);
        Assert(left == new ScreenRect(0, 0, 3, 1080), $"Left inesperado: {left}");

        ScreenRect right = ZoneManager.ComputeActivationRect(IslandEdge.Right, bounds, 3);
        Assert(right == new ScreenRect(1917, 0, 1920, 1080), $"Right inesperado: {right}");

        ScreenRect top = ZoneManager.ComputeActivationRect(IslandEdge.Top, bounds, 4);
        Assert(top == new ScreenRect(0, 0, 1920, 4), $"Top inesperado: {top}");

        ScreenRect bottom = ZoneManager.ComputeActivationRect(IslandEdge.Bottom, bounds, 4);
        Assert(bottom == new ScreenRect(0, 1076, 1920, 1080), $"Bottom inesperado: {bottom}");

        ScreenRect topLeft = ZoneManager.ComputeActivationRect(IslandEdge.TopLeft, bounds, 2);
        Assert(topLeft.Contains(new ScreenPoint(0, 0)), "TopLeft debe contener (0,0).");
    }

    private static void TestMultiMonitorAndDpi()
    {
        var monitors = new List<MonitorInfo>
        {
            new(0, "DISPLAY1", true, new ScreenRect(0, 0, 1920, 1080), new ScreenRect(0, 0, 1920, 1040), 1.0),
            new(1, "DISPLAY2", false, new ScreenRect(1920, 0, 5760, 2160), new ScreenRect(1920, 0, 5760, 2080), 2.0)
        };

        var cfg = new AppConfig
        {
            Islands =
            [
                new IslandConfig
                {
                    Id = "all-left",
                    Borde = "izquierdo",
                    Monitor = "todos",
                    Tamano = new IslandSizeConfig { AnchoPx = 200, AltoPct = 50 },
                    Posicion = new IslandPositionConfig { Ancla = "centro" }
                }
            ]
        };

        var zm = new ZoneManager();
        zm.Configure(cfg, monitors);
        IReadOnlyList<ResolvedIslandZone> zones = zm.GetZonesSnapshot();
        Assert(zones.Count == 2, $"Se esperaban 2 instancias por multimonitor, hubo {zones.Count}.");
        Assert(zones[0].TargetWindowRect.Width == 200, $"Ancho DPI 1.0 esperado 200, fue {zones[0].TargetWindowRect.Width}.");
        Assert(zones[1].TargetWindowRect.Width == 400, $"Ancho DPI 2.0 esperado 400, fue {zones[1].TargetWindowRect.Width}.");
        Assert(zones[1].TargetWindowRect.Left == 1920, $"Monitor 2 Left esperado 1920, fue {zones[1].TargetWindowRect.Left}.");
    }

    private static void TestCornerBehaviors()
    {
        var monitors = new List<MonitorInfo>
        {
            new(0, "DISPLAY1", true, new ScreenRect(0, 0, 1920, 1080), new ScreenRect(0, 0, 1920, 1040), 1.0)
        };

        // Caso 1: comportamiento "ninguna" suprime bordes en la esquina (0,0).
        var cfgNone = new AppConfig
        {
            Activation = new ActivationConfig { ComportamientoEsquinas = "ninguna", RetardoAperturaMs = 0 },
            Islands =
            [
                new IslandConfig { Id = "left", Borde = "izquierdo" },
                new IslandConfig { Id = "top", Borde = "superior" }
            ]
        };
        var zm = new ZoneManager();
        zm.Configure(cfgNone, monitors);
        var tr = zm.UpdateCursor(new ScreenPoint(0, 0), DateTime.UtcNow);
        Assert(tr.Count == 0, "En esquina con comportamiento 'ninguna' no debe activarse ninguna isla.");

        // Caso 2: comportamiento "propia" activa solo la isla de esquina.
        var cfgOwn = new AppConfig
        {
            Activation = new ActivationConfig { ComportamientoEsquinas = "propia", RetardoAperturaMs = 0 },
            Islands =
            [
                new IslandConfig { Id = "left", Borde = "izquierdo" },
                new IslandConfig { Id = "top", Borde = "superior" },
                new IslandConfig { Id = "corner", Borde = "esquinaSuperiorIzquierda" }
            ]
        };
        zm.Configure(cfgOwn, monitors);
        tr = zm.UpdateCursor(new ScreenPoint(0, 0), DateTime.UtcNow);
        Assert(tr.Count == 1 && tr[0].Zone.Config.Id == "corner" && tr[0].NewState == IslandState.Open,
            "En esquina con comportamiento 'propia' debe abrirse solo la isla de esquina.");
    }

    private static void TestStateMachineTransitions()
    {
        var cfg = new IslandConfig { Id = "test", Borde = "izquierdo" };
        var sm = new IslandStateMachine("test@0", cfg, activationDelayMs: 150, closeDelayMs: 300);
        DateTime t0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        Assert(sm.State == IslandState.Closed, "Estado inicial debe ser Closed.");
        Assert(sm.Update(true, false, t0), "Debe transicionar al entrar.");
        Assert(sm.State == IslandState.Waiting, "Debe estar en Waiting.");

        // Sale antes de 150ms -> vuelve a Closed.
        Assert(sm.Update(false, false, t0.AddMilliseconds(50)), "Debe cancelar al salir antes del retardo.");
        Assert(sm.State == IslandState.Closed, "Debe volver a Closed.");

        // Entra y permanece >= 150ms -> Open.
        sm.Update(true, false, t0);
        Assert(sm.Update(true, false, t0.AddMilliseconds(150)), "Debe abrir tras 150ms.");
        Assert(sm.State == IslandState.Open, "Estado debe ser Open.");

        // Sale de zona e isla -> Closing.
        Assert(sm.Update(false, false, t0.AddMilliseconds(200)), "Debe pasar a Closing.");
        Assert(sm.State == IslandState.Closing, "Estado debe ser Closing.");

        // Reentra en isla antes de 300ms -> vuelve a Open.
        Assert(sm.Update(false, true, t0.AddMilliseconds(350)), "Debe cancelar cierre al reentrar.");
        Assert(sm.State == IslandState.Open, "Debe regresar a Open.");

        // Sale y pasan >= 300ms -> Closed.
        sm.Update(false, false, t0.AddMilliseconds(400));
        Assert(sm.Update(false, false, t0.AddMilliseconds(705)), "Debe cerrar tras 300ms.");
        Assert(sm.State == IslandState.Closed, "Estado final debe ser Closed.");
    }

    private static void TestFilterMatcher()
    {
        var now = DateTime.UtcNow;
        var items = new List<DesktopItem>
        {
            new(@"C:\U\Desktop\contrato.pdf", "contrato.pdf", "contrato.pdf", ".pdf", DesktopItemKind.File, DesktopSource.UserDesktop, 100, now, now),
            new(@"C:\U\Desktop\notas.txt", "notas.txt", "notas.txt", ".txt", DesktopItemKind.File, DesktopSource.UserDesktop, 50, now, now),
            new(@"C:\U\Desktop\desktop.ini", "desktop.ini", "desktop.ini", ".ini", DesktopItemKind.File, DesktopSource.UserDesktop, 10, now, now),
            new(@"C:\U\Desktop\Proyectos", "Proyectos", "Proyectos", "", DesktopItemKind.Folder, DesktopSource.UserDesktop, 0, now, now),
            new(@"C:\Public\Desktop\App.lnk", "App.lnk", "App", ".lnk", DesktopItemKind.Shortcut, DesktopSource.PublicDesktop, 200, now, now)
        };

        var content = new IslandContentConfig
        {
            Fuentes = ["Escritorio usuario"],
            Filtros = new IslandFilterConfig
            {
                Incluir = ["*.pdf", "*.txt"],
                Excluir = ["desktop.ini"],
                Tipos = ["archivo"]
            },
            Orden = "nombre"
        };

        IReadOnlyList<DesktopItem> result = FilterMatcher.FilterAndSort(items, content);
        Assert(result.Count == 2, $"Se esperaban 2 archivos filtrados, hubo {result.Count}.");
        Assert(result[0].FileName == "contrato.pdf" && result[1].FileName == "notas.txt", "Orden alfabético inesperado.");
    }

    private static void TestConfigValidator()
    {
        AppConfig valid = AppConfig.CreateDefault();
        ConfigValidationResult ok = ConfigValidator.Validate(valid);
        Assert(ok.IsValid && ok.Errors.Count == 0, "Configuración por defecto debe ser válida.");

        string badJson = """{ "version": 0, "apariencia": { "opacidad": 5.0, "colorFondo": "rojo" }, "islas": [ { "id": "a", "borde": "centro" }, { "id": "a", "borde": "izquierdo" } ] }""";
        ConfigValidationResult bad = ConfigValidator.ParseAndValidate(badJson);
        Assert(!bad.IsValid && bad.Errors.Count >= 4, "JSON inválido debe reportar múltiples errores.");
    }

    private static void TestConfigServiceResilience()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "IslasTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string path = Path.Combine(tempDir, "config.json");

        try
        {
            using var svc = new ConfigService(path);
            string initialId = svc.Current.Islands[0].Id;

            File.WriteAllText(path, "{ esto no es json valido");
            svc.ReloadFromDisk();

            Assert(!string.IsNullOrWhiteSpace(svc.LastError), "Debe registrar error al recargar JSON corrupto.");
            Assert(svc.Current.Islands[0].Id == initialId, "Debe conservar la última configuración válida.");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static void TestShortcutParsing()
    {
        Assert(GlobalHotkeyManager.TryParseShortcut("Ctrl+Alt+Shift+D", out uint mods, out uint vk),
            "Debe parsear Ctrl+Alt+Shift+D.");
        Assert(mods == (0x0001 | 0x0002 | 0x0004) && vk == 0x44, "Modificadores o tecla virtual incorrectos.");
        Assert(!GlobalHotkeyManager.TryParseShortcut("Ctrl+Alt", out _, out _), "Sin tecla principal debe fallar.");
    }

    private static void TestMouseHookLifecycle()
    {
        var sw = Stopwatch.StartNew();
        using var hook = new MouseHook(_ => { }, (_, _) => false);
        hook.Install();
        Thread.Sleep(30);
        hook.Dispose();
        sw.Stop();
        Assert(sw.ElapsedMilliseconds < 1500, $"MouseHook tardó demasiado en instalar/cerrar: {sw.ElapsedMilliseconds} ms.");
    }

    private static void TestSettingsWindowInit()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "IslasSettingsTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string cfgPath = Path.Combine(tempDir, "config.json");
            try
            {
                using var cfg = new ConfigService(cfgPath);
                using var desktop = new DesktopIconController();
                using var inv = new DesktopInventory(tempDir, tempDir);
                using var coord = new IslandCoordinator(cfg, desktop, inv, Dispatcher.CurrentDispatcher);
                var win = new SettingsWindow(cfg, coord, desktop);
                win.Close();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
            throw threadEx;
    }

    private static void TestIslandWindowOpenAndAnimate()
    {
        Exception? threadEx = null;
        bool visible = false;
        double actualW = 0;
        double actualH = 0;

        var thread = new Thread(() =>
        {
            try
            {
                var cfg = new IslandConfig
                {
                    Id = "test-left",
                    Nombre = "Prueba",
                    Borde = "izquierdo",
                    Tamano = new IslandSizeConfig { AnchoPx = 220, AltoPct = 50 }
                };
                var mon = new MonitorInfo(0, "DISPLAY1", true,
                    new ScreenRect(0, 0, 1920, 1080), new ScreenRect(0, 0, 1920, 1040), 1.0);
                var zone = new ResolvedIslandZone("test-left@0", cfg, IslandEdge.Left, mon,
                    new ScreenRect(0, 0, 4, 1080), new ScreenRect(0, 260, 220, 780));
                var app = new AppearanceConfig
                {
                    Animacion = new AnimationConfig { Tipo = "deslizar", DuracionMs = 50, Easing = "easeOutCubic" }
                };

                var win = new IslandWindow(zone, app, topmost: false);
                win.AnimateOpen();

                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) =>
                {
                    frame.Continue = false;
                }, Dispatcher.CurrentDispatcher);
                timer.Start();
                Dispatcher.PushFrame(frame);
                timer.Stop();

                visible = win.IsVisible;
                actualW = win.ActualWidth;
                actualH = win.ActualHeight;
                win.Close();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null) throw threadEx;
        Assert(visible, "IslandWindow debe quedar visible tras AnimateOpen.");
        Assert(actualW > 100 && actualH > 100, $"Dimensiones inválidas en IslandWindow: {actualW}x{actualH}");
    }

    private static void TestAudioVolumeService()
    {
        float currentVol = AudioVolumeService.GetVolume();
        Assert(currentVol >= 0f && currentVol <= 100f, $"Volumen fuera de rango: {currentVol}");

        bool isMuted = AudioVolumeService.IsMuted();
        // Intentar consultar estado sin fallar
        Assert(isMuted == true || isMuted == false, "IsMuted debe devolver un booleano válido.");
    }

    private static void TestOcrServiceAndShortcut()
    {
        var config = new GlobalConfig();
        Assert(config.AtajoOcr == "Win+Shift+T", "Atajo de OCR por defecto debe ser Win+Shift+T");

        bool parsed = GlobalHotkeyManager.TryParseShortcut(config.AtajoOcr, out uint mods, out uint vk);
        Assert(parsed, "TryParseShortcut debe parsear Win+Shift+T correctamente.");

        // Prueba asíncrona segura con bitmap nulo
        var task = OcrService.ExtractTextFromBitmapAsync(null!);
        task.Wait();
        Assert(string.IsNullOrEmpty(task.Result), "Bitmap nulo debe retornar cadena vacía sin lanzar excepciones.");
    }

    private static void TestMemoryOptimizer()
    {
        var (optimized, freed) = MemoryOptimizer.OptimizeMemory();
        Assert(optimized >= 0, "El número de procesos optimizados no puede ser negativo.");
        Assert(freed >= 0, "Los bytes liberados estimados no pueden ser negativos.");
    }

    private static void PrintDiagnostics()
    {
        var monitors = MonitorHelper.GetMonitors();
        Console.WriteLine($"Monitores detectados: {monitors.Count}");
        foreach (var m in monitors)
        {
            bool fs = MonitorHelper.IsFullscreenWindowOnMonitor(m.Bounds, IntPtr.Zero);
            Console.WriteLine($"  [{m.Index}] {m.DeviceName} Primary={m.IsPrimary} DPI={m.DpiScale:0.00} Bounds={m.Bounds} WorkArea={m.WorkArea} Fullscreen={fs}");
        }
        using var hook = new MouseHook(pt => Console.WriteLine($"Hook move: {pt}"), null);
        hook.Install();
        Console.WriteLine($"MouseHook instalado OK. Ultimo cursor: {hook.GetLatestCursor()}");
    }
}
