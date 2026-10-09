using System;
using System.Runtime.InteropServices;

namespace AionInstructPreview.Chat.Wpf;

/// <summary>
/// Adds a process-scoped dynamic dependency on the installed Aion Instruct Preview
/// framework package. This puts the framework directory on the DLL search path
/// so unpackaged apps can activate AionInstructPreview.Text and load its native files.
/// </summary>
internal static class FrameworkDependency
{
    // PackageFamilyName of the installed framework MSIX
    // (Microsoft.AionInstructPreview.Framework.1.0). Verify with:
    //   Get-AppxPackage *AionInstructPreview* | Select PackageFamilyName
    private const string FamilyName =
        "Microsoft.AionInstructPreview.Framework.1.0_8wekyb3d8bbwe";

    // PackageDependencyProcessorArchitectures (appmodel.h)
    private const int ArchArm64 = 0x10;
    private const int ArchX64 = 0x4;

    // PackageDependencyLifetimeKind
    private const int LifetimeKindProcess = 0;

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

    public static void EnsureLoaded()
    {
        int arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? ArchArm64
            : ArchX64;

        // This release requires the catalog framework 1.0.0.3.
        int hr = TryCreatePackageDependency(
            IntPtr.Zero,
            FamilyName,
            minVersion: (1UL << 48) | 3UL,
            arch,
            LifetimeKindProcess,
            lifetimeArtifact: null,
            options: 0,
            out IntPtr depId);
        if (hr != 0)
        {
            throw new InvalidOperationException(
                $"TryCreatePackageDependency({FamilyName}) failed (HRESULT 0x{hr:X8}). " +
                "Install Aion Instruct Preview framework 1.0.0.3 or newer. Run.ps1 checks this.",
                Marshal.GetExceptionForHR(hr));
        }

        hr = AddPackageDependency(depId, rank: 0, options: 0, out _, out _);
        if (hr != 0)
        {
            throw new InvalidOperationException(
                $"AddPackageDependency failed (HRESULT 0x{hr:X8}).",
                Marshal.GetExceptionForHR(hr));
        }

        // depId is HeapAlloc'd; intentionally leaked. The Process-lifetime
        // dependency is released automatically at process exit, so there is
        // nothing to clean up for the app's lifetime.
    }
}
