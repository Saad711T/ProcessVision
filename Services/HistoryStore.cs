using System.IO;
using System.Text.Json;
using ProcessVision.Models;
namespace ProcessVision.Services;

public sealed class HistoryStore
{
    private readonly string _path;
    public HistoryStore(string? path = null) => _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProcessVision", "history.json");
    public IReadOnlyList<ProcessEntry> Load()
    {
        if (!File.Exists(_path)) return [];
        return (JsonSerializer.Deserialize<List<ProcessEntry>>(File.ReadAllText(_path)) ?? [])
            .Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Name))
            .OrderByDescending(x => x.StartedAt).Take(10)
            .Select(x => string.IsNullOrWhiteSpace(x.Publisher) || x.Publisher == "Unknown"
                ? x with { Publisher = PublisherReader.Read(x.ExecutablePath) } : x).ToList();
    }
    public void Save(IEnumerable<ProcessEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries.Take(10)));
        File.Move(temporary, _path, true);
    }
}
