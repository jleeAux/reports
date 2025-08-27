#!/bin/bash

# Daily Donations Report Runner
# This script generates and emails the daily donations report

echo "========================================="
echo "Daily Donations Report Generator"
echo "========================================="
echo ""

# Set working directory
cd /srv/reports/TheAuxilia.ReportService

# Check if we should run immediately or use test data
if [ "$1" == "--now" ]; then
    echo "Running Daily Donations Excel Report immediately..."
    dotnet run --project TheAuxilia.ReportService.csproj -- --excel
elif [ "$1" == "--test" ]; then
    echo "Generating test report with sample data..."
    # Create a test report with formatted output
    
    REPORT_DATE=$(date -d "yesterday" +"%m/%d/%Y")
    OUTPUT_DIR="/srv/reports/output"
    mkdir -p $OUTPUT_DIR
    
    REPORT_FILE="$OUTPUT_DIR/DailyDonations_$(date +%Y%m%d).txt"
    
    cat > $REPORT_FILE << EOF
DAILY DONATIONS REPORT
Date: $REPORT_DATE
========================================================================================================================

Source                              Donor ID   First Name      Last Name            Address                        
City                 State  Zip/Postal   Email                          Date        Gift Amount   Payment Method  
------------------------------------------------------------------------------------------------------------------------

000-No Solicitation
--------------------------------------------------------------------------------
    70579      Neumann Family   Foundation          128 W Wisconsin Ave Unit 502   
               Oconomowoc       WI     53066-5234                                  $REPORT_DATE    \$5,000.00     Check
    
    37743                       WELS                N16W23377 Stone Ridge Dr       
               Waukesha         WI     53188-1108                                  $REPORT_DATE    \$100.00       ACH - EFT
    
                                                                                    Total: \$5,100.00

906-June Appeal
--------------------------------------------------------------------------------
    71924      Bruce            Kaesermann          4077 Cambria Pt                
               Rhinelander      WI     54501-8357                                  $REPORT_DATE    \$150.00       Check
    
                                                                                    Total: \$150.00

907-July Appeal
--------------------------------------------------------------------------------
    13750      Carrie           Schoenwetter        2770 S Amor Dr                 
               New Berlin       WI     53146-2303                                  $REPORT_DATE    \$50.00        Check
    
    46867      Steve            Wolfram             323 Emily Ln                   
               Beaver Dam       WI     53916-1991                                  $REPORT_DATE    \$40.00        Check
    
    46151      Suzanne          Dorst               491 Highland Ct                
               Fond Du Lac      WI     54935-4729   suedorst@att.net               $REPORT_DATE    \$50.00        Check
    
    21194      Virgil           Norder              7400 Bennington Rd             
               Laingsburg       MI     48848-9628   kanor24@gmail.com              $REPORT_DATE    \$50.00        Check
    
    51596      Elsa             Manthey             825 E Greenfield Dr Apt 107    
               Little Chute     WI     54140-1394                                  $REPORT_DATE    \$50.00        Check
    
    36695      Edward           Parduhn             PO Box 82                      
               Oakfield         WI     53065-0082                                  $REPORT_DATE    \$30.00        Check
    
                                                                                    Total: \$270.00

932-Ministry Sponsorship
--------------------------------------------------------------------------------
    13440      Andrew           George              14064 W Cornell Ave            
               Lakewood         CO     80228-5301                                  $REPORT_DATE    \$25.00        Check
    
    25208      Mary             Artz                221 Valley View Dr             
               Brillion         WI     54110-1419   marykartz@gmail.com            $REPORT_DATE    \$25.00        Check
    
    4205       William          Peters              333 S 5th St                   
               Watseka          IL     60970-1632                                  $REPORT_DATE    \$125.00       Check
    
    31166      Ray              Linnemann           359 Cambridge Dr               
               Grayslake        IL     60030-3451   rwl359@gmail.com               $REPORT_DATE    \$50.00        Check
    
    13761      Harold           Arneson             S28W29031 Carmarthen Ct        
               Waukesha         WI     53188-9508   harneson@wi.rr.com             $REPORT_DATE    \$50.00        Check
    
    13592      Barbara          Zondag              W144 County Road A             
               Randolph         WI     53956-9727   bjzondag@yahoo.com             $REPORT_DATE    \$25.00        Check
    
                                                                                    Total: \$300.00

934-Life Tribute
--------------------------------------------------------------------------------
    16453      Thomas           Schoen              1239 Chalet Rd Apt 300         
               Naperville       IL     60563-8904                                  $REPORT_DATE    \$150.00       Check
    
    25222      Steve            Wasser              1700 Peters Rd                 
               Kaukauna         WI     54130-2908   tamarawasser21@gmail.com       $REPORT_DATE    \$1,000.00     Check
    
    10217      Marilyn          Grob                69 White Oak Ct                
               Winona           MN     55987-6000   mggrob@hbci.com                $REPORT_DATE    \$250.00       Check
    
                                                                                    Total: \$1,400.00

972-Congregation Solicitation
--------------------------------------------------------------------------------
    26973      St Matthew       Lutheran Church     308 Herman St                  
               Iron Ridge       WI     53035-9517   office@stmatthewironridge.com $REPORT_DATE    \$120.00       Check
    
                                                                                    Total: \$120.00

========================================================================================================================
                                                                               Grand Total: \$7,440.00

Total Donations: 21
Report Generated: $(date '+%Y-%m-%d %H:%M:%S')
EOF

    echo "Test report generated: $REPORT_FILE"
    echo ""
    cat $REPORT_FILE
    
else
    echo "Usage: $0 [--now|--test]"
    echo "  --now   Run the actual report from database"
    echo "  --test  Generate a test report with sample data"
    echo ""
    echo "The report service is scheduled to run daily at 5:00 AM EST"
    echo "Check the service status with: systemctl status theauxilia-reports"
fi

echo ""
echo "Report output directory: /srv/reports/output/"
echo "Log files: /srv/logs/reports/"
echo "========================================="