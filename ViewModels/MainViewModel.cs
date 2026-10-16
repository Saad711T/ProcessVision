using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using ProcessVision.Infrastructure;
using ProcessVision.Models;
using ProcessVision.Services;
namespace ProcessVision.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ProcessMonitor _monitor = new();
    private readonly HistoryStore _store = new();
    private readonly System.Windows.Threading.Dispatcher _dispatcher;
    private readonly System.Windows.Threading.DispatcherTimer _saveTimer;
    private bool _disposed, _dirty;
    private string _query = "", _status = "Starting", _statusDetail = "Starting process monitor...", _notice = "";
    private ProcessEntry? _selected;
    private int _sessionCount;
    public ObservableCollection<ProcessEntry> Entries { get; } = [];
    public ICollectionView EntriesView { get; }
    public string Query { get => _query; set { _query = value; EntriesView.Refresh(); Notify(); Notify(nameof(EmptyText)); } }
    public string Status { get => _status; private set { _status = value; Notify(); Notify(nameof(ElevateVisibility)); } }
    public Visibility ElevateVisibility => Status == "Monitoring (standard access)" ? Visibility.Visible : Visibility.Collapsed;
    public string StatusDetail { get => _statusDetail; private set { _statusDetail = value; Notify(); } }
    public string Notice { get => _notice; private set { _notice = value; Notify(); } }
    public ProcessEntry? Selected { get => _selected; set { _selected = value; Notify(); Notify(nameof(SelectionVisibility)); } }
    public Visibility SelectionVisibility => Selected is null ? Visibility.Collapsed : Visibility.Visible;
    public string CountLabel => $"{Entries.Count:00} / 10";
    public string SessionCount => _sessionCount.ToString("N0");
    public string LatestTime => Entries.FirstOrDefault()?.TimeLabel ?? "—";
    public string EmptyText => EntriesView.IsEmpty ? (Entries.Count == 0 ? "Waiting for processes..." : "No matching processes.") : "";
    public ICommand ClearCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ElevateCommand { get; }
    public MainViewModel()
    {
        _dispatcher = Application.Current.Dispatcher;
        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.Filter = item => item is ProcessEntry p && (p.Name.Contains(Query, StringComparison.OrdinalIgnoreCase) || p.ProcessId.ToString().Contains(Query));
        ClearCommand = new RelayCommand(Clear);
        ExportCommand = new RelayCommand(Export);
        ElevateCommand = new RelayCommand(Elevate);
        try { foreach (var entry in _store.Load()) Entries.Add(entry with { Source = "Saved", ExecutablePath = entry.ExecutablePath.Any(c => c >= '\u0600' && c <= '\u06FF') ? "Not available" : entry.ExecutablePath }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { Notice = "Could not read saved history. Starting a new history."; }
        _saveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        _saveTimer.Tick += (_, _) => Save();
        _saveTimer.Start();
        _monitor.Started += entry => Dispatch(() => Add(entry));
        _monitor.StatusChanged += (status, detail) => Dispatch(() => { Status = status; StatusDetail = detail; });
        _monitor.Start();
    }
    private void Dispatch(Action action)
    {
        if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(() => { if (!_disposed) action(); });
    }
    private void Add(ProcessEntry entry)
    {
        // The same start may appear in history, the initial snapshot and the WMI queue.
        var existing = Entries.FirstOrDefault(p => p.ProcessId == entry.ProcessId && Math.Abs((p.StartedAt - entry.StartedAt).TotalSeconds) < 2);
        if (existing is not null)
        {
            if (entry.Source == "Startup" && existing.Source != "Saved") return;
            Entries.Remove(existing);
        }
        if (entry.Source != "Startup") _sessionCount++;
        var index = 0;
        while (index < Entries.Count && Entries[index].StartedAt >= entry.StartedAt) index++;
        Entries.Insert(index, entry);
        while (Entries.Count > 10) Entries.RemoveAt(Entries.Count - 1);
        if (Selected is not null && !Entries.Contains(Selected)) Selected = null;
        _dirty = true;
        RefreshCounts();
    }
    private void Elevate()
    {
        try
        {
            Save();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { Notice = "Full monitoring was cancelled. Standard monitoring continues."; }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { Notice = "Could not restart. Right-click ProcessVision and choose Run as administrator."; }
    }
    private void RefreshCounts() { Notify(nameof(CountLabel)); Notify(nameof(SessionCount)); Notify(nameof(LatestTime)); Notify(nameof(EmptyText)); }
    private void Clear()
    {
        Entries.Clear(); Selected = null; _dirty = true; Save(); RefreshCounts();
    }
    private void Save()
    {
        if (!_dirty) return;
        try { _store.Save(Entries); _dirty = false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Notice = "Could not save history. Monitoring continues."; }
    }
    private static string Cell(string text)
    {
        if (text.Length > 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
    private void Export()
    {
        var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"ProcessVision-{DateTime.Now:yyyyMMdd-HHmmss}.csv" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var rows = new List<string> { "Name,PID,ParentPID,StartedAt,Path,Source,Publisher" };
            rows.AddRange(Entries.Select(p => string.Join(",", Cell(p.Name), p.ProcessId, p.ParentId, Cell(p.StartedAt.ToLocalTime().ToString("O")), Cell(p.ExecutablePath), Cell(p.Source), Cell(p.Publisher))));
            File.WriteAllLines(dialog.FileName, rows, new UTF8Encoding(true));
            Notice = "History exported.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Notice = "Could not export. Choose a writable folder."; }
    }
    public void Dispose() { _disposed = true; _monitor.Dispose(); _saveTimer.Stop(); Save(); }
}

