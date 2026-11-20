namespace Raider.Desktop.Services;

using System.Runtime.InteropServices;
using System.Security.Principal;

/// <summary>
/// Tells whether a process runs elevated (as administrator).
/// </summary>
internal static class ProcessElevation
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevationClass = 20;
    private const int ErrorAccessDenied = 5;

    public static bool IsCurrentProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// True when <paramref name="processId"/> is elevated, false when not, null when it cannot be told.
    /// A non-elevated caller is denied access to an elevated process's token, which also means elevated.
    /// </summary>
    public static bool? IsElevated(int processId)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero)
            return null;

        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token))
            {
                return Marshal.GetLastWin32Error() == ErrorAccessDenied && !IsCurrentProcessElevated() ? true : null;
            }

            try
            {
                return GetTokenInformation(token, TokenElevationClass, out var elevated, sizeof(int), out _)
                    ? elevated != 0
                    : null;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returnLength);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
