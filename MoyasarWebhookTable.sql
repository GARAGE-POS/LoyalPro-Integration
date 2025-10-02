-- Moyasar Payment Webhooks Table
CREATE TABLE MoyasarPaymentWebhooks (
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,

    -- Moyasar Payment ID
    PaymentId nvarchar(100) NOT NULL,

    -- Payment Status (paid, failed, authorized, captured, refunded, voided)
    Status nvarchar(50) NOT NULL,

    -- Payment Amount in halalas (smallest currency unit)
    Amount int NOT NULL,

    -- Currency (SAR, etc.)
    Currency nvarchar(10) NOT NULL,

    -- Payment Method (creditcard, applepay, stcpay)
    PaymentMethod nvarchar(50) NULL,

    -- Metadata fields from iOS/Android
    CustomerID int NULL,
    CustomerPhoneNumber nvarchar(50) NULL,
    OfferID int NULL,
    PaymentValue decimal(18,2) NULL,

    -- Original webhook payload (JSON)
    WebhookPayload nvarchar(MAX) NOT NULL,

    -- Event type (payment.paid, payment.failed, etc.)
    EventType nvarchar(100) NOT NULL,

    -- Webhook verification
    IsVerified bit NOT NULL DEFAULT 0,

    -- Processing status
    IsProcessed bit NOT NULL DEFAULT 0,
    ProcessedAt datetime2 NULL,

    -- Timestamps
    CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt datetime2 NULL,

    -- Indexes
    CONSTRAINT IX_MoyasarPaymentWebhooks_PaymentId UNIQUE (PaymentId)
);

CREATE INDEX IX_MoyasarPaymentWebhooks_Status ON MoyasarPaymentWebhooks(Status);
CREATE INDEX IX_MoyasarPaymentWebhooks_CustomerID ON MoyasarPaymentWebhooks(CustomerID);
CREATE INDEX IX_MoyasarPaymentWebhooks_CreatedAt ON MoyasarPaymentWebhooks(CreatedAt);
