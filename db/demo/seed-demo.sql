/* Datos de demostración. Ejecutar después de arrancar la API en Development (aplica migraciones). */
INSERT INTO dbo.Orders (Status, DriverId, DeliveryFee, TotalAmount, Currency) VALUES
    ('PENDING_PAYMENT', 7,    5000.00, 45000.00, 'COP'),  -- caso normal
    ('PENDING_PAYMENT', NULL, 4000.00, 30000.00, 'COP'),  -- sin repartidor asignado
    ('CANCELLED',       9,    6000.00, 52000.00, 'COP');  -- pedido cancelado
SELECT Id, Status, DriverId, DeliveryFee, TotalAmount, Currency FROM dbo.Orders;
