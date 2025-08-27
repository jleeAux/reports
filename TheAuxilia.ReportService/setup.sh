#!/bin/bash

echo "TheAuxilia Report Service Setup"
echo "==============================="

# Colors
GREEN='\033[0;32m'
RED='\033[0;31m'
NC='\033[0m'

# Create necessary directories
echo "Creating directories..."
sudo mkdir -p /srv/reports/output
sudo mkdir -p /srv/logs/reports
sudo chown -R www-data:www-data /srv/reports
sudo chown -R www-data:www-data /srv/logs/reports

# Build the project
echo "Building the project..."
dotnet build -c Release

if [ $? -ne 0 ]; then
    echo -e "${RED}Build failed${NC}"
    exit 1
fi

echo -e "${GREEN}Build successful${NC}"

# Publish the project
echo "Publishing the project..."
dotnet publish -c Release -o bin/Release/net8.0

if [ $? -ne 0 ]; then
    echo -e "${RED}Publish failed${NC}"
    exit 1
fi

# Install systemd service
echo "Installing systemd service..."
sudo cp theauxilia-reports.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable theauxilia-reports.service

echo -e "${GREEN}Service installed${NC}"

# Display status
echo ""
echo "Setup completed!"
echo ""
echo "Available commands:"
echo "  Start service:    sudo systemctl start theauxilia-reports"
echo "  Stop service:     sudo systemctl stop theauxilia-reports"
echo "  Service status:   sudo systemctl status theauxilia-reports"
echo "  View logs:        sudo journalctl -u theauxilia-reports -f"
echo "  Run now:          dotnet run -- --now"
echo ""
echo "The service will run daily at 4:00 AM EST"
echo "Recipients: jlee@theauxilia.com, jjacobsen@theauxilia.com"