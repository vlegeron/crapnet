using Crapnet.Application.Abstractions;
using Crapnet.Domain.Impairments;
using Crapnet.Domain.Networking;
using Crapnet.Domain.Rules;

namespace Crapnet.Application.Impairments;

/// <summary>
/// Turns a packet plus the active profile into a verdict.
/// </summary>
/// <remarks>
/// Deliberately knows nothing about packet buffers, capture drivers or threads: it takes a
/// descriptor and a length and returns a decision. That keeps the part of the system most worth
/// getting right — the ordering and interaction of nine impairments — exercisable in plain tests
/// with a scripted random source.
///
/// Order matters and is fixed by <see cref="ImpairmentKind"/>. Impairments that destroy a packet
/// run before impairments that cost time, so we never spend queue budget on a packet that was
/// going to be dropped anyway; delay-based impairments then compose by accumulating onto a single
/// release time.
/// </remarks>
public sealed class ImpairmentPipeline
{
    private readonly IRandomSource _random;

    public ImpairmentPipeline(IRandomSource random)
        => _random = random ?? throw new ArgumentNullException(nameof(random));

    public PacketPlan Evaluate(
        Profile profile,
        RuleStateTable states,
        in PacketDescriptor descriptor,
        int packetLength,
        long nowMilliseconds)
    {
        var rule = profile.Resolve(descriptor);

        // Unmatched traffic, and traffic matched by a rule with nothing switched on, passes
        // straight through. That is what makes a bare rule usable as an explicit exception.
        if (rule is null) return PacketPlan.Forward(nowMilliseconds);

        var set = rule.Impairments;
        if (!set.AnyEnabled) return PacketPlan.Forward(nowMilliseconds, rule);

        if (set.Block.Enabled) return PacketPlan.Discarded(DiscardReason.Blocked, rule);

        if (set.Drop.Enabled && Roll(set.Drop.Chance))
            return PacketPlan.Discarded(DiscardReason.Dropped, rule);

        // A reset only means anything for TCP; for anything else the impairment stands aside
        // rather than silently dropping traffic the user did not ask to lose.
        if (set.Reset.Enabled && descriptor.Protocol == TransportProtocol.Tcp && Roll(set.Reset.Chance))
        {
            return new PacketPlan
            {
                Action = PacketAction.Reset,
                ReleaseAtMilliseconds = nowMilliseconds,
                Rule = rule,
            };
        }

        var tamper = set.Tamper.Enabled && Roll(set.Tamper.Chance);
        var extraCopies = set.Duplicate.Enabled && Roll(set.Duplicate.Chance) ? set.Duplicate.Copies : 0;

        var state = states.GetOrAdd(rule.Id, descriptor.Direction);
        var releaseAt = nowMilliseconds;
        var reordered = false;

        if (set.Bandwidth.Enabled)
        {
            state.SyncBandwidth(set.Bandwidth);
            var reserved = state.Bandwidth.Reserve(packetLength, nowMilliseconds);

            // No capacity within the buffer budget: this is congestive loss, which is what a
            // saturated link actually does, and it is worth reporting separately from random drop.
            if (reserved is null) return PacketPlan.Discarded(DiscardReason.QueueOverflow, rule);

            releaseAt = Math.Max(releaseAt, reserved.Value);
        }

        if (set.Throttle.Enabled)
        {
            if (state.IsThrottling(nowMilliseconds))
            {
                if (state.ThrottleDiscards) return PacketPlan.Discarded(DiscardReason.ThrottleDiscarded, rule);
                releaseAt = Math.Max(releaseAt, state.ThrottleUntilMilliseconds);
            }
            else if (Roll(set.Throttle.Chance))
            {
                var until = nowMilliseconds + set.Throttle.TimeframeMilliseconds;
                state.BeginThrottle(until, set.Throttle.DropThrottled);

                if (set.Throttle.DropThrottled) return PacketPlan.Discarded(DiscardReason.ThrottleDiscarded, rule);
                releaseAt = Math.Max(releaseAt, until);
            }
        }

        if (set.Reorder.Enabled && Roll(set.Reorder.Chance))
        {
            releaseAt += _random.Next(1, set.Reorder.MaxDelayMilliseconds + 1);
            reordered = true;
        }

        if (set.Lag.Enabled && Roll(set.Lag.Chance))
        {
            var delay = set.Lag.DelayMilliseconds;
            if (set.Lag.JitterMilliseconds > 0)
                delay += _random.Next(-set.Lag.JitterMilliseconds, set.Lag.JitterMilliseconds + 1);

            releaseAt += Math.Max(0, delay);
        }

        return new PacketPlan
        {
            Action = PacketAction.Forward,
            ReleaseAtMilliseconds = releaseAt,
            ExtraCopies = extraCopies,
            Tamper = tamper,
            LeaveChecksumBroken = tamper && !set.Tamper.RecalculateChecksum,
            Reordered = reordered,
            Rule = rule,
        };
    }

    private bool Roll(Percentage chance)
    {
        if (chance.IsZero) return false;
        if (chance.IsCertain) return true;
        return _random.NextDouble() < chance.AsFraction;
    }
}
