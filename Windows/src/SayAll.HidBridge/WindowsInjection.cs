using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SayAll.HidBridge;

internal static partial class WindowsInjection
{
    private const uint ProcessCreateThread = 0x0002;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessVmRead = 0x0010;
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;
    private const uint WaitObject0 = 0;
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x00000002;
    private const int ErrorNotAllAssigned = 1300;

    public static void EnableDebugPrivilege()
    {
        if (!OpenProcessToken(
                GetCurrentProcess(),
                TokenAdjustPrivileges | TokenQuery,
                out var token))
        {
            ThrowLastWin32Error("OpenProcessToken");
        }

        try
        {
            if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out var luid))
            {
                ThrowLastWin32Error("LookupPrivilegeValue");
            }

            var privileges = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Privileges = new LuidAndAttributes
                {
                    Luid = luid,
                    Attributes = SePrivilegeEnabled,
                },
            };
            Marshal.SetLastPInvokeError(0);
            if (!AdjustTokenPrivileges(
                    token,
                    false,
                    ref privileges,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                ThrowLastWin32Error("AdjustTokenPrivileges");
            }

            var error = Marshal.GetLastPInvokeError();
            if (error == ErrorNotAllAssigned)
            {
                throw new UnauthorizedAccessException("SeDebugPrivilege is not assigned.");
            }
            if (error != 0)
            {
                throw new Win32Exception(error, "AdjustTokenPrivileges failed.");
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    public static void InjectLibrary(int processId, string dllPath)
    {
        var rights = ProcessCreateThread |
            ProcessQueryInformation |
            ProcessVmOperation |
            ProcessVmWrite |
            ProcessVmRead;
        var process = OpenProcess(rights, false, processId);
        if (process == IntPtr.Zero)
        {
            ThrowLastWin32Error("OpenProcess");
        }

        IntPtr remotePath = IntPtr.Zero;
        IntPtr thread = IntPtr.Zero;
        try
        {
            var encodedPath = Encoding.Unicode.GetBytes(Path.GetFullPath(dllPath) + '\0');
            remotePath = VirtualAllocEx(
                process,
                IntPtr.Zero,
                (nuint)encodedPath.Length,
                MemCommit | MemReserve,
                PageReadWrite);
            if (remotePath == IntPtr.Zero)
            {
                ThrowLastWin32Error("VirtualAllocEx");
            }

            if (!WriteProcessMemory(
                    process,
                    remotePath,
                    encodedPath,
                    (nuint)encodedPath.Length,
                    out var written) ||
                written != (nuint)encodedPath.Length)
            {
                ThrowLastWin32Error("WriteProcessMemory");
            }

            var kernel32 = GetModuleHandle("kernel32.dll");
            var loadLibrary = GetProcAddress(kernel32, "LoadLibraryW");
            if (loadLibrary == IntPtr.Zero)
            {
                ThrowLastWin32Error("GetProcAddress(LoadLibraryW)");
            }

            thread = CreateRemoteThread(
                process,
                IntPtr.Zero,
                0,
                loadLibrary,
                remotePath,
                0,
                out _);
            if (thread == IntPtr.Zero)
            {
                ThrowLastWin32Error("CreateRemoteThread");
            }

            if (WaitForSingleObject(thread, 20_000) != WaitObject0)
            {
                throw new TimeoutException("Remote LoadLibraryW timed out.");
            }

            if (!GetExitCodeThread(thread, out var exitCode) || exitCode == 0)
            {
                ThrowLastWin32Error("Remote LoadLibraryW");
            }
        }
        finally
        {
            if (thread != IntPtr.Zero)
            {
                CloseHandle(thread);
            }
            if (remotePath != IntPtr.Zero)
            {
                VirtualFreeEx(process, remotePath, 0, MemRelease);
            }
            CloseHandle(process);
        }
    }

    private static void ThrowLastWin32Error(string operation)
    {
        var error = Marshal.GetLastPInvokeError();
        throw new Win32Exception(error, $"{operation} failed.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LuidAndAttributes
    {
        public Luid Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public LuidAndAttributes Privileges;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentProcess();

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(
        IntPtr token,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TokenPrivileges newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr VirtualAllocEx(IntPtr process, IntPtr address, nuint size, uint allocationType, uint protection);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool VirtualFreeEx(IntPtr process, IntPtr address, nuint size, uint freeType);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WriteProcessMemory(
        IntPtr process,
        IntPtr address,
        byte[] buffer,
        nuint size,
        out nuint written);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr GetModuleHandle(string moduleName);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr GetProcAddress(IntPtr module, string procedureName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr CreateRemoteThread(
        IntPtr process,
        IntPtr threadAttributes,
        nuint stackSize,
        IntPtr startAddress,
        IntPtr parameter,
        uint creationFlags,
        out uint threadId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeThread(IntPtr thread, out uint exitCode);
}
