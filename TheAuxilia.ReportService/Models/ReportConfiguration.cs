namespace TheAuxilia.ReportService.Models;

public class ReportConfiguration
{
    public string OutputDirectory { get; set; } = "/srv/reports/output";
    public string FromEmail { get; set; } = "jlee@theauxilia.com";
    public string FromName { get; set; } = "J Lee";
    public List<string> Recipients { get; set; } = new List<string>();
    public bool IncludeAttachment { get; set; } = true;
    public bool GroupBySource { get; set; } = true;
    public bool ShowSubtotals { get; set; } = true;
    public bool ShowGrandTotal { get; set; } = true;
}