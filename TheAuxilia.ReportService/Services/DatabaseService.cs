using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Data;

namespace TheAuxilia.ReportService.Services;

public class DatabaseService
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseService> _logger;
    private const int WarmUpRetries = 3;
    private const int WarmUpDelaySeconds = 30;

    public DatabaseService(IConfiguration configuration, ILogger<DatabaseService> logger)
    {
        _connectionString = configuration.GetConnectionString("DataWarehouse") 
            ?? throw new InvalidOperationException("DataWarehouse connection string not found");
        _logger = logger;
    }

    public async Task<DataTable> ExecuteStoredProcedureAsync(string procedureName, Dictionary<string, object>? parameters = null)
    {
        _logger.LogInformation("Executing stored procedure: {ProcedureName}", procedureName);
        
        // Warm up the database connection if it's paused
        await WarmUpDatabaseAsync();
        
        var dataTable = new DataTable();
        
        try
        {
            using var connection = new SqlConnection(_connectionString);
            using var command = new SqlCommand(procedureName, connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 1020 // 17 minutes (increased by 120 seconds)
            };

            // Add parameters if provided
            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    command.Parameters.AddWithValue($"@{param.Key}", param.Value ?? DBNull.Value);
                }
            }

            await connection.OpenAsync();
            
            using var adapter = new SqlDataAdapter(command);
            adapter.Fill(dataTable);
            
            _logger.LogInformation("Stored procedure executed successfully. Rows returned: {RowCount}", dataTable.Rows.Count);
            return dataTable;
        }
        catch (SqlException ex) when (IsDatabaseUnavailable(ex))
        {
            _logger.LogWarning("Database appears to be paused or unavailable. Attempting retry with extended warm-up...");
            
            // Extended warm-up for paused database
            await Task.Delay(TimeSpan.FromSeconds(60));
            await WarmUpDatabaseAsync();
            
            // Retry the operation
            return await ExecuteStoredProcedureWithRetryAsync(procedureName, parameters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing stored procedure {ProcedureName}", procedureName);
            throw;
        }
    }
    
    private async Task<DataTable> ExecuteStoredProcedureWithRetryAsync(string procedureName, Dictionary<string, object>? parameters)
    {
        var dataTable = new DataTable();
        
        try
        {
            using var connection = new SqlConnection(_connectionString);
            using var command = new SqlCommand(procedureName, connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 1020 // 17 minutes (increased by 120 seconds)
            };

            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    command.Parameters.AddWithValue($"@{param.Key}", param.Value ?? DBNull.Value);
                }
            }

            await connection.OpenAsync();
            
            using var adapter = new SqlDataAdapter(command);
            adapter.Fill(dataTable);
            
            _logger.LogInformation("Stored procedure executed successfully on retry. Rows returned: {RowCount}", dataTable.Rows.Count);
            return dataTable;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing stored procedure {ProcedureName} on retry", procedureName);
            throw;
        }
    }
    
    /// <summary>
    /// Warm up the database connection by executing simple queries.
    /// This helps wake up paused Azure SQL databases.
    /// </summary>
    private async Task WarmUpDatabaseAsync()
    {
        try
        {
            // First warm-up query
            _logger.LogInformation("Performing first warm-up query to ensure database is available...");
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using var command = new SqlCommand("SELECT 1 as WarmUp, GETDATE() as ServerTime", connection)
                {
                    CommandTimeout = 1020 // 17 minutes (increased by 120 seconds)
                };
                
                var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var serverTime = reader["ServerTime"];
                    _logger.LogInformation("First warm-up query successful. Server time: {ServerTime}", serverTime);
                }
            }
            
            // Wait 1 minute before second warm-up
            _logger.LogInformation("Waiting 60 seconds before second warm-up query...");
            await Task.Delay(TimeSpan.FromMinutes(1));
            
            // Second warm-up query with more comprehensive check
            _logger.LogInformation("Performing second warm-up query...");
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using var command = new SqlCommand(@"
                    SELECT 
                        DB_NAME() as DatabaseName,
                        COUNT(*) as TableCount,
                        GETDATE() as ServerTime
                    FROM sys.tables", connection)
                {
                    CommandTimeout = 1020 // 17 minutes (increased by 120 seconds)
                };
                
                var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var dbName = reader["DatabaseName"];
                    var tableCount = reader["TableCount"];
                    var serverTime = reader["ServerTime"];
                    _logger.LogInformation("Second warm-up query successful. Database: {DbName}, Tables: {TableCount}, Time: {ServerTime}", 
                        dbName, tableCount, serverTime);
                }
            }
            
            _logger.LogInformation("Database warm-up completed successfully");
        }
        catch (SqlException ex) when (IsDatabaseUnavailable(ex))
        {
            _logger.LogWarning(ex, "Database appears to be paused. Waiting for it to resume...");
            
            // If database is not available, wait longer and retry
            for (int attempt = 1; attempt <= WarmUpRetries; attempt++)
            {
                _logger.LogInformation("Waiting 2 minutes for database to resume (attempt {Attempt}/{MaxAttempts})...", 
                    attempt, WarmUpRetries);
                await Task.Delay(TimeSpan.FromMinutes(2));
                
                try
                {
                    using var connection = new SqlConnection(_connectionString);
                    await connection.OpenAsync();
                    using var command = new SqlCommand("SELECT 1", connection)
                    {
                        CommandTimeout = 1020 // 17 minutes (increased by 120 seconds)
                    };
                    
                    await command.ExecuteScalarAsync();
                    _logger.LogInformation("Database is now available after {Attempt} attempts", attempt);
                    return;
                }
                catch (Exception retryEx)
                {
                    if (attempt == WarmUpRetries)
                    {
                        _logger.LogError(retryEx, "Database still unavailable after {Attempts} warm-up attempts", WarmUpRetries);
                        throw;
                    }
                    _logger.LogWarning("Database still unavailable on attempt {Attempt}", attempt);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Warm-up query failed. Will proceed with main query.");
        }
    }
    
    /// <summary>
    /// Check if the exception indicates the database is unavailable (paused or scaling)
    /// </summary>
    private bool IsDatabaseUnavailable(SqlException ex)
    {
        // Check for specific error messages and codes
        return ex.Message.Contains("not currently available", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("Database.*is paused", StringComparison.OrdinalIgnoreCase) ||
               ex.Number == 40613 || // Database on server is not currently available
               ex.Number == 40501 || // Service is currently busy
               ex.Number == 40197 || // Service error
               ex.Number == 49918 || // Cannot process request
               ex.Number == 49919 || // Cannot process create/update request
               ex.Number == 49920;   // Cannot process delete request
    }
}