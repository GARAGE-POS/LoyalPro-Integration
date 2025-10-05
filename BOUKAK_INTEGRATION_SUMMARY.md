# Boukak Loyalty Integration - Implementation Summary

## Overview
Complete Azure Functions integration with the Boukak loyalty platform API. This integration enables customer loyalty card creation and stamp management for the local ERP system.

## Files Created

### 1. Services/BoukakApiService.cs
**Purpose**: API client service for communicating with Boukak's partner API

**Key Features**:
- **Base URL**: Uses sandbox environment (`https://sandbox.api.partners.boukak.com`)
- **Authentication**: API key-based authentication in headers
- **Methods**:
  - `CreateCustomerCardAsync()`: Creates loyalty cards for customers
  - `AddStampsAsync()`: Adds stamps to customer cards

**Models Included**:
- Request models: `BoukakCustomerCardRequest`, `BoukakAddStampsRequest`
- Response models: `BoukakCustomerCardResponse`, `BoukakAddStampsResponse`
- Webhook models: `BoukakWebhookPayload`, `BoukakWebhookData`

**Configuration**:
```csharp
API Key: 7vojBs2S3OGtW7KwP8Mnr+z1QQ+Ps4p4nVmH8CgeruW/... (configured for sandbox)
Sandbox URL: https://sandbox.api.partners.boukak.com
```

### 2. Models/BoukakCustomerMapping.cs
**Purpose**: Database model for mapping local customers to Boukak loyalty cards

**Schema**:
```csharp
- Id: Primary key
- CustomerId: Local customer ID (FK to Customers)
- BoukakCustomerId: Boukak customer ID from API response header
- BoukakCardId: Boukak card ID from API response header
- LocationId: Location context (FK to Locations)
- CreatedAt: Timestamp
- UpdatedAt: Nullable timestamp
```

### 3. Functions/BoukakFunctions.cs
**Purpose**: Azure Functions for Boukak integration endpoints

**Endpoints Implemented**:

#### A. CreateBoukakCustomerCard (POST)
- **Route**: `/api/CreateBoukakCustomerCard`
- **Authentication**: Session-based (Bearer token)
- **Purpose**: Creates a Boukak loyalty card for a local customer
- **Request Body**:
  ```json
  {
    "customerId": 123,
    "templateId": "optional-template-id",
    "platform": "android|iOS",
    "language": "en|ar",
    "initialCashback": 0
  }
  ```
- **Response**:
  ```json
  {
    "message": "Boukak customer card created successfully",
    "customerId": 123,
    "boukakCustomerId": "...",
    "boukakCardId": "...",
    "applePassUrl": "...",
    "passWalletUrl": "...",
    "status": "created|existing"
  }
  ```

#### B. AddBoukakStamps (POST)
- **Route**: `/api/AddBoukakStamps`
- **Authentication**: Session-based (Bearer token)
- **Purpose**: Adds loyalty stamps when orders are completed
- **Request Body**:
  ```json
  {
    "orderId": 456,
    "stamps": 1
  }
  ```
- **Validation**:
  - Order must exist and belong to the location
  - Order must have a customer
  - Order status must be 600 (completed)
  - Customer must have a Boukak card
- **Response**:
  ```json
  {
    "message": "Stamps added successfully",
    "orderId": 456,
    "customerId": 123,
    "boukakCardId": "...",
    "stampsAdded": 1,
    "activeStamps": 5,
    "rewards": 0
  }
  ```

#### C. BoukakWebhook (POST)
- **Route**: `/api/BoukakWebhook`
- **Authentication**: None (public endpoint for Boukak callbacks)
- **Purpose**: Receives webhook events from Boukak
- **Supported Events**:
  - `CARD_INSTALLED`: Customer installed the loyalty pass
  - `CARD_UNINSTALLED`: Customer uninstalled the loyalty pass
- **Webhook Payload**:
  ```json
  {
    "event": "CARD_INSTALLED|CARD_UNINSTALLED",
    "data": {
      "cardId": "...",
      "customerId": "..."
    }
  }
  ```
- **Response**: Always returns HTTP 200 OK to acknowledge receipt

### 4. BoukakMappingTable.sql
**Purpose**: Database schema for Boukak integration

**Tables Created**:

#### IntegrationBoukakCustomerMappings
```sql
CREATE TABLE IntegrationBoukakCustomerMappings (
    Id int IDENTITY(1,1) NOT NULL,
    CustomerId int NOT NULL,
    BoukakCustomerId nvarchar(100) NOT NULL,
    BoukakCardId nvarchar(100) NOT NULL,
    LocationId int NOT NULL,
    CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt datetime2 NULL,
    CONSTRAINT PK_IntegrationBoukakCustomerMappings PRIMARY KEY (Id),
    CONSTRAINT FK_IntegrationBoukakCustomerMappings_Customers
        FOREIGN KEY (CustomerId) REFERENCES Customers(CustomerID),
    CONSTRAINT FK_IntegrationBoukakCustomerMappings_Locations
        FOREIGN KEY (LocationId) REFERENCES Locations(LocationID)
);
```

