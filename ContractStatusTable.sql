-- Create IntegrationSadeqContracts table to track Sadeq contract signing status
-- This table accommodates both PDF uploads and template-based contracts

CREATE TABLE IntegrationSadeqContracts (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    CompanyCode VARCHAR(100) NOT NULL,
    CompanyName NVARCHAR(255) NOT NULL,
    PhoneNumber NVARCHAR(20) NOT NULL,
    Terminals NVARCHAR(500) NULL,
    NationalId NVARCHAR(20) NOT NULL,
    Email NVARCHAR(255) NOT NULL,
    SadqSent BIT NOT NULL DEFAULT 0,
    Signed BIT NOT NULL DEFAULT 0,
    EnvelopId NVARCHAR(255) NULL,
    DocumentId NVARCHAR(255) NULL,
    TemplateId NVARCHAR(255) NULL,
    PdfFileName NVARCHAR(500) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME2 NULL,
    SignedAt DATETIME2 NULL,
    Notes NVARCHAR(1000) NULL,
    ErrorMessage NVARCHAR(1000) NULL,
    CONSTRAINT IntegrationSadeqContracts_UNIQUE UNIQUE (CompanyCode)
);

-- Create indexes for common queries
CREATE INDEX IX_IntegrationSadeqContracts_CompanyCode ON IntegrationSadeqContracts(CompanyCode);
CREATE INDEX IX_IntegrationSadeqContracts_NationalId ON IntegrationSadeqContracts(NationalId);
CREATE INDEX IX_IntegrationSadeqContracts_Email ON IntegrationSadeqContracts(Email);
CREATE INDEX IX_IntegrationSadeqContracts_PhoneNumber ON IntegrationSadeqContracts(PhoneNumber);
CREATE INDEX IX_IntegrationSadeqContracts_DocumentId ON IntegrationSadeqContracts(DocumentId);
CREATE INDEX IX_IntegrationSadeqContracts_SadqSent_Signed ON IntegrationSadeqContracts(SadqSent, Signed);
CREATE INDEX IX_IntegrationSadeqContracts_CreatedAt ON IntegrationSadeqContracts(CreatedAt);

-- Add comments for documentation
EXEC sp_addextendedproperty 
    @name = N'MS_Description', @value = 'Tracks Sadeq contract signing status for customers',
    @level0type = N'SCHEMA', @level0name = 'dbo',
    @level1type = N'TABLE', @level1name = 'IntegrationSadeqContracts';

EXEC sp_addextendedproperty 
    @name = N'MS_Description', @value = 'Template ID used for template-based contracts (mutually exclusive with PdfFileName)',
    @level0type = N'SCHEMA', @level0name = 'dbo',
    @level1type = N'TABLE', @level1name = 'IntegrationSadeqContracts',
    @level2type = N'COLUMN', @level2name = 'TemplateId';

EXEC sp_addextendedproperty 
    @name = N'MS_Description', @value = 'Original PDF filename for PDF-based contracts (mutually exclusive with TemplateId)',
    @level0type = N'SCHEMA', @level0name = 'dbo',
    @level1type = N'TABLE', @level1name = 'IntegrationSadeqContracts',
    @level2type = N'COLUMN', @level2name = 'PdfFileName';
