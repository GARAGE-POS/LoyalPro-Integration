-- Create IntegrationVomIntegrationVomUnitMappings table to store mappings between local units and Vom units
CREATE TABLE Garage_UAT.dbo.IntegrationVomIntegrationVomUnitMappings (
    Id int IDENTITY(1,1) NOT NULL,
    UnitId int NOT NULL,
    VomUnitId int NOT NULL,
    LocationId int NOT NULL,
    CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt datetime2 NULL,
    CONSTRAINT PK_IntegrationVomIntegrationVomUnitMappings PRIMARY KEY (Id),
    CONSTRAINT FK_IntegrationVomIntegrationVomUnitMappings_Units FOREIGN KEY (UnitId) REFERENCES Garage_UAT.dbo.Units(UnitID)
);

-- Create index for better query performance
CREATE INDEX IX_IntegrationVomIntegrationVomUnitMappings_UnitId ON Garage_UAT.dbo.IntegrationVomIntegrationVomUnitMappings(UnitId);
CREATE INDEX IX_IntegrationVomIntegrationVomUnitMappings_LocationId ON Garage_UAT.dbo.IntegrationVomIntegrationVomUnitMappings(LocationId);
CREATE INDEX IX_IntegrationVomIntegrationVomUnitMappings_VomUnitId ON Garage_UAT.dbo.IntegrationVomIntegrationVomUnitMappings(VomUnitId);

-- Create IntegrationVomIntegrationSupplierMappings table to store mappings between local suppliers and Vom suppliers
CREATE TABLE Garage_UAT.dbo.IntegrationVomIntegrationSupplierMappings (
    Id int IDENTITY(1,1) NOT NULL,
    SupplierId int NOT NULL,
    VomSupplierId int NOT NULL,
    LocationId int NOT NULL,
    CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt datetime2 NULL,
    CONSTRAINT PK_IntegrationVomIntegrationSupplierMappings PRIMARY KEY (Id),
    CONSTRAINT FK_IntegrationVomIntegrationSupplierMappings_Suppliers FOREIGN KEY (SupplierId) REFERENCES Garage_UAT.dbo.Supplier(SupplierID)
);

-- Create index for better query performance
CREATE INDEX IX_IntegrationVomIntegrationSupplierMappings_SupplierId ON Garage_UAT.dbo.IntegrationVomIntegrationSupplierMappings(SupplierId);
CREATE INDEX IX_IntegrationVomIntegrationSupplierMappings_LocationId ON Garage_UAT.dbo.IntegrationVomIntegrationSupplierMappings(LocationId);
CREATE INDEX IX_IntegrationVomIntegrationSupplierMappings_VomSupplierId ON Garage_UAT.dbo.IntegrationVomIntegrationSupplierMappings(VomSupplierId);

-- Create IntegrationVomIntegrationCategoryMappings table to store mappings between local categories and Vom categories
CREATE TABLE Garage_UAT.dbo.IntegrationVomIntegrationCategoryMappings (
    Id int IDENTITY(1,1) NOT NULL,
    CategoryId int NOT NULL,
    VomCategoryId int NOT NULL,
    LocationId int NOT NULL,
    CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt datetime2 NULL,
    CONSTRAINT PK_IntegrationVomIntegrationCategoryMappings PRIMARY KEY (Id),
    CONSTRAINT FK_IntegrationVomIntegrationCategoryMappings_Categories FOREIGN KEY (CategoryId) REFERENCES Garage_UAT.dbo.Category(CategoryID)
);

-- Create index for better query performance
CREATE INDEX IX_IntegrationVomIntegrationCategoryMappings_CategoryId ON Garage_UAT.dbo.IntegrationVomIntegrationCategoryMappings(CategoryId);
CREATE INDEX IX_IntegrationVomIntegrationCategoryMappings_LocationId ON Garage_UAT.dbo.IntegrationVomIntegrationCategoryMappings(LocationId);
CREATE INDEX IX_IntegrationVomIntegrationCategoryMappings_VomCategoryId ON Garage_UAT.dbo.IntegrationVomIntegrationCategoryMappings(VomCategoryId);