---
name: probar-api
description: Levantar SQL Server y la API de Fudis Pagos en local y enviar avisos de pago firmados para probar los escenarios a mano (pago normal, repetido, cobro doble, monto incorrecto, firma falsa). Usar para demostraciones o para verificar un cambio de punta a punta.
---

# Probar la API a mano

## 1. Levantar todo

```bash
docker compose up -d                          # SQL Server en localhost,14333 (usuario sa)
dotnet run --project src/Fudis.Pagos.Api      # http://localhost:5080, entorno Development
curl http://localhost:5080/health             # debe responder: Healthy
```

En Development la API crea la base `FudisPayments` y aplica `db/migrations/` al arrancar.

## 2. Cargar pedidos de ejemplo

Ejecutar `db/demo/seed-demo.sql` (SSMS, Azure Data Studio o sqlcmd). Crea:

| Id | Estado | Repartidor | Envío | Total |
|---|---|---|---|---|
| 1 | PENDING_PAYMENT | 7 | 5 000 | 45 000 COP |
| 2 | PENDING_PAYMENT | (ninguno) | 4 000 | 30 000 COP |
| 3 | CANCELLED | 9 | 6 000 | 52 000 COP |

## 3. Enviar avisos firmados

El script calcula la firma HMAC con el secreto de desarrollo (`dev-only-secret-change-me`).

```powershell
# Windows                                                        # Resultado esperado
.\scripts\send-notification.ps1 -OrderId 1 -EventId evt_A -PaymentId pay_1                  # PROCESADO
.\scripts\send-notification.ps1 -OrderId 1 -EventId evt_A -PaymentId pay_1                  # DUPLICADO
.\scripts\send-notification.ps1 -OrderId 1 -EventId evt_B -PaymentId pay_1                  # YA_PAGADO
.\scripts\send-notification.ps1 -OrderId 1 -EventId evt_C -PaymentId pay_2                  # PAGO_EN_CONFLICTO
.\scripts\send-notification.ps1 -OrderId 2 -EventId evt_D -Amount 100.00                    # MONTO_NO_COINCIDE
.\scripts\send-notification.ps1 -OrderId 2 -EventId evt_E -Amount 30000.00                  # PROCESADO (sin ganancia)
.\scripts\send-notification.ps1 -OrderId 3 -EventId evt_F -Amount 52000.00                  # PEDIDO_NO_PAGABLE
.\scripts\send-notification.ps1 -OrderId 1 -EventId evt_G -Secret otro                      # HTTP 401
```

```bash
# Linux/macOS: ./scripts/send-notification.sh <orderId> [eventId] [amount] [paymentId] [eventType]
./scripts/send-notification.sh 1 evt_A 45000.00 pay_1
WEBHOOK_SECRET=otro ./scripts/send-notification.sh 1 evt_G      # 401
```

## 4. Revisar el resultado

```sql
SELECT Id, Status, DriverId, PaymentId, PaidAt FROM dbo.Orders;
SELECT OrderId, DriverId, Amount, SharePercent FROM dbo.DriverEarnings;           -- 1 por pedido pagado con repartidor
SELECT EventId, OrderId, Outcome, RequiresReview FROM dbo.PaymentNotifications ORDER BY Id;
SELECT Type, Attempts, ProcessedAt, LastError FROM dbo.OutboxMessages;           -- ProcessedAt lleno a los ~2 s
```

En la consola de la API debe aparecer `Restaurante notificado: pedido N pagado ...` por cada pago procesado.

## 5. Volver a empezar

Borrar la base `FudisPayments` y reiniciar la API: se vuelve a crear vacía.
