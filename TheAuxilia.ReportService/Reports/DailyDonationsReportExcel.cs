using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using SendGrid;
using SendGrid.Helpers.Mail;
using TheAuxilia.ReportService.Models;

namespace TheAuxilia.ReportService.Reports;

public class DailyDonationsReportExcel : IReport
{
    private readonly ILogger<DailyDonationsReportExcel> _logger;
    private readonly string _connectionString;
    private readonly ISendGridClient _sendGridClient;
    private readonly ReportConfiguration _config;
    private readonly bool _testMode;
    private readonly string _testRecipient;
    private readonly bool _addTestBanner;

    static DailyDonationsReportExcel()
    {
        // EPPlus requires this for non-commercial use
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    public DailyDonationsReportExcel(
        ILogger<DailyDonationsReportExcel> logger,
        string connectionString,
        ISendGridClient sendGridClient,
        ReportConfiguration config,
        IConfiguration configuration = null)
    {
        _logger = logger;
        _connectionString = connectionString;
        _sendGridClient = sendGridClient;
        _config = config;
        
        // Read test mode settings from configuration if provided
        if (configuration != null)
        {
            _testMode = configuration.GetValue<bool>("TestMode:Enabled", false);
            _testRecipient = configuration["TestMode:TestRecipient"] ?? "jlee@theauxilia.com";
            _addTestBanner = configuration.GetValue<bool>("TestMode:AddTestBanner", true);
        }
        else
        {
            _testMode = false;
            _testRecipient = "jlee@theauxilia.com";
            _addTestBanner = true;
        }
        
        if (_testMode)
        {
            _logger.LogWarning("Daily Donations Report in TEST MODE - Emails will only go to {TestRecipient}", _testRecipient);
        }
    }

    public string Name => "Daily Donations Report (Excel)";
    public string Schedule => "0 0 5 * * ?"; // Daily at 5 AM

    /// <summary>
    /// Cleans a field value to ensure it doesn't break CSV/Excel formatting
    /// Handles commas, quotes, newlines, and other special characters
    /// </summary>
    private string CleanCsvField(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Trim whitespace
        value = value.Trim();

        // Remove any control characters except tabs and newlines
        value = System.Text.RegularExpressions.Regex.Replace(value, @"[\x00-\x08\x0B\x0C\x0E-\x1F]", "");

        // For Excel, we don't need to escape commas within cells as EPPlus handles this
        // But we should handle line breaks to prevent cell overflow
        value = value.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");

        // Limit field length to prevent Excel issues
        if (value.Length > 32767) // Excel cell character limit
            value = value.Substring(0, 32764) + "...";

        return value;
    }

    public async Task<ReportResult> GenerateAsync()
    {
        try
        {
            _logger.LogInformation("Starting Daily Donations Report (Excel) generation");

            // Get donation data from the Data Warehouse
            var donations = await GetDonationDataAsync();
            
            // Generate Excel report
            var excelBytes = GenerateExcelReport(donations);
            
            // Save report to file
            var fileName = $"DailyDonations_{DateTime.Now:yyyyMMdd}.xlsx";
            var filePath = Path.Combine(_config.OutputDirectory, fileName);
            await File.WriteAllBytesAsync(filePath, excelBytes);
            
            _logger.LogInformation($"Excel report saved to {filePath}");

            // Generate HTML for email body
            var htmlReport = GenerateHtmlSummary(donations);

            // Send email report with Excel attachment
            await SendEmailReportAsync(excelBytes, htmlReport, fileName);

            return new ReportResult
            {
                Success = true,
                ReportName = Name,
                GeneratedAt = DateTime.UtcNow,
                FilePath = filePath,
                RecordCount = donations.Count,
                Message = $"Successfully generated Excel report with {donations.Count} donations"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Daily Donations Report (Excel)");
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
                Source = CleanCsvField(reader["Source"]?.ToString() ?? "000-No Solicitation"),
                DonorId = "", // DonorID no longer returned by stored procedure
                FirstName = CleanCsvField(reader["FirstName"]?.ToString() ?? ""),
                LastName = CleanCsvField(reader["LastName"]?.ToString() ?? ""),
                Address = CleanCsvField(reader["Address"]?.ToString() ?? ""),
                City = CleanCsvField(reader["City"]?.ToString() ?? ""),
                State = CleanCsvField(reader["State"]?.ToString() ?? ""),
                ZipCode = CleanCsvField(reader["PostCode"]?.ToString() ?? ""),
                Email = CleanCsvField(reader["Email"]?.ToString() ?? ""),
                DonationDate = Convert.ToDateTime(reader["theDate"]),
                Amount = Convert.ToDecimal(reader["Amount"]),
                PaymentMethod = CleanCsvField(reader["Donation Method"]?.ToString() ?? "Unknown")
            });
        }
        
        return donations;
    }

