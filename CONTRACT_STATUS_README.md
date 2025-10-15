# Contract Status Tracking Implementation

## Overview
This implementation adds database tracking for Sadeq digital signature contracts, supporting both PDF uploads and template-based contracts.

## Database Schema

### ContractStatus Table
Created in: `ContractStatusTable.sql`

| Column | Type | Description |
|--------|------|-------------|
| Id | INT (PK) | Auto-incrementing primary key |
| CompanyName | NVARCHAR(255) | Customer company name |
| PhoneNumber | NVARCHAR(20) | Customer phone number |
| Terminals | NVARCHAR(500) | Terminal IDs (optional) |
| NationalId | NVARCHAR(20) | National ID number |
| Email | NVARCHAR(255) | Customer email address |
| SadqSent | BIT | Whether contract was sent successfully |
| Signed | BIT | Whether contract has been signed |
| EnvelopId | NVARCHAR(255) | Sadeq envelope ID |
| DocumentId | NVARCHAR(255) | Sadeq document ID |
| TemplateId | NVARCHAR(255) | Template ID (for template-based contracts) |
| PdfFileName | NVARCHAR(500) | PDF filename (for PDF-based contracts) |
| CreatedAt | DATETIME2 | When record was created |
| UpdatedAt | DATETIME2 | When record was last updated |
| SignedAt | DATETIME2 | When contract was signed |
| Notes | NVARCHAR(1000) | Additional notes |
| ErrorMessage | NVARCHAR(1000) | Error message if failed |

### Indexes
- `IX_ContractStatus_NationalId` - Fast lookup by national ID
- `IX_ContractStatus_Email` - Fast lookup by email
- `IX_ContractStatus_PhoneNumber` - Fast lookup by phone
- `IX_ContractStatus_DocumentId` - Fast lookup by document ID
- `IX_ContractStatus_SadqSent_Signed` - Fast filtering by status
- `IX_ContractStatus_CreatedAt` - Fast date-based queries

## Entity Framework Model
Created in: `Models/ContractStatus.cs`

The model includes:
- All database columns as properties
- Data annotations for validation
- Documentation comments
- Support for both PDF and template-based workflows

## API Endpoints

### 1. Template-Based Contract (`sadeq_request`)
**Endpoint:** `POST /api/sadeq_request`

**Request Body (JSON):**
```json
{
  "destinationName": "Company Name",
  "destinationEmail": "email@example.com",
  "destinationPhoneNumber": "+966123456789",
  "nationalId": "1234567890",
  "templateId": "template-uuid",
  "terminals": "T001, T002" // optional
}
```

**Workflow:**
1. Validates required fields
2. Gets Sadeq access token
3. Initiates envelope from template
4. Sends invitation to recipient
5. **Saves contract status to database** with:
   - TemplateId populated
   - PdfFileName = null
   - DocumentId and EnvelopId from API response
   - SadqSent = true/false based on API success

### 2. PDF-Based Contract (`sadeq_upload_pdf`)
**Endpoint:** `POST /api/sadeq_upload_pdf`

**Request Body (multipart/form-data):**
- `file`: PDF file
- `companyName`: Company name
- `phoneNumber`: Phone number
- `email`: Email address
- `nationalId`: National ID
- `terminals`: Terminal IDs (optional)

**Workflow:**
1. Validates PDF file and required fields
2. Converts PDF to base64
3. Gets Sadeq access token
4. Initiates envelope with base64 PDF
5. **Saves contract status to database** with:
   - PdfFileName populated
   - TemplateId = null
   - DocumentId and EnvelopId from API response
   - SadqSent = true/false based on API success

## Usage Examples

### Example 1: Send contract using template
```bash
curl -X POST https://your-function-app.azurewebsites.net/api/sadeq_request \
  -H "Content-Type: application/json" \
  -d '{
    "destinationName": "Karage Restaurant",
    "destinationEmail": "manager@karage.sa",
    "destinationPhoneNumber": "+966501234567",
    "nationalId": "1091456993",
    "templateId": "abc-123-template-id",
    "terminals": "T001, T002, T003"
  }'
```

### Example 2: Upload PDF contract
```bash
curl -X POST https://your-function-app.azurewebsites.net/api/sadeq_upload_pdf \
  -F "file=@contract.pdf" \
  -F "companyName=Karage Restaurant" \
  -F "phoneNumber=+966501234567" \
  -F "email=manager@karage.sa" \
  -F "nationalId=1091456993" \
  -F "terminals=T001, T002, T003"
```

## Database Queries

### Find all unsigned contracts
```sql
SELECT * FROM ContractStatus 
WHERE SadqSent = 1 AND Signed = 0 
ORDER BY CreatedAt DESC;
```

### Find contracts by customer
```sql
SELECT * FROM ContractStatus 
WHERE NationalId = '1091456993' 
   OR Email = 'manager@karage.sa'
ORDER BY CreatedAt DESC;
```

### Check contract status by document ID
```sql
SELECT 
    CompanyName,
    Email,
    SadqSent,
    Signed,
    SignedAt,
    CreatedAt,
    CASE 
        WHEN TemplateId IS NOT NULL THEN 'Template: ' + TemplateId
        WHEN PdfFileName IS NOT NULL THEN 'PDF: ' + PdfFileName
        ELSE 'Unknown'
    END AS ContractType
FROM ContractStatus 
WHERE DocumentId = 'your-document-id';
```

### Get signing statistics
```sql
SELECT 
    COUNT(*) AS TotalContracts,
    SUM(CASE WHEN SadqSent = 1 THEN 1 ELSE 0 END) AS SentSuccessfully,
    SUM(CASE WHEN Signed = 1 THEN 1 ELSE 0 END) AS SignedContracts,
    SUM(CASE WHEN SadqSent = 1 AND Signed = 0 THEN 1 ELSE 0 END) AS PendingSignature
FROM ContractStatus;
```

## Deployment Steps

1. **Run SQL migration:**
   ```sql
   -- Execute ContractStatusTable.sql on your database
   ```

2. **Build and publish the function:**
   ```bash
   dotnet build
   dotnet publish --configuration Release
   ```

3. **Deploy to Azure Functions**

4. **Test endpoints** using the examples above

## Future Enhancements

1. **Webhook endpoint** to update `Signed` status when Sadeq notifies of signature completion
2. **Query endpoint** to retrieve contract status by various filters
3. **Dashboard** to view all contracts and their statuses
4. **Automated reminders** for unsigned contracts
5. **Bulk upload** functionality for multiple contracts

## Notes

- Both `TemplateId` and `PdfFileName` are optional, but one should be populated
- `SadqSent` indicates API success, not delivery success
- `Signed` should be updated via webhook when customer signs
- Timestamps are stored in UTC
- `Terminals` field can store comma-separated terminal IDs or any format you prefer
