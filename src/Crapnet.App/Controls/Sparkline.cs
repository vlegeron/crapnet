using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Crapnet.App.Controls;

/// <summary>
/// A throughput trace: one polyline across a rolling window of samples, optionally filled.
/// </summary>
/// <remarks>
/// Drawn by hand rather than with a charting control because it redraws four times a second for
/// the life of the session. A geometry built per frame from a plain array costs nothing, whereas
/// a control that materialises a visual per point would not survive that update rate.
/// </remarks>
public sealed class Sparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> SamplesProperty =
        AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Samples));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(StrokeThickness), 1.25d);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(Maximum));

    static Sparkline()
    {
        AffectsRender<Sparkline>(SamplesProperty, StrokeProperty, FillProperty, StrokeThicknessProperty, MaximumProperty);
    }

    /// <summary>Oldest sample first. The list is replaced wholesale on each update, never mutated.</summary>
    public IReadOnlyList<double>? Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>Optional wash under the trace. Leave unset for a bare line.</summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>
    /// Value pinned to the top of the plot. Two sparklines sharing a scale must share this, which
    /// is why it is not derived from the samples the control happens to hold.
    /// </summary>
    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var samples = Samples;
        if (samples is null || samples.Count < 2) return;

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0d || height <= 0d) return;

        var scale = Maximum;
        if (scale <= 0d)
        {
            for (var i = 0; i < samples.Count; i++) scale = Math.Max(scale, samples[i]);
        }

        // A flat-zero window would otherwise divide by zero and spike the trace to the ceiling.
        if (scale <= 0d) scale = 1d;

        var step = width / (samples.Count - 1);

        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(PointAt(0, samples[0], step, height, scale), isFilled: false);
            for (var i = 1; i < samples.Count; i++)
            {
                sink.LineTo(PointAt(i, samples[i], step, height, scale));
            }

            sink.EndFigure(false);
        }

        if (Fill is { } fill)
        {
            var area = new StreamGeometry();
            using (var sink = area.Open())
            {
                sink.BeginFigure(new Point(0, height), isFilled: true);
                for (var i = 0; i < samples.Count; i++)
                {
                    sink.LineTo(PointAt(i, samples[i], step, height, scale));
                }

                sink.LineTo(new Point(width, height));
                sink.EndFigure(true);
            }

            context.DrawGeometry(fill, null, area);
        }

        if (Stroke is { } stroke)
        {
            context.DrawGeometry(null, new Pen(stroke, StrokeThickness, lineJoin: PenLineJoin.Round), geometry);
        }
    }

    private static Point PointAt(int index, double value, double step, double height, double scale)
    {
        var normalised = Math.Clamp(value / scale, 0d, 1d);

        // Half a pixel of headroom keeps a full-scale sample from being clipped by the stroke.
        return new Point(index * step, height - (normalised * (height - 1d)) - 0.5d);
    }
}
