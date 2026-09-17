using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Crapnet.Infrastructure.Capture;

/// <summary>The WinDivert layer a handle is opened on.</summary>
public enum WinDivertLayer
{
    /// <summary>Traffic to and from the local machine.</summary>
    Network = 0,

    /// <summary>Traffic the machine is routing on behalf of somebody else, which is where tethered clients live.</summary>
    NetworkForward = 1,

    Flow = 2,
    Socket = 3,
    Reflect = 4,
}

/// <summary>Flags for <c>WinDivertOpen</c>, combinable.</summary>
[Flags]
public enum WinDivertOpenFlags : ulong
{
    /// <summary>Capture and modify. This is what an impairment tool wants.</summary>
    None = 0,

    /// <summary>Copy packets instead of diverting them, so the network keeps working untouched.</summary>
    Sniff = 1,

    /// <summary>Divert and discard; nothing is ever handed to us.</summary>
    Drop = 2,

    ReceiveOnly = 4,
    SendOnly = 8,

    /// <summary>Fail rather than installing the driver service. We install deliberately, not by accident.</summary>
    NoInstall = 16,

    /// <summary>Deliver IP fragments individually rather than only the reassembled head.</summary>
    Fragments = 32,
}

/// <summary>How much of a handle to tear down in <c>WinDivertShutdown</c>.</summary>
public enum WinDivertShutdownHow
{
    None = 0,
    Receive = 1,
    Send = 2,
    Both = 3,
}

/// <summary>Tunable driver parameters settable through <c>WinDivertSetParam</c>.</summary>
public enum WinDivertParam
{
    /// <summary>Packets the driver may hold for us before it starts discarding them.</summary>
    QueueLength = 0,

    /// <summary>Milliseconds the driver may hold a single packet before giving up on us.</summary>
    QueueTime = 1,

    /// <summary>Total bytes the driver may hold for us.</summary>
    QueueSize = 2,
}

