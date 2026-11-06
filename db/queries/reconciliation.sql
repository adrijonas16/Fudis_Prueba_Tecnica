/* Consultas de apoyo operativo (solo lectura). */

-- Pedidos con más de una ganancia (bloquean la migración 001; efecto del bug original).
SELECT e.OrderId, COUNT(*) AS Earnings, SUM(e.Amount) AS TotalPaidToDriver, MIN(o.DeliveryFee) AS DeliveryFee
FROM dbo.DriverEarnings e
JOIN dbo.Orders o ON o.Id = e.OrderId
GROUP BY e.OrderId
HAVING COUNT(*) > 1;

-- Notificaciones que requieren revisión manual (doble cobro, monto distinto, pedido cancelado, inexistente).
SELECT Id, ReceivedAt, Outcome, OutcomeDetail, OrderId, PaymentId, Amount, Currency
FROM dbo.PaymentNotifications
WHERE RequiresReview = 1
ORDER BY ReceivedAt DESC;

-- Pedidos pagados cuya notificación al restaurante no se ha entregado (o está en dead-letter).
SELECT Id, Type, CreatedAt, Attempts, NextAttemptAt, DeadLetteredAt, LastError
FROM dbo.OutboxMessages
WHERE ProcessedAt IS NULL
ORDER BY CreatedAt;

-- Pedidos pagados sin ganancia (sin repartidor al momento del pago): pendientes del flujo de asignación.
SELECT o.Id, o.PaidAt, o.DriverId, o.DeliveryFee
FROM dbo.Orders o
WHERE o.PaymentId IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.DriverEarnings e WHERE e.OrderId = o.Id);
