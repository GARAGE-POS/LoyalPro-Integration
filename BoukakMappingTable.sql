-- Create IntegrationBoukakCustomerMappings table to store mappings between local customers and Boukak customer cards
-- This table links local Customer records to Boukak loyalty card IDs
CREATE TABLE Garage_UAT.dbo.IntegrationBoukakCustomerMappings (
    Id int IDENTITY(1,1) NOT NULL,
    CustomerId int NOT NULL,                    -- Local customer ID from Customers table
    BoukakCustomerId nvarchar(100) NOT NULL,    -- Boukak customer ID returned from API (from x-customer-id header)
    BoukakCardId nvarchar(100) NOT NULL,        -- Boukak card ID returned from API (from x-card-id header)
    LocationId int NOT NULL,                     -- Location context for multi-location support
    CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt datetime2 NULL,
    CONSTRAINT PK_IntegrationBoukakCustomerMappings PRIMARY KEY (Id),
    CONSTRAINT FK_IntegrationBoukakCustomerMappings_Customers FOREIGN KEY (CustomerId) REFERENCES Garage_UAT.dbo.Customers(CustomerID),
    CONSTRAINT FK_IntegrationBoukakCustomerMappings_Locations FOREIGN KEY (LocationId) REFERENCES Garage_UAT.dbo.Locations(LocationID)
);

-- Create indexes for better query performance
CREATE INDEX IX_IntegrationBoukakCustomerMappings_CustomerId ON Garage_UAT.dbo.IntegrationBoukakCustomerMappings(CustomerId);
CREATE INDEX IX_IntegrationBoukakCustomerMappings_LocationId ON Garage_UAT.dbo.IntegrationBoukakCustomerMappings(LocationId);
CREATE INDEX IX_IntegrationBoukakCustomerMappings_BoukakCardId ON Garage_UAT.dbo.IntegrationBoukakCustomerMappings(BoukakCardId);
CREATE INDEX IX_IntegrationBoukakCustomerMappings_BoukakCustomerId ON Garage_UAT.dbo.IntegrationBoukakCustomerMappings(BoukakCustomerId);

-- Create unique constraint to prevent duplicate mappings for same customer/location
CREATE UNIQUE INDEX UQ_IntegrationBoukakCustomerMappings_Customer_Location
    ON Garage_UAT.dbo.IntegrationBoukakCustomerMappings(CustomerId, LocationId);

-- Optional: Create webhook log table to track Boukak webhook events
CREATE TABLE Garage_UAT.dbo.IntegrationBoukakWebhookLogs (
    Id int IDENTITY(1,1) NOT NULL,
    EventType nvarchar(50) NOT NULL,            -- CARD_INSTALLED, CARD_UNINSTALLED, etc.
    BoukakCardId nvarchar(100) NULL,
    BoukakCustomerId nvarchar(100) NULL,
    Payload nvarchar(max) NULL,                 -- Full JSON payload for debugging
    ProcessedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT PK_IntegrationBoukakWebhookLogs PRIMARY KEY (Id)
);

-- Create index for webhook logs
CREATE INDEX IX_IntegrationBoukakWebhookLogs_EventType ON Garage_UAT.dbo.IntegrationBoukakWebhookLogs(EventType);
CREATE INDEX IX_IntegrationBoukakWebhookLogs_ProcessedAt ON Garage_UAT.dbo.IntegrationBoukakWebhookLogs(ProcessedAt);
CREATE INDEX IX_IntegrationBoukakWebhookLogs_BoukakCardId ON Garage_UAT.dbo.IntegrationBoukakWebhookLogs(BoukakCardId);