/// <summary>
/// The address record WinDivert pairs with every packet: where it came from and where a
/// reinjection of it has to go.
/// </summary>
/// <remarks>
/// <para>
/// This struct is a byte-for-byte mirror of the native <c>WINDIVERT_ADDRESS</c> from WinDivert 2.2
/// and must stay exactly 80 bytes. The driver writes into it through a raw pointer, so a layout
/// mistake here is silent memory corruption rather than an exception —
/// <see cref="WinDivertNative.VerifyLayout"/> exists to catch that at startup.
/// </para>
/// <para>
/// Offsets, all little-endian:
/// <code>
///  0..7   INT64  Timestamp
///  8..11  UINT32 packed bitfield, laid out from the least significant bit up, the order MSVC
///                assigns bitfield members:
///                    bits  0-7   Layer:8
///                    bits  8-15  Event:8
///                    bit   16    Sniffed:1
///                    bit   17    Outbound:1
///                    bit   18    Loopback:1
///                    bit   19    Impostor:1
///                    bit   20    IPv6:1
///                    bit   21    IPChecksum:1
///                    bit   22    TCPChecksum:1
///                    bit   23    UDPChecksum:1
///                    bits 24-31  Reserved1:8
/// 12..15  UINT32 Reserved2
/// 16..79  64-byte union over the per-layer data. Crapnet only ever opens NETWORK and
///         NETWORK_FORWARD, whose arm is WINDIVERT_DATA_NETWORK { UINT32 IfIdx; UINT32 SubIfIdx; },
///         so bytes 16..23 are named and the remaining 56 are carried opaquely.
/// </code>
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct WinDivertAddress
{
    /// <summary>Capture time on the driver's high-resolution clock, in <see cref="Stopwatch"/> ticks.</summary>
    public long Timestamp;

    /// <summary>The packed bitfield documented on this type. Read it through the properties below.</summary>
    public uint Bitfield;

    /// <summary>Reserved by the driver; carried through untouched so reinjection round-trips.</summary>
    public uint Reserved2;

    /// <summary>Interface index the packet arrived on, and the one it must be reinjected on.</summary>
    public uint IfIdx;

    /// <summary>Sub-interface index, paired with <see cref="IfIdx"/>.</summary>
    public uint SubIfIdx;

    /// <summary>
    /// The tail of the 64-byte per-layer union. Meaningless at the network layers but preserved so
    /// that whatever the driver put there survives a round trip.
    /// </summary>
    public fixed byte UnionTail[56];

    private const int LayerShift = 0;
    private const int EventShift = 8;
    private const int SniffedBit = 16;
    private const int OutboundBit = 17;
    private const int LoopbackBit = 18;
    private const int ImpostorBit = 19;
    private const int IPv6Bit = 20;
    private const int IPChecksumBit = 21;
    private const int TcpChecksumBit = 22;
    private const int UdpChecksumBit = 23;

    /// <summary>The layer the packet was captured on.</summary>
    public WinDivertLayer Layer
    {
        readonly get => (WinDivertLayer)(byte)((Bitfield >> LayerShift) & 0xFF);
        set => SetByte(LayerShift, (byte)value);
    }

    /// <summary>The event that produced the record; always <c>NETWORK_PACKET</c> (0) at our layers.</summary>
    public byte Event
    {
        readonly get => (byte)((Bitfield >> EventShift) & 0xFF);
        set => SetByte(EventShift, value);
    }

    /// <summary>True when the packet was copied rather than diverted, so reinjecting it would duplicate it.</summary>
    public readonly bool Sniffed => GetBit(SniffedBit);

    /// <summary>True when the packet is leaving the machine. Reinjection honours this direction.</summary>
    public bool Outbound
    {
        readonly get => GetBit(OutboundBit);
        set => SetBit(OutboundBit, value);
    }

    /// <summary>True for loopback traffic, which never belongs to a tethered client.</summary>
    public readonly bool Loopback => GetBit(LoopbackBit);

    /// <summary>True when the packet was injected by somebody else rather than seen on the wire.</summary>
    public readonly bool Impostor => GetBit(ImpostorBit);

    /// <summary>True for IPv6. Crapnet filters these out, since ICS is an IPv4 NAT.</summary>
    public readonly bool IPv6 => GetBit(IPv6Bit);

    /// <summary>
    /// Whether the IPv4 header checksum is currently valid. Settable because altering a payload
    /// invalidates it, and the driver trusts this flag when it offloads the recomputation.
    /// </summary>
    public bool IPChecksum
    {
        readonly get => GetBit(IPChecksumBit);
        set => SetBit(IPChecksumBit, value);
    }

    /// <summary>Whether the TCP checksum is currently valid. See <see cref="IPChecksum"/>.</summary>
    public bool TCPChecksum
    {
        readonly get => GetBit(TcpChecksumBit);
        set => SetBit(TcpChecksumBit, value);
    }

    /// <summary>Whether the UDP checksum is currently valid. See <see cref="IPChecksum"/>.</summary>
    public bool UDPChecksum
    {
        readonly get => GetBit(UdpChecksumBit);
        set => SetBit(UdpChecksumBit, value);
    }

    /// <summary>
    /// A blank record describing an outbound IPv4 network-layer packet, for traffic Crapnet crafts
    /// itself rather than lifts off the wire.
    /// </summary>
    public static WinDivertAddress CreateOutbound(WinDivertLayer layer)
    {
        var address = default(WinDivertAddress);
        address.Timestamp = 0;
        address.Reserved2 = 0;
        address.IfIdx = 0;
        address.SubIfIdx = 0;
        address.Layer = layer;
        address.Event = 0;
        address.Outbound = true;
        return address;
    }

    private readonly bool GetBit(int bit) => ((Bitfield >> bit) & 1u) != 0u;

    private void SetBit(int bit, bool value)
        => Bitfield = value ? Bitfield | (1u << bit) : Bitfield & ~(1u << bit);

    private void SetByte(int shift, byte value)
        => Bitfield = (Bitfield & ~(0xFFu << shift)) | ((uint)value << shift);
}

/// <summary>
/// Raw P/Invoke surface for WinDivert 2.2. Nothing above <see cref="WinDivertPacketGateway"/>
/// should touch it.
/// </summary>
/// <remarks>
/// The driver DLL is loaded from the application directory, so <c>WinDivert.dll</c> and
/// <c>WinDivert64.sys</c> must sit next to the executable; <see cref="WinDivertDriverProbe"/>
/// checks for that before anything here is called.
/// </remarks>
public static unsafe class WinDivertNative
{
    private const string LibraryName = "WinDivert.dll";

    /// <summary>The value <c>WinDivertOpen</c> returns on failure.</summary>
    public static readonly nint InvalidHandleValue = -1;

    /// <summary>The size the native <c>WINDIVERT_ADDRESS</c> must have, per the WinDivert 2.2 headers.</summary>
    public const int AddressSize = 80;

    static WinDivertNative()
        => Debug.Assert(sizeof(WinDivertAddress) == AddressSize, "WINDIVERT_ADDRESS layout drifted from the native 80-byte record.");

