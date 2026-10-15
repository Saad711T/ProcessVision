namespace ProcessesVision.Models;

public sealed record ProcessEntry(string Name, int ProcessId, int ParentId, DateTime StartedAt, string ExecutablePath, string Source)
{
    public string Publisher { get; init; } = "Unknown";
    public string TimeLabel => StartedAt.ToLocalTime().ToString("HH:mm:ss");
    public string DateLabel => StartedAt.ToLocalTime().ToString("yyyy/MM/dd");
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
    public string ParentLabel => ParentId == 0 ? "Not available" : ParentId.ToString();
}
