using IslasEscritorio.Desktop;

namespace IslasEscritorio;

internal static class Program
{
    private const string MutexName = "Local\\IslasEscritorioApp_V1";

    [STAThread]
    private static int Main(string[] args)
    {
        // Modo vigilante o restauración inmediata sin inicializar WPF.
        int specialExit = DesktopIconController.RunWatcherIfRequested(args);
        if (specialExit >= 0)
            return specialExit;

        using var instanceMutex = new Mutex(true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            System.Windows.MessageBox.Show(
                "AERIS ya se está ejecutando en la bandeja del sistema.",
                "AERIS",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return 0;
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
