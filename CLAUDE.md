# CLAUDE.md — Fudis · Confirmación de pagos

Guía para asistentes de IA y desarrolladores que trabajen en este repositorio. Léela antes de cambiar código.

## Qué es

API en .NET 8 que recibe el aviso de la pasarela de pagos (webhook) cuando un cliente paga un pedido. En **una sola transacción**: registra el aviso, marca el pedido como pagado, registra la ganancia del repartidor (70 % del envío) y deja pendiente el aviso al restaurante. Un worker en segundo plano envía después ese aviso, con reintentos. Es dinero: **la corrección importa más que la velocidad de entrega.**

## Comandos

```bash
docker compose up -d                                   # SQL Server en localhost,14333
dotnet build                                           # TreatWarningsAsErrors: 0 advertencias
dotnet test                                            # 91 pruebas; requiere Docker (Testcontainers)
dotnet run --project src/Fudis.Pagos.Api               # http://localhost:5080 (Development aplica migraciones)
./scripts/send-notification.ps1 -OrderId 1             # simula la pasarela (firma HMAC)
dotnet format --verify-no-changes                      # formato
dotnet list package --vulnerable --include-transitive  # debe dar 0
```

Si ya hay un SQL Server, las pruebas pueden usarlo: `FUDIS_TEST_SQLSERVER="Server=localhost,14333;User Id=sa;Password=...;TrustServerCertificate=True"`.

## Estructura

```
src/Fudis.Pagos.Api/
  Controllers/    Solo HTTP: leer cuerpo crudo, verificar firma, validar, llamar al servicio, responder.
  Services/       Lógica de negocio. ConfirmacionPagoService es el núcleo.
  Repositories/   Un repositorio por tabla. UnitOfWork los expone y maneja la transacción.
  Models/         Entities (tablas) y Contracts (entrada/salida del endpoint).
  Workers/        BackgroundService que envía los mensajes pendientes (Outbox).
  Notifications/  INotificadorRestaurante (puerto) y su implementación de ejemplo.
  Database/       FudisDbContext, migrador, health check, errores de SQL Server.
  Configuration/  Options, DependencyInjection, extensiones.
db/               migrations/ (000 = esquema original supuesto, 001 = cambios), queries/, demo/. Ver db/README.md.
tests/            UnitTests (sin BD) e IntegrationTests (SQL Server real).
```

Flujo de lectura recomendado: `PagosController` → `ConfirmacionPagoService` → `UnitOfWork` → `PedidoRepository`.

## Convenciones

- **Idioma:** los términos técnicos van en inglés (`Controller`, `Service`, `Repository`, `UnitOfWork`, `Worker`, `Options`, `Tests`). Lo de negocio va en español (`Pedido`, `Ganancia`, `ConfirmarPagoAsync`, `cancelacion`).
- **Base de datos en inglés:** tablas y columnas (`Orders.Status`, `DriverEarnings.OrderId`) **no se renombran**: ya existen en producción. El mapeo está en `FudisDbContext`.
- **Contrato externo en inglés:** la ruta `api/payments/confirm-payment`, los campos del JSON (`eventId`, `orderId`...) y la cabecera `X-Gateway-Signature` son el contrato con la pasarela. No cambiarlos sin versionar.
- **Comentarios en español simple**, pensados para un desarrollador junior. Toda clase y todo método llevan `/// <summary>`. Explican el **por qué**, no repiten el código.
- Los servicios reciben **un solo** `UnitOfWork` y acceden a los datos con `_unitOfWork.Pedidos`, `_unitOfWork.Ganancias`, etc. No se inyectan repositorios sueltos.
- Clases `sealed`, métodos asíncronos con sufijo `Async` y `CancellationToken` como último parámetro.
- La hora se obtiene con `TimeProvider.AhoraUtc()`, nunca con `DateTime.UtcNow`: es necesario para las pruebas y para la precisión de `datetime2(3)`.

## Reglas que NO se pueden romper (invariantes de dinero)

1. **Un pedido se paga una sola vez.** Se marca con el `UPDATE ... WHERE Status = 'PENDING_PAYMENT' AND TotalAmount = @monto AND Currency = @moneda` de `PedidoRepository.IntentarMarcarComoPagadoAsync`. **Nunca** reemplazarlo por "leer, preguntar con un if y guardar": ese era el error original y paga dos veces.
2. **Un aviso se procesa una sola vez.** Índice único `(Provider, EventId)` en `PaymentNotifications`.
3. **Una ganancia por pedido.** Índice único `UX_DriverEarnings_OrderId`. No quitarlo.
4. **Pago, ganancia y mensaje pendiente van en la misma transacción.** Todo lo que escriba en el flujo de pago pasa por el mismo `UnitOfWork`.
5. **Nunca llamar a servicios externos (HTTP, colas) dentro de la transacción.** Se guarda un `MensajePendiente` (Outbox) y lo envía el worker.
6. **La firma se verifica sobre el cuerpo crudo**, antes de deserializar. No usar `[FromBody]` en el webhook.
7. **Casos raros = 200 + `RequiereRevision`**, no 4xx/5xx: la pasarela no debe reintentar algo que reintentar no arregla. Solo los errores transitorios devuelven 5xx.
8. **El tiempo máximo por envío debe ser menor que el tiempo de bloqueo** del mensaje (`EnvioNotificacionesOptions.Validar`).

## Seguridad

- No subir secretos. `appsettings.json` no tiene contraseñas. En producción: variables de entorno `ConnectionStrings__Fudis` y `FirmaPasarela__Secretos__0`, o Key Vault.
- `appsettings.Development.json` tiene valores **solo de desarrollo** (contenedor local). No reutilizarlos en otros entornos.
- La API no debe arrancar si falta configuración obligatoria. Mantener la validación en `DependencyInjection.AddPagosServices`.

## Base de datos

- Cambios de esquema **solo** con scripts nuevos en `db/migrations/` (`002_...sql`). Seguir las reglas de `db/README.md`: idempotentes, separados con `GO`, y **nunca** borrar ni modificar datos de dinero automáticamente.
- No usar EF Migrations ni `EnsureCreated`: los scripts son la fuente de verdad y las pruebas los ejecutan tal cual.

## Pruebas

- Las pruebas de integración usan **SQL Server real**. No usar el proveedor InMemory ni SQLite: no tienen las transacciones, los bloqueos ni los índices únicos que se están probando.
- Todo cambio de comportamiento lleva su prueba. Todo bug corregido lleva una prueba que lo reproduzca.
- Los nombres de las pruebas describen la regla en español: `Un_pedido_cancelado_o_reembolsado_no_vuelve_a_quedar_pagado`.
- `CodigoOriginalTests` reproduce los errores del código original. No borrarlas: son la evidencia de los hallazgos.

## Antes de dar algo por terminado

1. `dotnet build`: 0 advertencias.
2. `dotnet test`: todo en verde. Si se tocó concurrencia o el Outbox, correrlo 3 veces.
3. `dotnet format --verify-no-changes`.
4. Comentarios `/// <summary>` en lo nuevo.
5. README y `db/README.md` actualizados si cambió el comportamiento, el contrato o el esquema.

## Skills del proyecto

En `.claude/skills/`:
- `nueva-migracion`: cómo agregar un cambio de base de datos de forma segura.
- `probar-api`: levantar todo y enviar avisos firmados para probar a mano.
- `revision-codigo`: lista de verificación antes de entregar o aprobar un cambio.
