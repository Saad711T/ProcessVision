using System.Diagnostics;
using System.IO;

namespace ProcessVision.Services;

public static class PublisherReader
{
    // CompanyName is executable metadata, not a verified signing identity.
    public static string Read(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath)) return "Unknown";
        try
        {
            var company = FileVersionInfo.GetVersionInfo(executablePath).CompanyName;
            return string.IsNullOrWhiteSpace(company) ? "Unknown" : company.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return "Unknown";
        }
    }
}
