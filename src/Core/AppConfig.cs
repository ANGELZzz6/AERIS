using System.Text.Json;
using System.Text.Json.Serialization;

namespace IslasEscritorio.Core;

public sealed class AppConfig
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("global")]
    public GlobalConfig Global { get; set; } = new();

    [JsonPropertyName("activacion")]
    public ActivationConfig Activation { get; set; } = new();

    [JsonPropertyName("apariencia")]
    public AppearanceConfig Appearance { get; set; } = new();

    [JsonPropertyName("islas")]
    public List<IslandConfig> Islands { get; set; } = [];

    public static AppConfig CreateDefault() => new()
    {
        Version = 1,
        Global = new GlobalConfig(),
        Activation = new ActivationConfig(),
        Appearance = new AppearanceConfig(),
        Islands =
        [
            new IslandConfig
            {
                Id = "izquierda",
                Nombre = "Trabajo",
                Habilitada = true,
                Borde = "izquierdo",
                Posicion = new IslandPositionConfig { Ancla = "centro", DesplazamientoX = 0, DesplazamientoY = 0 },
                Tamano = new IslandSizeConfig { AnchoPx = 240, AltoPct = 60, MinAnchoPx = 160, MinAltoPx = 180 },
                Monitor = "todos",
                Contenido = new IslandContentConfig
                {
                    Fuentes = ["Escritorio usuario", "Escritorio público"],
                    Filtros = new IslandFilterConfig
                    {
                        Incluir = ["*.docx", "*.xlsx", "*.pdf", "*.pptx", "*.txt", "*.lnk"],
                        Excluir = ["desktop.ini"],
                        Tipos = ["archivo", "carpeta", "acceso directo"]
                    },
                    Orden = "nombre",
                    Agrupacion = "ninguna",
                    Disposicion = "lista"
                },
                Estilo = new IslandStyleConfig { ColorAcento = "#89B4FA" }
            },
            new IslandConfig
            {
                Id = "derecha",
                Nombre = "Accesos y Juegos",
                Habilitada = true,
                Borde = "derecho",
                Posicion = new IslandPositionConfig { Ancla = "centro" },
                Tamano = new IslandSizeConfig { AnchoPx = 240, AltoPct = 45, MinAnchoPx = 160, MinAltoPx = 160 },
                Monitor = "todos",
                Contenido = new IslandContentConfig
                {
                    Fuentes = ["Escritorio usuario", "Escritorio público"],
                    Filtros = new IslandFilterConfig
                    {
                        Incluir = ["*.exe", "*.lnk", "*.url"],
                        Excluir = ["desktop.ini"],
                        Tipos = ["archivo", "acceso directo"]
                    },
                    Orden = "ultimoUso",
                    Agrupacion = "ninguna",
                    Disposicion = "rejilla"
                },
                Estilo = new IslandStyleConfig { ColorAcento = "#A6E3A1" }
            },
            new IslandConfig
            {
                Id = "superior",
                Nombre = "Carpetas",
                Habilitada = true,
                Borde = "superior",
                Posicion = new IslandPositionConfig { Ancla = "centro" },
                Tamano = new IslandSizeConfig { AltoPx = 140, AnchoPct = 50, MinAnchoPx = 240, MinAltoPx = 100 },
                Monitor = "todos",
                Contenido = new IslandContentConfig
                {
                    Fuentes = ["Escritorio usuario", "Escritorio público"],
                    Filtros = new IslandFilterConfig
                    {
                        Incluir = ["*"],
                        Excluir = ["desktop.ini"],
                        Tipos = ["carpeta"]
                    },
                    Orden = "nombre",
                    Agrupacion = "ninguna",
                    Disposicion = "rejilla"
                },
                Estilo = new IslandStyleConfig { ColorAcento = "#F9E2AF" }
            },
            CreateDefaultMusicIsland()
        ]
    };

    public static IslandConfig CreateDefaultMusicIsland() => new()
    {
        Id = "musica",
        Tipo = "musica",
        Nombre = "Música",
        Habilitada = true,
        Borde = "inferior",
        Monitor = "principal",
        Tamano = new IslandSizeConfig { AnchoPx = 360, AltoPx = 180, MinAnchoPx = 320, MinAltoPx = 150 },
        Presentacion = new SpecialIslandPresentation { Modo = "borde", X = 300, Y = 300 },
        Contenido = new IslandContentConfig
        {
            Fuentes = [],
            Filtros = new IslandFilterConfig { Incluir = [], Excluir = [], Tipos = [] },
            Disposicion = "lista"
        },
        Estilo = new IslandStyleConfig { ColorAcento = "#89DCEB" }
    };

    public AppConfig Clone()
    {
        string json = JsonSerializer.Serialize(this, ConfigSerializerOptions.Default);
        return JsonSerializer.Deserialize<AppConfig>(json, ConfigSerializerOptions.Default)!;
    }
}

