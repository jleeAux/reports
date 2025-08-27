namespace TheAuxilia.ReportService.Models;

public class ReportResult
{
    public bool Success { get; set; }
    public string ReportName { get; set; } = "";
    public DateTime GeneratedAt { get; set; }
    public string? FilePath { get; set; }
    public int RecordCount { get; set; }
    public string Message { get; set; } = "";
}