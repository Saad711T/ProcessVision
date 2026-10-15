using System.Diagnostics;
using System.Runtime.InteropServices;
using ProcessesVision.Models;
namespace ProcessesVision.Services;

/// <summary>WMI runs on one background MTA thread; no COM objects cross threads.</summary>
public sealed class ProcessMonitor : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Thread? _thread;
    public event Action<ProcessEntry>? Started;
    public event Action<string, string>? StatusChanged;
    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "Process start observer" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }
    private void Run()
    {
        object? locator = null, services = null, source = null;
        try
        {
            locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator", true)!);
            services = ((dynamic)locator!).ConnectServer(".", @"root\cimv2");
            source = ((dynamic)services).ExecNotificationQuery("SELECT * FROM Win32_ProcessStartTrace");
            PublishStartup(Snapshot());
            StatusChanged?.Invoke("Live monitoring", "Listening for process starts.");
            while (!_stop.IsCancellationRequested)
            {
                object? item = null;
                try
                {
                    item = ((dynamic)source).NextEvent(500);
                    dynamic e = item;
                    var pid = Convert.ToInt32(e.ProcessID);
                    var time = DateTime.FromFileTimeUtc(Convert.ToInt64(e.TIME_CREATED));
                    Started?.Invoke(WithFileDetails(new ProcessEntry(Convert.ToString(e.ProcessName) ?? "Unknown", pid,
                        Convert.ToInt32(e.ParentProcessID), time, "", "Live event")));
                }
                catch (COMException ex) when (ex.HResult == unchecked((int)0x80043001)) { /* Timed out: check cancellation. */ }
                finally { Release(item); }
            }
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or System.Runtime.InteropServices.InvalidComObjectException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or TypeLoadException)
        {
            if (!_stop.IsCancellationRequested) Poll();
        }
        finally { Release(source); Release(services); Release(locator); }
    }
    private void Poll()
    {
        var known = Snapshot(); // Existing processes are a baseline, not newly launched events.
        PublishStartup(known);
        StatusChanged?.Invoke("Monitoring (standard access)", "Checking every 200 ms. Very short processes may be missed. Enable full monitoring for system events.");
        while (!_stop.Token.WaitHandle.WaitOne(200))
        {
            var current = Snapshot();
            foreach (var pair in current.OrderBy(x => x.Value.StartedAt))
                if (!known.ContainsKey(pair.Key)) Started?.Invoke(WithFileDetails(pair.Value));
            known = current;
        }
    }
    private void PublishStartup(Dictionary<(int, long), ProcessEntry> snapshot)
    {
        foreach (var entry in snapshot.Values.OrderByDescending(x => x.StartedAt).Take(10).Reverse())
            Started?.Invoke(WithFileDetails(entry with { Source = "Startup" }));
    }
    private static ProcessEntry WithFileDetails(ProcessEntry entry)
    {
        var path = TryPath(entry.ProcessId, entry.StartedAt);
        return entry with { ExecutablePath = path, Publisher = PublisherReader.Read(path) };
    }
    private static Dictionary<(int, long), ProcessEntry> Snapshot()
    {
        var result = new Dictionary<(int, long), ProcessEntry>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var time = process.StartTime.ToUniversalTime();
                    result[(process.Id, time.Ticks)] = new(process.ProcessName + ".exe", process.Id, 0, time, "Not available", "Observed");
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        return result;
    }
    private static string TryPath(int pid, DateTime eventTime)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            // Avoid attaching a path from a different process after PID reuse.
            if (Math.Abs((process.StartTime.ToUniversalTime() - eventTime).TotalSeconds) > 2) return "Not available (process exited)";
            return process.MainModule?.FileName ?? "Not available";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or NotSupportedException)
        { return "Not available (process exited or access restricted)"; }
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    public void Dispose() { _stop.Cancel(); /* Background thread releases COM after NextEvent timeout. */ }
}
