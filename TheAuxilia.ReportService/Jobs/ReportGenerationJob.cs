using Microsoft.Extensions.Logging;
using Quartz;
using TheAuxilia.ReportService.Services;

namespace TheAuxilia.ReportService.Jobs;

public class ReportGenerationJob : IJob
{
    private readonly ReportGeneratorService _reportGenerator;
    private readonly EmailService _emailService;
    private readonly ILogger<ReportGenerationJob> _logger;

    public ReportGenerationJob(
        ReportGeneratorService reportGenerator,
        EmailService emailService,
        ILogger<ReportGenerationJob> logger)
    {
        _reportGenerator = reportGenerator;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Report generation job started at {Time}", DateTime.Now);
        
        try
        {
            await _reportGenerator.GenerateAndSendReportAsync();
            _logger.LogInformation("Report generation job completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Report generation job failed");
            
            // Send failure notification
            try
            {
                await _emailService.SendFailureNotificationAsync(
                    "Recurring Cron Epoch Report Failed",
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