-- =====================================================================
-- AGONEAIHub — Default Schema + Seed Data
-- Run this against your SQL Server database after EF migrations,
-- OR use it standalone to see the full table structure.
-- =====================================================================

-- Create schema if not exists
IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'aihub')
    EXEC('CREATE SCHEMA [aihub]');
GO

-- =====================================================================
-- TABLE 1: PromptTemplates
-- Hierarchical: Project → Module → Section → PromptKey
-- =====================================================================
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'aihub' AND TABLE_NAME = 'PromptTemplates')
CREATE TABLE [aihub].[PromptTemplates] (
    [Id]                 INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Project]            NVARCHAR(50)   NOT NULL,     -- AGONESPot, AGONELearn, etc.
    [Module]             NVARCHAR(100)  NOT NULL,     -- FileClassification, ReportGeneration
    [Section]            NVARCHAR(100)  NOT NULL DEFAULT 'General',  -- SectionA, SectionB, General
    [PromptKey]          NVARCHAR(200)  NOT NULL,     -- classify-file, generate-section-a
    [Name]               NVARCHAR(500)  NOT NULL,
    [Description]        NVARCHAR(MAX)  NULL,
    [SystemPrompt]       NVARCHAR(MAX)  NOT NULL,
    [UserPromptTemplate] NVARCHAR(MAX)  NOT NULL,
    [Model]              NVARCHAR(100)  NOT NULL DEFAULT 'gpt-4o',
    [MaxTokens]          INT            NOT NULL DEFAULT 4096,
    [Temperature]        FLOAT          NOT NULL DEFAULT 0.7,
    [IsActive]           BIT            NOT NULL DEFAULT 1,
    [Version]            INT            NOT NULL DEFAULT 1,
    [Tags]               NVARCHAR(500)  NULL,
    [CreatedAt]          DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt]          DATETIME2      NULL,
    [CreatedBy]          NVARCHAR(200)  NULL
);
GO

CREATE UNIQUE INDEX IX_Prompt_Lookup ON [aihub].[PromptTemplates] ([Project], [Module], [Section], [PromptKey]);
CREATE INDEX IX_Prompt_Module ON [aihub].[PromptTemplates] ([Project], [Module]);
GO

-- =====================================================================
-- TABLE 2: PromptExecutionLogs
-- Every AI call is logged here with full request/response
-- =====================================================================
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'aihub' AND TABLE_NAME = 'PromptExecutionLogs')
CREATE TABLE [aihub].[PromptExecutionLogs] (
    [Id]                  INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [CorrelationId]       NVARCHAR(64)   NOT NULL,
    [Project]             NVARCHAR(50)   NOT NULL,
    [PromptTemplateId]    INT            NULL REFERENCES [aihub].[PromptTemplates]([Id]) ON DELETE SET NULL,
    [Module]              NVARCHAR(100)  NOT NULL DEFAULT '',
    [Section]             NVARCHAR(100)  NOT NULL DEFAULT '',
    [PromptKey]           NVARCHAR(200)  NOT NULL DEFAULT '',
    [Model]               NVARCHAR(100)  NOT NULL DEFAULT '',
    [InputText]           NVARCHAR(MAX)  NULL,
    [RenderedSystemPrompt] NVARCHAR(MAX) NULL,
    [RenderedUserPrompt]  NVARCHAR(MAX)  NULL,
    [OutputText]          NVARCHAR(MAX)  NULL,
    [VariablesJson]       NVARCHAR(MAX)  NULL,
    [PromptTokens]        INT            NULL,
    [CompletionTokens]    INT            NULL,
    [TotalTokens]         INT            NULL,
    [DurationMs]          BIGINT         NOT NULL DEFAULT 0,
    [Success]             BIT            NOT NULL DEFAULT 0,
    [ErrorMessage]        NVARCHAR(MAX)  NULL,
    [ErrorStackTrace]     NVARCHAR(MAX)  NULL,
    [CreatedAt]           DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt]           DATETIME2      NULL,
    [CreatedBy]           NVARCHAR(200)  NULL
);
GO

CREATE INDEX IX_ExecLog_Correlation ON [aihub].[PromptExecutionLogs] ([CorrelationId]);
CREATE INDEX IX_ExecLog_Date ON [aihub].[PromptExecutionLogs] ([CreatedAt]);
CREATE INDEX IX_ExecLog_Module ON [aihub].[PromptExecutionLogs] ([Project], [Module], [Section]);
GO

