CREATE TABLE dbo.MapUniqueItemID (
    ProductName NVARCHAR(255),
    ItemID INT,
    LocationID INT,
    LocationName NVARCHAR(255),
    UniqueItemID INT
);


INSERT INTO dbo.MapUniqueItemID (ProductName, ItemID, LocationID, LocationName, UniqueItemID)
SELECT
    i.Name AS ProductName,
    inv.ItemID,
    l.LocationID,
    l.Name AS LocationName,
    MIN(inv.ItemID) OVER (PARTITION BY i.Name) AS UniqueItemID
FROM dbo.Items i
JOIN dbo.Inventory inv ON i.ItemID = inv.ItemID
JOIN dbo.Locations l ON inv.LocationID = l.LocationID
ORDER BY i.Name, inv.ItemID, l.LocationID;

