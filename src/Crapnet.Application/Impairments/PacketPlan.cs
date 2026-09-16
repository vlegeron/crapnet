using Crapnet.Domain.Rules;

namespace Crapnet.Application.Impairments;

/// <summary>What the pipeline decided to do with a packet.</summary>
public enum PacketAction
{
    /// <summary>Put it back on the wire, possibly after a delay.</summary>
    Forward,

    /// <summary>Never send it. Because we hold every captured packet, discarding it is the drop.</summary>
    Discard,

    /// <summary>Turn it into a TCP reset aimed back at its sender, then send that instead.</summary>
    Reset,
}

/// <summary>Why a packet was discarded, so the statistics can tell loss apart from congestion.</summary>
public enum DiscardReason
{
    None = 0,
    Blocked,
    Dropped,
    ThrottleDiscarded,
    QueueOverflow,
}

/// <summary>
/// The pipeline's verdict for one packet: immutable, so it can be computed and asserted on
/// without any packet buffer in hand.
/// </summary>
public readonly record struct PacketPlan
{
    public required PacketAction Action { get; init; }

    /// <summary>Clock time at which the packet may be sent. At or before "now" means immediately.</summary>
    public long ReleaseAtMilliseconds { get; init; }

    /// <summary>Copies to send on top of the original.</summary>
    public int ExtraCopies { get; init; }

    /// <summary>Corrupt the payload before sending.</summary>
    public bool Tamper { get; init; }

    /// <summary>Leave the checksum wrong after tampering, so the peer drops the packet itself.</summary>
    public bool LeaveChecksumBroken { get; init; }

    /// <summary>The packet was deliberately pushed back so later packets could overtake it.</summary>
    public bool Reordered { get; init; }

    public DiscardReason Discard { get; init; }

    /// <summary>The rule that claimed the packet, or null when nothing matched.</summary>
    public Rule? Rule { get; init; }

    public bool IsDelayed(long nowMilliseconds) => ReleaseAtMilliseconds > nowMilliseconds;

    public static PacketPlan Forward(long releaseAt, Rule? rule = null) => new()
    {
        Action = PacketAction.Forward,
        ReleaseAtMilliseconds = releaseAt,
        Rule = rule,
    };

    public static PacketPlan Discarded(DiscardReason reason, Rule? rule = null) => new()
    {
        Action = PacketAction.Discard,
        Discard = reason,
        Rule = rule,
    };
}