-- =====================================================================
-- TABLE 3: ApiRequestLogs
-- Auto-captured by middleware — every HTTP request/response
-- =====================================================================
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'aihub' AND TABLE_NAME = 'ApiRequestLogs')
CREATE TABLE [aihub].[ApiRequestLogs] (
    [Id]              BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [CorrelationId]   NVARCHAR(64)   NOT NULL,
    [HttpMethod]      NVARCHAR(10)   NOT NULL,
    [Path]            NVARCHAR(500)  NOT NULL,
    [QueryString]     NVARCHAR(MAX)  NULL,
    [RequestBody]     NVARCHAR(MAX)  NULL,
    [RequestHeaders]  NVARCHAR(MAX)  NULL,
    [StatusCode]      INT            NOT NULL DEFAULT 0,
    [ResponseBody]    NVARCHAR(MAX)  NULL,
    [DurationMs]      BIGINT         NOT NULL DEFAULT 0,
    [Project]         NVARCHAR(50)   NULL,
    [UserAgent]       NVARCHAR(500)  NULL,
    [ClientIp]        NVARCHAR(50)   NULL,
    [CreatedAt]       DATETIME2      NOT NULL DEFAULT GETUTCDATE()
);
GO

CREATE INDEX IX_ApiLog_Correlation ON [aihub].[ApiRequestLogs] ([CorrelationId]);
CREATE INDEX IX_ApiLog_Date ON [aihub].[ApiRequestLogs] ([CreatedAt]);
CREATE INDEX IX_ApiLog_Path ON [aihub].[ApiRequestLogs] ([Path]);
CREATE INDEX IX_ApiLog_Status ON [aihub].[ApiRequestLogs] ([StatusCode]);
GO

-- =====================================================================
-- TABLE 4: ErrorNotifications
-- Auto-created on failures. Dashboard for ops/support.
-- =====================================================================
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'aihub' AND TABLE_NAME = 'ErrorNotifications')
CREATE TABLE [aihub].[ErrorNotifications] (
    [Id]              INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Project]         NVARCHAR(50)   NOT NULL,
    [CorrelationId]   NVARCHAR(64)   NOT NULL DEFAULT '',
    [Service]         NVARCHAR(100)  NOT NULL,
    [Operation]       NVARCHAR(200)  NOT NULL,
    [ErrorMessage]    NVARCHAR(MAX)  NOT NULL,
    [ErrorStackTrace] NVARCHAR(MAX)  NULL,
    [RequestPayload]  NVARCHAR(MAX)  NULL,
    [Status]          NVARCHAR(50)   NOT NULL DEFAULT 'New',
    [AcknowledgedAt]  DATETIME2      NULL,
    [AcknowledgedBy]  NVARCHAR(200)  NULL,
    [ResolvedAt]      DATETIME2      NULL,
    [ResolvedBy]      NVARCHAR(200)  NULL,
    [ResolutionNotes] NVARCHAR(MAX)  NULL,
    [CreatedAt]       DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt]       DATETIME2      NULL,
    [CreatedBy]       NVARCHAR(200)  NULL
);
GO

CREATE INDEX IX_ErrNotif_Status ON [aihub].[ErrorNotifications] ([Status]);
CREATE INDEX IX_ErrNotif_Date ON [aihub].[ErrorNotifications] ([CreatedAt]);
GO

-- =====================================================================
-- TABLE 5: DocumentProcessingJobs
-- =====================================================================
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'aihub' AND TABLE_NAME = 'DocumentProcessingJobs')
CREATE TABLE [aihub].[DocumentProcessingJobs] (
    [Id]                INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Project]           NVARCHAR(50)   NOT NULL,
    [DocumentUrl]       NVARCHAR(MAX)  NOT NULL,
    [FileName]          NVARCHAR(500)  NULL,
    [ModelId]           NVARCHAR(200)  NOT NULL DEFAULT 'prebuilt-document',
    [Status]            NVARCHAR(50)   NOT NULL DEFAULT 'Pending',
    [ExtractedDataJson] NVARCHAR(MAX)  NULL,
    [CompletedAt]       DATETIME2      NULL,
    [ErrorMessage]      NVARCHAR(MAX)  NULL,
    [CreatedAt]         DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt]         DATETIME2      NULL,
    [CreatedBy]         NVARCHAR(200)  NULL
);
GO

-- =====================================================================
-- TABLE 6: SearchIndexConfigs
-- =====================================================================
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'aihub' AND TABLE_NAME = 'SearchIndexConfigs')
CREATE TABLE [aihub].[SearchIndexConfigs] (
    [Id]          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Project]     NVARCHAR(50)  NOT NULL,
    [IndexName]   NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(MAX) NULL,
    [FieldsJson]  NVARCHAR(MAX) NULL,
    [IsActive]    BIT           NOT NULL DEFAULT 1,
    [CreatedAt]   DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt]   DATETIME2     NULL,
    [CreatedBy]   NVARCHAR(200) NULL
);
GO

CREATE UNIQUE INDEX IX_SearchIdx_Lookup ON [aihub].[SearchIndexConfigs] ([Project], [IndexName]);
GO

