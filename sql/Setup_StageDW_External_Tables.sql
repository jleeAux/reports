-- =============================================
-- Setup Script for StageDW External Tables
-- Target: Azure SQL Database - StageDW
-- Source: StageAuxiliaClientDB (same server)
-- Created: 2025-08-13
-- =============================================

USE StageDW;
GO

-- =============================================
-- STEP 1: Enable PolyBase (if needed)
-- =============================================
-- Note: Azure SQL Database has PolyBase enabled by default
-- This is just for verification
SELECT SERVERPROPERTY ('IsPolyBaseInstalled') AS IsPolyBaseInstalled;
GO

-- =============================================
-- STEP 2: Create Master Key (if not exists)
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.symmetric_keys WHERE name = '##MS_DatabaseMasterKey##')
BEGIN
    CREATE MASTER KEY ENCRYPTION BY PASSWORD = 'Stage$DW#2025@Secure!Key';
    PRINT 'Master Key created successfully';
END
ELSE
BEGIN
    PRINT 'Master Key already exists';
END
GO

-- =============================================
-- STEP 3: Create Database Scoped Credential
-- =============================================
-- Since both databases are on the same server, we can use SQL authentication
IF EXISTS (SELECT * FROM sys.database_scoped_credentials WHERE name = 'StageClientDBCredential')
BEGIN
    DROP DATABASE SCOPED CREDENTIAL StageClientDBCredential;
END

CREATE DATABASE SCOPED CREDENTIAL StageClientDBCredential
WITH IDENTITY = 'auxilliadblog20',  -- User from connection string
SECRET = 'auxi!!apAss@20@#';        -- Password from connection string
GO

PRINT 'Database scoped credential created successfully';
GO

-- =============================================
-- STEP 4: Create External Data Source
-- =============================================
IF EXISTS (SELECT * FROM sys.external_data_sources WHERE name = 'StageClientDB_DataSource')
BEGIN
    DROP EXTERNAL DATA SOURCE StageClientDB_DataSource;
END

-- For same-server cross-database queries in Azure SQL Database
CREATE EXTERNAL DATA SOURCE StageClientDB_DataSource
WITH (
    TYPE = RDBMS,
    LOCATION = 'sql-theauxilia-dev-stage.database.windows.net',
    DATABASE_NAME = 'StageAuxiliaClientDB',
    CREDENTIAL = StageClientDBCredential
);
GO

PRINT 'External data source created successfully';
GO

-- =============================================
-- STEP 5: Create Schema for External Tables
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'ext')
BEGIN
    EXEC('CREATE SCHEMA ext');
    PRINT 'Schema [ext] created for external tables';
END
GO

-- =============================================
-- STEP 6: Create External Tables for Client Data
-- =============================================

-- 6.1: External table for Clients
IF OBJECT_ID('ext.Clients', 'U') IS NOT NULL
    DROP EXTERNAL TABLE ext.Clients;

CREATE EXTERNAL TABLE ext.Clients (
    ID uniqueidentifier NOT NULL,
    CreatedOn datetime2(7),
    CreatedByID nvarchar(450),
    CreatedByUserName nvarchar(256),
    UpdatedOn datetime2(7),
    UpdatedByID nvarchar(450),
    UpdatedByUserName nvarchar(256),
    IsDeleted bit,
    DeletedOn datetime2(7),
    DeletedByID nvarchar(450),
    DeletedByUserName nvarchar(256),
    ClientName nvarchar(256),
    OrganizationName nvarchar(256),
    Address nvarchar(500),
    City nvarchar(100),
    State nvarchar(50),
    PostCode nvarchar(20),
    Country nvarchar(100),
    PrimaryContactName nvarchar(256),
    PrimaryContactEmail nvarchar(256),
    PrimaryContactPhone nvarchar(50),
    SecondaryContactName nvarchar(256),
    SecondaryContactEmail nvarchar(256),
    SecondaryContactPhone nvarchar(50),
    Status int,
    ClientType int,
    Website nvarchar(500),
    TaxId nvarchar(50),
    Notes nvarchar(max)
)
WITH (
    DATA_SOURCE = StageClientDB_DataSource,
    SCHEMA_NAME = 'dbo',
    OBJECT_NAME = 'Clients'
);
GO

PRINT 'External table [ext].[Clients] created successfully';
GO

-- 6.2: External table for ClientContacts
IF OBJECT_ID('ext.ClientContacts', 'U') IS NOT NULL
    DROP EXTERNAL TABLE ext.ClientContacts;

CREATE EXTERNAL TABLE ext.ClientContacts (
    ID uniqueidentifier NOT NULL,
    ClientID uniqueidentifier,
    ContactType int,
    FirstName nvarchar(100),
    LastName nvarchar(100),
    Email nvarchar(256),
    Phone nvarchar(50),
    Mobile nvarchar(50),
    Title nvarchar(100),
    Department nvarchar(100),
    IsPrimary bit,
    IsActive bit,
    CreatedOn datetime2(7),
    CreatedByID nvarchar(450),
    UpdatedOn datetime2(7),
    UpdatedByID nvarchar(450),
    Notes nvarchar(max)
)
WITH (
    DATA_SOURCE = StageClientDB_DataSource,
    SCHEMA_NAME = 'dbo',
    OBJECT_NAME = 'ClientContacts'
);
GO

