using Crapnet.Domain.Impairments;
using Crapnet.Domain.Networking;
using Crapnet.Domain.Rules;

namespace Crapnet.Application.Impairments;

/// <summary>
/// The mutable state a rule accumulates while it runs: link occupancy and any stall in progress.
/// </summary>
/// <remarks>
/// Held per rule <em>and</em> per direction. A 1 Mbps cap means 1 Mbps up and 1 Mbps down, which is
/// how people read it and how real links behave; sharing one limiter between directions would make
/// a download eat the device's upload capacity.
/// </remarks>
public sealed class RuleRuntimeState
{
    public RateLimiter Bandwidth { get; } = new();

    /// <summary>Clock time the current stall ends. Zero when traffic is flowing normally.</summary>
    public long ThrottleUntilMilliseconds { get; private set; }

    /// <summary>Whether the packets held by the current stall get released or binned.</summary>
    public bool ThrottleDiscards { get; private set; }

    private int _configuredKilobits = -1;
    private int _configuredBurst = -1;

    public void SyncBandwidth(BandwidthSetting setting)
    {
        if (_configuredKilobits == setting.KilobitsPerSecond && _configuredBurst == setting.EffectiveBurstKilobits)
            return;

        _configuredKilobits = setting.KilobitsPerSecond;
        _configuredBurst = setting.EffectiveBurstKilobits;
        Bandwidth.Configure(setting.KilobitsPerSecond, setting.EffectiveBurstKilobits);
        Bandwidth.Reset();
    }

    public bool IsThrottling(long nowMilliseconds) => nowMilliseconds < ThrottleUntilMilliseconds;

    public void BeginThrottle(long untilMilliseconds, bool discards)
    {
        ThrottleUntilMilliseconds = untilMilliseconds;
        ThrottleDiscards = discards;
    }

    public void Reset()
    {
        ThrottleUntilMilliseconds = 0;
        ThrottleDiscards = false;
        _configuredKilobits = -1;
        _configuredBurst = -1;
        Bandwidth.Reset();
    }
}

/// <summary>
/// Keyed store of per-rule state, so editing a rule mid-session does not lose its link occupancy
/// and deleting one does not leak it.
/// </summary>
public sealed class RuleStateTable
{
    private readonly Dictionary<(Guid RuleId, LinkDirection Direction), RuleRuntimeState> _states = new();

    public RuleRuntimeState GetOrAdd(Guid ruleId, LinkDirection direction)
    {
        var key = (ruleId, direction);
        if (_states.TryGetValue(key, out var state)) return state;

        state = new RuleRuntimeState();
        _states[key] = state;
        return state;
    }

    /// <summary>Forgets state for rules no longer in the profile, which happens on every edit.</summary>
    public void Retain(Profile profile)
    {
        if (_states.Count == 0) return;

        var live = new HashSet<Guid>(profile.Rules.Select(rule => rule.Id));
        var stale = _states.Keys.Where(key => !live.Contains(key.RuleId)).ToList();
        foreach (var key in stale) _states.Remove(key);
    }

    public void Clear() => _states.Clear();
}
