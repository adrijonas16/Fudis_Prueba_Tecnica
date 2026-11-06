# Fudis · Confirmación de pagos

Cuando un cliente paga, la pasarela de pagos llama a nuestro endpoint `confirm-payment`. Este marca el pedido como pagado, registra la ganancia del repartidor (70 % del envío) y avisa al restaurante.

**¿El código original está listo para producción? No.** Pasa las pruebas funcionales porque esas pruebas envían un aviso válido, una sola vez y sin fallos. En producción hay reintentos, avisos simultáneos, caídas y llamadas falsas, y en esos casos el código pierde o duplica dinero. Los problemas principales están **demostrados con pruebas** (`CodigoOriginalTests.cs`).

---

## 1. Cómo ejecutarlo

| Herramienta | Versión |
|---|---|
| .NET SDK | 8.0 o superior (probado con 8.0.133 y 9.0.302) |
| Entity Framework Core | 8.0.33 (último parche de .NET 8 LTS) |
| SQL Server | 2022, en Docker |
| xUnit / Testcontainers | 2.9.3 / 4.16.0 |

```bash
# 1. Levantar SQL Server (puerto 14333)
docker compose up -d

# 2. Ejecutar la API. En Development crea la base y aplica las migraciones al arrancar.
#    En Visual Studio: abrir Fudis.Pagos.sln y presionar F5.
dotnet run --project src/Fudis.Pagos.Api            # http://localhost:5080

# 3. Cargar pedidos de ejemplo: ejecutar db/demo/seed-demo.sql en SSMS

# 4. Simular la pasarela (envía un aviso firmado)
./scripts/send-notification.ps1 -OrderId 1 -EventId evt_1     # Windows
./scripts/send-notification.sh 1 evt_1                        # Linux/macOS

# 5. Pruebas (necesita Docker abierto: levanta su propio SQL Server)
dotnet test
```

---

### Configuración en producción

`appsettings.json` **no tiene contraseñas**. En producción se configuran con variables de entorno (o Key Vault):

```
ConnectionStrings__Fudis = Server=...;Database=...;User Id=...;Password=...
FirmaPasarela__Secretos__0 = <secreto compartido con la pasarela>
```

Si falta alguna, o la configuración del envío no tiene sentido, **la API no arranca** y dice qué falta. Es mejor que falle al desplegar que con el primer pago.
`GET /health` revisa la conexión a SQL Server y responde en máximo 3 s: 200 si está sana, 503 si no.

---

## 2. Estructura del código

Se usa **Controller → Service → Repository**, todo en un solo proyecto:

```
src/Fudis.Pagos.Api/
├── Controllers/
│   └── PagosController.cs                Recibe el aviso HTTP, verifica la firma y valida el JSON. Nada de lógica.
├── Services/
│   ├── ConfirmacionPagoService.cs        ★ LÓGICA PRINCIPAL: los 5 pasos del pago en una transacción.
│   ├── EnvioNotificacionesService.cs     Envía los avisos pendientes al restaurante, con reintentos.
│   ├── VerificacionFirmaService.cs       Comprueba que el aviso viene de la pasarela (HMAC).
│   └── CalculoGananciaService.cs         La regla del 70 % y el redondeo.
├── Repositories/
│   ├── UnitOfWork.cs                     ★ Expone todos los repositorios y maneja la transacción:
│   │                                       _unitOfWork.Pedidos, _unitOfWork.Ganancias, ...
│   ├── PedidoRepository.cs               ★ IntentarMarcarComoPagadoAsync: el UPDATE que evita pagos dobles.
│   ├── GananciaRepository.cs
│   ├── NotificacionPagoRepository.cs     Registra cada aviso y detecta los repetidos.
│   └── MensajePendienteRepository.cs     La "bandeja de salida" de avisos al restaurante (Outbox).
├── Models/
│   ├── Entities/                         Pedido, GananciaRepartidor, NotificacionPago, MensajePendiente.
│   └── Contracts/                        SolicitudConfirmarPago (lo que entra), ResultadoConfirmacion (lo que sale).
├── Notifications/                        INotificadorRestaurante y su versión de ejemplo (escribe en el log).
├── Workers/
│   └── EnvioNotificacionesWorker.cs      Proceso en segundo plano: cada 2 s envía los avisos pendientes.
├── Database/                             FudisDbContext (une clases en español con columnas en inglés), migrador.
└── Configuration/                        Options, registro de dependencias (DependencyInjection.cs).
db/                                       SQL: migrations/, queries/, demo/. Ver db/README.md.
tests/Fudis.Pagos.UnitTests/              55 pruebas sin base de datos.
tests/Fudis.Pagos.IntegrationTests/       36 pruebas contra SQL Server real.
```

