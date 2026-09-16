namespace Crapnet.Application.Abstractions;

/// <summary>
/// Port for intercepting and reinjecting packets. Implemented over WinDivert on Windows.
/// </summary>
/// <remarks>
/// A packet handed out by <see cref="TryReceive"/> is withheld from the network until it is either
/// sent back with <see cref="Send"/> or discarded by releasing it. That is what makes dropping a
/// packet the same operation as simply not forwarding it.
/// </remarks>
public interface IPacketGateway : IDisposable
{
    bool IsOpen { get; }

    /// <summary>Opens the capture. Throws if the driver is missing or the process is not elevated.</summary>
    void Open(CaptureFilter filter);

    /// <summary>
    /// Blocks until a packet arrives. Returns false once the gateway is closed or the wait is
    /// cancelled, in which case <paramref name="packet"/> is null.
    /// </summary>
    bool TryReceive(out CapturedPacket? packet, CancellationToken cancellationToken);

    /// <summary>Puts a packet back on the wire. May be called more than once to duplicate it.</summary>
    void Send(CapturedPacket packet);

    /// <summary>Returns a packet to the pool. Every received packet must be released exactly once.</summary>
    void Release(CapturedPacket packet);

    /// <summary>Recomputes IP and transport checksums in place after the payload has been altered.</summary>
    void RecalculateChecksums(CapturedPacket packet);

    void Close();
}