    private byte[] GenerateExcelReport(List<DonationRecord> donations)
    {
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Daily Donations");
        
        var reportDate = DateTime.Now.AddDays(-1).ToString("MM/dd/yyyy");
        
        // Title
        worksheet.Cells["A1"].Value = "DAILY DONATIONS REPORT";
        worksheet.Cells["A1:K1"].Merge = true;
        worksheet.Cells["A1"].Style.Font.Size = 16;
        worksheet.Cells["A1"].Style.Font.Bold = true;
        worksheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        
        worksheet.Cells["A2"].Value = $"Date: {reportDate}";
        worksheet.Cells["A2:K2"].Merge = true;
        worksheet.Cells["A2"].Style.Font.Size = 12;
        worksheet.Cells["A2"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        
        // Headers
        var headerRow = 4;
        worksheet.Cells[headerRow, 1].Value = "Source/Campaign";
        worksheet.Cells[headerRow, 2].Value = "First Name";
        worksheet.Cells[headerRow, 3].Value = "Last Name";
        worksheet.Cells[headerRow, 4].Value = "Address";
        worksheet.Cells[headerRow, 5].Value = "City";
        worksheet.Cells[headerRow, 6].Value = "State";
        worksheet.Cells[headerRow, 7].Value = "Zip/Postal";
        worksheet.Cells[headerRow, 8].Value = "Email";
        worksheet.Cells[headerRow, 9].Value = "Date";
        worksheet.Cells[headerRow, 10].Value = "Gift Amount";
        worksheet.Cells[headerRow, 11].Value = "Payment Method";
        
        // Style headers
        using (var range = worksheet.Cells[headerRow, 1, headerRow, 11])
        {
            range.Style.Font.Bold = true;
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(70, 130, 180)); // Steel Blue
            range.Style.Font.Color.SetColor(Color.White);
            range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thick;
        }
        
        // Group donations by source
        var groupedDonations = donations
            .GroupBy(d => d.Source)
            .OrderBy(g => g.Key);
        
        var currentRow = headerRow + 1;
        decimal grandTotal = 0;
        int totalDonations = 0;
        
        foreach (var sourceGroup in groupedDonations)
        {
            // Source header
            worksheet.Cells[currentRow, 1].Value = sourceGroup.Key;
            worksheet.Cells[currentRow, 1, currentRow, 11].Merge = true;
            worksheet.Cells[currentRow, 1].Style.Font.Bold = true;
            worksheet.Cells[currentRow, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            worksheet.Cells[currentRow, 1].Style.Fill.BackgroundColor.SetColor(Color.LightGray);
            worksheet.Cells[currentRow, 1].Style.Border.Top.Style = ExcelBorderStyle.Thin;
            worksheet.Cells[currentRow, 1].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            currentRow++;
            
            decimal sourceTotal = 0;
            var sourceStartRow = currentRow;
            
            foreach (var donation in sourceGroup.OrderBy(d => d.LastName).ThenBy(d => d.FirstName))
            {
                worksheet.Cells[currentRow, 1].Value = ""; // Empty for source column in detail rows
                worksheet.Cells[currentRow, 2].Value = donation.FirstName;
                worksheet.Cells[currentRow, 3].Value = donation.LastName;
                worksheet.Cells[currentRow, 4].Value = donation.Address;
                worksheet.Cells[currentRow, 5].Value = donation.City;
                worksheet.Cells[currentRow, 6].Value = donation.State;
                worksheet.Cells[currentRow, 7].Value = donation.ZipCode;
                worksheet.Cells[currentRow, 8].Value = donation.Email;
                worksheet.Cells[currentRow, 9].Value = donation.DonationDate;
                worksheet.Cells[currentRow, 9].Style.Numberformat.Format = "MM/dd/yyyy";
                worksheet.Cells[currentRow, 10].Value = donation.Amount;
                worksheet.Cells[currentRow, 10].Style.Numberformat.Format = "$#,##0.00";
                worksheet.Cells[currentRow, 11].Value = donation.PaymentMethod;
                
                // Alternate row coloring
                if ((currentRow - sourceStartRow) % 2 == 1)
                {
                    worksheet.Cells[currentRow, 1, currentRow, 11].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[currentRow, 1, currentRow, 11].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(240, 240, 240));
                }
                
                sourceTotal += donation.Amount;
                totalDonations++;
                currentRow++;
            }
            
            // Source subtotal
            worksheet.Cells[currentRow, 9].Value = "Subtotal:";
            worksheet.Cells[currentRow, 9].Style.Font.Bold = true;
            worksheet.Cells[currentRow, 9].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
            worksheet.Cells[currentRow, 10].Value = sourceTotal;
            worksheet.Cells[currentRow, 10].Style.Numberformat.Format = "$#,##0.00";
            worksheet.Cells[currentRow, 10].Style.Font.Bold = true;
            worksheet.Cells[currentRow, 9, currentRow, 10].Style.Fill.PatternType = ExcelFillStyle.Solid;
            worksheet.Cells[currentRow, 9, currentRow, 10].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 255, 200));
            worksheet.Cells[currentRow, 9, currentRow, 10].Style.Border.Top.Style = ExcelBorderStyle.Thin;
            worksheet.Cells[currentRow, 9, currentRow, 10].Style.Border.Bottom.Style = ExcelBorderStyle.Double;
            
