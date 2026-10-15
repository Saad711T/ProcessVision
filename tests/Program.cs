using System.Collections.Concurrent;
using System.Diagnostics;
using ProcessesVision.Models;
using ProcessesVision.Services;

if (args.Contains("--hold")) { Thread.Sleep(3000); return; }

var systemPublisher = PublisherReader.Read(Path.Combine(Environment.SystemDirectory, "cmd.exe"));
if (!systemPublisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) throw new Exception($"Unexpected system publisher: {systemPublisher}");
if (PublisherReader.Read("Not available") != "Unknown" || PublisherReader.Read(Path.Combine(AppContext.BaseDirectory, "missing.exe")) != "Unknown") throw new Exception("Publisher fallback failed");
var legacy = System.Text.Json.JsonSerializer.Deserialize<ProcessEntry>("{\"Name\":\"cmd.exe\",\"ProcessId\":1,\"ParentId\":0,\"StartedAt\":\"2026-01-01T00:00:00Z\",\"ExecutablePath\":\"\",\"Source\":\"Saved\"}")!;
if (legacy.Publisher != "Unknown") throw new Exception("Legacy history compatibility failed");
var serialized = System.Text.Json.JsonSerializer.Serialize(legacy with { Publisher = systemPublisher });
if (System.Text.Json.JsonSerializer.Deserialize<ProcessEntry>(serialized)!.Publisher != systemPublisher) throw new Exception("Publisher persistence failed");
Console.WriteLine($"PASS: publisher ({systemPublisher}), unavailable files, legacy history and publisher persistence.");
if (args.Contains("--publisher-only")) return;

var historyPath = Path.Combine(AppContext.BaseDirectory, "test-history.json");
var store = new HistoryStore(historyPath);
var entries = Enumerable.Range(1, 12).Select(i => new ProcessEntry("test.exe", i, 0, DateTime.UtcNow.AddSeconds(i), "test", "test")).OrderByDescending(e => e.StartedAt).ToList();
store.Save(entries);
var loaded = store.Load();
if (loaded.Count != 10 || loaded[0].ProcessId != 12 || loaded[^1].ProcessId != 3) throw new Exception("History limit/order failed");
store.Save([]);
if (store.Load().Count != 0) throw new Exception("History clear failed");
Console.WriteLine("PASS: history roundtrip, newest-first order, ten-entry limit, clear.");

using var monitor = new ProcessMonitor();
using var ready = new ManualResetEventSlim();
var received = new ConcurrentDictionary<int, ProcessEntry>();
string mode = "";
monitor.StatusChanged += (status, detail) => { mode = status; Console.WriteLine(detail); ready.Set(); };
monitor.Started += entry => received[entry.ProcessId] = entry;
monitor.Start();
if (!ready.Wait(TimeSpan.FromSeconds(15))) throw new Exception("Monitor did not initialize");
var expected = new List<int>();
for (var i = 0; i < 12; i++)
{
    using var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false, CreateNoWindow = true })!;
    expected.Add(p.Id);
    p.WaitForExit();
}
var deadline = DateTime.UtcNow.AddSeconds(12);
while (DateTime.UtcNow < deadline && expected.Any(pid => !received.ContainsKey(pid))) Thread.Sleep(100);
var captured = expected.Count(received.ContainsKey);
Console.WriteLine($"Captured {captured}/12 short-lived processes. Mode: {mode}");
if (mode == "Live monitoring" && captured != 12) throw new Exception("WMI missed test processes");
if (mode != "Live monitoring")
{
    Console.WriteLine("Testing standard-access monitoring with a process that remains alive for 3 seconds.");
    using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--hold") { UseShellExecute = false, CreateNoWindow = true })!;
    var pid = p.Id;
    p.WaitForExit();
    if (!received.ContainsKey(pid)) throw new Exception("Polling failed to detect a running process");
    if (string.IsNullOrWhiteSpace(received[pid].ExecutablePath)) throw new Exception("Missing path result");
    Console.WriteLine("PASS: standard-access monitoring detected a real process and retained it after exit.");
    Console.WriteLine("LIMITATION: polling does not guarantee capture of very short processes.");
}
else Console.WriteLine("PASS: real WMI events retained after all test processes exited.");
