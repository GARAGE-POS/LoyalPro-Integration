-- TamaraOrders mapping table
-- Maps local OrderID with Tamara's OrderID and CheckoutID

CREATE TABLE IntegrationTamaraOrders (
    Id int IDENTITY(1,1) NOT NULL,
    OrderID int NOT NULL,                    -- References Orders.OrderID
    TamaraOrderID nvarchar(50) NOT NULL,     -- Tamara's order_id (e.g., "ffced737-3c79-4c1d-b0c2-4985a7e136d1")
    TamaraCheckoutID nvarchar(50) NOT NULL,  -- Tamara's checkout_id (e.g., "bf5b2ef3-d431-43a0-9b50-24255c233890")
    CreatedAt datetime2 NOT NULL DEFAULT GETDATE(),
    UpdatedAt datetime2 NULL,
    CONSTRAINT PK_TamaraOrders PRIMARY KEY (Id),
    CONSTRAINT FK_TamaraOrders_Orders FOREIGN KEY (OrderID) REFERENCES Orders(OrderID),
    CONSTRAINT UQ_TamaraOrders_OrderID UNIQUE (OrderID),
    CONSTRAINT UQ_TamaraOrders_TamaraOrderID UNIQUE (TamaraOrderID),
    CONSTRAINT UQ_TamaraOrders_TamaraCheckoutID UNIQUE (TamaraCheckoutID)
);

-- Indexes for performance
CREATE INDEX IX_TamaraOrders_OrderID ON TamaraOrders(OrderID);
CREATE INDEX IX_TamaraOrders_TamaraOrderID ON TamaraOrders(TamaraOrderID);
CREATE INDEX IX_TamaraOrders_TamaraCheckoutID ON TamaraOrders(TamaraCheckoutID);