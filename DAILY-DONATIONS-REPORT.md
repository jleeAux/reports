# Daily Donations Report Documentation

**Created**: August 7, 2025  
**Version**: 1.0.0  
**Service**: TheAuxilia Report Service

## Overview

The Daily Donations Report is an automated report that generates and emails a formatted summary of all donations received on the previous day. The report groups donations by source/campaign, provides subtotals for each source, and includes a grand total at the bottom.

## Report Formats

### Excel Format (Primary)
The report is now generated as an Excel file (.xlsx) with professional formatting and two worksheets:

#### Main Worksheet - "Daily Donations"
- **Headers**: Source/Campaign, First Name, Last Name, Address, City, State, Zip, Email, Date, Gift Amount, Payment Method
- **Formatting**:
  - Steel blue header row with white text
  - Gray source/campaign group headers
  - Alternating white/light gray rows for readability
  - Yellow highlighted subtotals for each source
  - Green highlighted grand total
  - Currency formatting for amounts
  - Date formatting (MM/DD/YYYY)
- **Features**:
  - Auto-filters on all columns
  - Frozen header row
  - Auto-fitted column widths
  - Print-ready landscape layout

#### Summary Worksheet
- **Source Breakdown**: Count, Total Amount, Average per source
- **Statistics**: Largest, Smallest, Average, Median donation amounts
- **Professional formatting with color coding

### Text Format (Legacy)
```
DAILY DONATIONS REPORT
Date: 08/06/2025
========================================================================================================================

Source                              First Name      Last Name            Address                        
City                 State  Zip/Postal   Email                          Date        Gift Amount   Payment Method  
------------------------------------------------------------------------------------------------------------------------

000-No Solicitation
--------------------------------------------------------------------------------
               Neumann Family   Foundation          128 W Wisconsin Ave Unit 502   
               Oconomowoc       WI     53066-5234                                  08/06/2025    $5,000.00     Check
    
                                WELS                N16W23377 Stone Ridge Dr       
               Waukesha         WI     53188-1108                                  08/06/2025    $100.00       ACH - EFT
    
                                                                                    Total: $5,100.00

907-July Appeal
--------------------------------------------------------------------------------
               Carrie           Schoenwetter        2770 S Amor Dr                 
               New Berlin       WI     53146-2303                                  08/06/2025    $50.00        Check
    
                                                                                    Total: $50.00

========================================================================================================================
                                                                               Grand Total: $5,150.00
```

**Note**: DonorID field has been removed from reports as of August 2025 update

## Components

### 1. Database Stored Procedure
**Location**: Azure SQL Data Warehouse (dwtheauxprod)  
**Procedure**: `pDonationsPriorDay`

#### Stored Procedure (`pDonationsPriorDay`)
- Returns previous day's donations for a specific client
- Hardcoded client ID: `bd900e63-dd0c-424b-85fd-5b436d0ea7b7`
- Limited to 10,000 records
- Grouping and subtotals handled by C# code

### 2. Report Service
**Location**: `/srv/reports/TheAuxilia.ReportService/`  
**Classes**: 
- `DailyDonationsReport.cs` - Text format generator (legacy)
- `DailyDonationsReportExcel.cs` - Excel format generator (primary)

#### Features
- Generates Excel workbook with multiple sheets
- Groups donations by source/campaign
- Calculates subtotals per source
- Calculates grand total
- Professional Excel formatting with colors and styles
- Sends email with Excel attachment via SendGrid
- Saves report to disk

### 3. Configuration
**File**: `/srv/reports/TheAuxilia.ReportService/appsettings.json`

```json
"DailyDonationsReport": {
    "Enabled": true,
    "Schedule": "0 0 5 * * ?",  // Daily at 5 AM
    "Recipients": [
        {
            "Email": "jlee@theauxilia.com",
            "Name": "J Lee"
        },
        {
            "Email": "donations@theauxilia.com",
            "Name": "Donations Team"
        }
    ],
    "IncludeAttachment": true,
    "GroupBySource": true,
    "ShowSubtotals": true,
    "ShowGrandTotal": true
}
```

## Schedule