            grandTotal += sourceTotal;
            currentRow += 2; // Add blank row after subtotal
        }
        
        // Grand total
        currentRow++;
        worksheet.Cells[currentRow, 9].Value = "GRAND TOTAL:";
        worksheet.Cells[currentRow, 9].Style.Font.Bold = true;
        worksheet.Cells[currentRow, 9].Style.Font.Size = 12;
        worksheet.Cells[currentRow, 9].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
        worksheet.Cells[currentRow, 10].Value = grandTotal;
        worksheet.Cells[currentRow, 10].Style.Numberformat.Format = "$#,##0.00";
        worksheet.Cells[currentRow, 10].Style.Font.Bold = true;
        worksheet.Cells[currentRow, 10].Style.Font.Size = 12;
        worksheet.Cells[currentRow, 9, currentRow, 10].Style.Fill.PatternType = ExcelFillStyle.Solid;
        worksheet.Cells[currentRow, 9, currentRow, 10].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(200, 255, 200));
        worksheet.Cells[currentRow, 9, currentRow, 10].Style.Border.Top.Style = ExcelBorderStyle.Double;
        worksheet.Cells[currentRow, 9, currentRow, 10].Style.Border.Bottom.Style = ExcelBorderStyle.Double;
        
        // Summary statistics
        currentRow += 2;
        worksheet.Cells[currentRow, 1].Value = $"Total Donations: {totalDonations}";
        worksheet.Cells[currentRow, 1].Style.Font.Bold = true;
        currentRow++;
        worksheet.Cells[currentRow, 1].Value = $"Number of Sources: {groupedDonations.Count()}";
        worksheet.Cells[currentRow, 1].Style.Font.Bold = true;
        currentRow++;
        worksheet.Cells[currentRow, 1].Value = $"Report Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        worksheet.Cells[currentRow, 1].Style.Font.Italic = true;
        
        // Auto-fit columns
        worksheet.Cells.AutoFitColumns(10, 80);
        
        // Set minimum column widths for better readability
        worksheet.Column(1).Width = Math.Max(worksheet.Column(1).Width, 20); // Source
        worksheet.Column(5).Width = Math.Max(worksheet.Column(5).Width, 30); // Address
        worksheet.Column(9).Width = Math.Max(worksheet.Column(9).Width, 25); // Email
        
        // Add filters
        worksheet.Cells[headerRow, 1, currentRow, 11].AutoFilter = true;
        
        // Freeze panes (freeze header row)
        worksheet.View.FreezePanes(headerRow + 1, 1);
        
        // Add print settings
        worksheet.PrinterSettings.RepeatRows = worksheet.Cells[$"{headerRow}:{headerRow}"];
        worksheet.PrinterSettings.PrintArea = worksheet.Cells[1, 1, currentRow, 11];
        worksheet.PrinterSettings.FitToPage = true;
        worksheet.PrinterSettings.FitToWidth = 1;
        worksheet.PrinterSettings.FitToHeight = 0;
        worksheet.PrinterSettings.Orientation = eOrientation.Landscape;
        
        // Create a summary sheet
        var summarySheet = package.Workbook.Worksheets.Add("Summary");
        CreateSummarySheet(summarySheet, donations, groupedDonations, grandTotal);
        
        return package.GetAsByteArray();
    }

    private void CreateSummarySheet(ExcelWorksheet worksheet, List<DonationRecord> donations, 
        IOrderedEnumerable<IGrouping<string, DonationRecord>> groupedDonations, decimal grandTotal)
    {
        var reportDate = DateTime.Now.AddDays(-1).ToString("MM/dd/yyyy");
        
        // Title
        worksheet.Cells["A1"].Value = "DAILY DONATIONS SUMMARY";
        worksheet.Cells["A1:D1"].Merge = true;
        worksheet.Cells["A1"].Style.Font.Size = 16;
        worksheet.Cells["A1"].Style.Font.Bold = true;
        worksheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        
        worksheet.Cells["A2"].Value = $"Date: {reportDate}";
        worksheet.Cells["A2:D2"].Merge = true;
        worksheet.Cells["A2"].Style.Font.Size = 12;
        worksheet.Cells["A2"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        
        // Summary by Source
        var currentRow = 4;
        worksheet.Cells[currentRow, 1].Value = "Source/Campaign";
        worksheet.Cells[currentRow, 2].Value = "Count";
        worksheet.Cells[currentRow, 3].Value = "Total Amount";
        worksheet.Cells[currentRow, 4].Value = "Average";
        
        // Style headers
        using (var range = worksheet.Cells[currentRow, 1, currentRow, 4])
        {
            range.Style.Font.Bold = true;
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(70, 130, 180));
            range.Style.Font.Color.SetColor(Color.White);
            range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thick;
        }
        
        currentRow++;
        
        foreach (var sourceGroup in groupedDonations)
        {
            var sourceCount = sourceGroup.Count();
            var sourceTotal = sourceGroup.Sum(d => d.Amount);
            var sourceAverage = sourceCount > 0 ? sourceTotal / sourceCount : 0;
            
            worksheet.Cells[currentRow, 1].Value = sourceGroup.Key;
            worksheet.Cells[currentRow, 2].Value = sourceCount;
            worksheet.Cells[currentRow, 3].Value = sourceTotal;
            worksheet.Cells[currentRow, 3].Style.Numberformat.Format = "$#,##0.00";
            worksheet.Cells[currentRow, 4].Value = sourceAverage;
            worksheet.Cells[currentRow, 4].Style.Numberformat.Format = "$#,##0.00";
            
            if (currentRow % 2 == 0)
            {
                worksheet.Cells[currentRow, 1, currentRow, 4].Style.Fill.PatternType = ExcelFillStyle.Solid;
                worksheet.Cells[currentRow, 1, currentRow, 4].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(240, 240, 240));
            }
            
            currentRow++;
        }
        
        // Total row
        currentRow++;
        worksheet.Cells[currentRow, 1].Value = "TOTAL";
        worksheet.Cells[currentRow, 1].Style.Font.Bold = true;
        worksheet.Cells[currentRow, 2].Value = donations.Count;
        worksheet.Cells[currentRow, 2].Style.Font.Bold = true;
        worksheet.Cells[currentRow, 3].Value = grandTotal;
        worksheet.Cells[currentRow, 3].Style.Numberformat.Format = "$#,##0.00";
        worksheet.Cells[currentRow, 3].Style.Font.Bold = true;
        worksheet.Cells[currentRow, 4].Value = donations.Count > 0 ? grandTotal / donations.Count : 0;
        worksheet.Cells[currentRow, 4].Style.Numberformat.Format = "$#,##0.00";
        worksheet.Cells[currentRow, 4].Style.Font.Bold = true;
        
        using (var range = worksheet.Cells[currentRow, 1, currentRow, 4])
        {
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(200, 255, 200));
            range.Style.Border.Top.Style = ExcelBorderStyle.Double;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Double;
        }
        
        // Statistics
        currentRow += 2;
        worksheet.Cells[currentRow, 1].Value = "Statistics";
        worksheet.Cells[currentRow, 1].Style.Font.Bold = true;
        worksheet.Cells[currentRow, 1].Style.Font.Size = 14;
        currentRow++;
        
        worksheet.Cells[currentRow, 1].Value = "Largest Donation:";
        worksheet.Cells[currentRow, 2].Value = donations.Count > 0 ? donations.Max(d => d.Amount) : 0;
        worksheet.Cells[currentRow, 2].Style.Numberformat.Format = "$#,##0.00";
        currentRow++;
        
        worksheet.Cells[currentRow, 1].Value = "Smallest Donation:";
        worksheet.Cells[currentRow, 2].Value = donations.Count > 0 ? donations.Min(d => d.Amount) : 0;
        worksheet.Cells[currentRow, 2].Style.Numberformat.Format = "$#,##0.00";
        currentRow++;
        
        worksheet.Cells[currentRow, 1].Value = "Average Donation:";
        worksheet.Cells[currentRow, 2].Value = donations.Count > 0 ? donations.Average(d => d.Amount) : 0;
        worksheet.Cells[currentRow, 2].Style.Numberformat.Format = "$#,##0.00";
        currentRow++;
        
        worksheet.Cells[currentRow, 1].Value = "Median Donation:";
        var sortedAmounts = donations.Select(d => d.Amount).OrderBy(a => a).ToList();
        var median = sortedAmounts.Count == 0 ? 0 : sortedAmounts.Count % 2 == 0 
            ? (sortedAmounts[sortedAmounts.Count / 2 - 1] + sortedAmounts[sortedAmounts.Count / 2]) / 2
            : sortedAmounts[sortedAmounts.Count / 2];
        worksheet.Cells[currentRow, 2].Value = median;
        worksheet.Cells[currentRow, 2].Style.Numberformat.Format = "$#,##0.00";
        
        // Auto-fit columns
        worksheet.Cells.AutoFitColumns(10, 50);
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
    </style>
