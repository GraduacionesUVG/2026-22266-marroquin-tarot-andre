using System.Runtime.InteropServices;
using BotnetMiningDetector.App.Data.Models;

namespace BotnetMiningDetector.App.Agent;

// lector de recursos globales del sistema
public sealed class SystemResourceReader
{
    private ulong? _previousIdle;
    private ulong? _previousKernel;
    private ulong? _previousUser;

    // lee cpu memoria y estado del sistema
    public SystemMetric ReadSystemMetric(DateTime timestamp, string status)
    {
        var cpu = ReadCpuTotalPercent();
        var memory = ReadMemory();

        return new SystemMetric
        {
            Timestamp = timestamp,
            CpuTotalPercent = cpu,
            MemoryTotalMb = memory.TotalMb,
            MemoryUsedMb = memory.UsedMb,
            MemoryUsedPercent = memory.UsedPercent,
            MonitoringStatus = status
        };
    }

    // calcula el uso total de cpu
    private double ReadCpuTotalPercent()
    {
        try
        {
            if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            {
                return 0;
            }

            var idle = ToUInt64(idleTime);
            var kernel = ToUInt64(kernelTime);
            var user = ToUInt64(userTime);

            if (_previousIdle is null || _previousKernel is null || _previousUser is null)
            {
                _previousIdle = idle;
                _previousKernel = kernel;
                _previousUser = user;
                return 0;
            }

            var idleDelta = idle - _previousIdle.Value;
            var kernelDelta = kernel - _previousKernel.Value;
            var userDelta = user - _previousUser.Value;
            var total = kernelDelta + userDelta;

            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;

            if (total == 0)
            {
                return 0;
            }

            var busy = total > idleDelta ? total - idleDelta : 0;
            return Math.Round(Math.Clamp(busy * 100.0 / total, 0, 100), 2);
        }
        catch
        {
            return 0;
        }
    }

    // lee el uso global de memoria
    private static MemorySnapshot ReadMemory()
    {
        try
        {
            var status = new MemoryStatusEx();
            status.dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
            if (!GlobalMemoryStatusEx(ref status))
            {
                return new MemorySnapshot();
            }

            var totalMb = status.ullTotalPhys / 1024d / 1024d;
            var availableMb = status.ullAvailPhys / 1024d / 1024d;
            var usedMb = totalMb - availableMb;

            return new MemorySnapshot
            {
                TotalMb = Math.Round(totalMb, 2),
                UsedMb = Math.Round(usedMb, 2),
                UsedPercent = Math.Round((double)status.dwMemoryLoad, 2)
            };
        }
        catch
        {
            return new MemorySnapshot();
        }
    }

    // convierte el tiempo del sistema a un valor numerico
    private static ulong ToUInt64(FileTime fileTime)
    {
        return ((ulong)fileTime.HighDateTime << 32) | fileTime.LowDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    // obtiene los tiempos del sistema
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    // obtiene el estado global de memoria
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    // estructura de tiempo del sistema
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    // estructura de estado de memoria
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    // datos de memoria recolectados
    private sealed class MemorySnapshot
    {
        public double TotalMb { get; init; }
        public double UsedMb { get; init; }
        public double UsedPercent { get; init; }
    }
}
