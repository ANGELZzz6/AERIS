# Rendimiento y arquitectura de AERIS

## 1. Diagnóstico del problema de lentitud del ratón (Fase 2)

En la versión inicial de Fase 2, el ratón del sistema se volvía extremadamente lento al probar la aplicación por tres motivos combinados:

1. **Hook `WH_MOUSE_LL` sin bucle de mensajes Win32 en su hilo**:
   `SetWindowsHookEx(WH_MOUSE_LL, ...)` entrega los eventos a través de la cola de mensajes del hilo que instaló el hook. En Fase 2, el hilo principal instalaba el hook y luego bloqueaba en `while (!inputTask.IsCompleted) Thread.Sleep(100);` sin despachar mensajes (`GetMessage`/`DispatchMessage`). Como consecuencia, Windows detenía cada evento de ratón hasta agotar `LowLevelHooksTimeout` (300–1000 ms).
2. **Sondeo continuo innecesario**:
   Existían temporizadores de 50 ms para evaluar zonas y un bucle de 250 ms (`Thread.Sleep`) para vigilar Explorer, además de llamadas repetidas a `Screen.AllScreens` en cada movimiento.

### Solución implementada
- `MouseHook` (`src/Input/MouseHook.cs`) crea un hilo dedicado con un bucle Win32 real (`GetMessage` / `TranslateMessage` / `DispatchMessage`) y lee `MSLLHOOKSTRUCT` directamente sin asignaciones en heap.
- El callback ejecuta un filtro rápido sin bloqueos (`ZoneManager.IsPointRelevantFast`): si todas las islas están cerradas y el cursor no toca ninguna zona de activación, retorna inmediatamente con `CallNextHookEx` sin despertar al hilo de interfaz.
- Los retardos de apertura y cierre usan un único `DispatcherTimer` *one-shot* programado al instante exacto del siguiente vencimiento y detenido por completo cuando no hay transiciones activas.
- La detección de reinicio de Explorer y cambios de pantalla es 100 % dirigida por eventos (`TaskbarCreated`, `WM_DISPLAYCHANGE`, `SystemEvents.DisplaySettingsChanged` y `SetWinEventHook` filtrado al PID de Explorer).

## 2. Cómo medir CPU y RAM en reposo y bajo carga

1. Compila y ejecuta en modo Release:
   ```powershell
   dotnet build .\IslasEscritorio.sln -c Release
   .\src\IslasEscritorio\bin\Release\net8.0-windows\AERIS.exe
   ```
2. Abre el **Administrador de tareas** o ejecuta en PowerShell:
   ```powershell
   Get-Process IslasEscritorio | Select-Object ProcessName, CPU, @{N='WorkingSetMB';E={[math]::Round($_.WorkingSet64/1MB,2)}}, @{N='PrivateMB';E={[math]::Round($_.PrivateMemorySize64/1MB,2)}}
   ```
3. Para medir consumo en reposo durante 60 segundos con `typeperf`:
   ```powershell
   typeperf "\Process(IslasEscritorio)\% Processor Time" "\Process(IslasEscritorio)\Working Set - Private" -si 2 -sc 30
   ```
   En reposo (sin abrir ni animar islas), el consumo de CPU debe mantenerse en ~0 % y la memoria privada por debajo de 30 MB en reposo / < 50 MB tras abrir múltiples islas e iconos.
