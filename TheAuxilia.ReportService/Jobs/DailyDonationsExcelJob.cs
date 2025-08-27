using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Quartz;
using SendGrid;
using TheAuxilia.ReportService.Models;
using TheAuxilia.ReportService.Reports;
using TheAuxilia.ReportService.Services;

namespace TheAuxilia.ReportService.Jobs;

public class DailyDonationsExcelJob : IJob
{
    private readonly IConfiguration _configuration;
    private readonly EmailService _emailService;
    private readonly ILogger<DailyDonationsExcelJob> _logger;

    public DailyDonationsExcelJob(
        IConfiguration configuration,
        EmailService emailService,
        ILogger<DailyDonationsExcelJob> logger)
    {
        _configuration = configuration;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Daily Donations Excel Report job started at {Time}", DateTime.Now);
        
        try
        {
            // Get configuration
            var connectionString = _configuration.GetConnectionString("DataWarehouse");
            var sendGridApiKey = _configuration["SendGrid:ApiKey"];
            var fromEmail = _configuration["SendGrid:FromEmail"] ?? "jlee@theauxilia.com";
            var fromName = _configuration["SendGrid:FromName"] ?? "J Lee";
            
            // Get recipients from DailyDonationsReport config
            var recipients = new List<string>();
            var recipientsConfig = _configuration.GetSection("DailyDonationsReport:Recipients").GetChildren();
            foreach (var recipient in recipientsConfig)
            {
                var email = recipient["Email"];
                if (!string.IsNullOrEmpty(email))
                {
                    recipients.Add(email);
                    _logger.LogInformation("Added recipient: {Email}", email);
                }
            }
            
            if (recipients.Count == 0)
            {
                recipients.Add("jlee@theauxilia.com");
                _logger.LogWarning("No recipients configured, using default: jlee@theauxilia.com");
            }
            
            var config = new ReportConfiguration
            {
                OutputDirectory = "/srv/reports/output",
                FromEmail = fromEmail,
                FromName = fromName,
                Recipients = recipients,
                IncludeAttachment = _configuration.GetValue<bool>("DailyDonationsReport:IncludeAttachment", true),
                GroupBySource = _configuration.GetValue<bool>("DailyDonationsReport:GroupBySource", true),
                ShowSubtotals = _configuration.GetValue<bool>("DailyDonationsReport:ShowSubtotals", true),
                ShowGrandTotal = _configuration.GetValue<bool>("DailyDonationsReport:ShowGrandTotal", true)
            };
            
            // Create report instance
            var sendGridClient = new SendGridClient(sendGridApiKey);
            var reportLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<DailyDonationsReportExcel>.Instance;
            var report = new DailyDonationsReportExcel(
                reportLogger,
                connectionString,
                sendGridClient,
                config,
                _configuration
            );
            
            // Generate and send report
            var result = await report.GenerateAsync();
            
            if (result.Success)
            {
                _logger.LogInformation("Daily Donations Excel Report generated successfully");
                _logger.LogInformation("File: {FilePath}, Records: {RecordCount}", 
                    result.FilePath, result.RecordCount);
            }
            else
            {
                throw new Exception($"Report generation failed: {result.Message}");
            }
            
            _logger.LogInformation("Daily Donations Excel Report job completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Daily Donations Excel Report job failed");
            
            // Send failure notification
            try
            {
                await _emailService.SendFailureNotificationAsync(
                    "Daily Donations Excel Report Failed",
                    $"Error: {ex.Message}",
                    ex.ToString());
            }
            catch (Exception emailEx)
            {
                _logger.LogError(emailEx, "Failed to send failure notification email");
            }
            
            throw; // Re-throw to mark job as failed
        }
    }
}