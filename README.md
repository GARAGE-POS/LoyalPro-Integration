# LoyalPro Integration - Azure Functions

This project is an Azure Functions application built with .NET 9.0 that provides integration services for the LoyalPro system.

## 🚧 Work in Progress

**Note:** This project is currently under active development. Features may be incomplete, APIs may change, and documentation may not be fully up-to-date. Please check back regularly for updates.

## Overview

The LoyalPro Integration service exposes several HTTP-triggered Azure Functions for managing customers, products, and merchant verification within the LoyalPro ecosystem.

## Features

- **Customer Management**: Functions for handling customer data and operations
- **Product Management**: Functions for managing product catalogs and inventory
- **Merchant Verification**: Functions for verifying merchant credentials and API access
- **Database Integration**: Uses Entity Framework Core with SQL Server for data persistence

## Project Structure

```
├── Functions/
│   ├── CustomerFunctions.cs    # Customer-related operations
│   ├── ProductFunctions.cs     # Product-related operations
│   └── VerifyMerchant.cs       # Merchant verification functions
├── Models/
│   ├── Category.cs
│   ├── Customer.cs
│   ├── Item.cs
│   ├── Location.cs
│   ├── SubCategory.cs
│   └── User.cs
├── Services/
│   └── ApiKeyService.cs        # API key validation service
├── Data/
│   └── V1DbContext.cs          # Entity Framework database context
└── Program.cs                  # Application entry point
```

## Prerequisites

- .NET 9.0 SDK
- Azure Functions Core Tools
- SQL Server (local or Azure SQL Database)
- Azure CLI (for deployment)

## Setup

1. **Clone the repository**
   ```bash
   git clone https://github.com/GARAGE-POS/LoyalPro-Integration.git
   cd LoyalPro-Integration
   ```

2. **Restore dependencies**
   ```bash
   dotnet restore
   ```

3. **Configure local settings**
   - Copy `local.settings.json` and update connection strings and configuration values
   - Ensure your SQL Server connection string is properly configured

4. **Database setup**
   - Run Entity Framework migrations to create/update the database schema
   ```bash
   dotnet ef database update
   ```

## Running Locally

1. **Build the project**
   ```bash
   dotnet build
   ```

2. **Start the Azure Functions host**
   ```bash
   func start
   ```

The functions will be available at `http://localhost:7071` by default.

## Deployment

### Azure Deployment

1. **Build for release**
   ```bash
   dotnet publish --configuration Release
   ```

2. **Deploy to Azure Functions**
   ```bash
   func azure functionapp publish <your-function-app-name>
   ```

### Environment Variables

Configure the following environment variables in your Azure Function App:

- `ConnectionStrings__DefaultConnection`: SQL Server connection string
- `AzureWebJobsStorage`: Azure Storage account connection string
- Other application-specific settings as needed

## API Endpoints

The following functions are available (subject to change during development):

- **CreateCustomer**: `POST /api/CreateCustomer` - Create a new customer
- **Customers**: `GET /api/Customers` - Retrieve customer list
- **GetProducts**: `GET /api/GetProducts` - Retrieve product list
- **UpdateCustomer**: `PUT /api/UpdateCustomer/{customerId}` - Update an existing customer
- **VerifyMerchant**: `GET /api/VerifyMerchant` - Verify merchant credentials

Detailed API documentation will be provided as the project matures.

## Contributing

This project is in active development. Contributions are welcome, but please note that the codebase may undergo significant changes.

## License

[Add license information here]

## Contact

For questions or support, please contact the development team.
