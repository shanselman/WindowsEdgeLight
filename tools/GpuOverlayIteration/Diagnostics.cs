using System.Diagnostics;
using System.Runtime.InteropServices;

internal enum Phase { Input, Geometry, Draw, Present, ResourceSample }

internal static class Diagnostics
{
    public static bool Enabled = Environment.GetEnvironmentVariable("GPU_PROFILE") == "1";
    public static bool LightweightSampler = Environment.GetEnvironmentVariable("GPU_LIGHT_SAMPLER") == "1";
    private static readonly List<(int Id, string Name, string Module, IntPtr Handle)> threads = [];
    private static readonly List<object> frames = [];
    private static readonly double[] totalMs = new double[5], maxMs = new double[5];
    private static readonly long[] counts = new long[5], allocated = new long[5];
    private static readonly ulong[] cycles = new ulong[5];
    private static int uiThreadId;

    public readonly struct Scope : IDisposable
    {
        private readonly Phase phase;
        private readonly long time, bytes;
        private readonly ulong cycles;
        public Scope(Phase phase)
        {
            this.phase = phase;
            time = Enabled ? Stopwatch.GetTimestamp() : 0;
            bytes = Enabled ? GC.GetAllocatedBytesForCurrentThread() : 0;
            cycles = 0;
            if (Enabled) NativeProcess.QueryThreadCycleTime(new IntPtr(-2), out cycles);
        }
        public void Dispose()
        {
            if (!Enabled) return;
            var elapsed = Stopwatch.GetElapsedTime(time).TotalMilliseconds;
            var index = (int)phase;
            totalMs[index] += elapsed;
            maxMs[index] = Math.Max(maxMs[index], elapsed);
            counts[index]++;
            allocated[index] += GC.GetAllocatedBytesForCurrentThread() - bytes;
            NativeProcess.QueryThreadCycleTime(new IntPtr(-2), out var endCycles);
            Diagnostics.cycles[index] += endCycles - cycles;
        }
    }

    public static Scope Measure(Phase phase) => new(phase);

    public static void Start()
    {
        if (!Enabled) return;
        Array.Clear(totalMs); Array.Clear(maxMs); Array.Clear(counts); Array.Clear(allocated); Array.Clear(cycles);
        uiThreadId = (int)NativeProcess.GetCurrentThreadId();
        using var process = Process.GetCurrentProcess();
        var modules = process.Modules.Cast<ProcessModule>().ToArray();
        foreach (ProcessThread thread in process.Threads)
        {
            using (thread)
            {
                var handle = NativeProcess.OpenThread(0x0800 | 0x0040, false, (uint)thread.Id);
                if (handle == IntPtr.Zero) continue;
                NativeProcess.GetThreadDescription(handle, out var description);
                var name = description == IntPtr.Zero ? "" : Marshal.PtrToStringUni(description) ?? "";
                if (description != IntPtr.Zero) NativeProcess.LocalFree(description);
                NativeProcess.NtQueryInformationThread(handle, 9, out var address, IntPtr.Size, IntPtr.Zero);
                var module = modules.FirstOrDefault(m => address.ToInt64() >= m.BaseAddress.ToInt64() &&
                    address.ToInt64() < m.BaseAddress.ToInt64() + m.ModuleMemorySize)?.ModuleName ?? "unknown";
                threads.Add((thread.Id, name, module, handle));
            }
        }
        Sample();
    }

    public static void Sample()
    {
        if (!Enabled) return;
        frames.Add(new
        {
            utc = DateTime.UtcNow, uiThreadId, totalAllocatedBytes = GC.GetTotalAllocatedBytes(),
            gc = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) },
            phases = Enum.GetValues<Phase>().Select(p => new { name = p.ToString(), count = counts[(int)p],
                totalMs = totalMs[(int)p], maxMs = maxMs[(int)p], cpuCycles = cycles[(int)p], allocatedBytes = allocated[(int)p] }).ToArray(),
            threads = threads.Select(t =>
            {
                NativeProcess.GetThreadTimes(t.Handle, out _, out _, out var kernel, out var user);
                return new { t.Id, t.Name, t.Module, cpuMs = (kernel + user) / 10000.0 };
            }).ToArray()
        });
    }

    public static object Result() => frames;
    public static void Close() { foreach (var t in threads) NativeProcess.CloseHandle(t.Handle); threads.Clear(); }
}

internal static class NativeProcess
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryCounters
    {
        public uint Size, PageFaultCount;
        public UIntPtr PeakWorkingSet, WorkingSet, QuotaPeakPaged, QuotaPaged,
            QuotaPeakNonPaged, QuotaNonPaged, Pagefile, PeakPagefile, PrivateUsage;
    }
    public static long Cpu()
    {
        if (!GetProcessTimes(new IntPtr(-1), out _, out _, out var kernel, out var user))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return (long)(kernel + user);
    }
    public static object Resources()
    {
        var memory = new MemoryCounters { Size = (uint)Marshal.SizeOf<MemoryCounters>() };
        if (!K32GetProcessMemoryInfo(new IntPtr(-1), ref memory, memory.Size) ||
            !GetProcessHandleCount(new IntPtr(-1), out var handles))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return new { handles, gdi = Native.GetGuiResources(new IntPtr(-1), 0),
            user = Native.GetGuiResources(new IntPtr(-1), 1), privateBytes = memory.PrivateUsage.ToUInt64(),
            workingSetBytes = memory.WorkingSet.ToUInt64() };
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(IntPtr handle, out ulong creation, out ulong exit, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool K32GetProcessMemoryInfo(IntPtr process, ref MemoryCounters counters, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessHandleCount(IntPtr process, out uint count);
    [DllImport("kernel32.dll")] internal static extern bool QueryThreadCycleTime(IntPtr thread, out ulong cycles);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] internal static extern IntPtr OpenThread(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll")] internal static extern int GetThreadDescription(IntPtr thread, out IntPtr description);
    [DllImport("kernel32.dll")] internal static extern IntPtr LocalFree(IntPtr pointer);
    [DllImport("kernel32.dll")] internal static extern bool GetThreadTimes(IntPtr thread, out ulong creation, out ulong exit, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
    [DllImport("ntdll.dll")] internal static extern int NtQueryInformationThread(IntPtr thread, int infoClass, out IntPtr address, int size, IntPtr length);
}
