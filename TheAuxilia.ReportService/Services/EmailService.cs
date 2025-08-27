using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Text;

namespace TheAuxilia.ReportService.Services;

public class EmailService
{
    private readonly ISendGridClient _sendGridClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly bool _testMode;
    private readonly string _testRecipient;
    private readonly bool _addTestBanner;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        
        var apiKey = configuration["SendGrid:ApiKey"] 
            ?? throw new InvalidOperationException("SendGrid API key not found");
        
        _sendGridClient = new SendGridClient(apiKey);
        _fromEmail = configuration["SendGrid:FromEmail"] ?? "support@theauxilia.com";
        _fromName = configuration["SendGrid:FromName"] ?? "Auxilia Support Center";
        
        // Test mode configuration
        _testMode = configuration.GetValue<bool>("TestMode:Enabled", false);
        _testRecipient = configuration["TestMode:TestRecipient"] ?? "jlee@theauxilia.com";
        _addTestBanner = configuration.GetValue<bool>("TestMode:AddTestBanner", true);
        
        if (_testMode)
        {
            _logger.LogWarning("EMAIL SERVICE IN TEST MODE - All emails will be sent to {TestRecipient}", _testRecipient);
        }
    }

    public async Task SendReportEmailAsync(string csvContent, string fileName, List<EmailRecipient> recipients)
    {
        // Apply test mode filtering
        var actualRecipients = GetActualRecipients(recipients);
        
        _logger.LogInformation("Sending report email to {RecipientCount} recipients{TestMode}", 
            actualRecipients.Count, _testMode ? " (TEST MODE)" : "");
        
        try
        {
            var subject = _testMode && _addTestBanner 
                ? $"[TEST] TheAuxilia Recurring Cron Report - {DateTime.Now:yyyy-MM-dd}"
                : $"TheAuxilia Recurring Cron Report - {DateTime.Now:yyyy-MM-dd}";
            
            var msg = new SendGridMessage()
            {
                From = new EmailAddress(_fromEmail, _fromName),
                Subject = subject,
                HtmlContent = GenerateEmailHtml()
            };

            // Add recipients
            foreach (var recipient in actualRecipients)
            {
                msg.AddTo(new EmailAddress(recipient.Email, recipient.Name));
                _logger.LogInformation("Added recipient: {Email}", recipient.Email);
            }

            // Attach CSV file
            var csvBytes = Encoding.UTF8.GetBytes(csvContent);
            var csvBase64 = Convert.ToBase64String(csvBytes);
            
            msg.AddAttachment(new Attachment()
            {
                Content = csvBase64,
                Type = "text/csv",
                Filename = fileName,
                Disposition = "attachment"
            });

            var response = await _sendGridClient.SendEmailAsync(msg);
            
            if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
            {
                _logger.LogInformation("Email sent successfully. Status: {StatusCode}", response.StatusCode);
            }
            else
            {
                var body = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid returned status {StatusCode}. Response: {Response}", 
                    response.StatusCode, body);
                throw new Exception($"Failed to send email. Status: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email");
            throw;
        }
    }

    private List<EmailRecipient> GetActualRecipients(List<EmailRecipient> requestedRecipients)
    {
        if (_testMode)
        {
            _logger.LogInformation("Test mode enabled. Original recipients: {Recipients}", 
                string.Join(", ", requestedRecipients.Select(r => r.Email)));
            
            return new List<EmailRecipient> 
            { 
                new EmailRecipient { Email = _testRecipient, Name = "Test Recipient" } 
            };
        }
        
        return requestedRecipients;
    }
    
    private string GenerateEmailHtml()
    {
        var testBanner = _testMode && _addTestBanner ? @"
            <div style='background-color: #ffc107; color: #000; padding: 10px; text-align: center; font-weight: bold; margin-bottom: 20px;'>
                ⚠️ TEST MODE - This is a test email. In production, this would be sent to multiple recipients.
            </div>" : "";
        
        return $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #2c3e50; color: white; padding: 20px; text-align: center; border-radius: 5px 5px 0 0; }}
        .content {{ background-color: #f8f9fa; padding: 30px; border: 1px solid #dee2e6; }}
        .info-box {{ background-color: white; padding: 20px; margin: 20px 0; border-radius: 5px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .footer {{ text-align: center; margin-top: 30px; font-size: 12px; color: #6c757d; }}
        ul {{ margin: 10px 0; }}
        li {{ margin: 5px 0; }}
    </style>
</head>
<body>
    <div class='container'>
        {testBanner}
        <div class='header'>
            <h1>Recurring Cron Report</h1>
            <p>{DateTime.Now:dddd, MMMM d, yyyy}</p>
        </div>
        <div class='content'>
            <h2>Daily Report Summary</h2>
            <div class='info-box'>
                <h3>Report Details:</h3>
                <ul>
                    <li><strong>Report Type:</strong> Recurring Cron Epoch Analysis</li>
                    <li><strong>Generated:</strong> {DateTime.Now:yyyy-MM-dd HH:mm:ss} EST</li>
                    <li><strong>Stored Procedure:</strong> precurringcronepoch</li>
                    <li><strong>Database:</strong> TheAuxiliaAppDW</li>
                </ul>
            </div>
            <p>Please find the detailed report data attached as a CSV file.</p>
            <p>This automated report runs daily at 4:00 AM EST and contains the latest recurring donation cron schedule analysis.</p>
            
            <div class='info-box' style='background-color: #f0f8ff;'>
                <h4>Report Contents:</h4>
                <p>The attached CSV file contains the results of the precurringcronepoch stored procedure, which analyzes recurring donation schedules and their cron expressions.</p>
            </div>
        </div>
        <div class='footer'>
            <p>This is an automated report from TheAuxilia Report Service</p>
            <p>Auxilia Support Center | support@theauxilia.com</p>
            <p style='font-size: 10px; color: #999;'>To modify this report or its schedule, please contact your system administrator.</p>
        </div>
    </div>
</body>
</html>";
    }
    
    public async Task SendFailureNotificationAsync(string subject, string errorMessage, string fullError)
    {
        _logger.LogInformation("Sending failure notification email{TestMode}", _testMode ? " (TEST MODE)" : "");
        
        var from = new EmailAddress(_fromEmail, _fromName);
        var to = new EmailAddress(_testMode ? _testRecipient : "jlee@theauxilia.com", "J Lee");
        
        var htmlContent = GenerateFailureEmailHtml(subject, errorMessage, fullError);
        
        var alertSubject = _testMode && _addTestBanner 
            ? $"[TEST] [ALERT] {subject}"
            : $"[ALERT] {subject}";
        
        var msg = MailHelper.CreateSingleEmail(
            from, 
            to, 
            alertSubject, 
            $"Report/Cron Job Failed: {errorMessage}", 
            htmlContent);
        
        msg.SetReplyTo(new EmailAddress(_fromEmail));
        
        var response = await _sendGridClient.SendEmailAsync(msg);
        
        if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
        {
            _logger.LogInformation("Failure notification sent successfully to jlee@theauxilia.com");
        }
        else
        {
            var body = await response.Body.ReadAsStringAsync();
            _logger.LogError("Failed to send failure notification. Status: {Status}, Body: {Body}", 
                response.StatusCode, body);
        }
    }
    
    private string GenerateFailureEmailHtml(string subject, string errorMessage, string fullError)
    {
        var testBanner = _testMode && _addTestBanner ? @"
            <div style='background-color: #ffc107; color: #000; padding: 10px; text-align: center; font-weight: bold;'>
                ⚠️ TEST MODE - This is a test alert. In production, this would be sent to the configured alert recipients.
            </div>" : "";
        
        return $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #dc3545; color: white; padding: 20px; text-align: center; border-radius: 5px 5px 0 0; }}
        .content {{ background-color: #f8f9fa; padding: 30px; border: 1px solid #dee2e6; }}
        .error-box {{ background-color: #fff5f5; padding: 20px; margin: 20px 0; border-radius: 5px; border-left: 4px solid #dc3545; }}
        .details {{ background-color: white; padding: 15px; margin: 20px 0; border-radius: 5px; font-family: monospace; font-size: 12px; white-space: pre-wrap; word-wrap: break-word; max-height: 400px; overflow-y: auto; }}
        .footer {{ text-align: center; margin-top: 30px; font-size: 12px; color: #6c757d; }}
        .timestamp {{ font-size: 14px; color: #6c757d; margin-top: 10px; }}
    </style>
</head>
<body>
    <div class='container'>
        {testBanner}
        <div class='header'>
            <h1>⚠️ SYSTEM ALERT</h1>
            <p>{subject}</p>
        </div>
        <div class='content'>
            <div class='error-box'>
                <h3>Error Summary:</h3>
                <p><strong>{errorMessage}</strong></p>
                <div class='timestamp'>
                    <strong>Time:</strong> {DateTime.Now:yyyy-MM-dd HH:mm:ss} UTC<br>
                    <strong>Server:</strong> {Environment.MachineName}
                </div>
            </div>
            
            <h3>Full Error Details:</h3>
            <div class='details'>{System.Web.HttpUtility.HtmlEncode(fullError)}</div>
            
            <div class='footer'>
                <p>This is an automated alert from TheAuxilia Report Service.<br>
                Please investigate and resolve the issue promptly.</p>
            </div>
        </div>
    </div>
</body>
</html>";
    }
}

public class EmailRecipient
{
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}