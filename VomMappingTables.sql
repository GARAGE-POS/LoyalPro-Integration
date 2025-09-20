-- Create UnitMappings table to store mappings between local units and Vom units
CREATE TABLE Garage_Live.dbo.UnitMappings (
    Id int IDENTITY(1,1) NOT NULL,
    UnitId int NOT NULL,
    VomUnitId int NOT NULL,
    LocationId int NOT NULL,
    CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt datetime2 NULL,
    CONSTRAINT PK_UnitMappings PRIMARY KEY (Id),
    CONSTRAINT FK_UnitMappings_Units FOREIGN KEY (UnitId) REFERENCES Garage_Live.dbo.Units(UnitID)
);

-- Create index for better query performance
CREATE INDEX IX_UnitMappings_UnitId ON Garage_Live.dbo.UnitMappings(UnitId);
CREATE INDEX IX_UnitMappings_LocationId ON Garage_Live.dbo.UnitMappings(LocationId);
CREATE INDEX IX_UnitMappings_VomUnitId ON Garage_Live.dbo.UnitMappings(VomUnitId);