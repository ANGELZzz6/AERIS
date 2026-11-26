using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using IslasEscritorio.Core;
using Point = System.Windows.Point;
using System.Windows.Forms;

namespace IslasEscritorio.Islands;

public partial class OcrOverlayWindow : Window
{
    private Point _startPoint;
    private bool _isSelecting;
    private readonly Action<string>? _onTextExtracted;

    public OcrOverlayWindow(Action<string>? onTextExtracted = null)
    {
        InitializeComponent();
        _onTextExtracted = onTextExtracted;

        // Cubrir toda la pantalla virtual (soporte multimonitor)
        Left = SystemInformation.VirtualScreen.Left;
        Top = SystemInformation.VirtualScreen.Top;
        Width = SystemInformation.VirtualScreen.Width;
        Height = SystemInformation.VirtualScreen.Height;
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        _isSelecting = true;
        _startPoint = e.GetPosition(SelectionCanvas);

        Canvas.SetLeft(SelectionRectangle, _startPoint.X);
        Canvas.SetTop(SelectionRectangle, _startPoint.Y);
        SelectionRectangle.Width = 0;
        SelectionRectangle.Height = 0;
        SelectionRectangle.Visibility = Visibility.Visible;
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isSelecting) return;

        Point currentPoint = e.GetPosition(SelectionCanvas);

        double x = Math.Min(_startPoint.X, currentPoint.X);
        double y = Math.Min(_startPoint.Y, currentPoint.Y);
        double width = Math.Abs(currentPoint.X - _startPoint.X);
        double height = Math.Abs(currentPoint.Y - _startPoint.Y);

        Canvas.SetLeft(SelectionRectangle, x);
        Canvas.SetTop(SelectionRectangle, y);
        SelectionRectangle.Width = width;
        SelectionRectangle.Height = height;
    }

    private async void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting) return;
        _isSelecting = false;

        Point endPoint = e.GetPosition(SelectionCanvas);

        double dipLeft = Math.Min(_startPoint.X, endPoint.X) + Left;
        double dipTop = Math.Min(_startPoint.Y, endPoint.Y) + Top;
        double dipWidth = Math.Abs(endPoint.X - _startPoint.X);
        double dipHeight = Math.Abs(endPoint.Y - _startPoint.Y);

        // Ocultar ventana antes de hacer la captura para no capturar el overlay
        Visibility = Visibility.Collapsed;

        if (dipWidth > 8 && dipHeight > 8)
        {
            // Convertir coordenadas DIP a píxeles físicos del sistema
            var source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            int pxLeft = (int)Math.Round(dipLeft * dpiX);
            int pxTop = (int)Math.Round(dipTop * dpiY);
            int pxWidth = (int)Math.Round(dipWidth * dpiX);
            int pxHeight = (int)Math.Round(dipHeight * dpiY);

            try
            {
                using var bmp = new Bitmap(pxWidth, pxHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(pxLeft, pxTop, 0, 0, new System.Drawing.Size(pxWidth, pxHeight), CopyPixelOperation.SourceCopy);
                }

                string extractedText = await OcrService.ExtractTextFromBitmapAsync(bmp);

                if (!string.IsNullOrWhiteSpace(extractedText))
                {
                    System.Windows.Clipboard.SetText(extractedText);
                    ShowFeedbackToast(extractedText);
                    _onTextExtracted?.Invoke(extractedText);
                }
                else
                {
                    ShowFeedbackToast("No se detectó texto reconocible en la región seleccionada.", isError: true);
                }
            }
            catch (Exception ex)
            {
                ShowFeedbackToast($"Error al procesar la imagen: {ex.Message}", isError: true);
            }
        }

        Close();
    }

    private static void ShowFeedbackToast(string text, bool isError = false)
    {
        try
        {
            string summary = text.Length > 80 ? text[..80] + "..." : text;
            string title = isError ? "AERIS - OCR Error" : "AERIS - Texto Copiado al Portapapeles 📋";

            // Mostrar ventana flotante de confirmación rápida
            var toast = new Window
            {
                Width = 360,
                Height = 85,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Topmost = true,
                ShowInTaskbar = false,
                Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isError ? "#311B1B" : "#1E1E2E")),
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            var border = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isError ? "#F38BA8" : "#89B4FA")),
                Padding = new Thickness(14, 10, 14, 10)
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isError ? "#F38BA8" : "#89B4FA")),
                Margin = new Thickness(0, 0, 0, 4)
            });
            stack.Children.Add(new TextBlock
            {
                Text = summary,
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Opacity = 0.9
            });

            border.Child = stack;
            toast.Content = border;
            toast.Show();

            Task.Delay(3000).ContinueWith(_ => toast.Dispatcher.Invoke(toast.Close));
        }
        catch
        {
            // Ignorar errores visuales del toast si la aplicación se está cerrando
        }
    }
}
