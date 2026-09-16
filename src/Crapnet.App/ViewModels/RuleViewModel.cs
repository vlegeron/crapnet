using Crapnet.Domain.Impairments;
using Crapnet.Domain.Rules;

namespace Crapnet.App.ViewModels;

/// <summary>
/// One row of the rule list and everything the editor shows for it.
/// </summary>
/// <remarks>
/// The rule's identity is carried across every rebuild so that reordering, saving and applying all
/// refer to the same rule the user is looking at.
/// </remarks>
public sealed class RuleViewModel : EditableViewModel
{
    private string _name;
    private bool _enabled;
    private string _summary = string.Empty;
    private string _badges = string.Empty;

    public RuleViewModel(Rule rule)
    {
        Id = rule.Id;
        _name = rule.Name;
        _enabled = rule.Enabled;

        Match = new MatchCriteriaViewModel(rule.Match);
        Impairments = ImpairmentCardViewModel.CreateAll(rule.Impairments);

        Track(Match);
        foreach (var card in Impairments) Track(card);

        Refresh();
    }

    public Guid Id { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>Master switch: a disabled rule is skipped during matching but keeps its settings.</summary>
    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public MatchCriteriaViewModel Match { get; }

    public IReadOnlyList<ImpairmentCardViewModel> Impairments { get; }

    /// <summary>The match condition in a handful of glyphs, shown under the name in the list.</summary>
    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    /// <summary>Glyphs of the impairments currently switched on.</summary>
    public string Badges
    {
        get => _badges;
        private set => SetProperty(ref _badges, value);
    }

    public bool IsValid => Match.IsValid;

    public Rule ToRule() => new()
    {
        Id = Id,
        Name = string.IsNullOrWhiteSpace(Name) ? "Rule" : Name,
        Enabled = Enabled,
        Match = Match.ToCriteria(),
        Impairments = ToImpairmentSet(),
    };

    protected override void OnEdited() => Refresh();

    private ImpairmentSet ToImpairmentSet()
    {
        var set = ImpairmentSet.None;
        foreach (var card in Impairments) set = card.ApplyTo(set);
        return set;
    }

    private void Refresh()
    {
        Summary = Match.ToCriteria().Describe();
        Badges = string.Concat(Impairments.Where(card => card.IsEnabled).Select(card => card.Glyph + " ")).TrimEnd();
        OnPropertyChanged(nameof(IsValid));
    }
}