public sealed class GlobalConfig
{
    [JsonPropertyName("ocultarIconosEscritorio")]
    public bool OcultarIconosEscritorio { get; set; } = true;

    [JsonPropertyName("idioma")]
    public string Idioma { get; set; } = "es";

    [JsonPropertyName("iniciarConWindows")]
    public bool IniciarConWindows { get; set; } = false;

    [JsonPropertyName("atajoRestaurar")]
    public string AtajoRestaurar { get; set; } = "Ctrl+Alt+Shift+D";

    [JsonPropertyName("atajoPausar")]
    public string AtajoPausar { get; set; } = "Ctrl+Alt+P";

    [JsonPropertyName("atajoAlternar")]
    public string AtajoAlternar { get; set; } = "Ctrl+Alt+I";

    [JsonPropertyName("atajoAjustes")]
    public string AtajoAjustes { get; set; } = "Ctrl+Alt+A";
}

public sealed class ActivationConfig
{
    [JsonPropertyName("retardoAperturaMs")]
    public int RetardoAperturaMs { get; set; } = 150;

    [JsonPropertyName("retardoCierreMs")]
    public int RetardoCierreMs { get; set; } = 300;

    [JsonPropertyName("grosorZonaPx")]
    public int GrosorZonaPx { get; set; } = 8;

    [JsonPropertyName("monitores")]
    public string Monitores { get; set; } = "todos";

    [JsonPropertyName("comportamientoEsquinas")]
    public string ComportamientoEsquinas { get; set; } = "propia";

    [JsonPropertyName("desactivarEnPantallaCompleta")]
    public bool DesactivarEnPantallaCompleta { get; set; } = true;

    [JsonPropertyName("desactivarMientrasSeArrastra")]
    public bool DesactivarMientrasSeArrastra { get; set; } = true;

    [JsonPropertyName("siempreEncima")]
    public bool SiempreEncima { get; set; } = true;
}

public sealed class AppearanceConfig
{
    [JsonPropertyName("tema")]
    public string Tema { get; set; } = "oscuro";

    [JsonPropertyName("colorFondo")]
    public string ColorFondo { get; set; } = "#1E1E2E";

    [JsonPropertyName("opacidad")]
    public double Opacidad { get; set; } = 0.88;

    [JsonPropertyName("desenfoque")]
    public bool Desenfoque { get; set; } = true;

    [JsonPropertyName("estiloVidrio")]
    public bool EstiloVidrio { get; set; } = false;

    [JsonPropertyName("colorTexto")]
    public string ColorTexto { get; set; } = "#ECEFF8";

    [JsonPropertyName("barraDesplazamiento")]
    public string BarraDesplazamiento { get; set; } = "linea";

    [JsonPropertyName("radioEsquinasPx")]
    public int RadioEsquinasPx { get; set; } = 12;

    [JsonPropertyName("sombra")]
    public bool Sombra { get; set; } = true;

    [JsonPropertyName("fuente")]
    public string Fuente { get; set; } = "Segoe UI";

    [JsonPropertyName("tamanoIconoPx")]
    public int TamanoIconoPx { get; set; } = 48;

    [JsonPropertyName("animacion")]
    public AnimationConfig Animacion { get; set; } = new();
}

public sealed class AnimationConfig
{
    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = "deslizar";

    [JsonPropertyName("duracionMs")]
    public int DuracionMs { get; set; } = 200;

