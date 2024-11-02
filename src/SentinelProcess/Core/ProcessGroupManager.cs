using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SentinelProcess.Core;

#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time

public class ProcessGroupManager : IDisposable
{
    private readonly ILogger? _logger;
    private IntPtr _jobHandle = IntPtr.Zero;
    private int _groupId;
    private bool _disposed;
    private readonly string _pidFilePath;
    private readonly bool _isChildProcess;
    private FileSystemWatcher? _parentWatcher;

    private const int SIGTERM = 15;
    private const int SIGKILL = 9;

    #region Win32 API
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    private enum JobObjectInfoType
    {
        ExtendedLimitInformation = 9
    }

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    #endregion

    #region Unix API
    [DllImport("libc")]
    private static extern int setpgid(int pid, int pgid);

    [DllImport("libc")]

    private static extern int getpgid(int pid);

    [DllImport("libc")]
    private static extern int getpid();

    [DllImport("libc")]
    private static extern int kill(int pid, int sig);
    #endregion

    public ProcessGroupManager(string processName, ILogger? logger = null)
    {
        _logger = logger;
        _pidFilePath = Path.Combine(
            Path.GetTempPath(),
            $"SentinelProcess_{processName}_{Environment.ProcessId}.pid"
        );
        _isChildProcess = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SENTINEL_PARENT_PID"));

        InitializeProcessGroup();
        InitializeParentProcessMonitoring();
    }

    private void InitializeProcessGroup()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            InitializeWindowsJobObject();
        }
        else
        {
            InitializeUnixProcessGroup();
        }

        SaveProcessInfo(Environment.ProcessId);
    }

    private void InitializeWindowsJobObject()
    {
        _jobHandle = CreateJobObject(IntPtr.Zero, null);
        if (_jobHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create job object");
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };

        var length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
        var extendedInfoPtr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(info, extendedInfoPtr, false);

            if (!SetInformationJobObject(_jobHandle, JobObjectInfoType.ExtendedLimitInformation, extendedInfoPtr, (uint)length))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to set job object information");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(extendedInfoPtr);
        }
    }

    private void InitializeUnixProcessGroup()
    {
        try
        {
            int pid = getpid();
            if (setpgid(pid, 0) != 0)
            {
                throw new InvalidOperationException("Failed to create process group");
            }

            _groupId = getpgid(pid);
            _logger?.LogInformation("Created Unix process group with ID: {GroupId}", _groupId);

            AppDomain.CurrentDomain.ProcessExit += (s, e) => CleanupProcessGroup();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize Unix process group");
            throw;
        }
    }

    private void InitializeParentProcessMonitoring()
    {
        if (!_isChildProcess) return;

        var parentPidFile = Path.Combine(
            Path.GetTempPath(),
            $"SentinelProcess_*_{Environment.GetEnvironmentVariable("SENTINEL_PARENT_PID")}.pid"
        );

        _parentWatcher = new FileSystemWatcher(Path.GetTempPath())
        {
            Filter = Path.GetFileName(parentPidFile),
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size
        };

        _parentWatcher.Deleted += async (sender, e) =>
        {
            try
            {
                _logger?.LogWarning("Parent process PID file was deleted. Initiating cleanup.");
                await CleanupAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during parent process cleanup");
            }
        };

        _parentWatcher.Error += (sender, e) =>
        {
            _logger?.LogError("File watcher error occurred");
        };

        _parentWatcher.EnableRaisingEvents = true;

        // 초기 파일 존재 확인
        if (!File.Exists(parentPidFile))
        {
            _logger?.LogWarning("Parent process PID file not found. Initiating cleanup.");
            CleanupProcessGroup();
        }
    }

    public void AssignProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!AssignProcessToJobObject(_jobHandle, process.Handle))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to assign process to job object");
                }
                _logger?.LogInformation("Process {ProcessId} assigned to job object", process.Id);
            }
            else
            {
                if (setpgid(process.Id, _groupId) != 0)
                {
                    throw new InvalidOperationException($"Failed to assign process {process.Id} to group {_groupId}");
                }
                _logger?.LogInformation("Process {ProcessId} assigned to Unix process group {GroupId}",
                    process.Id, _groupId);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to assign process {ProcessId} to process group", process.Id);
            throw;
        }
    }

    private void SaveProcessInfo(int processId)
    {
        try
        {
            File.WriteAllText(_pidFilePath, processId.ToString());
            _logger?.LogInformation("Saved process information to {PidFile}", _pidFilePath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save process information");
            throw;
        }
    }

    private async Task CleanupAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            CleanupWindowsResources();
        }
        else
        {
            await CleanupUnixResourcesAsync();
        }

        CleanupPidFile();
    }

    private void CleanupWindowsResources()
    {
        if (_jobHandle != IntPtr.Zero)
        {
            CloseHandle(_jobHandle);
            _jobHandle = IntPtr.Zero;
        }
    }

    private async Task CleanupUnixResourcesAsync()
    {
        if (_groupId != 0)
        {
            try
            {
                // SIGTERM 전송
                _ = kill(-_groupId, SIGTERM);

                // 정상 종료 대기
                await Task.Delay(TimeSpan.FromSeconds(2));

                // SIGKILL 전송
                _ = kill(-_groupId, SIGKILL);
                _logger?.LogInformation("Cleaned up Unix process group {GroupId}", _groupId);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error cleaning up Unix process group {GroupId}", _groupId);
            }
        }
    }

    private void CleanupPidFile()
    {
        try
        {
            if (File.Exists(_pidFilePath))
            {
                File.Delete(_pidFilePath);
                _logger?.LogInformation("Cleaned up PID file: {PidFile}", _pidFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to cleanup PID file");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _parentWatcher?.Dispose();
        CleanupAsync().GetAwaiter().GetResult();

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void CleanupProcessGroup()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _groupId != 0)
        {
            try
            {
                // 먼저 SIGTERM으로 정상 종료 시도
                _ = kill(-_groupId, SIGTERM);

                // 잠시 대기 후 여전히 실행 중인 프로세스는 SIGKILL로 강제 종료
                Thread.Sleep(2000);
                _ = kill(-_groupId, SIGKILL);

                _logger?.LogInformation("Cleaned up Unix process group {GroupId}", _groupId);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error cleaning up Unix process group {GroupId}", _groupId);
            }
        }
    }
}
#pragma warning restore SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time