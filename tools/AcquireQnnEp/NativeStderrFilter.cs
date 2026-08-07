using System.Runtime.InteropServices;

namespace AionInstructPreview.Tools.AcquireQnnEp;

/// <summary>
/// Suppresses native cpuinfo and ONNX Runtime warnings emitted directly to
/// Win32 stderr and CRT file descriptor 2 while QNN registers.
/// </summary>
internal sealed class NativeStderrFilter : IDisposable
{
    private const int StandardErrorHandle = -12;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const int OpenWriteOnly = 0x0001;
    private const int OpenBinary = 0x8000;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    private static int _active;

    private bool _owns;
    private int _savedFileDescriptor = -1;
    private IntPtr _nulHandle;

    public NativeStderrFilter()
    {
        if (Interlocked.Exchange(ref _active, 1) != 0)
        {
            return;
        }

        _owns = true;
        try
        {
            Console.Error.Flush();
            Engage();
        }
        catch
        {
            // Native-noise suppression is best effort and must never prevent
            // acquisition. Dispose also releases the process-global flag.
            Dispose();
        }
    }

    private void Engage()
    {
        int savedFileDescriptor = DuplicateFileDescriptor(2);
        IntPtr nulHandle = OpenNulHandle();
        IntPtr nulCrtHandle = OpenNulHandle();
        int nulFileDescriptor = -1;

        if (nulCrtHandle != InvalidHandleValue)
        {
            nulFileDescriptor = OpenOsFileHandle(
                nulCrtHandle,
                OpenWriteOnly | OpenBinary);
            if (nulFileDescriptor == -1)
            {
                _ = CloseHandle(nulCrtHandle);
                nulCrtHandle = IntPtr.Zero;
            }
        }

        if (savedFileDescriptor != -1 &&
            nulHandle != InvalidHandleValue &&
            nulFileDescriptor != -1 &&
            SetStdHandle(StandardErrorHandle, nulHandle) &&
            DuplicateFileDescriptorTo(nulFileDescriptor, 2) == 0)
        {
            _savedFileDescriptor = savedFileDescriptor;
            _nulHandle = nulHandle;
            _ = CloseFileDescriptor(nulFileDescriptor);
            return;
        }

        if (savedFileDescriptor != -1)
        {
            _ = DuplicateFileDescriptorTo(savedFileDescriptor, 2);
            _ = CloseFileDescriptor(savedFileDescriptor);
        }

        if (nulFileDescriptor != -1)
        {
            _ = CloseFileDescriptor(nulFileDescriptor);
        }
        else if (nulCrtHandle != IntPtr.Zero &&
                 nulCrtHandle != InvalidHandleValue)
        {
            _ = CloseHandle(nulCrtHandle);
        }

        if (nulHandle != InvalidHandleValue)
        {
            _ = CloseHandle(nulHandle);
        }

        ResyncNativeStandardError();
        TryRebindConsoleError();
    }

    public void Dispose()
    {
        if (!_owns)
        {
            return;
        }

        try
        {
            if (_savedFileDescriptor != -1)
            {
                if (DuplicateFileDescriptorTo(_savedFileDescriptor, 2) == 0)
                {
                    ResyncNativeStandardError();
                }
            }
        }
        finally
        {
            if (_savedFileDescriptor != -1)
            {
                _ = CloseFileDescriptor(_savedFileDescriptor);
                _savedFileDescriptor = -1;
            }

            if (_nulHandle != IntPtr.Zero)
            {
                _ = CloseHandle(_nulHandle);
                _nulHandle = IntPtr.Zero;
            }

            TryRebindConsoleError();
            _owns = false;
            Volatile.Write(ref _active, 0);
        }
    }

    private static IntPtr OpenNulHandle() =>
        CreateFile(
            "NUL",
            GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

    private static void ResyncNativeStandardError()
    {
        IntPtr restoredHandle = GetOsFileHandle(2);
        if (restoredHandle == InvalidHandleValue)
        {
            return;
        }

        _ = SetStdHandle(StandardErrorHandle, restoredHandle);
    }

    private static void TryRebindConsoleError()
    {
        try
        {
            // Console.Error may have cached the handle that _dup2 closed.
            // Rebind it so later genuine errors remain visible.
            var writer = new StreamWriter(
                Console.OpenStandardError(),
                Console.Error.Encoding)
            {
                AutoFlush = true,
            };
            Console.SetError(writer);
        }
        catch
        {
            // Restoring native stderr is authoritative. Managed rebinding is
            // best effort and must not mask the registration result.
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int standardHandle, IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ucrtbase.dll", EntryPoint = "_dup", CallingConvention = CallingConvention.Cdecl)]
    private static extern int DuplicateFileDescriptor(int fileDescriptor);

    [DllImport("ucrtbase.dll", EntryPoint = "_dup2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int DuplicateFileDescriptorTo(
        int sourceFileDescriptor,
        int targetFileDescriptor);

    [DllImport("ucrtbase.dll", EntryPoint = "_close", CallingConvention = CallingConvention.Cdecl)]
    private static extern int CloseFileDescriptor(int fileDescriptor);

    [DllImport("ucrtbase.dll", EntryPoint = "_open_osfhandle", CallingConvention = CallingConvention.Cdecl)]
    private static extern int OpenOsFileHandle(IntPtr osFileHandle, int flags);

    [DllImport("ucrtbase.dll", EntryPoint = "_get_osfhandle", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GetOsFileHandle(int fileDescriptor);
}
