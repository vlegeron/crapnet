using System.Runtime.InteropServices;

namespace Crapnet.Infrastructure.Sharing;

/// <summary>Which side of the share a connection sits on. Mirrors <c>SHARINGCONNECTIONTYPE</c>.</summary>
internal enum SharingConnectionType
{
    /// <summary><c>ICSSHARINGTYPE_PUBLIC</c>: the connection the internet comes from.</summary>
    Public = 0,

    /// <summary><c>ICSSHARINGTYPE_PRIVATE</c>: the connection the internet is shared to.</summary>
    Private = 1,
}

/// <summary>Mirrors <c>SHARINGCONNECTION_ENUM_FLAGS</c>.</summary>
internal enum SharingConnectionEnumFlags
{
    /// <summary><c>ICSSC_DEFAULT</c>: every connection of that role.</summary>
    Default = 0,

    /// <summary><c>ICSSC_ENABLED</c>: only connections that currently have sharing on.</summary>
    Enabled = 1,
}

/// <summary>
/// Hand-written declarations of the <c>HNetCfg</c> sharing interfaces.
/// </summary>
/// <remarks>
/// <para>
/// <b>Method order is the vtable order and nothing else.</b> These are declared as
/// <see cref="ComInterfaceType.InterfaceIsIUnknown"/>, so the runtime calls slot <c>3 + n</c> for
/// the n-th method declared here and pays no attention to the names. Reordering, inserting or
/// removing a member silently redirects every later call to the wrong function, which surfaces as
/// a corrupted return value or an access violation rather than as a compile error. The order below
/// is the one declared in <c>netcon.idl</c> and must not be touched.
/// </para>
/// <para>
/// Every interface here except <c>INetConnection</c> derives from <c>IDispatch</c>, which occupies
/// four vtable slots of its own between <c>IUnknown</c> and the first real method. COM interop
/// does not inherit vtable slots across <c>[ComImport]</c> interfaces, so those four slots are
/// spelled out at the top of each interface. They are never called.
/// </para>
/// </remarks>
internal static class NetSharingInterop
{
    /// <summary><c>CLSID_NetSharingManager</c>, the only creatable class in the set.</summary>
    internal static readonly Guid NetSharingManagerClsid = new("5C63C1AD-3956-4FF8-8486-40034758315B");
}

