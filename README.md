# AERIS

<p align="center">
  <img src="assets/aeris-brand.jpg" alt="AERIS: organización del escritorio en islas" width="520" />
</p>

<p align="center"><strong>Un escritorio más limpio, con tus archivos a un gesto de distancia.</strong></p>

AERIS es una utilidad ligera para Windows que oculta los iconos nativos del escritorio sin mover ni modificar archivos y los presenta en paneles llamados **islas** cuando acercas el cursor a los bordes de la pantalla.

> Estado: proyecto en desarrollo. La aplicación está pensada para Windows 10/11 y .NET 8. La publicación de versiones instalables todavía no está configurada.

## Funciones

- Oculta y restaura la vista de iconos de Explorer de forma segura.
- Activa islas desde los bordes y esquinas con retardo configurable.
- Presenta archivos, carpetas, accesos directos e iconos del escritorio.
- Permite abrir elementos y acceder al menú contextual de Windows.
- Permite reasignar elementos entre islas desde Ajustes; las asignaciones se guardan.
- Personaliza tema, color de fondo y texto, transparencia, desenfoque, esquinas, sombra y desplazamiento.
- Vigila los cambios del escritorio y actualiza el inventario.
- Incluye bandeja del sistema, atajos, pausa y restauración de emergencia.
- Funciona sin conexiones de red.

## Requisitos

- Windows 10 o Windows 11.
- .NET 8 SDK para compilar desde el código fuente.
- PowerShell para los comandos de desarrollo que aparecen abajo.

## Compilar y ejecutar

Clona el repositorio y abre PowerShell en la carpeta del proyecto:

```powershell
git clone https://github.com/ANGELZzz6/AERIS.git
cd AERIS
dotnet build .\IslasEscritorio.sln -c Release
dotnet run --project .\src\IslasEscritorio\IslasEscritorio.csproj -c Release
```

AERIS se ejecuta en la bandeja del sistema. Haz clic derecho en su icono y elige **Ajustes** para configurar las islas y su contenido.

## Controles y recuperación

Atajos predeterminados:

| Atajo | Acción |
| --- | --- |
| `Ctrl+Alt+Shift+D` | Restaurar los iconos nativos y pausar la detección |
| `Ctrl+Alt+P` | Pausar o reanudar |
| `Ctrl+Alt+I` | Mostrar u ocultar las islas |
| `Ctrl+Alt+A` | Abrir Ajustes |

También puedes elegir **Restaurar escritorio** o **Salir** en el menú de la bandeja. AERIS no cambia nombres, contenido, permisos ni ubicación de tus archivos; oculta únicamente la vista de iconos de Explorer.

## Ajustes de contenido

En **Ajustes → Asignaciones**, elige una isla. La lista refleja inicialmente los elementos que coinciden con sus filtros. Arrastra elementos entre las listas para organizarlos; pulsa **Aplicar** para guardar. Las asignaciones explícitas prevalecen sobre los filtros automáticos de esa isla.

En **Ajustes → Apariencia** puedes modificar el fondo, opacidad, desenfoque, modo vidrio, esquinas, color del texto y barra de desplazamiento.

## Configuración

La configuración del usuario se guarda en:

```text
%APPDATA%\IslasEscritorio\config.json
```

El archivo base del repositorio está en [`config/config.default.json`](config/config.default.json). Si el JSON del usuario no es válido, AERIS conserva la última configuración válida y muestra un aviso.

## Estructura del repositorio

```text
src/Core/       Configuración, validación y filtros
src/Desktop/    Integración con los iconos de Explorer y recuperación
src/Input/      Hook de ratón, zonas y atajos
src/Inventory/  Inventario del escritorio y caché de iconos
src/Islands/    Ventanas, animaciones y contenido de islas
src/Settings/   Interfaz de ajustes
src/Tray/       Icono y menú de la bandeja
config/         Configuración inicial
 tests/         Pruebas del proyecto
 docs/          Arquitectura y rendimiento
 assets/        Marca e icono de AERIS
```

## Desarrollo y comprobaciones

```powershell
dotnet build .\IslasEscritorio.sln -c Release
dotnet run --project .\tests\IslasEscritorio.Tests\IslasEscritorio.Tests.csproj -c Release
```

Lee [`docs/rendimiento-y-arquitectura.md`](docs/rendimiento-y-arquitectura.md) para conocer el diseño de bajo consumo y las recomendaciones de medición. La integración con Explorer, distintos monitores y escalas debe comprobarse en una sesión real de Windows.

## Límites actuales

AERIS continúa en desarrollo. Las islas y ajustes se están validando en Windows; la configuración de arranque, la recuperación y el comportamiento con cambios de pantalla pueden depender de la versión de Explorer. No hay soporte para macOS/Linux, sincronización en la nube ni instalador publicado.

## Contribuciones

Las sugerencias y reportes de errores son bienvenidos mediante GitHub Issues. Incluye versión de Windows, pasos para reproducir y, si aplica, el mensaje de error. No adjuntes archivos privados del escritorio.

## Licencia

Aún no se ha definido una licencia. Hasta que se añada una, el repositorio no concede permisos adicionales de redistribución o modificación.

### Isla de música

Al crear o editar una isla, selecciona **Música** en el campo **Función**. AERIS consulta la sesión multimedia que Windows considera activa y muestra título, artista/aplicación y controles de pista anterior, reproducir/pausar y siguiente. La API del sistema funciona con reproductores que publican una sesión de controles multimedia; la disponibilidad depende de la app y su configuración. La isla actualiza su estado cada dos segundos mientras está activa.
