using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AYVUNoLag;

public partial class OverlayWindow : Window
{
    private static readonly SolidColorBrush BrushAccent  = new(Color.FromRgb(0xFF, 0x44, 0x00));
    private static readonly SolidColorBrush BrushSuccess = new(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly SolidColorBrush BrushWarning = new(Color.FromRgb(0xF5, 0xA6, 0x23));
    private static readonly SolidColorBrush BrushDanger  = new(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly SolidColorBrush BrushMuted   = new(Color.FromRgb(0xA3, 0xA3, 0xA3));

    public OverlayWindow()
    {
        InitializeComponent();

        // Posiciona no canto inferior direito da área de trabalho
        var area = SystemParameters.WorkArea;
        Left = area.Right  - Width  - 20;
        Top  = area.Bottom - Height - 20;
    }

    // ── Atualiza métricas (chamado pelo MainWindow a cada tick) ───────────────
    public void UpdateMetrics(double avgPing, double jitter, double loss, bool optimized)
    {
        Dispatcher.Invoke(() =>
        {
            PingValue.Text   = avgPing >= 0 ? $"{avgPing:0}"  : "--";
            JitterValue.Text = jitter  >= 0 ? $"{jitter:0}"   : "--";
            LossValue.Text   = loss    >= 0 ? $"{loss:0.0}"   : "--";

            // Cor do ping
            PingValue.Foreground = avgPing switch
            {
                <= 0              => BrushMuted,
                < 60              => BrushSuccess,
                < 120             => BrushWarning,
                _                 => BrushDanger,
            };

            // Cor do jitter
            JitterValue.Foreground = jitter switch
            {
                <= 0  => BrushMuted,
                < 15  => BrushSuccess,
                < 30  => BrushWarning,
                _     => BrushDanger,
            };

            // Cor do loss
            LossValue.Foreground = loss switch
            {
                <= 0  => BrushMuted,
                < 5   => BrushSuccess,
                < 20  => BrushWarning,
                _     => BrushDanger,
            };

            // Status pill
            if (optimized)
            {
                StatusText.Text       = "● Otimizado";
                StatusText.Foreground = BrushAccent;
            }
            else
            {
                StatusText.Text       = "● Normal";
                StatusText.Foreground = BrushMuted;
            }
        });
    }

    // ── Drag para mover ───────────────────────────────────────────────────────
    private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    // ── Fechar (esconde, não destrói) ─────────────────────────────────────────
    private void CloseOverlay_Click(object sender, RoutedEventArgs e)
        => Hide();
}