/// <summary>
/// A network connection as the sharing component sees it.
/// </summary>
/// <remarks>
/// Declared with no members on purpose. Crapnet only ever hands one of these back to the manager,
/// which then answers questions about it, so the interface exists solely to give the marshaller an
/// IID to <c>QueryInterface</c> for. Declaring methods we never call would be an invitation to get
/// their signatures wrong for no benefit.
/// </remarks>
[ComImport]
[Guid("C08956A0-1CD3-11D1-B1C5-00805FC1270E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INetConnection
{
}

/// <summary>Descriptive properties of a connection.</summary>
[ComImport]
[Guid("F4277C95-CE5B-463D-8167-5662D9BCAA72")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INetConnectionProps
{
    // --- IDispatch, four slots, never called. ---
    void GetTypeInfoCount(out uint typeInfoCount);
    void GetTypeInfo(uint index, uint lcid, out IntPtr typeInfo);
    void GetIDsOfNames(ref Guid riid, IntPtr names, uint nameCount, uint lcid, IntPtr dispIds);
    void Invoke(int dispIdMember, ref Guid riid, uint lcid, ushort flags, IntPtr dispParams, IntPtr result, IntPtr exceptionInfo, IntPtr argumentError);

    /// <summary>The adapter GUID in braces, which is the same text as <c>NetworkInterface.Id</c>.</summary>
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetGuid();

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetName();

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetDeviceName();

    /// <summary><c>NETCON_STATUS</c>. Unused, but its slot has to be accounted for.</summary>
    int GetStatus();

    /// <summary><c>NETCON_MEDIATYPE</c>. Unused, but its slot has to be accounted for.</summary>
    int GetMediaType();

    /// <summary><c>NCCF_*</c> characteristics. Unused, but its slot has to be accounted for.</summary>
    uint GetCharacteristics();
}

/// <summary>The sharing settings of one connection.</summary>
[ComImport]
[Guid("C08956B4-1CD3-11D1-B1C5-00805FC1270E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INetSharingConfiguration
{
    // --- IDispatch, four slots, never called. ---
    void GetTypeInfoCount(out uint typeInfoCount);
    void GetTypeInfo(uint index, uint lcid, out IntPtr typeInfo);
    void GetIDsOfNames(ref Guid riid, IntPtr names, uint nameCount, uint lcid, IntPtr dispIds);
    void Invoke(int dispIdMember, ref Guid riid, uint lcid, ushort flags, IntPtr dispParams, IntPtr result, IntPtr exceptionInfo, IntPtr argumentError);

    [return: MarshalAs(UnmanagedType.VariantBool)]
    bool GetSharingEnabled();

    SharingConnectionType GetSharingConnectionType();

    void DisableSharing();

    void EnableSharing(SharingConnectionType type);

    // Slots past this point are the firewall and port-mapping members. Crapnet never touches
    // them, and declaring them would mean guessing at NETCON port-mapping signatures, so they are
    // deliberately left out: omitting trailing slots cannot disturb the ones above.
}

/// <summary>Every connection on the machine, shared or not.</summary>
[ComImport]
[Guid("33C4643C-7811-46FA-A89A-768597BD7223")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INetSharingEveryConnectionCollection
{
    // --- IDispatch, four slots, never called. ---
    void GetTypeInfoCount(out uint typeInfoCount);
    void GetTypeInfo(uint index, uint lcid, out IntPtr typeInfo);
    void GetIDsOfNames(ref Guid riid, IntPtr names, uint nameCount, uint lcid, IntPtr dispIds);
    void Invoke(int dispIdMember, ref Guid riid, uint lcid, ushort flags, IntPtr dispParams, IntPtr result, IntPtr exceptionInfo, IntPtr argumentError);

    /// <summary>
    /// <c>DISPID_NEWENUM</c>, which precedes <c>Count</c> in the vtable despite its negative
    /// dispid. Returns an <c>IEnumVARIANT</c> over <c>INetConnection</c> objects.
    /// </summary>
    [return: MarshalAs(UnmanagedType.IUnknown)]
    object GetNewEnum();

    int GetCount();
}

/// <summary>The entry point into Internet Connection Sharing.</summary>
[ComImport]
[Guid("C08956B5-1CD3-11D1-B1C5-00805FC1270E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INetSharingManager
{
    // --- IDispatch, four slots, never called. ---
    void GetTypeInfoCount(out uint typeInfoCount);
    void GetTypeInfo(uint index, uint lcid, out IntPtr typeInfo);
    void GetIDsOfNames(ref Guid riid, IntPtr names, uint nameCount, uint lcid, IntPtr dispIds);
    void Invoke(int dispIdMember, ref Guid riid, uint lcid, ushort flags, IntPtr dispParams, IntPtr result, IntPtr exceptionInfo, IntPtr argumentError);

    /// <summary>False on SKUs and configurations where the sharing service is not present.</summary>
    [return: MarshalAs(UnmanagedType.VariantBool)]
    bool GetSharingInstalled();

    /// <summary>Unused; its slot has to be accounted for. The pointer is never dereferenced.</summary>
    IntPtr GetEnumPublicConnections(SharingConnectionEnumFlags flags);

    /// <summary>Unused; its slot has to be accounted for. The pointer is never dereferenced.</summary>
    IntPtr GetEnumPrivateConnections(SharingConnectionEnumFlags flags);

    [return: MarshalAs(UnmanagedType.Interface)]
    INetSharingConfiguration GetConfigurationForConnection([MarshalAs(UnmanagedType.Interface)] INetConnection connection);

    [return: MarshalAs(UnmanagedType.Interface)]
    INetSharingEveryConnectionCollection GetEnumEveryConnection();

    [return: MarshalAs(UnmanagedType.Interface)]
    INetConnectionProps GetNetConnectionProps([MarshalAs(UnmanagedType.Interface)] INetConnection connection);
}
