using Avalonia;
using Avalonia.Controls;

namespace Crapnet.App.Controls;

/// <summary>
/// The chrome around one impairment: a glyph, a name, its own switch, and a slot for whatever
/// parameters that impairment happens to need.
/// </summary>
/// <remarks>
/// The switch lives in the card rather than in each parameter template so that every impairment
/// looks and toggles identically no matter how many knobs it carries. A card that is switched off
/// keeps its switch at full strength and fades only the body, so the row of cards still reads as
/// a set of switches at a glance.
/// </remarks>
public class ImpairmentCard : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ImpairmentCard, string?>(nameof(Title));

    public static readonly StyledProperty<string?> GlyphProperty =
        AvaloniaProperty.Register<ImpairmentCard, string?>(nameof(Glyph));

    public static readonly StyledProperty<string?> SummaryProperty =
        AvaloniaProperty.Register<ImpairmentCard, string?>(nameof(Summary));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<ImpairmentCard, bool>(
            nameof(IsActive),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public ImpairmentCard() => UpdatePseudoClasses(IsActive);

    protected override Type StyleKeyOverride => typeof(ImpairmentCard);

    /// <summary>Short name, one word wherever possible.</summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Current settings in a few characters, shown next to the title.</summary>
    public string? Summary
    {
        get => GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsActiveProperty)
        {
            UpdatePseudoClasses(change.GetNewValue<bool>());
        }
    }

    private void UpdatePseudoClasses(bool isActive) => PseudoClasses.Set(":active", isActive);
}
