namespace Crapnet.Domain.Impairments;

/// <summary>
/// The impairments Crapnet can apply. Order is the order the pipeline evaluates them in.
/// </summary>
public enum ImpairmentKind
{
    /// <summary>Discard everything matched: a hard blackhole.</summary>
    Block,

    /// <summary>Discard a share of matched packets.</summary>
    Drop,

    /// <summary>Answer TCP with a reset instead of forwarding, simulating a refused connection.</summary>
    Reset,

    /// <summary>Corrupt payload bytes, optionally leaving checksums wrong so the peer discards them.</summary>
    Tamper,

    /// <summary>Send matched packets more than once.</summary>
    Duplicate,

    /// <summary>Cap sustained throughput with a token bucket.</summary>
    Bandwidth,

    /// <summary>Hold traffic then release it in bursts.</summary>
    Throttle,

    /// <summary>Hold packets back so they arrive behind later ones.</summary>
    Reorder,

    /// <summary>Delay every matched packet, with optional jitter.</summary>
    Lag,
}
