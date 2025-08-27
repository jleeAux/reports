using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Serilog;
using TheAuxilia.ReportService.Jobs;
using TheAuxilia.ReportService.Services;

namespace TheAuxilia.ReportService;

public class Program
{
    public static async Task Main(string[] args)
    {
        // Configure Serilog
        var configuration = GetConfiguration();
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();

        try
        {
            Log.Information("Starting TheAuxilia Report Service");
            
            var host = CreateHostBuilder(args).Build();
            
            // Run immediately if --now argument is provided
            if (args.Contains("--now"))
            {
                Log.Information("Running report generation immediately");
                using (var scope = host.Services.CreateScope())
                {
                    var reportGenerator = scope.ServiceProvider.GetRequiredService<ReportGeneratorService>();
                    await reportGenerator.GenerateAndSendReportAsync();
                }
                Log.Information("Immediate report generation completed");
                return;
            }
            
            // Run Excel report if --excel argument is provided
            if (args.Contains("--excel"))
            {
                Log.Information("Running Excel report generation");
                await RunExcelReportAsync(configuration);
                Log.Information("Excel report generation completed");
                return;
            }
            
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                // Add services
                services.AddScoped<DatabaseService>();
                services.AddScoped<EmailService>();
                services.AddScoped<ReportGeneratorService>();
                
                // Configure Quartz
                services.AddQuartz(q =>
                {
                    // Configure Recurring Cron Epoch Report job
                    var jobKey = new JobKey("ReportGenerationJob");
                    q.AddJob<ReportGenerationJob>(opts => opts.WithIdentity(jobKey));
                    
                    // Configure trigger for 4 AM EST daily
                    q.AddTrigger(opts => opts
                        .ForJob(jobKey)
                        .WithIdentity("ReportGenerationTrigger")
                        .WithCronSchedule(
                            context.Configuration["Scheduler:CronExpression"] ?? "0 0 4 * * ?",
                            x => x.InTimeZone(TimeZoneInfo.FindSystemTimeZoneById(
                                context.Configuration["Scheduler:TimeZone"] ?? "America/New_York")))
                        .WithDescription("Daily report generation at 4 AM EST")
                    );
                    
                    // Configure Daily Donations Excel Report job
                    if (context.Configuration.GetValue<bool>("DailyDonationsReport:Enabled", true))
                    {
                        var excelJobKey = new JobKey("DailyDonationsExcelJob");
                        q.AddJob<DailyDonationsExcelJob>(opts => opts.WithIdentity(excelJobKey));
                        
                        // Configure trigger from configuration
                        q.AddTrigger(opts => opts
                            .ForJob(excelJobKey)
                            .WithIdentity("DailyDonationsExcelTrigger")
                            .WithCronSchedule(
                                context.Configuration["DailyDonationsReport:Schedule"] ?? "0 0 5 * * ?",
                                x => x.InTimeZone(TimeZoneInfo.FindSystemTimeZoneById(
                                    context.Configuration["Scheduler:TimeZone"] ?? "America/New_York")))
                            .WithDescription("Daily Donations Excel Report")
                        );
                        
                        var schedule = context.Configuration["DailyDonationsReport:Schedule"] ?? "0 0 5 * * ?";
                        Log.Information("Daily Donations Excel Report scheduled with cron: {Schedule}", schedule);
                    }
                });
                
                // Add the Quartz hosted service
                services.AddQuartzHostedService(options =>
                {
                    options.WaitForJobsToComplete = true;
                });
                
                // Log the schedule
                var cronExpression = context.Configuration["Scheduler:CronExpression"] ?? "0 0 4 * * ?";
                var timeZone = context.Configuration["Scheduler:TimeZone"] ?? "America/New_York";
                Log.Information("Report scheduled with cron expression: {CronExpression} in timezone: {TimeZone}", 
                    cronExpression, timeZone);
            });

    private static IConfiguration GetConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();
    }
    
    private static async Task RunExcelReportAsync(IConfiguration configuration)
    {
        try
        {
            // Set EPPlus license context
            OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
            
            var connectionString = configuration.GetConnectionString("DataWarehouse");
            var sendGridApiKey = configuration["SendGrid:ApiKey"];
            var fromEmail = configuration["SendGrid:FromEmail"];
            var fromName = configuration["SendGrid:FromName"];
            
            var recipients = new List<string>();
            var recipientsConfig = configuration.GetSection("DailyDonationsReport:Recipients").GetChildren();
            foreach (var recipient in recipientsConfig)
            {
                var email = recipient["Email"];
                if (!string.IsNullOrEmpty(email))
                    recipients.Add(email);
            }
            
            if (recipients.Count == 0)
                recipients.Add("jlee@theauxilia.com");
            
            var config = new Models.ReportConfiguration
            {
                OutputDirectory = "/srv/reports/output",
                FromEmail = fromEmail ?? "jlee@theauxilia.com",
                FromName = fromName ?? "J Lee",
                Recipients = recipients,
                IncludeAttachment = configuration.GetValue<bool>("DailyDonationsReport:IncludeAttachment", true),
                GroupBySource = configuration.GetValue<bool>("DailyDonationsReport:GroupBySource", true),
                ShowSubtotals = configuration.GetValue<bool>("DailyDonationsReport:ShowSubtotals", true),
                ShowGrandTotal = configuration.GetValue<bool>("DailyDonationsReport:ShowGrandTotal", true)
            };
            
            var sendGridClient = new SendGrid.SendGridClient(sendGridApiKey);
            var excelLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<Reports.DailyDonationsReportExcel>.Instance;
            
            var report = new Reports.DailyDonationsReportExcel(
                excelLogger,
                connectionString,
                sendGridClient,
                config
            );
            
            var result = await report.GenerateAsync();
            
            if (result.Success)
            {
                Log.Information("✓ Excel report generated successfully!");
                Log.Information("  File: {FilePath}", result.FilePath);
                Log.Information("  Records: {RecordCount}", result.RecordCount);
                Console.WriteLine($"✓ Excel report generated: {result.FilePath}");
                Console.WriteLine($"  Total donations: {result.RecordCount}");
            }
            else
            {
                Log.Error("Failed to generate Excel report: {Message}", result.Message);
                Console.WriteLine($"✗ Failed: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error running Excel report");
            Console.WriteLine($"Error: {ex.Message}");
            throw;
        }
    }
}