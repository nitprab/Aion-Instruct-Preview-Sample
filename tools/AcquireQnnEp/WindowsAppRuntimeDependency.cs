using System.Runtime.InteropServices;

namespace AionInstructPreview.Tools.AcquireQnnEp;

internal static class WindowsAppRuntimeDependency
{
    private const string FamilyName = "Microsoft.WindowsAppRuntime.1.8_8wekyb3d8bbwe";
    private const int Arm64Architecture = 0x10;
    private const int ProcessLifetime = 0;
    private static readonly object Sync = new();

    private static IntPtr _context;
    private static string? _packageFullName;

    public static string EnsureLoaded()
    {
        lock (Sync)
        {
            if (_context != IntPtr.Zero)
            {
                return _packageFullName!;
            }

            if (RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
            {
                throw new PlatformNotSupportedException(
                    $"AcquireQnnEp requires an ARM64 process; current architecture is " +
                    $"{RuntimeInformation.ProcessArchitecture}.");
            }

            IntPtr dependencyId = IntPtr.Zero;
            IntPtr packageFullName = IntPtr.Zero;
            try
            {
                // Match the framework MSIX's WAR 1.8 GA floor; the ORT/QNN
                // generation remains compatible across the 1.8 servicing line.
                int hr = TryCreatePackageDependency(
                    IntPtr.Zero,
                    FamilyName,
                    MakePackageVersion(8000, 836, 2153, 0),
                    Arm64Architecture,
                    ProcessLifetime,
                    lifetimeArtifact: null,
                    options: 0,
                    out dependencyId);
                ThrowIfFailed(hr, $"TryCreatePackageDependency({FamilyName})");

                hr = AddPackageDependency(
                    dependencyId,
                    rank: 0,
                    options: 0,
                    out IntPtr context,
                    out packageFullName);
                ThrowIfFailed(hr, "AddPackageDependency");

                string loadedPackage = Marshal.PtrToStringUni(packageFullName)
                    ?? throw new InvalidOperationException(
                        "AddPackageDependency returned an empty package full name.");
                _context = context;
                _packageFullName = loadedPackage;
                return loadedPackage;
            }
            finally
            {
                FreeProcessHeapString(dependencyId);
                FreeProcessHeapString(packageFullName);
            }
        }
    }

    private static ulong MakePackageVersion(
        ushort major,
        ushort minor,
        ushort build,
        ushort revision) =>
        ((ulong)major << 48) |
        ((ulong)minor << 32) |
        ((ulong)build << 16) |
        revision;

    private static void ThrowIfFailed(int hr, string operation)
    {
        if (hr >= 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{operation} failed with HRESULT 0x{hr:X8}. " +
            "Verify that Windows App Runtime 1.8 version 8000.836.2153.0 or newer is installed.",
            Marshal.GetExceptionForHR(hr));
    }

    private static void FreeProcessHeapString(IntPtr value)
    {
        if (value != IntPtr.Zero)
        {
            _ = HeapFree(GetProcessHeap(), 0, value);
        }
    }

    [DllImport("kernelbase.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int TryCreatePackageDependency(
        IntPtr user,
        string packageFamilyName,
        ulong minVersion,
        int processorArchitectures,
        int lifetimeKind,
        string? lifetimeArtifact,
        int options,
        out IntPtr packageDependencyId);

    [DllImport("kernelbase.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int AddPackageDependency(
        IntPtr packageDependencyId,
        int rank,
        int options,
        out IntPtr packageDependencyContext,
        out IntPtr packageFullName);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetProcessHeap();

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HeapFree(IntPtr heap, uint flags, IntPtr memory);
}
