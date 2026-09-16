using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Crapnet.App.ViewModels;

/// <summary>
/// A number the user can either drag on a slider or type into a box, with both views of it kept
/// in step.
/// </summary>
/// <remarks>
/// The text is deliberately not regenerated while the user is typing a value that is already in
/// range, because rewriting it would move the caret mid-keystroke. Out-of-range input is snapped
/// and rewritten, which is the one case where the correction has to be visible.
/// </remarks>
public sealed class NumericField : ObservableObject
{
    private readonly string _format;
    private double _value;
    private string _text;
    private bool _hasError;

    public NumericField(double value, double minimum, double maximum, string format = "0.##")
    {
        Minimum = minimum;
        Maximum = maximum;
        _format = format;
        _value = Math.Clamp(value, minimum, maximum);
        _text = Format(_value);
    }

    public double Minimum { get; }

    public double Maximum { get; }

    /// <summary>Step used by the slider, if the field is shown with one.</summary>
    public double Step { get; init; } = 1d;

    public double Value
    {
        get => _value;
        set => Assign(Math.Clamp(value, Minimum, Maximum), rewriteText: true);
    }

    /// <summary>The value rounded for the settings records, which are all integral.</summary>
    public int IntValue => (int)Math.Round(_value, MidpointRounding.AwayFromZero);

    public string Text
    {
        get => _text;
        set
        {
            if (!SetProperty(ref _text, value ?? string.Empty)) return;

            if (!double.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                HasError = _text.Length > 0;
                return;
            }

            HasError = false;
            var clamped = Math.Clamp(parsed, Minimum, Maximum);
            Assign(clamped, rewriteText: clamped != parsed);
        }
    }

    /// <summary>True when what is in the box is not a number at all.</summary>
    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    private void Assign(double value, bool rewriteText)
    {
        if (!SetProperty(ref _value, value, nameof(Value))) return;

        OnPropertyChanged(nameof(IntValue));
        if (rewriteText) SetProperty(ref _text, Format(value), nameof(Text));
    }

    private string Format(double value) => value.ToString(_format, CultureInfo.InvariantCulture);
}

/// <summary>Signature shared by <c>Ipv4Selector.TryParse</c> and <c>PortSelector.TryParse</c>.</summary>
public delegate bool SelectorParser<T>(string? text, out T value, out string error);

/// <summary>
/// A text box backed by one of the domain selector parsers.
/// </summary>
/// <remarks>
/// The last value that parsed is kept, so a half-typed address never reaches the engine and the
/// running session carries on with the previous match until the field is valid again.
/// </remarks>
public sealed class SelectorField<T> : ObservableObject
    where T : class
{
    private readonly SelectorParser<T> _parse;
    private string _text;
    private T _value;
    private bool _hasError;
    private string _error = string.Empty;

    public SelectorField(string text, T fallback, SelectorParser<T> parse)
    {
        _parse = parse;
        _text = text;
        _value = fallback;

        if (_parse(text, out var parsed, out _)) _value = parsed;
    }

    public string Text
    {
        get => _text;
        set
        {
            if (!SetProperty(ref _text, value ?? string.Empty)) return;

            if (_parse(_text, out var parsed, out var error))
            {
                Value = parsed;
                HasError = false;
                Error = string.Empty;
            }
            else
            {
                HasError = true;
                Error = error;
            }
        }
    }

    /// <summary>The most recent value that parsed cleanly.</summary>
    public T Value
    {
        get => _value;
        private set => SetProperty(ref _value, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    /// <summary>Why the text was rejected, shown as the field's tooltip.</summary>
    public string Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }
}
