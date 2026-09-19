using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JetBrains.Annotations;

namespace VRCVideoCacher.Utils;

/// <summary>
/// Tracks spawned child processes and ensures they are reliably terminated when the main process exits.
/// On Windows, binds the application process tree to a Win32 Job Object with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
/// so the OS kernel guarantees cleanup even on abnormal termination or crash.
/// </summary>
public partial class ChildProcessTracker : Singleton<ChildProcessTracker>
{
    private readonly ConcurrentDictionary<Process, byte> _trackedProcesses = new();
    private IntPtr _jobHandle = IntPtr.Zero;
    private bool _terminating;

    public ChildProcessTracker()
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

        AppDomain.CurrentDomain.ProcessExit += (_, _) => TerminateAll2();
    }

    [SupportedOSPlatform("windows")]
    private void InitJobObject()
    {
        var jobHandle = CreateJobObject(IntPtr.Zero, null);
        if (jobHandle == IntPtr.Zero)
        {
            Log.Debug("CreateJobObject failed with error {Error}", Marshal.GetLastPInvokeError());
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

        try
        {
            if (!SetInformationJobObject(jobHandle, JobObjectInfoType.ExtendedLimitInformation, ref extendedInfo,
                    (uint)length))
            {
                Log.Debug("SetInformationJobObject failed with error {Error}", Marshal.GetLastPInvokeError());
                return;
            }

            using var currentProcess = Process.GetCurrentProcess();

            if (!AssignProcessToJobObject(jobHandle, currentProcess.Handle))
            {
                Log.Debug("AssignProcessToJobObject failed with error {Error}", Marshal.GetLastPInvokeError());
                return;
            }

            _jobHandle = jobHandle;
            jobHandle = IntPtr.Zero;
        }
        finally
        {
            if (jobHandle != IntPtr.Zero && !CloseHandle(jobHandle))
                Log.Debug("CloseHandle failed with error {Error}", Marshal.GetLastPInvokeError());
        }
    }

    /// <summary>
    /// Registers a child process to be tracked and terminated when the application exits.
    /// </summary>
    [PublicAPI]
    public void Track2(Process? process)
    {
        if (process == null) return;
        if (Volatile.Read(ref _terminating))
        {
            KillProcess(process);
            return;
        }

        if (!_trackedProcesses.TryAdd(process, 0)) return;

        if (Volatile.Read(ref _terminating))
        {
            if (_trackedProcesses.TryRemove(process, out _))
                KillProcess(process);
            return;
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
    [PublicAPI]
    public void Untrack2(Process? process)
    {
        if (process == null) return;
        _trackedProcesses.TryRemove(process, out _);
    }

    [PublicAPI]
    public void TrackWhile2(Process? process, Action<Process> callback)
    {
        if (process == null) return;
        Track2(process);
        try
        {
            callback(process);
        }
        finally
        {
            Untrack2(process);
        }
    }

    /// <summary>
    /// Forces termination of all currently tracked child processes in parallel.
    /// </summary>
    [PublicAPI]
    public void TerminateAll2()
    {
        if (Interlocked.Exchange(ref _terminating, true)) return;
        var toKill = _trackedProcesses.Keys.ToList();
        _trackedProcesses.Clear();
        Parallel.ForEach(toKill, KillProcess);
    }

    private static void KillProcess(Process proc)
    {
        try
        {
            if (proc.HasExited) return;
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
                // Ignore
            }
        }
    }

    #region Win32 P/Invoke

    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType jobObjectInformationClass,
        ref JobObjectExtendedLimitInformation lpJobObjectInformation, uint cbJobObjectInformationLength);

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