    [JsonPropertyName("easing")]
    public string Easing { get; set; } = "easeOutCubic";
}

public sealed class IslandConfig
{
    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = "archivos";

    [JsonPropertyName("presentacion")]
    public SpecialIslandPresentation Presentacion { get; set; } = new();
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = "Nueva isla";

    [JsonPropertyName("habilitada")]
    public bool Habilitada { get; set; } = true;

    [JsonPropertyName("borde")]
    public string Borde { get; set; } = "izquierdo";

    [JsonPropertyName("posicion")]
    public IslandPositionConfig Posicion { get; set; } = new();

    [JsonPropertyName("tamano")]
    public IslandSizeConfig Tamano { get; set; } = new();

    [JsonPropertyName("monitor")]
    public string Monitor { get; set; } = "principal";

    [JsonPropertyName("contenido")]
    public IslandContentConfig Contenido { get; set; } = new();

    [JsonPropertyName("estilo")]
    public IslandStyleConfig Estilo { get; set; } = new();
}

public sealed class SpecialIslandPresentation
{
    [JsonPropertyName("modo")]
    public string Modo { get; set; } = "borde";
    [JsonPropertyName("x")]
    public int X { get; set; } = 300;
    [JsonPropertyName("y")]
    public int Y { get; set; } = 300;
    [JsonPropertyName("colorFondo")]
    public string? ColorFondo { get; set; }
    [JsonPropertyName("opacidad")]
    public double? Opacidad { get; set; }
}

public sealed class IslandPositionConfig
{
    [JsonPropertyName("ancla")]
    public string Ancla { get; set; } = "centro";

    [JsonPropertyName("desplazamientoX")]
    public int DesplazamientoX { get; set; }

    [JsonPropertyName("desplazamientoY")]
    public int DesplazamientoY { get; set; }
}

public sealed class IslandSizeConfig
{
    [JsonPropertyName("anchoPx")]
    public int? AnchoPx { get; set; }

    [JsonPropertyName("altoPx")]
    public int? AltoPx { get; set; }

    [JsonPropertyName("anchoPct")]
    public double? AnchoPct { get; set; }

    [JsonPropertyName("altoPct")]
    public double? AltoPct { get; set; }

    [JsonPropertyName("minAnchoPx")]
    public int? MinAnchoPx { get; set; }

    [JsonPropertyName("minAltoPx")]
    public int? MinAltoPx { get; set; }

    [JsonPropertyName("maxAnchoPx")]
    public int? MaxAnchoPx { get; set; }

    [JsonPropertyName("maxAltoPx")]
    public int? MaxAltoPx { get; set; }
}

public sealed class IslandContentConfig
{
    [JsonPropertyName("elementosAsignados")]
    public List<string> ElementosAsignados { get; set; } = [];
    [JsonPropertyName("fuentes")]
    public List<string> Fuentes { get; set; } = ["Escritorio usuario", "Escritorio público"];

    [JsonPropertyName("filtros")]
    public IslandFilterConfig Filtros { get; set; } = new();

    [JsonPropertyName("orden")]
    public string Orden { get; set; } = "nombre";

    [JsonPropertyName("agrupacion")]
    public string Agrupacion { get; set; } = "ninguna";

    [JsonPropertyName("disposicion")]
    public string Disposicion { get; set; } = "lista";
}

public sealed class IslandFilterConfig
{
    [JsonPropertyName("incluir")]
    public List<string> Incluir { get; set; } = ["*"];

    [JsonPropertyName("excluir")]
    public List<string> Excluir { get; set; } = ["desktop.ini"];

    [JsonPropertyName("tipos")]
    public List<string> Tipos { get; set; } = ["archivo", "carpeta", "acceso directo"];
}

public sealed class IslandStyleConfig
{
    [JsonPropertyName("colorAcento")]
    public string ColorAcento { get; set; } = "#89B4FA";

    [JsonPropertyName("colorFondo")]
    public string? ColorFondo { get; set; }

    [JsonPropertyName("opacidad")]
    public double? Opacidad { get; set; }
}

public static class ConfigSerializerOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
