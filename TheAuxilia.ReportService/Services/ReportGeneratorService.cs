using CsvHelper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Globalization;
using System.Text;

namespace TheAuxilia.ReportService.Services;

public class ReportGeneratorService
{
    private readonly DatabaseService _databaseService;
    private readonly EmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReportGeneratorService> _logger;
    private readonly string _outputPath;

    public ReportGeneratorService(
        DatabaseService databaseService,
        EmailService emailService,
        IConfiguration configuration,
        ILogger<ReportGeneratorService> logger)
    {
        _databaseService = databaseService;
        _emailService = emailService;
        _configuration = configuration;
        _logger = logger;
        _outputPath = configuration["ReportSettings:OutputPath"] ?? "/srv/reports/output";
        
        // Ensure output directory exists
        Directory.CreateDirectory(_outputPath);
    }

    public async Task GenerateAndSendReportAsync()
    {
        _logger.LogInformation("Starting report generation at {Time}", DateTime.Now);
        
        try
        {
            // Execute stored procedure
            var storedProcName = _configuration["ReportSettings:StoredProcedure"] ?? "precurringcronepoch";
            _logger.LogInformation("Executing stored procedure: {StoredProc}", storedProcName);
            
            var data = await _databaseService.ExecuteStoredProcedureAsync(storedProcName);
            
            if (data.Rows.Count == 0)
            {
                _logger.LogWarning("Stored procedure returned no data");
            }
            
            // Convert to CSV
            var csvContent = ConvertDataTableToCsv(data);
            var fileName = $"recurring_cron_report_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            
            // Save to disk
            var filePath = Path.Combine(_outputPath, fileName);
            await File.WriteAllTextAsync(filePath, csvContent);
            _logger.LogInformation("Report saved to: {FilePath}", filePath);
            
            // Get recipients
            var recipients = _configuration.GetSection("ReportSettings:Recipients")
                .Get<List<EmailRecipient>>() ?? new List<EmailRecipient>();
            
            if (recipients.Count == 0)
            {
                _logger.LogWarning("No recipients configured");
                return;
            }
            
            // Send email
            await _emailService.SendReportEmailAsync(csvContent, fileName, recipients);
            
            _logger.LogInformation("Report generation and delivery completed successfully");
            
            // Clean up old files
            await CleanupOldReportsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating report");
            throw;
        }
    }

    private string ConvertDataTableToCsv(DataTable dataTable)
    {
        var sb = new StringBuilder();
        using (var writer = new StringWriter(sb))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            // Write headers
            foreach (DataColumn column in dataTable.Columns)
            {
                csv.WriteField(column.ColumnName);
            }
            csv.NextRecord();

            // Write data
            foreach (DataRow row in dataTable.Rows)
            {
                for (var i = 0; i < dataTable.Columns.Count; i++)
                {
                    var value = row[i];
                    
                    // Handle null values
                    if (value == null || value == DBNull.Value)
                    {
                        csv.WriteField("");
                    }
                    else if (value is DateTime dateTime)
                    {
                        csv.WriteField(dateTime.ToString("yyyy-MM-dd HH:mm:ss"));
                    }
                    else if (value is decimal || value is double || value is float)
                    {
                        csv.WriteField(value.ToString());
                    }
                    else
                    {
                        csv.WriteField(value.ToString());
                    }
                }
                csv.NextRecord();
            }
        }
        
        _logger.LogInformation("Converted {RowCount} rows to CSV", dataTable.Rows.Count);
        return sb.ToString();
    }

    private Task CleanupOldReportsAsync()
    {
        try
        {
            var retentionDays = _configuration.GetValue<int>("ReportSettings:RetentionDays", 30);
            var cutoffDate = DateTime.Now.AddDays(-retentionDays);
            
            var files = Directory.GetFiles(_outputPath, "*.csv")
                .Select(f => new FileInfo(f))
                .Where(f => f.CreationTime < cutoffDate)
                .ToList();
            
            foreach (var file in files)
            {
                file.Delete();
                _logger.LogInformation("Deleted old report: {FileName}", file.Name);
            }
            
            if (files.Count > 0)
            {
                _logger.LogInformation("Cleaned up {Count} old report files", files.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cleaning up old reports");
            // Don't throw - this is not critical
        }
        
        return Task.CompletedTask;
    }
}