### Automatic Execution
- **Schedule**: Daily at 5:00 AM EST ✅ ACTIVE
- **Service**: `theauxilia-reports.service`
- **Job Class**: `DailyDonationsExcelJob`
- **Cron Expression**: `0 0 5 * * ?`
- **Status**: Scheduled and running automatically since August 7, 2025

### Manual Execution

#### Run Actual Report
```bash
/srv/reports/run_daily_donations_report.sh --now
```

#### Generate Test Report
```bash
/srv/reports/run_daily_donations_report.sh --test
```

#### Service Commands
```bash
# Check service status
sudo systemctl status theauxilia-reports

# View logs
sudo journalctl -u theauxilia-reports -f

# Restart service
sudo systemctl restart theauxilia-reports
```

## Report Fields

| Field | Description | Source |
|-------|-------------|---------|
| Source | Campaign/Appeal code | eventdonations.SourceValue |
| First Name | Donor's first name | ClientDonor.FirstName |
| Last Name | Donor's last name | ClientDonor.LastName |
| Address | Street address | ClientDonor.Street1 |
| City | City | ClientDonor.City |
| State | State abbreviation | ClientDonor.State |
| Zip/Postal | Postal code | ClientDonor.PostCode |
| Email | Email used for donation | eventdonations.EmailUsedForDonation |
| Date | Donation date | eventdonations.CreatedOn |
| Gift Amount | Donation amount | eventdonations.DonationAmmount |
| Payment Method | Payment type | Mapped from DonationMethod |

**Note**: DonorID field removed in August 2025 update

### Payment Method Mapping
- 0 = Credit Card
- 1 = ACH - EFT
- 2 = Check
- 3 = Cash

## Output

### File Location
- **Directory**: `/srv/reports/output/`
- **Formats**: 
  - `DailyDonations_YYYYMMDD.xlsx` (Excel - primary)
  - `DailyDonations_YYYYMMDD.txt` (Text - legacy)
- **Retention**: 30 days (auto-cleanup)

### Email Delivery
- **From**: jlee@theauxilia.com
- **Subject**: Daily Donations Report - MM/DD/YYYY
- **Format**: HTML body with Excel attachment (.xlsx)
- **Recipients**: Configured in appsettings.json:
  - jlee@theauxilia.com
  - jjacobsen@theauxilia.com
- **Attachment Type**: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet

## Monitoring

### Log Files
```bash
# Report service logs
/srv/logs/reports/report-service-YYYYMMDD.log

# Daily donations specific logs
/srv/logs/reports/daily-donations-YYYYMMDD.log
```

### Health Checks
```bash
# Check if reports are being generated
ls -la /srv/reports/output/DailyDonations_*.txt

# Check last run time
grep "Daily Donations Report" /srv/logs/reports/report-service-*.log | tail -5
```

## Troubleshooting

### Common Issues

#### No Data in Report
- **Cause**: No donations for the specified date
- **Solution**: Check eventdonations table for data on report date

#### Email Not Sent
- **Cause**: SendGrid API issues or invalid recipients
- **Solution**: Check SendGrid API key and recipient email addresses

#### Service Not Running
```bash
# Check service status
systemctl status theauxilia-reports

# Start service if stopped
sudo systemctl start theauxilia-reports

# Enable auto-start on boot
sudo systemctl enable theauxilia-reports
```

#### Permission Issues
```bash
# Fix output directory permissions
sudo chown -R www-data:www-data /srv/reports/output
sudo chmod 755 /srv/reports/output
```

## Database Testing

To test the stored procedure in Azure SQL DW:

```sql
-- Connect to dwtheauxprod database
-- Test the procedure
EXEC pDonationsPriorDay;
```

## Future Enhancements

1. **Multi-Client Support**: Generate separate reports for each client
2. **Custom Date Ranges**: Support weekly/monthly summaries
3. **Excel Format**: Add XLSX export option
4. **Dashboard Integration**: Display report metrics in admin portal
5. **Donation Trends**: Include comparison with previous periods
6. **Donor Analytics**: Add donor retention metrics

## Support

For issues or modifications to the Daily Donations Report:
1. Check logs in `/srv/logs/reports/`
2. Verify database connectivity to Azure SQL DW
3. Confirm SendGrid API key is valid
4. Review recipient email configuration

---

**Note**: This report uses the production Data Warehouse database. Ensure proper security and access controls are maintained.