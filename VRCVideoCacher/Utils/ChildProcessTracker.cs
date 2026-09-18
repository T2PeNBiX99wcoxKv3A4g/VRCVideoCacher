using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;

namespace VRCVideoCacher.Utils;

/// <summary>
/// Tracks spawned child processes and ensures they are reliably terminated when the main process exits.
/// On Windows, binds the application process tree to a Win32 Job Object with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
/// so the OS kernel guarantees cleanup even on abnormal termination or crash.
/// </summary>
public partial class ChildProcessTracker
{
    private static readonly ILogger Log = Program.Logger.ForContext<ChildProcessTracker>();
    private static readonly List<Process> TrackedProcesses = [];
    private static readonly Lock Lock = new();
    private static IntPtr _jobHandle = IntPtr.Zero;

    static ChildProcessTracker()
    {
        if (OperatingSystem.IsWindows())
            try
            {
                InitJobObject();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to initialize Windows Job Object for child process cleanup");
            }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => TerminateAll();
    }

    /// <summary>
    /// Explicit initialization method to ensure the static constructor runs early at startup.
    /// </summary>
    public static void Initialize()
    {
        // Triggers the static constructor.
    }

    [SupportedOSPlatform("windows")]
    private static void InitJobObject()
    {
        _jobHandle = CreateJobObject(IntPtr.Zero, null);
        if (_jobHandle == IntPtr.Zero)
        {
            Log.Debug("CreateJobObject failed with error {Error}", Marshal.GetLastWin32Error());
            return;
        }

        var info = new JobObjectBasicLimitInformation
        {
            LimitFlags = JobObjectLimitKillOnJobClose
        };

        var extendedInfo = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = info
        };

        var length = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var extendedInfoPtr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);
            if (!SetInformationJobObject(_jobHandle, JobObjectInfoType.ExtendedLimitInformation, extendedInfoPtr,
                    (uint)length))
            {
                Log.Debug("SetInformationJobObject failed with error {Error}", Marshal.GetLastWin32Error());
                return;
            }

            using var currentProcess = Process.GetCurrentProcess();
            if (AssignProcessToJobObject(_jobHandle, currentProcess.Handle)) return;
            Log.Debug("AssignProcessToJobObject failed with error {Error}", Marshal.GetLastWin32Error());
            _jobHandle = IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(extendedInfoPtr);
        }
    }

    /// <summary>
    /// Registers a child process to be tracked and terminated when the application exits.
    /// </summary>
    public static void Track(Process? process)
    {
        if (process == null) return;

        lock (Lock)
            TrackedProcesses.Add(process);

        if (!OperatingSystem.IsWindows() || _jobHandle == IntPtr.Zero) return;
        try
        {
            if (!process.HasExited)
                AssignProcessToJobObject(_jobHandle, process.Handle);
        }
        catch
        {
            // Process may have already exited or handle cannot be assigned
        }
    }

    /// <summary>
    /// Unregisters a tracked child process once it has exited normally.
    /// </summary>
    public static void Untrack(Process? process)
    {
        if (process == null) return;
        lock (Lock)
            TrackedProcesses.Remove(process);
    }

    /// <summary>
    /// Forces termination of all currently tracked child processes.
    /// </summary>
    public static void TerminateAll()
    {
        List<Process> toKill;
        lock (Lock)
        {
            toKill = [.. TrackedProcesses];
            TrackedProcesses.Clear();
        }

        foreach (var proc in toKill)
            try
            {
                if (proc.HasExited) continue;
                proc.Kill(true);
                proc.WaitForExit(1000);
            }
            catch
            {
                // Best-effort cleanup
            }
            finally
            {
                try
                {
                    proc.Dispose();
                }
                catch
                {
                    /* Ignore */
                }
            }
    }

    #region Win32 P/Invoke

    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType jobObjectInformationClass,
        IntPtr lpJobObjectInformation, uint cbJobObjectInformationLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    private enum JobObjectInfoType
    {
        ExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryLimit;
        public UIntPtr PeakJobMemoryLimit;
    }

    #endregion
}