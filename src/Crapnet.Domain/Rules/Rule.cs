using Crapnet.Domain.Impairments;
using Crapnet.Domain.Networking;

namespace Crapnet.Domain.Rules;

/// <summary>
/// A match condition paired with the impairments to apply to whatever it catches.
/// </summary>
public sealed record Rule
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Master switch for the rule. A disabled rule is skipped entirely during matching.</summary>
    public bool Enabled { get; init; } = true;

    public MatchCriteria Match { get; init; } = MatchCriteria.Any;
    public ImpairmentSet Impairments { get; init; } = ImpairmentSet.None;

    public static Rule Create(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
    };

    /// <summary>True when the rule is on but would not actually change any traffic.</summary>
    public bool IsInert => !Enabled || !Impairments.AnyEnabled;

    public bool Claims(in PacketDescriptor packet) => Enabled && Match.Matches(packet);
}

/// <summary>
/// An ordered set of rules plus the hotspot settings they were authored against.
/// </summary>
/// <remarks>
/// Rules are evaluated top to bottom and the first one that matches wins, exactly as a firewall
/// list behaves. That keeps behaviour predictable: a narrow rule placed above a broad one carves
/// out an exception, and nothing is applied twice to the same packet.
/// </remarks>
public sealed record Profile
{
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<Rule> Rules { get; init; } = Array.Empty<Rule>();

    public static Profile Empty(string name = "Untitled") => new() { Name = name };

    /// <summary>The first enabled rule that claims the packet, or null when nothing matches.</summary>
    public Rule? Resolve(in PacketDescriptor packet)
    {
        // Indexed loop: this runs per packet, and foreach over IReadOnlyList allocates an enumerator.
        for (var i = 0; i < Rules.Count; i++)
        {
            var rule = Rules[i];
            if (rule.Claims(packet)) return rule;
        }

        return null;
    }

    public bool AnyActive => Rules.Any(rule => !rule.IsInert);

    public Profile WithRule(Rule rule)
    {
        var rules = Rules.ToList();
        var index = rules.FindIndex(existing => existing.Id == rule.Id);
        if (index >= 0) rules[index] = rule;
        else rules.Add(rule);
        return this with { Rules = rules };
    }

    public Profile WithoutRule(Guid ruleId)
        => this with { Rules = Rules.Where(rule => rule.Id != ruleId).ToList() };

    /// <summary>Moves a rule within the list, which changes which rule wins a contested packet.</summary>
    public Profile MoveRule(Guid ruleId, int newIndex)
    {
        var rules = Rules.ToList();
        var index = rules.FindIndex(rule => rule.Id == ruleId);
        if (index < 0) return this;

        var clamped = Math.Clamp(newIndex, 0, rules.Count - 1);
        if (clamped == index) return this;

        var rule = rules[index];
        rules.RemoveAt(index);
        rules.Insert(clamped, rule);
        return this with { Rules = rules };
    }
}
