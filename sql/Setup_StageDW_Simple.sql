-- =============================================
-- Simplified Setup Script for StageDW
-- For Azure SQL Database Cross-Database Queries
-- Target: StageDW on sql-theauxilia-dev-stage
-- Created: 2025-08-13
-- =============================================

-- Note: Run this script in Azure Portal Query Editor or SSMS
-- Connect to: sql-theauxilia-dev-stage.database.windows.net/StageDW

USE StageDW;
GO

-- =============================================
-- Option 1: Using Elastic Query (Recommended for same server)
-- =============================================

-- Step 1: Create Master Key
CREATE MASTER KEY ENCRYPTION BY PASSWORD = 'Stage$DW#2025@Secure!Key';
GO

-- Step 2: Create Database Scoped Credential
-- This uses the same credentials as the Stage databases
CREATE DATABASE SCOPED CREDENTIAL StageDBCredential
WITH IDENTITY = 'adminuser',  -- The admin user for the server
SECRET = 'YourAdminPassword'; -- Replace with actual admin password
GO

-- Step 3: Create External Data Source for same server
CREATE EXTERNAL DATA SOURCE StageClientDB_Source
WITH (
    TYPE = RDBMS,
    LOCATION = 'sql-theauxilia-dev-stage.database.windows.net',
    DATABASE_NAME = 'StageAuxiliaClientDB',
    CREDENTIAL = StageDBCredential
);
GO

-- Step 4: Create Schema for External Tables
CREATE SCHEMA stage;
GO

-- Step 5: Create External Table Example
-- First, we need to know the exact schema of the source table
-- Run this in StageAuxiliaClientDB to get the schema:
/*
SELECT 
    c.COLUMN_NAME,
    c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH,
    c.IS_NULLABLE,
    c.COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME = 'Clients'
    AND c.TABLE_SCHEMA = 'dbo'
ORDER BY c.ORDINAL_POSITION;
*/

-- Step 6: Create a simple external table (adjust columns as needed)
CREATE EXTERNAL TABLE stage.Clients (
    ID uniqueidentifier NOT NULL,
    ClientName nvarchar(256),
    OrganizationName nvarchar(256),
    Address nvarchar(500),
    City nvarchar(100),
    State nvarchar(50),
    PostCode nvarchar(20),
    Country nvarchar(100),
    PrimaryContactEmail nvarchar(256),
    IsDeleted bit,
    CreatedOn datetime2(7),
    UpdatedOn datetime2(7)
)
WITH (
    DATA_SOURCE = StageClientDB_Source,
    SCHEMA_NAME = 'dbo',
    OBJECT_NAME = 'Clients'
);
GO

-- =============================================
-- Option 2: Create Local Summary Tables
-- If external tables don't work, create local tables
-- =============================================

-- Create schema for staging data
CREATE SCHEMA staging;
GO

-- Create a local clients summary table
CREATE TABLE staging.ClientSummary (
    ClientID uniqueidentifier PRIMARY KEY,
    ClientName nvarchar(256),
    OrganizationName nvarchar(256),
    City nvarchar(100),
    State nvarchar(50),
    Country nvarchar(100),
    ClientType int,
    Status int,
    IsActive bit,
    CreatedDate datetime2(7),
    UpdatedDate datetime2(7),
    ContactCount int,
    LastActivityDate datetime2(7)
);
GO

-- Create indexes for performance
CREATE INDEX IX_ClientSummary_ClientName ON staging.ClientSummary(ClientName);
CREATE INDEX IX_ClientSummary_Status ON staging.ClientSummary(Status);
CREATE INDEX IX_ClientSummary_CreatedDate ON staging.ClientSummary(CreatedDate);
GO

-- =============================================
-- Test Queries
-- =============================================

-- Test external table (if created)
-- SELECT TOP 10 * FROM stage.Clients;

-- Get database information
SELECT 
    DB_NAME() as CurrentDatabase,
    SERVERPROPERTY('ServerName') as ServerName,
    SERVERPROPERTY('Edition') as SQLEdition,
    SERVERPROPERTY('ProductVersion') as Version;
GO

PRINT 'Setup script completed. Please execute in Azure Portal or SSMS with proper credentials.';
GO