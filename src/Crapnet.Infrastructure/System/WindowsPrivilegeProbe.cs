using System.Security.Principal;
using Crapnet.Application.Abstractions;

// The namespace deliberately does not mirror the folder name. A namespace called
// Crapnet.Infrastructure.System would shadow the global System namespace for every file in this
// assembly, so that any sibling writing System.Runtime.InteropServices.X would stop compiling.
namespace Crapnet.Infrastructure.SystemServices;

/// <summary>
/// Reports whether the process is running elevated.
/// </summary>
/// <remarks>
/// Both of Crapnet's privileged paths — opening the capture driver and reconfiguring Internet
/// Connection Sharing — fail with opaque errors when they are not. Answering the question up
/// front lets the UI say "restart as administrator" instead of surfacing an access-denied code.
/// </remarks>
public sealed class WindowsPrivilegeProbe : IPrivilegeProbe
{
    private readonly bool _isElevated;

    public WindowsPrivilegeProbe() => _isElevated = DetectElevation();

    /// <inheritdoc />
    /// <remarks>
    /// Evaluated once at construction: a process cannot gain or lose membership of the
    /// administrators group while it runs, so re-checking would only cost impersonation work.
    /// </remarks>
    public bool IsElevated => _isElevated;

    private static bool DetectElevation()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            // An identity we cannot read is an identity we cannot trust to be elevated.
            return false;
        }
    }
}
