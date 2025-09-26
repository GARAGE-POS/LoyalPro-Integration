
-- Garage_UAT.dbo.inv_Bill definition

-- Drop table

-- DROP TABLE Garage_UAT.dbo.inv_Bill;

CREATE TABLE Garage_UAT.dbo.inv_Bill (
	BillID int IDENTITY(1,1) NOT NULL,
	PurchaseOrderID int NULL,
	BillNo nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[Date] datetime NULL,
	DueDate datetime NULL,
	Remarks nvarchar(250) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	SubTotal float NULL,
	Discount float NULL,
	Tax float NULL,
	Total float NULL,
	ImagePath nvarchar(MAX) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	PaymentStatus int NULL,
	LastUpdatedDate datetime NULL,
	LastUpdatedBy nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	CreateOn datetime NULL,
	CreatedBy nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	StatusID int NULL,
	LocationID int NOT NULL,
	StoreID int NULL,
	SupplierID int NULL,
	CONSTRAINT PK_Bill PRIMARY KEY (BillID)
);


-- Garage_UAT.dbo.inv_Bill foreign keys

ALTER TABLE Garage_UAT.dbo.inv_Bill ADD CONSTRAINT FK_Bill_Locations FOREIGN KEY (LocationID) REFERENCES Garage_UAT.dbo.Locations(LocationID);
ALTER TABLE Garage_UAT.dbo.inv_Bill ADD CONSTRAINT FK_Bill_PurchaseOrder FOREIGN KEY (PurchaseOrderID) REFERENCES Garage_UAT.dbo.inv_PurchaseOrder(PurchaseOrderID);
ALTER TABLE Garage_UAT.dbo.inv_Bill ADD CONSTRAINT FK_inv_Bill_Stores FOREIGN KEY (StoreID) REFERENCES Garage_UAT.dbo.Stores(StoreID);
ALTER TABLE Garage_UAT.dbo.inv_Bill ADD CONSTRAINT FK_inv_Bill_Supplier FOREIGN KEY (SupplierID) REFERENCES Garage_UAT.dbo.Supplier(SupplierID);


-- Garage_UAT.dbo.inv_BillDetail definition

-- Drop table

-- DROP TABLE Garage_UAT.dbo.inv_BillDetail;

CREATE TABLE Garage_UAT.dbo.inv_BillDetail (
	BillDetailID int IDENTITY(1,1) NOT NULL,
	BillID int NULL,
	ItemID int NULL,
	Cost float NULL,
	Price float NULL,
	Quantity int NULL,
	Total float NULL,
	StatusID int NULL,
	Remarks nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	LastUpdatedDate datetime NULL,
	LastUpdatedBy nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	CreatedOn datetime NULL,
	CreatedBy nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	CONSTRAINT PK_inv_BillDetail PRIMARY KEY (BillDetailID)
);


-- Garage_UAT.dbo.inv_BillDetail foreign keys

ALTER TABLE Garage_UAT.dbo.inv_BillDetail ADD CONSTRAINT FK_BillDetail_Bill FOREIGN KEY (BillID) REFERENCES Garage_UAT.dbo.inv_Bill(BillID);
ALTER TABLE Garage_UAT.dbo.inv_BillDetail ADD CONSTRAINT FK_BillDetail_Items FOREIGN KEY (ItemID) REFERENCES Garage_UAT.dbo.Items(ItemID);


-- Garage_UAT.dbo.inv_Reconciliation definition

-- Drop table

-- DROP TABLE Garage_UAT.dbo.inv_Reconciliation;

CREATE TABLE Garage_UAT.dbo.inv_Reconciliation (
	ReconciliationID int IDENTITY(1,1) NOT NULL,
	PurchaseOrderID int NULL,
	Code nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[Date] datetime NULL,
	Reason nvarchar(250) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	LastUpdatedDate datetime NULL,
	LastUpdatedBy nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	StatusID int NULL,
	LocationID int NOT NULL,
	StoreID int NULL,
	UserID int NULL,
	CONSTRAINT PK_Reconciliation PRIMARY KEY (ReconciliationID)
);


-- Garage_UAT.dbo.inv_Reconciliation foreign keys

ALTER TABLE Garage_UAT.dbo.inv_Reconciliation ADD CONSTRAINT FK_Reconciliation_Locations FOREIGN KEY (LocationID) REFERENCES Garage_UAT.dbo.Locations(LocationID);
ALTER TABLE Garage_UAT.dbo.inv_Reconciliation ADD CONSTRAINT FK_inv_Reconciliation_Stores FOREIGN KEY (StoreID) REFERENCES Garage_UAT.dbo.Stores(StoreID);


-- Garage_UAT.dbo.inv_ReconciliationDetail definition

-- Drop table

-- DROP TABLE Garage_UAT.dbo.inv_ReconciliationDetail;

CREATE TABLE Garage_UAT.dbo.inv_ReconciliationDetail (
	ReconciliationDetailID int IDENTITY(1,1) NOT NULL,
	ReconciliationID int NULL,
	ItemID int NOT NULL,
	Cost float NULL,
	Price float NULL,
	Quantity int NULL,
	Total float NULL,
	StatusID int NULL,
	Reason nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	CONSTRAINT PK_ReconciliationDetail PRIMARY KEY (ReconciliationDetailID)
);


-- Garage_UAT.dbo.inv_ReconciliationDetail foreign keys

ALTER TABLE Garage_UAT.dbo.inv_ReconciliationDetail ADD CONSTRAINT FK_ReconciliationDetail_Items FOREIGN KEY (ItemID) REFERENCES Garage_UAT.dbo.Items(ItemID);
ALTER TABLE Garage_UAT.dbo.inv_ReconciliationDetail ADD CONSTRAINT FK_ReconciliationDetail_Reconciliation FOREIGN KEY (ReconciliationID) REFERENCES Garage_UAT.dbo.inv_Reconciliation(ReconciliationID);