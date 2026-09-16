using Crapnet.Domain.Impairments;

namespace Crapnet.App.ViewModels;

/// <summary>
/// One impairment as the editor sees it: a switch, a name and whatever parameters it carries.
/// </summary>
/// <remarks>
/// Every impairment toggles on its own, so each card owns its enabled flag rather than sharing a
/// single "mode" with the others. <see cref="ApplyTo"/> is the only way a card writes back, which
/// keeps the projection into the immutable <see cref="ImpairmentSet"/> in one obvious place.
/// </remarks>
public abstract class ImpairmentCardViewModel : EditableViewModel
{
    private bool _isEnabled;

    protected ImpairmentCardViewModel(IImpairmentSetting setting, string title, string glyph)
    {
        Kind = setting.Kind;
        Title = title;
        Glyph = glyph;
        _isEnabled = setting.Enabled;
    }

    public ImpairmentKind Kind { get; }

    /// <summary>One word wherever the language allows it.</summary>
    public string Title { get; }

    public string Glyph { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    /// <summary>The domain's own one-line description of the current parameters.</summary>
    public string Summary { get; private set; } = string.Empty;

    /// <summary>Writes this card's parameters into a copy of <paramref name="set"/>.</summary>
    public abstract ImpairmentSet ApplyTo(ImpairmentSet set);

    /// <summary>Builds the full row of cards for a rule, in pipeline order.</summary>
    public static IReadOnlyList<ImpairmentCardViewModel> CreateAll(ImpairmentSet set) =>
    [
        new BlockCardViewModel(set.Block),
        new DropCardViewModel(set.Drop),
        new ResetCardViewModel(set.Reset),
        new TamperCardViewModel(set.Tamper),
        new DuplicateCardViewModel(set.Duplicate),
        new BandwidthCardViewModel(set.Bandwidth),
        new ThrottleCardViewModel(set.Throttle),
        new ReorderCardViewModel(set.Reorder),
        new LagCardViewModel(set.Lag),
    ];

    protected override void OnEdited()
    {
        // Reuse the domain's formatting rather than duplicating it: project into an empty set and
        // ask the freshly built setting to describe itself.
        var described = ApplyTo(ImpairmentSet.None).Get(Kind).Describe();
        if (described == Summary) return;

        Summary = described;
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>Called by each card once its own fields exist, to build the first summary.</summary>
    protected void Initialise(params NumericField[] fields)
    {
        foreach (var field in fields) Track(field);
        OnEdited();
    }
}