    /// <summary>
    /// Fails loudly if <see cref="WinDivertAddress"/> no longer matches the native record.
    /// </summary>
    /// <remarks>
    /// Called before the first <c>WinDivertOpen</c>. A mismatch would let the driver write past the
    /// end of our struct, and a crash at open time is far kinder than corruption on the hot path.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The managed struct is not 80 bytes.</exception>
    public static void VerifyLayout()
    {
        var actual = sizeof(WinDivertAddress);
        if (actual != AddressSize)
        {
            throw new InvalidOperationException(
                $"WINDIVERT_ADDRESS must marshal to {AddressSize} bytes but this build produced {actual}. " +
                "Capture is disabled rather than risk corrupting memory.");
        }
    }

    [DllImport(LibraryName, SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern nint WinDivertOpen(string filter, WinDivertLayer layer, short priority, WinDivertOpenFlags flags);

    [DllImport(LibraryName, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertRecv(nint handle, byte* packet, uint packetLength, uint* recvLength, WinDivertAddress* address);

    [DllImport(LibraryName, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertSend(nint handle, byte* packet, uint packetLength, uint* sendLength, WinDivertAddress* address);

    [DllImport(LibraryName, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertShutdown(nint handle, WinDivertShutdownHow how);

    [DllImport(LibraryName, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertClose(nint handle);

    [DllImport(LibraryName, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertSetParam(nint handle, WinDivertParam param, ulong value);

    [DllImport(LibraryName, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertHelperCalcChecksums(byte* packet, uint packetLength, WinDivertAddress* address, ulong flags);

    /// <summary>Win32 error codes the capture loop has to distinguish, rather than merely report.</summary>
    public static class ErrorCodes
    {
        /// <summary><c>ERROR_FILE_NOT_FOUND</c>: the driver .sys file is not beside the DLL.</summary>
        public const int FileNotFound = 2;

        /// <summary><c>ERROR_INVALID_HANDLE</c>: the handle was closed underneath us.</summary>
        public const int InvalidHandle = 6;

        /// <summary><c>ERROR_ACCESS_DENIED</c>: loading a driver needs administrator.</summary>
        public const int AccessDenied = 5;

        /// <summary><c>ERROR_INVALID_PARAMETER</c>: the filter string did not compile.</summary>
        public const int InvalidParameter = 87;

        /// <summary><c>ERROR_NO_DATA</c>: the handle was shut down, so no further packets will arrive.</summary>
        public const int NoData = 232;

        /// <summary><c>ERROR_SERVICE_DOES_NOT_EXIST</c>: the driver service was removed.</summary>
        public const int ServiceDoesNotExist = 1060;

        /// <summary><c>ERROR_DRIVER_BLOCKED</c>: a security product is refusing the driver.</summary>
        public const int DriverBlocked = 1275;

        /// <summary><c>ERROR_INVALID_IMAGE_HASH</c>: the driver signature was rejected.</summary>
        public const int InvalidImageHash = 577;

        /// <summary><c>ERROR_OPERATION_ABORTED</c>: the pending receive was cancelled.</summary>
        public const int OperationAborted = 995;
    }

    /// <summary>
    /// Turns a <c>WinDivertOpen</c> failure into something a tester can act on.
    /// </summary>
    /// <remarks>
    /// The raw Win32 text for these codes ("Access is denied") says nothing about drivers or
    /// elevation, which is exactly the information somebody staring at a failed start needs.
    /// </remarks>
    public static string DescribeOpenError(int errorCode) => errorCode switch
    {
        ErrorCodes.FileNotFound =>
            "WinDivert64.sys was not found next to WinDivert.dll. Rebuild Crapnet or re-extract the release archive.",
        ErrorCodes.AccessDenied =>
            "Loading the WinDivert driver requires administrator rights. Restart Crapnet elevated.",
        ErrorCodes.InvalidParameter =>
            "WinDivert rejected the capture filter. This is a Crapnet bug; the generated filter string was not valid.",
        ErrorCodes.InvalidImageHash =>
            "Windows refused the WinDivert driver's signature. Disable test signing enforcement or install a signed WinDivert build.",
        ErrorCodes.DriverBlocked =>
            "The WinDivert driver was blocked from loading, usually by anti-virus or an endpoint protection policy. Allow WinDivert64.sys and try again.",
        ErrorCodes.ServiceDoesNotExist =>
            "The WinDivert driver service is missing and could not be created. Check WinDivert64.sys sits next to Crapnet.exe and restart Crapnet elevated.",
        _ => new Win32Exception(errorCode).Message,
    };
}