PRINT 'External table [ext].[ClientContacts] created successfully';
GO

-- 6.3: External table for ClientSettings
IF OBJECT_ID('ext.ClientSettings', 'U') IS NOT NULL
    DROP EXTERNAL TABLE ext.ClientSettings;

CREATE EXTERNAL TABLE ext.ClientSettings (
    ID uniqueidentifier NOT NULL,
    ClientID uniqueidentifier,
    SettingKey nvarchar(100),
    SettingValue nvarchar(max),
    SettingType nvarchar(50),
    IsActive bit,
    CreatedOn datetime2(7),
    UpdatedOn datetime2(7)
)
WITH (
    DATA_SOURCE = StageClientDB_DataSource,
    SCHEMA_NAME = 'dbo',
    OBJECT_NAME = 'ClientSettings'
);
GO

PRINT 'External table [ext].[ClientSettings] created successfully';
GO

-- 6.4: External table for ClientSubscriptions
IF OBJECT_ID('ext.ClientSubscriptions', 'U') IS NOT NULL
    DROP EXTERNAL TABLE ext.ClientSubscriptions;

CREATE EXTERNAL TABLE ext.ClientSubscriptions (
    ID uniqueidentifier NOT NULL,
    ClientID uniqueidentifier,
    SubscriptionType nvarchar(100),
    StartDate datetime2(7),
    EndDate datetime2(7),
    Status int,
    PaymentFrequency nvarchar(50),
    Amount decimal(18,2),
    Currency nvarchar(10),
    IsActive bit,
    CreatedOn datetime2(7),
    UpdatedOn datetime2(7)
)
WITH (
    DATA_SOURCE = StageClientDB_DataSource,
    SCHEMA_NAME = 'dbo',
    OBJECT_NAME = 'ClientSubscriptions'
);
GO

PRINT 'External table [ext].[ClientSubscriptions] created successfully';
GO

-- =============================================
-- STEP 7: Create Views for Easy Access
-- =============================================

-- Create a view to simplify querying
CREATE OR ALTER VIEW dbo.vw_StageClients
AS
SELECT 
    c.ID as ClientID,
    c.ClientName,
    c.OrganizationName,
    c.Address,
    c.City,
    c.State,
    c.PostCode,
    c.Country,
    c.PrimaryContactName,
    c.PrimaryContactEmail,
    c.PrimaryContactPhone,
    c.Status,
    c.ClientType,
    c.Website,
    c.TaxId,
    c.IsDeleted,
    c.CreatedOn,
    c.UpdatedOn,
    cs.SubscriptionType,
    cs.StartDate as SubscriptionStart,
    cs.EndDate as SubscriptionEnd,
    cs.Amount as SubscriptionAmount,
    cs.Status as SubscriptionStatus
FROM ext.Clients c
LEFT JOIN ext.ClientSubscriptions cs ON c.ID = cs.ClientID AND cs.IsActive = 1
WHERE c.IsDeleted = 0;
GO

PRINT 'View [dbo].[vw_StageClients] created successfully';
GO

-- =============================================
-- STEP 8: Create Statistics for Performance
-- =============================================

-- Create statistics on commonly queried columns
CREATE STATISTICS stat_Clients_ClientName ON ext.Clients(ClientName);
CREATE STATISTICS stat_Clients_Status ON ext.Clients(Status);
CREATE STATISTICS stat_Clients_ClientType ON ext.Clients(ClientType);
CREATE STATISTICS stat_Clients_IsDeleted ON ext.Clients(IsDeleted);
GO

PRINT 'Statistics created for performance optimization';
GO

-- =============================================
-- STEP 9: Test Queries
-- =============================================

-- Test 1: Count total clients
SELECT COUNT(*) as TotalClients FROM ext.Clients;
GO

-- Test 2: Get active clients
SELECT TOP 10 
    ClientName,
    OrganizationName,
    City,
    State,
    Status
FROM ext.Clients
WHERE IsDeleted = 0
ORDER BY CreatedOn DESC;
GO

-- Test 3: Get client with contacts
SELECT TOP 5
    c.ClientName,
    c.OrganizationName,
    cc.FirstName + ' ' + cc.LastName as ContactName,
    cc.Email as ContactEmail,
    cc.Title
FROM ext.Clients c
LEFT JOIN ext.ClientContacts cc ON c.ID = cc.ClientID
WHERE c.IsDeleted = 0 
    AND cc.IsActive = 1
ORDER BY c.CreatedOn DESC;
GO

PRINT '=============================================';
PRINT 'Setup completed successfully!';
PRINT 'External tables are now available in the [ext] schema';
PRINT 'Use the view [dbo].[vw_StageClients] for simplified access';
PRINT '=============================================';
GO