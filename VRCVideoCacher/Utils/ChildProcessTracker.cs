using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VRCVideoCacher.Utils;

/// <summary>
/// Tracks spawned child processes and ensures they are reliably terminated when the main process exits.
/// On Windows, binds the application process tree to a Win32 Job Object with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
/// so the OS kernel guarantees cleanup even on abnormal termination or crash.
/// </summary>
public static class ChildProcessTracker
{
    private static readonly List<Process> TrackedProcesses = [];
    private static readonly Lock Lock = new();
    private static IntPtr _jobHandle = IntPtr.Zero;

    static ChildProcessTracker()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                InitJobObject();
            }
            catch (Exception ex)
            {
                Program.Logger.Debug(ex, "Failed to initialize Windows Job Object for child process cleanup");
            }
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
            return;

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
            if (!SetInformationJobObject(_jobHandle, JobObjectInfoType.ExtendedLimitInformation, extendedInfoPtr, (uint)length))
            {
                Program.Logger.Debug("SetInformationJobObject failed with error {Error}", Marshal.GetLastWin32Error());
                return;
            }

            using var currentProcess = Process.GetCurrentProcess();
            AssignProcessToJobObject(_jobHandle, currentProcess.Handle);
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
        {
            TrackedProcesses.Add(process);
        }

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
        {
            TrackedProcesses.Remove(process);
        }
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
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(1000);
                }
            }
            catch
            {
                // Best-effort cleanup
            }
            finally
            {
                try { proc.Dispose(); } catch { /* Ignore */ }
            }
        }
    }

    #region Win32 P/Invoke

    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType jobObjectInformationClass,
        IntPtr lpJobObjectInformation, uint cbJobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

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
