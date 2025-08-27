-- =============================================
-- StageDW Setup Complete - External Tables Configuration
-- Server: sql-theauxilia-dev-stage.database.windows.net
-- Database: StageDW
-- Created: 2025-08-13
-- =============================================

-- CONNECTION INFORMATION:
-- Server: sql-theauxilia-dev-stage.database.windows.net
-- Database: StageDW
-- Username: adminuser
-- Password: A9+cznR2jfK6f7NO+1V=

-- =============================================
-- OBJECTS CREATED:
-- =============================================

-- 1. Master Key (encrypted)
-- 2. Database Scoped Credential: StageClientDBCredential
-- 3. External Data Source: StageClientDB_DataSource
-- 4. Schema: ext
-- 5. External Tables:
--    - ext.Client (201 records)
--    - ext.ClientDonor (2,516,881 records)
--    - ext.EventDonations (0 records currently)
-- 6. View: dbo.vw_ClientSummary

-- =============================================
-- EXTERNAL TABLES CREATED:
-- =============================================

-- ext.Client
-- Points to: StageAuxiliaClientDB.dbo.Client
-- Key columns: ID (uniqueidentifier), Name, PhisicalCity, PhisicalState (int)

-- ext.ClientDonor  
-- Points to: StageAuxiliaClientDB.dbo.ClientDonor
-- Key columns: ID, ClientID, DonorID

-- ext.EventDonations
-- Points to: StageAuxiliaClientDB.dbo.EventDonations
-- Key columns: ID, ClientID, DonorID, DonationAmmount

-- =============================================
-- SAMPLE QUERIES:
-- =============================================

-- Count records in each external table
SELECT 'Client' as TableName, COUNT(*) as RecordCount FROM ext.Client
UNION ALL
SELECT 'ClientDonor', COUNT(*) FROM ext.ClientDonor
UNION ALL
SELECT 'EventDonations', COUNT(*) FROM ext.EventDonations;

-- Get client distribution by state (note: State is stored as INT)
SELECT 
    PhisicalState as StateCode,
    COUNT(*) as ClientCount
FROM ext.Client
WHERE IsDeleted = 0 OR IsDeleted IS NULL
GROUP BY PhisicalState
ORDER BY ClientCount DESC;

-- Get top clients by donor count
WITH ClientDonorCounts AS (
    SELECT 
        ClientID,
        COUNT(*) as DonorCount
    FROM ext.ClientDonor
    WHERE IsDeleted = 0 OR IsDeleted IS NULL
    GROUP BY ClientID
)
SELECT TOP 10
    c.Name as ClientName,
    c.PhisicalCity as City,
    cdc.DonorCount
FROM ext.Client c
INNER JOIN ClientDonorCounts cdc ON c.ID = cdc.ClientID
WHERE c.IsDeleted = 0 OR c.IsDeleted IS NULL
ORDER BY cdc.DonorCount DESC;

-- =============================================
-- NOTES:
-- =============================================

-- 1. External tables allow querying StageAuxiliaClientDB data without copying
-- 2. Performance may be slower than local tables for large queries
-- 3. Cannot create indexes on external tables
-- 4. PhisicalState and BillingState columns are INT (likely enum values)
-- 5. Many text columns use nvarchar(max) indicated by -1 length
-- 6. EventDonations table currently has no data in Stage environment

-- =============================================
-- TROUBLESHOOTING:
-- =============================================

-- If getting schema mismatch errors:
-- 1. Check actual column data types in source database
-- 2. INT columns cannot be mapped to NVARCHAR in external tables
-- 3. Use datetimeoffset for date columns, not datetime2
-- 4. nvarchar(max) columns should be defined as nvarchar(max) in external table

-- To drop and recreate:
/*
DROP EXTERNAL TABLE ext.Client;
DROP EXTERNAL TABLE ext.ClientDonor;
DROP EXTERNAL TABLE ext.EventDonations;
DROP EXTERNAL DATA SOURCE StageClientDB_DataSource;
DROP DATABASE SCOPED CREDENTIAL StageClientDBCredential;
*/

-- =============================================