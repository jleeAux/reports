using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;
using TheAuxilia.ReportService.Models;

namespace TheAuxilia.ReportService.Reports;

public class DailyDonationsReport : IReport
{
    private readonly ILogger<DailyDonationsReport> _logger;
    private readonly string _connectionString;
    private readonly ISendGridClient _sendGridClient;
    private readonly ReportConfiguration _config;

    public DailyDonationsReport(
        ILogger<DailyDonationsReport> logger,
        string connectionString,
        ISendGridClient sendGridClient,
        ReportConfiguration config)
    {
        _logger = logger;
        _connectionString = connectionString;
        _sendGridClient = sendGridClient;
        _config = config;
    }

    public string Name => "Daily Donations Report";
    public string Schedule => "0 0 5 * * ?"; // Daily at 5 AM

    public async Task<ReportResult> GenerateAsync()
    {
        try
        {
            _logger.LogInformation("Starting Daily Donations Report generation");

            // Get donation data from the Data Warehouse
            var donations = await GetDonationDataAsync();
            
            // Generate report in both text and HTML formats
            var textReport = GenerateTextReport(donations);
            var htmlReport = GenerateHtmlReport(donations);
            
            // Save report to file
            var fileName = $"DailyDonations_{DateTime.Now:yyyyMMdd}.txt";
            var filePath = Path.Combine(_config.OutputDirectory, fileName);
            await File.WriteAllTextAsync(filePath, textReport);
            
            _logger.LogInformation($"Report saved to {filePath}");

            // Send email report
            await SendEmailReportAsync(textReport, htmlReport, fileName);

            return new ReportResult
            {
                Success = true,
                ReportName = Name,
                GeneratedAt = DateTime.UtcNow,
                FilePath = filePath,
                RecordCount = donations.Count,
                Message = $"Successfully generated report with {donations.Count} donations"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Daily Donations Report");
            return new ReportResult
            {
                Success = false,
                ReportName = Name,
                GeneratedAt = DateTime.UtcNow,
                Message = $"Error: {ex.Message}"
            };
        }
    }

    private async Task<List<DonationRecord>> GetDonationDataAsync()
    {
        var donations = new List<DonationRecord>();
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        
        using var command = new SqlCommand("pDonationsPriorDay", connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 60
        };
        
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            donations.Add(new DonationRecord
            {
                Source = reader["Source"]?.ToString() ?? "000-No Solicitation",
                DonorId = reader["DonorID"]?.ToString() ?? "",
                FirstName = reader["FirstName"]?.ToString() ?? "",
                LastName = reader["LastName"]?.ToString() ?? "",
                Address = reader["Address"]?.ToString() ?? "",
                City = reader["City"]?.ToString() ?? "",
                State = reader["State"]?.ToString() ?? "",
                ZipCode = reader["PostCode"]?.ToString() ?? "",
                Email = reader["Email"]?.ToString() ?? "",
                DonationDate = Convert.ToDateTime(reader["theDate"]),
                Amount = Convert.ToDecimal(reader["Amount"]),
                PaymentMethod = reader["Donation Method"]?.ToString() ?? "Unknown"
            });
        }
        
        return donations;
    }

