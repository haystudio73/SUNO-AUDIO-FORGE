using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace StemForge;

public class WaveformView : FrameworkElement
{
    double[] values = [];
    double[] processed = [];
    public sealed record Layer(string Name, double[] Samples, string BrushKey);
    public IReadOnlyList<Layer> Layers { get; private set; } = [];
    public void SetLayers(IEnumerable<Layer> layers) { Layers = layers.ToArray(); InvalidateVisual(); }
    public bool HasProcessedSamples => processed.Length > 0;
    // Both layers keep the same scale, including when either layer is hidden.
    public double AmplitudeMaximum => Math.Max(ScaleMinimum, Math.Max(1, Math.Max(values.DefaultIfEmpty().Max(), processed.DefaultIfEmpty().Max())));
    public static readonly DependencyProperty ScaleMinimumProperty = DependencyProperty.Register("ScaleMinimum", typeof(double), typeof(WaveformView), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double ScaleMinimum { get => (double)GetValue(ScaleMinimumProperty); set => SetValue(ScaleMinimumProperty, value); }
    double position;
    public event Action<double>? SeekRequested;
    public double PositionRatio { get => position; set { position = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1); InvalidateVisual(); } }
    public WaveformView()
    {
        Cursor = Cursors.Hand; Focusable = true;
        ToolTip = L.T("Bấm hoặc kéo để tua audio đang nghe. Phím trái/phải tua 1%.");
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "Accent");
        SetResourceReference(ProcessedBrushProperty, "ProcessedWave");
        SetResourceReference(GridBrushProperty, "Line");
        SetResourceReference(PlayheadBrushProperty, "Text");
    }
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register("Background", typeof(Brush), typeof(WaveformView), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register("Foreground", typeof(Brush), typeof(WaveformView), new FrameworkPropertyMetadata(Brushes.Green, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register("GridBrush", typeof(Brush), typeof(WaveformView), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PlayheadBrushProperty = DependencyProperty.Register("PlayheadBrush", typeof(Brush), typeof(WaveformView), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Background { get => (Brush)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public static readonly DependencyProperty ProcessedBrushProperty = DependencyProperty.Register("ProcessedBrush", typeof(Brush), typeof(WaveformView), new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ShowOriginalProperty = DependencyProperty.Register("ShowOriginal", typeof(bool), typeof(WaveformView), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ShowProcessedProperty = DependencyProperty.Register("ShowProcessed", typeof(bool), typeof(WaveformView), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush ProcessedBrush { get => (Brush)GetValue(ProcessedBrushProperty); set => SetValue(ProcessedBrushProperty, value); }
    public bool ShowOriginal { get => (bool)GetValue(ShowOriginalProperty); set => SetValue(ShowOriginalProperty, value); }
    public bool ShowProcessed { get => (bool)GetValue(ShowProcessedProperty); set => SetValue(ShowProcessedProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public Brush PlayheadBrush { get => (Brush)GetValue(PlayheadBrushProperty); set => SetValue(PlayheadBrushProperty, value); }
    void Seek(Point point) { PositionRatio = point.X / Math.Max(1, ActualWidth); SeekRequested?.Invoke(PositionRatio); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { base.OnMouseLeftButtonDown(e); Focus(); CaptureMouse(); Seek(e.GetPosition(this)); e.Handled = true; }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) Seek(e.GetPosition(this)); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { base.OnMouseLeftButtonUp(e); if (IsMouseCaptured) { Seek(e.GetPosition(this)); ReleaseMouseCapture(); } e.Handled = true; }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            PositionRatio = e.Key switch { Key.Home => 0, Key.End => 1, Key.Left => PositionRatio-.01, _ => PositionRatio+.01 };
            SeekRequested?.Invoke(PositionRatio); e.Handled = true;
        }
        base.OnKeyDown(e);
    }
    public void SetSamples(double[] samples) { values = samples; InvalidateVisual(); }
    public void SetProcessedSamples(double[] samples) { processed = samples; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth; var height = ActualHeight;
        dc.DrawRectangle(Background, null, new Rect(0, 0, width, height));
        var grid = new Pen(GridBrush, 1);
        for (int i = 1; i < 12; i++) dc.DrawLine(grid, new(width*i/12, 0), new(width*i/12, height));
        dc.DrawLine(grid, new(0, height/2), new(width, height/2));
        if (width <= 0 || height <= 0) return;
        double maximum = AmplitudeMaximum;
        var pen = new Pen(Foreground, Math.Max(1, width/Math.Max(1, values.Length)*.65));
        for (int i = 0; ShowOriginal && i < values.Length; i++)
        {
            var x = (i+.5)*width/values.Length;
            var amplitude = values[i]/maximum*(height*.42);
            dc.DrawLine(pen, new(x, height/2-amplitude), new(x, height/2+amplitude));
        }
        // An outline remains distinguishable over the original's vertical bars.
        if (ShowProcessed && processed.Length > 0)
        {
            var envelope = new StreamGeometry();
            using (var path = envelope.Open())
            {
                for (int side = -1; side <= 1; side += 2)
                    for (int i = 0; i < processed.Length; i++)
                    {
                        var point = new Point((i+.5)*width/processed.Length, height/2+side*processed[i]/maximum*height*.42);
                        if (i == 0) path.BeginFigure(point, false, false);
                        else path.LineTo(point, true, false);
                    }
            }
            dc.DrawGeometry(null, new Pen(ProcessedBrush, 1.6), envelope);
        }
        double head = Math.Clamp(position*width, 1, Math.Max(1, width-1));
        foreach (var layer in Layers)
        {
            if (layer.Samples.Length == 0) continue;
            var shape = new StreamGeometry();
            using (var path = shape.Open())
                for (int side = -1; side <= 1; side += 2)
                    for (int i = 0; i < layer.Samples.Length; i++)
                    {
                        var point = new Point((i+.5)*width/layer.Samples.Length, height/2+side*layer.Samples[i]/maximum*height*.42);
                        if (i == 0) path.BeginFigure(point, false, false);
                        else path.LineTo(point, true, false);
                    }
            dc.DrawGeometry(null, new Pen((Brush)FindResource(layer.BrushKey), 1.3), shape);
        }
        dc.DrawLine(new Pen(PlayheadBrush, 2), new(head, 0), new(head, height));
        dc.DrawEllipse(PlayheadBrush, null, new(head, 5), 4, 4);
    }
}