**Convención de nombres:** los términos técnicos van en inglés (`Controller`, `Service`, `Repository`, `UnitOfWork`, `Worker`) y lo de negocio en español (`Pedido`, `Ganancia`, `ConfirmarPagoAsync`).

**Otros archivos útiles:** `docs/enunciado.md` (el enunciado de la prueba), `docs/test-results/` (evidencia de las pruebas y de la demostración), `CLAUDE.md` (reglas del proyecto y lo que no se puede romper, para personas y asistentes de IA), `.claude/skills/` (cómo agregar una migración, probar la API y revisar código) y `db/README.md` (para qué sirve cada SQL).

**Para leer el código, empieza por** `PagosController` → `ConfirmacionPagoService` → `UnitOfWork` → `PedidoRepository`.

> Las clases están en español, pero las tablas y columnas de la base siguen en inglés (`Orders`, `DriverEarnings`...), porque ya existen en producción. `FudisDbContext.cs` dice qué propiedad va con qué columna.

---

## 3. Problemas encontrados (de mayor a menor impacto)

| # | Problema | Qué puede pasar | Prueba |
|---|---|---|---|
| 1 | **Nadie verifica quién llama al endpoint.** | Cualquiera que sepa un número de pedido lo marca como pagado sin pagar. | `Codigo_original_marca_pagado_un_pedido_cancelado_solo_con_el_id` |
| 2 | **No revisa qué dice la pasarela.** Solo llega `OrderId`: ni estado del pago, ni monto, ni moneda. | Un pago rechazado o incompleto deja el pedido como pagado. | `Solo_con_orderId_como_el_codigo_original_falta_todo_lo_demas` |
| 3 | **Leer → preguntar → guardar no es atómico**, y no hay índice único en las ganancias. | Dos avisos simultáneos (la pasarela reintenta) pasan los dos el `if` y **el repartidor cobra dos veces**. | `Codigo_original_dos_avisos_simultaneos_le_pagan_dos_veces_al_repartidor` |
| 4 | **El aviso al restaurante va fuera de la transacción, sin reintento.** | Si falla después de guardar, la pasarela reintenta, ve `PAID`, responde `Ok` y **el restaurante nunca se entera**. | `Codigo_original_si_falla_el_aviso_al_restaurante_nunca_se_reintenta` |
| 5 | **No hay reglas de estado.** | Un pedido cancelado vuelve a quedar pagado. | (misma que #1) |
| 6 | **No queda registro de los avisos.** | No hay auditoría ni forma de distinguir un reintento de un cobro doble. | — |
| 7 | **`DriverId` puede ser null.** | El pago puede llegar antes de asignar repartidor: ganancia sin dueño o error. | `Un_pedido_sin_repartidor_se_paga...` |
| 8 | **El 70 % está escrito a mano**, sin redondeo definido. | Diferencias de centavos; no se puede auditar si cambia el porcentaje. | `CalculoGananciaServiceTests` |

---

## 4. Cómo funciona la solución

```
Pasarela ──POST firmado──▶ PagosController
                             1. ¿Firma válida?  No → 401
                             2. ¿JSON completo? No → 400
                             3. ConfirmacionPagoService ──── UNA transacción ─────────────────┐
                                  PASO 1  Registrar el aviso      (índice único: Pasarela+EventoId)  │
                                  PASO 2  UPDATE pedido a PAGADO  (solo si está pendiente y monto ok)│
                                  PASO 3  Registrar ganancia      (índice único: un pedido = una)    │
                                  PASO 4  Dejar aviso pendiente   (tabla OutboxMessages)             │
                                  PASO 5  Guardar el resultado y CONFIRMAR ──────────────────────────┘
                           ◀── 200 {resultado}

EnvioNotificacionesWorker (cada 2 s) ── toma avisos pendientes ── avisa al restaurante
                                          └─ si falla: reintenta (5 s, 10 s, 20 s...) y se rinde tras 10 intentos
```

### Garantías

| Garantía | Cómo se logra |
|---|---|
| Solo la pasarela puede confirmar pagos | Firma HMAC-SHA256 del JSON + marca de tiempo (máx. 5 min, evita reenvíos). Se aceptan varios secretos para poder cambiarlos sin cortar el servicio. |
| Un aviso se procesa una sola vez | Índice único `(Provider, EventId)`. Si llegan dos iguales a la vez, SQL Server hace esperar al segundo y luego lo rechaza. |
| Un pedido se paga una sola vez | El `UPDATE ... WHERE Status = 'PENDING_PAYMENT'` es una sola sentencia: solo uno puede ganar. |
| El repartidor cobra máximo una vez por pedido | Lo anterior **más** el índice único en `DriverEarnings.OrderId`, como red de seguridad. |
| No se acepta un monto incorrecto | El monto y la moneda forman parte del `WHERE`. Si el pedido no tiene monto, no se acepta. |
| Pago y ganancia nunca quedan a medias | Todo va en una transacción: si algo falla, se deshace todo. |
| El restaurante siempre se entera | El aviso se guarda en la misma transacción y el trabajador lo envía **al menos una vez**, con reintentos. Cada envío tiene un tiempo máximo (30 s, menor que el bloqueo de 60 s), así un restaurante que no responde no deja el mensaje colgado ni provoca envíos dobles. |
| Se puede tener varios servidores | No hay nada en memoria; todo se coordina en la base de datos. |

---

## 5. Contrato de entrada

`POST /api/payments/confirm-payment`, con cabecera de firma:

```
X-Gateway-Signature: t=<segundos desde 1970>,v1=<HMAC-SHA256(secreto, "<t>.<json>") en hexadecimal>
```
```json
{
  "eventId":    "evt_9f2c",            // único por aviso: sirve para detectar repetidos
  "eventType":  "payment.succeeded",   // o "payment.failed"
  "paymentId":  "pay_71ab",            // id del pago en la pasarela
  "orderId":    1001,
  "amount":     45000.00,              // debe coincidir con el total del pedido
  "currency":   "COP",
  "occurredAt": "2026-11-05T19:00:00Z"
}
```

| Respuesta | Significado | ¿La pasarela debe reintentar? |
|---|---|---|
| 200 `{resultado, requiereRevision, detalle}` | Aviso registrado | No |
| 400 | JSON inválido o incompleto | No |
| 401 | Firma ausente, falsa o vieja | No |
| 500 | Error inesperado; no quedó nada a medias | **Sí, es seguro** |

Resultados: `PROCESADO`, `DUPLICADO`, `YA_PAGADO`, `IGNORADO`, y los que **requieren revisión**: `PEDIDO_NO_EXISTE`, `MONTO_NO_COINCIDE`, `PEDIDO_NO_PAGABLE`, `PAGO_EN_CONFLICTO`.
Los casos raros responden 200 a propósito, porque reintentar no los arregla. Quedan marcados para revisión (`db/queries/reconciliation.sql`).

---

## 6. ¿Qué pasa si la pasarela envía el mismo aviso varias veces?

| Caso | Resultado |
|---|---|
| Mismo `eventId`, uno después del otro | `DUPLICADO`: no se hace nada. |
| Mismo `eventId`, **al mismo tiempo** (probado con 25 a la vez y con 15 peticiones HTTP) | 1 `PROCESADO` y el resto `DUPLICADO`. |
| Otro `eventId`, mismo pago (probado con 25 a la vez) | 1 `PROCESADO` y el resto `YA_PAGADO`. |
| Otro pago distinto para un pedido ya pagado | `PAGO_EN_CONFLICTO`: posible cobro doble, para revisión. |
| `payment.failed` después de un pago exitoso | `IGNORADO`: no deshace el pago. |

## 7. ¿Cómo se mantienen consistentes el pago, la ganancia y el aviso?

- **Pago y ganancia:** van en la misma transacción. O se guardan los dos o ninguno.
- **Aviso al restaurante:** no se puede deshacer una llamada HTTP, así que no se llama dentro de la transacción. Se guarda un **mensaje pendiente** en la misma transacción (patrón *Outbox*). Si el pedido quedó pagado, el aviso pendiente también existe. El trabajador lo envía después, con reintentos.
- El aviso se entrega **al menos una vez**. En un caso raro (el sistema se cae justo después de enviar) podría llegar dos veces. Por eso lleva un `MensajeId` único, para que el restaurante ignore el repetido.

---

## 8. Cambios en la base de datos

Los scripts están en `db/migrations/`, se pueden ejecutar varias veces sin problema, y **son los mismos que ejecutan las pruebas**.

- `000_baseline_existing_schema.sql`: el esquema original que suponemos. Solo para desarrollo y pruebas.
- `001_payment_confirmation_hardening.sql`:
  - **Primero revisa los datos.** Si ya hay ganancias duplicadas (por el error original), se detiene sin cambiar nada. Borrar registros de dinero automáticamente no es aceptable: hay que conciliarlos con finanzas.
  - `Orders`: agrega `TotalAmount`, `Currency`, `PaymentId` y `PaidAt`.
  - `DriverEarnings`: índice único por pedido, FK a `Orders`, monto ≥ 0, y columnas de auditoría.
  - Tablas nuevas: `PaymentNotifications` (avisos recibidos) y `OutboxMessages` (avisos por enviar).

En producción se ejecuta con `sqlcmd -b -i db/migrations/001_...sql`, en un horario de poco tráfico.

---

## 9. Pruebas: **91 de 91 en verde**

Se ejecutaron tres veces seguidas. La evidencia está en `docs/test-results/`.

| Archivo | # | Qué prueba |
|---|---|---|
| `CalculoGananciaServiceTests` | 10 | El 70 % y el redondeo. |
| `VerificacionFirmaServiceTests` | 13 | Firma válida, JSON alterado, secreto incorrecto, firma vieja, cambio de secreto. |
| `SolicitudConfirmarPagoTests` | 11 | Validación del JSON de entrada. |
| `ReglasNegocioTests` | 21 | Estados, casos para revisión, espera entre reintentos, configuración inválida. |
| `ConfirmacionPagoServiceTests` | 16 | Pago normal, repetidos, **25 avisos simultáneos**, cobro doble, monto incorrecto, cancelado, sin repartidor... |
| `EnvioNotificacionesServiceTests` | 6 | Reintentos, descarte, trabajador caído, restaurante que no responde (tiempo límite), **4 trabajadores en paralelo**. |
| `PagosControllerTests` | 8 | De punta a punta por HTTP: 200, 401, 400, **15 peticiones simultáneas**, `/health` con y sin base, la API no arranca sin secretos. |
| `DatabaseMigratorTests` | 3 | Se detiene ante duplicados, conserva los datos, se puede repetir. |
| `CodigoOriginalTests` | 3 | **Demuestran los errores del código original.** |

**Pendientes (no implementadas):** prueba de carga, simular una caída de red a mitad de la transacción, y pruebas con el sandbox de una pasarela real.

---

## 10. Revisión final (code review)

| Hallazgo | Corrección |
|---|---|
| `SSH.NET` con vulnerabilidad alta (dependencia de Testcontainers, solo en pruebas) | Testcontainers 4.0.0 → 4.16.0. `dotnet list package --vulnerable`: 0 vulnerabilidades. |
| Paquetes sin los últimos parches de seguridad | EF Core y Mvc.Testing 8.0.11 → 8.0.33 (misma versión mayor, .NET 8 LTS). |
| Contraseña de `sa` en `appsettings.json` (archivo base que se sube al repositorio) | Movida a `appsettings.Development.json`. En producción viene de una variable de entorno. |
| Sin secretos de firma, la API arrancaba y fallaba con el primer pago | Validación al arrancar: si falta configuración, no inicia. |
| El aviso al restaurante no tenía tiempo límite: si se colgaba, el bloqueo vencía y otro trabajador lo reenviaba | Tiempo máximo por envío (30 s) y validación de que sea menor que el bloqueo. |
| `/health` decía "Healthy" con la base caída | Revisa SQL Server; responde 503 en máximo 3 s. |
| Formato y advertencias | `dotnet format`: 0 cambios. Compilación con `TreatWarningsAsErrors`: 0 advertencias. |

## 11. Supuestos

1. La pasarela puede firmar con HMAC y enviar `eventId`, `paymentId`, `amount` y `currency`, como hacen las pasarelas comunes. Si no pudiera, habría que consultar el estado del pago a la API de la pasarela antes de confirmar.
2. `TotalAmount` y `Currency` los llena el proceso que crea el pedido. Si faltan, el pago queda para revisión.
3. Una ganancia por pedido. Ajustes o propinas serían otros movimientos.
4. Si no hay repartidor al pagar, la ganancia la crea el proceso de asignación (fuera de alcance).
5. Redondeo a 2 decimales hacia arriba en la mitad; el porcentaje se configura en `appsettings.json`. Hay que confirmarlo con finanzas.

## 12. Decisiones

- **Controller → Service → Repository** con un **UnitOfWork** que expone los repositorios (`_unitOfWork.Pedidos`...): cada servicio recibe un solo objeto y todos los repositorios comparten la misma transacción.
- **UPDATE condicional** en vez de "leer, preguntar y guardar": una sola sentencia, sin carreras.
- **Outbox** en vez de llamar al restaurante dentro del pago: es la única forma de garantizar el aviso sin transacciones distribuidas.
- **Responder 200 en los casos raros**: reintentar no los arregla; quedan registrados para revisión.
- **Scripts SQL a mano** en vez de EF Migrations: la base ya existe, los scripts revisan la calidad de los datos y los puede revisar un DBA.
- **Sin microservicios:** el pago y la ganancia deben ser atómicos. El Outbox deja preparado el camino para separar las notificaciones en otro servicio si hace falta.

## 13. Limitaciones y trabajo pendiente

- **Ganancia cuando no hay repartidor:** falta el proceso de asignación.
- **Revisión y reembolsos** de `PAGO_EN_CONFLICTO` y `PEDIDO_NO_PAGABLE`: hoy quedan marcados y en el log; faltan las alertas y el proceso de reembolso.
- **Notificador real:** `NotificadorRestaurantePorLog` es de ejemplo. Hay que reemplazarlo por el cliente HTTP o cola, enviando `MensajeId` como `Idempotency-Key`.
- **Mover la verificación de firma a un filtro** (`[ValidarFirmaPasarela]`), para que el controlador quede aún más simple.
- **Limpieza** de mensajes enviados antiguos y de `RawPayload` (job de retención).
- **Métricas y alertas:** avisos por resultado, mensajes pendientes, descartados.
- Índice único en `Orders.PaymentId` (un pago no puede pagar dos pedidos), pendiente de confirmar con la pasarela.
