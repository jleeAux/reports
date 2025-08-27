using TheAuxilia.ReportService.Models;

namespace TheAuxilia.ReportService.Reports;

public interface IReport
{
    string Name { get; }
    string Schedule { get; }
    Task<ReportResult> GenerateAsync();
}