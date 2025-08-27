using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;
using TheAuxilia.ReportService.Models;

namespace TheAuxilia.ReportService.Reports;

public class DailyDonationsReportCsv : IReport
{
    private readonly ILogger<DailyDonationsReportCsv> _logger;
    private readonly string _connectionString;
    private readonly ISendGridClient _sendGridClient;
    private readonly ReportConfiguration _config;

    public DailyDonationsReportCsv(
        ILogger<DailyDonationsReportCsv> logger,
        string connectionString,
        ISendGridClient sendGridClient,
        ReportConfiguration config)
    {
        _logger = logger;
        _connectionString = connectionString;
        _sendGridClient = sendGridClient;
        _config = config;
    }

    public string Name => "Daily Donations Report (CSV)";
    public string Schedule => "0 0 5 * * ?"; // Daily at 5 AM

    public async Task<ReportResult> GenerateAsync()
    {
        try
        {
            _logger.LogInformation("Starting Daily Donations Report (CSV) generation");

            // Get donation data from the Data Warehouse
            var donations = await GetDonationDataAsync();
            
            // Generate CSV report
            var csvContent = GenerateCsvReport(donations);
            
            // Save report to file
            var fileName = $"DailyDonations_{DateTime.Now:yyyyMMdd}.csv";
            var filePath = Path.Combine(_config.OutputDirectory, fileName);
            await File.WriteAllTextAsync(filePath, csvContent);
            
            _logger.LogInformation($"CSV report saved to {filePath}");

            // Generate HTML for email body
            var htmlReport = GenerateHtmlSummary(donations);

            // Send email report with CSV attachment
            await SendEmailReportAsync(csvContent, htmlReport, fileName);

            return new ReportResult
            {
                Success = true,
                ReportName = Name,
                GeneratedAt = DateTime.UtcNow,
                FilePath = filePath,
                RecordCount = donations.Count,
                Message = $"Successfully generated CSV report with {donations.Count} donations"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Daily Donations Report (CSV)");
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
                DonorId = "", // DonorID no longer returned by stored procedure
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

    private string GenerateCsvReport(List<DonationRecord> donations)
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        
        // Configure CSV writer to properly handle commas and quotes
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Quote = '"',
            ShouldQuote = (field) => field.Field?.Contains(',') == true || 
                                     field.Field?.Contains('"') == true || 
                                     field.Field?.Contains('\n') == true ||
                                     field.Field?.Contains('\r') == true,
            TrimOptions = TrimOptions.Trim
        };
        
        using var csv = new CsvWriter(writer, config);
        
        // Write custom header with report title and date
        writer.WriteLine($"# DAILY DONATIONS REPORT - {DateTime.Now.AddDays(-1):MM/dd/yyyy}");
        writer.WriteLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        writer.WriteLine($"# Total Donations: {donations.Count}");
        writer.WriteLine($"# Total Amount: ${donations.Sum(d => d.Amount):N2}");
        writer.WriteLine();
        
        // Write column headers
        csv.WriteField("Source/Campaign");
        csv.WriteField("First Name");
        csv.WriteField("Last Name");
        csv.WriteField("Address");
        csv.WriteField("City");
        csv.WriteField("State");
        csv.WriteField("Zip/Postal");
        csv.WriteField("Email");
        csv.WriteField("Date");
        csv.WriteField("Gift Amount");
        csv.WriteField("Payment Method");
        csv.NextRecord();
        
        // Group donations by source
        var groupedDonations = donations
            .GroupBy(d => d.Source)
            .OrderBy(g => g.Key);
        
        decimal grandTotal = 0;
        
        foreach (var sourceGroup in groupedDonations)
        {
            decimal sourceTotal = 0;
            
            // Write source group header
            csv.WriteField($"=== {sourceGroup.Key} ===");
            for (int i = 1; i < 11; i++) csv.WriteField("");
            csv.NextRecord();
            
            // Write donations for this source
            foreach (var donation in sourceGroup.OrderBy(d => d.LastName).ThenBy(d => d.FirstName))
            {
                csv.WriteField(donation.Source);
                csv.WriteField(donation.FirstName);
                csv.WriteField(donation.LastName);
                csv.WriteField(donation.Address);
                csv.WriteField(donation.City);
                csv.WriteField(donation.State);
                csv.WriteField(donation.ZipCode);
                csv.WriteField(donation.Email);
                csv.WriteField(donation.DonationDate.ToString("MM/dd/yyyy"));
                csv.WriteField(donation.Amount.ToString("F2"));
                csv.WriteField(donation.PaymentMethod);
                csv.NextRecord();
                
                sourceTotal += donation.Amount;
            }
            
            // Write source subtotal
            csv.WriteField("");
            csv.WriteField("");
            csv.WriteField("");
            csv.WriteField("");
            csv.WriteField("");
            csv.WriteField("");
            csv.WriteField("");
            csv.WriteField("");
            csv.WriteField("Subtotal:");
            csv.WriteField(sourceTotal.ToString("F2"));
            csv.WriteField("");
            csv.NextRecord();
            
            // Blank row between sources
            csv.NextRecord();
            
            grandTotal += sourceTotal;
        }
        
        // Write grand total
        csv.WriteField("");
        csv.WriteField("");
        csv.WriteField("");
        csv.WriteField("");
        csv.WriteField("");
        csv.WriteField("");
        csv.WriteField("");
        csv.WriteField("");
        csv.WriteField("GRAND TOTAL:");
        csv.WriteField(grandTotal.ToString("F2"));
        csv.WriteField("");
        csv.NextRecord();
        
        return sb.ToString();
    }

    private string GenerateHtmlSummary(List<DonationRecord> donations)
    {
        var sb = new StringBuilder();
        var reportDate = DateTime.Now.AddDays(-1).ToString("MM/dd/yyyy");
        var groupedDonations = donations.GroupBy(d => d.Source).OrderBy(g => g.Key);
        var grandTotal = donations.Sum(d => d.Amount);
        
        sb.AppendLine(@"<!DOCTYPE html>
<html>
<head>
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; color: #333; }
        h1 { color: #4682B4; }
        .summary-table { border-collapse: collapse; width: 100%; max-width: 600px; margin: 20px 0; }
        .summary-table th, .summary-table td { text-align: left; padding: 10px; border: 1px solid #ddd; }
        .summary-table th { background-color: #4682B4; color: white; }
        .summary-table tr:nth-child(even) { background-color: #f9f9f9; }
        .total-row { font-weight: bold; background-color: #e8f5e9 !important; }
        .stats { margin: 20px 0; padding: 15px; background-color: #f5f5f5; border-radius: 5px; }
        .stats h3 { margin-top: 0; color: #4682B4; }
        .footer { margin-top: 30px; padding-top: 20px; border-top: 1px solid #ddd; font-size: 12px; color: #666; }
        .warning { background-color: #fff3cd; border: 1px solid #ffc107; padding: 10px; margin: 10px 0; border-radius: 5px; }
    </style>
</head>
<body>");
        
        sb.AppendLine($"<h1>Daily Donations Report - {reportDate}</h1>");
        sb.AppendLine($"<p>Please find attached the Daily Donations CSV Report for {reportDate}.</p>");
        
        sb.AppendLine("<div class='warning'>");
        sb.AppendLine("<strong>Note:</strong> This CSV file properly handles special characters including commas, quotes, and line breaks in all fields.");
        sb.AppendLine("</div>");
        
        sb.AppendLine("<div class='stats'>");
        sb.AppendLine("<h3>Report Summary</h3>");
        sb.AppendLine($"<p><strong>Total Donations:</strong> {donations.Count}</p>");
        sb.AppendLine($"<p><strong>Total Amount:</strong> ${grandTotal:N2}</p>");
        sb.AppendLine($"<p><strong>Number of Sources:</strong> {groupedDonations.Count()}</p>");
        sb.AppendLine($"<p><strong>Average Donation:</strong> ${donations.Average(d => d.Amount):N2}</p>");
        sb.AppendLine("</div>");
        
        sb.AppendLine("<div class='footer'>");
        sb.AppendLine($"<p>This is an automated report from TheAuxilia Report Service.<br>");
        sb.AppendLine($"Generated at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}<br>");
        sb.AppendLine("The detailed donation list is available in the attached CSV file.</p>");
        sb.AppendLine("</div>");
        
        sb.AppendLine("</body></html>");
        
        return sb.ToString();
    }

    private async Task SendEmailReportAsync(string csvContent, string htmlReport, string fileName)
    {
        var subject = $"Daily Donations Report (CSV) - {DateTime.Now.AddDays(-1):MM/dd/yyyy}";
        
        var msg = new SendGridMessage
        {
            From = new EmailAddress(_config.FromEmail, _config.FromName),
            Subject = subject
        };
        
        // Add recipients
        foreach (var recipient in _config.Recipients)
        {
            msg.AddTo(new EmailAddress(recipient));
            _logger.LogInformation("Added recipient: {Email}", recipient);
        }
        
        // Set HTML content
        msg.AddContent(MimeType.Html, htmlReport);
        
        // Attach CSV file
        var csvBytes = Encoding.UTF8.GetBytes(csvContent);
        var file = Convert.ToBase64String(csvBytes);
        msg.AddAttachment(fileName, file, "text/csv");
        
        // Send email
        var response = await _sendGridClient.SendEmailAsync(msg);
        
        if (response.StatusCode != System.Net.HttpStatusCode.Accepted)
        {
            var body = await response.Body.ReadAsStringAsync();
            _logger.LogError($"Failed to send email report. Status: {response.StatusCode}, Body: {body}");
            throw new Exception($"Failed to send email report: {response.StatusCode}");
        }
        
        _logger.LogInformation("Email report with CSV attachment sent successfully");
    }
}