</head>
<body>");
        
        // Add test mode banner if enabled
        if (_testMode && _addTestBanner)
        {
            sb.AppendLine("<div style='background-color: #ffc107; color: #000; padding: 10px; text-align: center; font-weight: bold; margin-bottom: 20px;'>");
            sb.AppendLine("⚠️ TEST MODE - This is a test email. In production, this would be sent to multiple recipients.");
            sb.AppendLine("</div>");
        }
        
        sb.AppendLine($"<h1>Daily Donations Report - {reportDate}</h1>");
        sb.AppendLine($"<p>Please find attached the Daily Donations Excel Report for {reportDate}.</p>");
        
        sb.AppendLine("<div class='stats'>");
        sb.AppendLine("<h3>Report Summary</h3>");
        sb.AppendLine($"<p><strong>Total Donations:</strong> {donations.Count}</p>");
        sb.AppendLine($"<p><strong>Total Amount:</strong> ${grandTotal:N2}</p>");
        sb.AppendLine($"<p><strong>Number of Sources:</strong> {groupedDonations.Count()}</p>");
        sb.AppendLine($"<p><strong>Average Donation:</strong> ${(donations.Count > 0 ? donations.Average(d => d.Amount) : 0):N2}</p>");
        sb.AppendLine("</div>");
        
        sb.AppendLine("<h3>Summary by Source</h3>");
        sb.AppendLine("<table class='summary-table'>");
        sb.AppendLine("<thead><tr><th>Source/Campaign</th><th>Count</th><th>Total</th><th>Average</th></tr></thead>");
        sb.AppendLine("<tbody>");
        
        foreach (var sourceGroup in groupedDonations)
        {
            var count = sourceGroup.Count();
            var total = sourceGroup.Sum(d => d.Amount);
            var average = count > 0 ? total / count : 0;
            
            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>{sourceGroup.Key}</td>");
            sb.AppendLine($"<td>{count}</td>");
            sb.AppendLine($"<td>${total:N2}</td>");
            sb.AppendLine($"<td>${average:N2}</td>");
            sb.AppendLine("</tr>");
        }
        
        sb.AppendLine("<tr class='total-row'>");
        sb.AppendLine($"<td>TOTAL</td>");
        sb.AppendLine($"<td>{donations.Count}</td>");
        sb.AppendLine($"<td>${grandTotal:N2}</td>");
        sb.AppendLine($"<td>${(donations.Count > 0 ? grandTotal / donations.Count : 0):N2}</td>");
        sb.AppendLine("</tr>");
        
        sb.AppendLine("</tbody></table>");
        
        sb.AppendLine("<div class='footer'>");
        sb.AppendLine($"<p>This is an automated report from TheAuxilia Report Service.<br>");
        sb.AppendLine($"Generated at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}<br>");
        sb.AppendLine("The detailed donation list is available in the attached Excel file.</p>");
        sb.AppendLine("</div>");
        
        sb.AppendLine("</body></html>");
        
        return sb.ToString();
    }

    private async Task SendEmailReportAsync(byte[] excelBytes, string htmlReport, string fileName)
    {
        var subject = _testMode && _addTestBanner
            ? $"[TEST] Daily Donations Report - {DateTime.Now.AddDays(-1):MM/dd/yyyy}"
            : $"Daily Donations Report - {DateTime.Now.AddDays(-1):MM/dd/yyyy}";
        
        var msg = new SendGridMessage
        {
            From = new EmailAddress(_config.FromEmail, _config.FromName),
            Subject = subject
        };
        
        // Apply test mode recipient filtering
        var actualRecipients = _testMode 
            ? new List<string> { _testRecipient } 
            : _config.Recipients;
        
        if (_testMode)
        {
            _logger.LogInformation("Test mode: Overriding recipients. Original: [{Original}], Sending to: {TestRecipient}",
                string.Join(", ", _config.Recipients), _testRecipient);
        }
        
        // Add recipients
        foreach (var recipient in actualRecipients)
        {
            msg.AddTo(new EmailAddress(recipient));
            _logger.LogInformation("Added recipient: {Email}", recipient);
        }
        
        // Set HTML content
        msg.AddContent(MimeType.Html, htmlReport);
        
        // Attach Excel file
        var file = Convert.ToBase64String(excelBytes);
        msg.AddAttachment(fileName, file, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        
        // Send email
        var response = await _sendGridClient.SendEmailAsync(msg);
        
        if (response.StatusCode != System.Net.HttpStatusCode.Accepted)
        {
            var body = await response.Body.ReadAsStringAsync();
            _logger.LogError($"Failed to send email report. Status: {response.StatusCode}, Body: {body}");
            throw new Exception($"Failed to send email report: {response.StatusCode}");
        }
        
        _logger.LogInformation("Email report with Excel attachment sent successfully");
    }
}

// DonationRecord class is defined in DailyDonationsReport.cs