    private string GenerateTextReport(List<DonationRecord> donations)
    {
        var sb = new StringBuilder();
        var reportDate = DateTime.Now.AddDays(-1).ToString("MM/dd/yyyy");
        
        // Header
        sb.AppendLine("DAILY DONATIONS REPORT");
        sb.AppendLine($"Date: {reportDate}");
        sb.AppendLine(new string('=', 120));
        sb.AppendLine();
        sb.AppendLine($"{"Source",-35} {"Donor ID",-10} {"First Name",-15} {"Last Name",-20} {"Address",-30}");
        sb.AppendLine($"{"City",-20} {"State",-6} {"Zip/Postal",-12} {"Email",-30} {"Date",-12} {"Gift Amount",-12} {"Payment Method",-15}");
        sb.AppendLine(new string('-', 120));
        sb.AppendLine();

        // Group donations by source
        var groupedDonations = donations
            .GroupBy(d => d.Source)
            .OrderBy(g => g.Key);

        decimal grandTotal = 0;

        foreach (var sourceGroup in groupedDonations)
        {
            // Source header
            sb.AppendLine(sourceGroup.Key);
            sb.AppendLine(new string('-', 80));
            
            decimal sourceTotal = 0;
            
            foreach (var donation in sourceGroup.OrderBy(d => d.LastName).ThenBy(d => d.FirstName))
            {
                sb.AppendLine($"    {donation.DonorId,-10} {donation.FirstName,-15} {donation.LastName,-20} {donation.Address,-30}");
                sb.AppendLine($"    {"",10} {donation.City,-20} {donation.State,-6} {donation.ZipCode,-12} {donation.Email,-30}");
                sb.AppendLine($"    {"",10} {"",15} {"",20} {"",30} {donation.DonationDate:MM/dd/yyyy} ${donation.Amount,10:N2} {donation.PaymentMethod,-15}");
                sb.AppendLine();
                
                sourceTotal += donation.Amount;
            }
            
            // Source subtotal
            sb.AppendLine($"    {"",-100} Total: ${sourceTotal,10:N2}");
            sb.AppendLine();
            
            grandTotal += sourceTotal;
        }
        
        // Grand total
        sb.AppendLine(new string('=', 120));
        sb.AppendLine($"{"Grand Total:",-100} ${grandTotal,10:N2}");
        sb.AppendLine();
        sb.AppendLine($"Total Donations: {donations.Count}");
        sb.AppendLine($"Report Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        
        return sb.ToString();
    }

    private string GenerateHtmlReport(List<DonationRecord> donations)
    {
        var sb = new StringBuilder();
        var reportDate = DateTime.Now.AddDays(-1).ToString("MM/dd/yyyy");
        
        sb.AppendLine(@"<!DOCTYPE html>
<html>
<head>
    <style>
        body { font-family: 'Courier New', monospace; margin: 20px; }
        h1 { color: #333; }
        table { border-collapse: collapse; width: 100%; margin: 20px 0; }
        th, td { text-align: left; padding: 8px; border-bottom: 1px solid #ddd; }
        th { background-color: #4CAF50; color: white; }
        .source-header { background-color: #f2f2f2; font-weight: bold; }
        .subtotal { font-weight: bold; text-align: right; background-color: #f9f9f9; }
        .grand-total { font-weight: bold; font-size: 1.2em; text-align: right; background-color: #e6e6e6; }
        .donation-row:hover { background-color: #f5f5f5; }
    </style>
</head>
<body>");
        
        sb.AppendLine($"<h1>Daily Donations Report - {reportDate}</h1>");
        
        // Group donations by source
        var groupedDonations = donations
            .GroupBy(d => d.Source)
            .OrderBy(g => g.Key);
        
        decimal grandTotal = 0;
        
        sb.AppendLine("<table>");
        sb.AppendLine("<thead><tr>");
        sb.AppendLine("<th>Source</th><th>Donor ID</th><th>First Name</th><th>Last Name</th>");
        sb.AppendLine("<th>Address</th><th>City</th><th>State</th><th>Zip</th>");
        sb.AppendLine("<th>Email</th><th>Date</th><th>Amount</th><th>Payment</th>");
        sb.AppendLine("</tr></thead>");
        sb.AppendLine("<tbody>");
        
        foreach (var sourceGroup in groupedDonations)
        {
            // Source header row
            sb.AppendLine($"<tr class='source-header'><td colspan='12'>{sourceGroup.Key}</td></tr>");
            
            decimal sourceTotal = 0;
            
            foreach (var d in sourceGroup.OrderBy(d => d.LastName).ThenBy(d => d.FirstName))
            {
                sb.AppendLine("<tr class='donation-row'>");
                sb.AppendLine($"<td></td>");
                sb.AppendLine($"<td>{d.DonorId}</td>");
                sb.AppendLine($"<td>{d.FirstName}</td>");
                sb.AppendLine($"<td>{d.LastName}</td>");
                sb.AppendLine($"<td>{d.Address}</td>");
                sb.AppendLine($"<td>{d.City}</td>");
                sb.AppendLine($"<td>{d.State}</td>");
                sb.AppendLine($"<td>{d.ZipCode}</td>");
                sb.AppendLine($"<td>{d.Email}</td>");
                sb.AppendLine($"<td>{d.DonationDate:MM/dd/yyyy}</td>");
                sb.AppendLine($"<td>${d.Amount:N2}</td>");
                sb.AppendLine($"<td>{d.PaymentMethod}</td>");
                sb.AppendLine("</tr>");
                
                sourceTotal += d.Amount;
            }
            
            // Source subtotal row
            sb.AppendLine($"<tr class='subtotal'><td colspan='10'>Subtotal:</td><td>${sourceTotal:N2}</td><td></td></tr>");
            
            grandTotal += sourceTotal;
        }
        
        // Grand total row
        sb.AppendLine($"<tr class='grand-total'><td colspan='10'>GRAND TOTAL:</td><td>${grandTotal:N2}</td><td></td></tr>");
        
        sb.AppendLine("</tbody></table>");
        
        sb.AppendLine($"<p>Total Donations: {donations.Count}</p>");
        sb.AppendLine($"<p>Report Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
        
        sb.AppendLine("</body></html>");
        
        return sb.ToString();
    }

    private async Task SendEmailReportAsync(string textReport, string htmlReport, string fileName)
    {
        var msg = new SendGridMessage
        {
            From = new EmailAddress(_config.FromEmail, _config.FromName),
            Subject = $"Daily Donations Report - {DateTime.Now.AddDays(-1):MM/dd/yyyy}"
        };
        
        // Add recipients
        foreach (var recipient in _config.Recipients)
        {
            msg.AddTo(new EmailAddress(recipient));
        }
        
        // Set content
        msg.AddContent(MimeType.Text, textReport);
        msg.AddContent(MimeType.Html, htmlReport);
        
        // Attach text file
        var bytes = Encoding.UTF8.GetBytes(textReport);
        var file = Convert.ToBase64String(bytes);
        msg.AddAttachment(fileName, file, "text/plain");
        
        // Send email
        var response = await _sendGridClient.SendEmailAsync(msg);
        
        if (response.StatusCode != System.Net.HttpStatusCode.Accepted)
        {
            var body = await response.Body.ReadAsStringAsync();
            _logger.LogError($"Failed to send email report. Status: {response.StatusCode}, Body: {body}");
            throw new Exception($"Failed to send email report: {response.StatusCode}");
        }
        
        _logger.LogInformation("Email report sent successfully");
    }
}

public class DonationRecord
{
    public string Source { get; set; } = "";
    public string DonorId { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Address { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string ZipCode { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime DonationDate { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "";
}