**Indexes**:
- `IX_IntegrationBoukakCustomerMappings_CustomerId`
- `IX_IntegrationBoukakCustomerMappings_LocationId`
- `IX_IntegrationBoukakCustomerMappings_BoukakCardId`
- `IX_IntegrationBoukakCustomerMappings_BoukakCustomerId`
- `UQ_IntegrationBoukakCustomerMappings_Customer_Location` (unique constraint)

#### IntegrationBoukakWebhookLogs (Optional)
```sql
CREATE TABLE IntegrationBoukakWebhookLogs (
    Id int IDENTITY(1,1) NOT NULL,
    EventType nvarchar(50) NOT NULL,
    BoukakCardId nvarchar(100) NULL,
    BoukakCustomerId nvarchar(100) NULL,
    Payload nvarchar(max) NULL,
    ProcessedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT PK_IntegrationBoukakWebhookLogs PRIMARY KEY (Id)
);
```

### 5. Updated Files

#### Data/V1DbContext.cs
**Changes**:
- Added `DbSet<BoukakCustomerMapping>` property
- Configured entity relationships for `BoukakCustomerMapping`
- Added foreign key constraints with `DeleteBehavior.Restrict`

#### Program.cs
**Changes**:
- Registered `IBoukakApiService` and `BoukakApiService` as scoped services
- Added HttpClient configuration for `BoukakApiService` with 30-second timeout

## Architecture Pattern

This implementation follows the **same architecture pattern** as the existing VOM integration:

1. **Service Layer**: `BoukakApiService` handles all API communication
2. **Models Layer**: Separate models for API requests/responses and database entities
3. **Functions Layer**: Azure Functions expose HTTP endpoints with session authentication
4. **Data Layer**: Entity Framework mappings in `V1DbContext`
5. **Database Layer**: SQL mapping tables to track integration state

## Authentication Flow

1. **Session Authentication** (for customer and stamp endpoints):
   - Client sends `Authorization: Bearer POS-xxxxx` header
   - `SessionAuthService` validates session and extracts user/location context
   - All operations are scoped to the authenticated location

2. **API Key Authentication** (for Boukak API):
   - Service adds `api-key` header to all outbound requests
   - Uses sandbox API key stored in `BoukakApiService`

## Usage Examples

### 1. Create Customer Card
```bash
curl -X POST "https://<your-function-app>/api/CreateBoukakCustomerCard" \
  -H "Authorization: Bearer POS-3d6kqv" \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": 123,
    "platform": "android",
    "language": "en",
    "initialCashback": 50
  }'
```

### 2. Add Stamps to Order
```bash
curl -X POST "https://<your-function-app>/api/AddBoukakStamps" \
  -H "Authorization: Bearer POS-3d6kqv" \
  -H "Content-Type: application/json" \
  -d '{
    "orderId": 456,
    "stamps": 1
  }'
```

### 3. Webhook Endpoint (for Boukak to call)
```bash
# Boukak will POST to this endpoint when events occur
POST https://<your-function-app>/api/BoukakWebhook
Content-Type: application/json

{
  "event": "CARD_INSTALLED",
  "data": {
    "cardId": "abc123",
    "customerId": "xyz789"
  }
}
```

## Database Setup

Run the SQL script to create required tables:
```bash
sqlcmd -S <server> -d Garage_UAT -i BoukakMappingTable.sql
```

Or manually execute the SQL from `/workspaces/functions/BoukakMappingTable.sql`

## Error Handling

The implementation includes comprehensive error handling:

1. **Customer Card Creation**:
   - Validates customer exists and is active
   - Prevents duplicate card creation (checks existing mappings)
   - Validates API response includes customer ID and card ID
   - Logs all errors for debugging

2. **Stamp Addition**:
   - Validates order exists and belongs to location
   - Validates order has a customer
   - Validates order status is completed (StatusID = 600)
   - Validates customer has a Boukak card
   - Handles API failures gracefully

3. **Webhooks**:
   - Always returns HTTP 200 to prevent retries
   - Logs all webhook payloads for debugging
   - Handles unknown event types gracefully

## Configuration Requirements

### API Key
The Boukak API key is hardcoded in `BoukakApiService.cs`:
```csharp
private const string ApiKey = "7vojBs2S3OGtW7KwP8Mnr+z1QQ+Ps4p4nVmH8CgeruW/...";
```

**For production**: Move to configuration/environment variables.

### Template ID
Default template ID is set to `"default-template-id"` in `BoukakFunctions.cs`:
```csharp
private const string DefaultTemplateId = "default-template-id";
```

**For production**: Configure per location or retrieve from database.

### Environment
Currently using sandbox: `https://sandbox.api.partners.boukak.com`

