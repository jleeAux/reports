#!/bin/bash

echo "TheAuxilia Report Service Status"
echo "================================"
echo ""

# Service status
echo "Service Status:"
sudo systemctl status theauxilia-reports --no-pager | head -15
echo ""

# Last execution
echo "Last Generated Reports:"
ls -lt /srv/reports/output/*.csv 2>/dev/null | head -5
echo ""

# Schedule info
echo "Schedule Information:"
echo "- Cron Expression: 0 0 4 * * ? (Daily at 4:00 AM)"
echo "- Timezone: America/New_York (EST/EDT)"
echo "- Next Run: Tomorrow at 4:00 AM EST"
echo ""

# Recipients
echo "Email Recipients:"
echo "- jlee@theauxilia.com"
echo "- jjacobsen@theauxilia.com"
echo ""

# Logs
echo "Recent Logs:"
sudo journalctl -u theauxilia-reports -n 10 --no-pager