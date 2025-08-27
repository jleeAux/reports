# TheAuxilia Report Service

Automated report generation and email delivery service for TheAuxilia platform.

## Quick Start

```bash
# Check status
./status.sh

# Run report manually
cd TheAuxilia.ReportService
dotnet run -- --now

# View logs
sudo journalctl -u theauxilia-reports -f
```

## Service Details

- **Schedule**: Daily at 4:00 AM EST
- **Recipients**: jlee@theauxilia.com, jjacobsen@theauxilia.com, dkubat@theauxilia.com
- **Report**: Recurring Cron Epoch Analysis from Azure SQL DW
- **Service**: theauxilia-reports (systemd)

## Documentation

See `/srv/docs/REPORT_SERVICE_DOCUMENTATION.md` for full documentation.

## File Structure

```
/srv/reports/
├── TheAuxilia.ReportService/    # .NET application
│   ├── appsettings.json        # Configuration
│   ├── *.cs                    # Source code
│   └── bin/                    # Compiled binaries
└── output/                     # Generated reports (30-day retention)
```

## Management

- Start: `sudo systemctl start theauxilia-reports`
- Stop: `sudo systemctl stop theauxilia-reports`
- Status: `sudo systemctl status theauxilia-reports`
- Logs: `sudo journalctl -u theauxilia-reports`# reports
