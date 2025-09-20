-- Create IntegrationVomIntegrationVomUnitMappings table to store mappings between local units and Vom units
CREATE TABLE Garage_Live.dbo.IntegrationVomIntegrationVomUnitMappings (
    Id int IDENTITY(1,1) NOT NULL,
    UnitId int NOT NULL,
    VomUnitId int NOT NULL,
    LocationId int NOT NULL,
    CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt datetime2 NULL,
    CONSTRAINT PK_IntegrationVomIntegrationVomUnitMappings PRIMARY KEY (Id),
    CONSTRAINT FK_IntegrationVomIntegrationVomUnitMappings_Units FOREIGN KEY (UnitId) REFERENCES Garage_Live.dbo.Units(UnitID)
);

-- Create index for better query performance
CREATE INDEX IX_IntegrationVomIntegrationVomUnitMappings_UnitId ON Garage_Live.dbo.IntegrationVomIntegrationVomUnitMappings(UnitId);
CREATE INDEX IX_IntegrationVomIntegrationVomUnitMappings_LocationId ON Garage_Live.dbo.IntegrationVomIntegrationVomUnitMappings(LocationId);
CREATE INDEX IX_IntegrationVomIntegrationVomUnitMappings_VomUnitId ON Garage_Live.dbo.IntegrationVomIntegrationVomUnitMappings(VomUnitId);

-- Create IntegrationVomIntegrationSupplierMappings table to store mappings between local suppliers and Vom suppliers
CREATE TABLE Garage_Live.dbo.IntegrationVomIntegrationSupplierMappings (
    Id int IDENTITY(1,1) NOT NULL,
    SupplierId int NOT NULL,
    VomSupplierId int NOT NULL,
    LocationId int NOT NULL,
    CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt datetime2 NULL,
    CONSTRAINT PK_IntegrationVomIntegrationSupplierMappings PRIMARY KEY (Id),
    CONSTRAINT FK_IntegrationVomIntegrationSupplierMappings_Suppliers FOREIGN KEY (SupplierId) REFERENCES Garage_Live.dbo.Supplier(SupplierID)
);

-- Create index for better query performance
CREATE INDEX IX_IntegrationVomIntegrationSupplierMappings_SupplierId ON Garage_Live.dbo.IntegrationVomIntegrationSupplierMappings(SupplierId);
CREATE INDEX IX_IntegrationVomIntegrationSupplierMappings_LocationId ON Garage_Live.dbo.IntegrationVomIntegrationSupplierMappings(LocationId);
CREATE INDEX IX_IntegrationVomIntegrationSupplierMappings_VomSupplierId ON Garage_Live.dbo.IntegrationVomIntegrationSupplierMappings(VomSupplierId);