**For production**: Update `BaseUrl` property in `BoukakApiService.cs` to:
```csharp
private string BaseUrl => "https://api.partners.boukak.com";
```

## Integration Points

### Customer Creation Flow
```
Local System → Create Customer
    ↓
Call CreateBoukakCustomerCard endpoint
    ↓
BoukakApiService → Boukak API (POST /v1/create-customer-card)
    ↓
Receive customer ID & card ID in response headers
    ↓
Store mapping in IntegrationBoukakCustomerMappings
    ↓
Return pass URLs to customer
```

### Order Completion Flow
```
Local System → Complete Order (StatusID = 600)
    ↓
Call AddBoukakStamps endpoint
    ↓
Validate order and customer
    ↓
Lookup Boukak card ID from mapping
    ↓
BoukakApiService → Boukak API (POST /v1/add-stamps)
    ↓
Update local customer points (optional)
    ↓
Return updated stamp count
```

### Webhook Flow
```
Boukak → POST to /api/BoukakWebhook
    ↓
Parse event type (CARD_INSTALLED/CARD_UNINSTALLED)
    ↓
Find customer by card ID in mappings
    ↓
Execute business logic (welcome message, feedback request, etc.)
    ↓
Return HTTP 200 OK
```

## Testing Checklist

- [ ] Create mapping table in database
- [ ] Update API key and template ID with real values
- [ ] Test customer card creation with valid customer
- [ ] Verify mapping is stored correctly
- [ ] Test duplicate card creation prevention
- [ ] Test stamp addition with completed order
- [ ] Test stamp addition validation (order status, customer card)
- [ ] Configure webhook URL with Boukak
- [ ] Test CARD_INSTALLED webhook event
- [ ] Test CARD_UNINSTALLED webhook event
- [ ] Verify error handling for all endpoints
- [ ] Check logging output for debugging

## Security Considerations

1. **API Key Security**:
   - Currently hardcoded in source
   - Should be moved to Azure Key Vault or environment variables

2. **Webhook Security**:
   - Currently no authentication on webhook endpoint
   - Boukak docs don't specify verification method
   - Consider IP whitelisting or shared secret validation

3. **Session Validation**:
   - All customer/stamp endpoints use session authentication
   - Prevents unauthorized access to customer data

## Future Enhancements

1. **Configurable Template IDs**: Store template IDs per location in database
2. **Webhook Logging**: Enable `IntegrationBoukakWebhookLogs` table for audit trail
3. **Automatic Stamp Rules**: Configure stamp quantities based on order amount
4. **Customer Notifications**: Send SMS/email when cards are created
5. **Cashback Management**: Implement cashback tracking and redemption
6. **Multi-language Support**: Allow language selection per customer preference
7. **Retry Logic**: Add retry mechanism for failed API calls
8. **Monitoring**: Add Application Insights tracking for API calls

## Dependencies

- **Microsoft.EntityFrameworkCore**: Database access
- **Microsoft.AspNetCore**: HTTP request handling
- **System.Text.Json**: JSON serialization
- **Azure Functions Worker**: Function runtime
- **HttpClient**: API communication

## Support & Troubleshooting

### Common Issues

1. **Customer card not created**:
   - Check API key is valid
   - Verify customer mobile number format
   - Check Boukak API logs for detailed error

2. **Stamps not added**:
   - Ensure order status is 600 (completed)
   - Verify customer has a Boukak card (check mappings table)
   - Check card ID is valid in Boukak system

3. **Webhooks not received**:
   - Verify webhook URL is publicly accessible
   - Check firewall/network settings
   - Confirm webhook URL is registered with Boukak

### Debug Logging

All operations log to Application Insights:
- Customer card creation attempts and results
- Stamp addition requests and responses
- Webhook payloads and processing
- API errors with full response bodies

## File Locations Summary

```
/workspaces/functions/
├── Services/
│   └── BoukakApiService.cs          # API client service
├── Models/
│   └── BoukakCustomerMapping.cs     # Database mapping model
├── Functions/
│   └── BoukakFunctions.cs           # Azure Functions endpoints
├── Data/
│   └── V1DbContext.cs               # Updated with Boukak DbSet
├── Program.cs                        # Updated service registration
└── BoukakMappingTable.sql           # Database schema
```

## API Documentation

Full Boukak API documentation: https://boukak.gitbook.io/boukak-api

Key endpoints used:
- `POST /v1/create-customer-card`: Customer card creation
- `POST /v1/add-stamps`: Stamp addition
- Webhooks: `CARD_INSTALLED`, `CARD_UNINSTALLED`

## Build Status

✅ **Build Successful** - All files compile without errors
✅ **Dependencies Registered** - Services configured in DI container
✅ **Database Models** - Entity Framework configuration complete
✅ **API Integration** - Boukak API client implemented
✅ **Authentication** - Session-based auth integrated
✅ **Error Handling** - Comprehensive validation and logging