-- =====================================================================
-- SEED DATA: AGONESPot Prompt Templates
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM [aihub].[PromptTemplates] WHERE [Project] = 'AGONESPot')
BEGIN
    INSERT INTO [aihub].[PromptTemplates]
        ([Project], [Module], [Section], [PromptKey], [Name], [Description],
         [SystemPrompt], [UserPromptTemplate], [Model], [MaxTokens], [Temperature], [Tags])
    VALUES
        ('AGONESPot', 'FileClassification', 'General', 'classify-file',
         'Classify File',
         'Analyzes a file and returns its type, category, and risk level.',
         'You are an expert document classifier for the AGONESPot audit platform. Analyze the provided file content and return a JSON object with: fileType (Invoice, Contract, Report, Certificate, Other), category (Financial, Legal, Compliance, HR, Operations), riskLevel (Low, Medium, High, Critical), summary (one-line description).',
         'File name: {{fileName}}\n\nFile content:\n{{fileContent}}\n\n{{additionalContext}}',
         'gpt-4o', 1024, 0.3, 'classification,spot,file'),

        ('AGONESPot', 'ReportGeneration', 'SectionA', 'generate-section-a',
         'Spot Report - Section A: Executive Summary',
         'Generates the Executive Summary section of a Spot audit report.',
         'You are an audit report writer for AGONESPot. Write a professional Executive Summary (Section A) based on the provided audit data. Be concise, factual, and highlight key findings.',
         'Report Title: {{reportTitle}}\n\nAudit Data:\n{{auditData}}\n\nWrite the Executive Summary section.',
         'gpt-4o', 2048, 0.5, 'report,spot,section-a,executive-summary'),

        ('AGONESPot', 'ReportGeneration', 'SectionB', 'generate-section-b',
         'Spot Report - Section B: Findings & Observations',
         'Generates the Findings section with detailed observations.',
         'You are an audit report writer for AGONESPot. Write detailed Findings & Observations (Section B). List each finding with: observation, evidence, impact, and recommendation.',
         'Report Title: {{reportTitle}}\n\nAudit Data:\n{{auditData}}\n\nWrite the Findings & Observations section.',
         'gpt-4o', 4096, 0.5, 'report,spot,section-b,findings'),

        ('AGONESPot', 'ReportGeneration', 'SectionC', 'generate-section-c',
         'Spot Report - Section C: Risk Assessment',
         'Generates the Risk Assessment section with risk matrix.',
         'You are an audit report writer for AGONESPot. Write a Risk Assessment (Section C). Categorize risks by likelihood and impact. Provide a risk matrix and mitigation strategies.',
         'Report Title: {{reportTitle}}\n\nAudit Data:\n{{auditData}}\n\nWrite the Risk Assessment section.',
         'gpt-4o', 3072, 0.4, 'report,spot,section-c,risk'),

        ('AGONESPot', 'ReportGeneration', 'SectionD', 'generate-section-d',
         'Spot Report - Section D: Recommendations & Action Plan',
         'Generates recommendations with priority, owner, and timeline.',
         'You are an audit report writer for AGONESPot. Write Recommendations & Action Plan (Section D). For each recommendation provide: priority (Critical/High/Medium/Low), responsible party, timeline, and expected outcome.',
         'Report Title: {{reportTitle}}\n\nAudit Data:\n{{auditData}}\n\nWrite the Recommendations & Action Plan section.',
         'gpt-4o', 3072, 0.5, 'report,spot,section-d,recommendations');

    PRINT 'Seeded 5 AGONESPot prompt templates.';
END
GO

-- =====================================================================
-- USEFUL VIEWS
-- =====================================================================
CREATE OR ALTER VIEW [aihub].[vw_PromptUsageSummary] AS
SELECT
    Project,
    Module,
    Section,
    PromptKey,
    COUNT(*)              AS TotalCalls,
    SUM(CASE WHEN Success = 1 THEN 1 ELSE 0 END) AS SuccessCalls,
    SUM(CASE WHEN Success = 0 THEN 1 ELSE 0 END) AS FailedCalls,
    AVG(DurationMs)       AS AvgDurationMs,
    SUM(TotalTokens)      AS TotalTokensUsed,
    MAX(CreatedAt)        AS LastUsedAt
FROM [aihub].[PromptExecutionLogs]
GROUP BY Project, Module, Section, PromptKey;
GO

CREATE OR ALTER VIEW [aihub].[vw_ErrorDashboard] AS
SELECT
    Project,
    Service,
    Operation,
    Status,
    COUNT(*)        AS ErrorCount,
    MAX(CreatedAt)  AS LastErrorAt
FROM [aihub].[ErrorNotifications]
GROUP BY Project, Service, Operation, Status;
GO

PRINT 'AGONEAIHub schema + seed data complete.';
GO
