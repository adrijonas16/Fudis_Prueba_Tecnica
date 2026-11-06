# Uso de herramientas de IA

**Herramienta:** Claude (Anthropic), usado como asistente de análisis y programación durante toda la prueba.

**Cómo se trabajó, en una frase:** primero se **entendió el problema**, después se **diseñó** la solución, la IA **escribió el código** según ese diseño y la arquitectura indicada, y todo se **revisó, corrigió y documentó** en ciclos hasta quedar entendible y probado.

```
 1. Análisis  ──▶  2. Escenarios  ──▶  3. Diseño  ──▶  4. Arquitectura  ──▶  5. Código
                                                                              │
              8. Entrega  ◀──  7. Revisión final  ◀──  6. Revisión, comentarios y correcciones (en ciclos)
```

---

## 1. Análisis del código original

Lo primero fue leer juntos el endpoint `confirm-payment` y preguntarnos si estaba listo para producción. La conclusión fue que **no**: funciona en el caso feliz, pero falla justo en las situaciones normales de un sistema de pagos (reintentos, concurrencia, caídas y llamadas falsas).

Problemas identificados, ordenados por impacto:

| # | Problema | Consecuencia en el negocio |
|---|---|---|
| 1 | Nadie verifica quién llama al endpoint. | Cualquiera que sepa un número de pedido puede marcarlo como pagado sin pagar. |
| 2 | Solo se recibe el `OrderId`: no se revisa estado del pago, monto ni moneda. | Un pago rechazado o incompleto deja el pedido como pagado. |
| 3 | El patrón "leer → preguntar con un `if` → guardar" no es atómico, y no hay índice único en las ganancias. | Dos avisos simultáneos pagan **dos veces** al repartidor. |
| 4 | El aviso al restaurante se envía fuera de la transacción y sin reintentos. | Si falla, el reintento de la pasarela ve el pedido ya pagado y **nunca** se avisa al restaurante. |
| 5 | No hay reglas de estado. | Un pedido cancelado puede volver a quedar pagado. |
| 6 | No queda registro de los avisos recibidos. | No hay auditoría ni forma de distinguir un reintento de un cobro doble. |
| 7 | `DriverId` puede ser null. | El pago puede llegar antes de asignar repartidor: ganancia sin dueño o error. |
| 8 | El 70 % está escrito a mano, sin regla de redondeo. | Diferencias de centavos y sin trazabilidad si el porcentaje cambia. |

## 2. Escenarios planteados

Entre ambos se armó la lista de situaciones que el sistema debía resistir. Cada una se convirtió después en una prueba automática:

| Escenario | Comportamiento esperado |
|---|---|
| Llega un aviso válido | Pedido pagado, ganancia del 70 % registrada, restaurante notificado. |
| La pasarela reenvía **el mismo** aviso | No se hace nada; se responde que ya se había recibido. |
| Llegan **25 avisos iguales al mismo tiempo** | Solo uno se procesa; una sola ganancia. |
| Llega otro aviso del **mismo pago** con distinto identificador | Se reconoce como ya pagado; no se duplica nada. |
| Llega un **pago distinto** para un pedido ya pagado | Se marca como posible cobro doble para revisión. |
| El monto o la moneda no coinciden | No se marca pagado; queda para revisión. |
| El pedido está cancelado o no existe | No se toca; queda para revisión. |
| La pasarela informa un pago **fallido** | Se registra y se ignora; no deshace un pago ya confirmado. |
| El pedido aún no tiene repartidor | Se paga y se avisa; la ganancia queda para cuando se asigne. |
| El servicio del restaurante **falla o no responde** | Se reintenta más tarde, esperando cada vez el doble, y se descarta tras varios intentos. |
| Alguien envía un aviso **falso o modificado** | Se rechaza (401) sin tocar nada. |
| La base de datos se cae a mitad del proceso | No queda nada a medias; la pasarela puede reintentar sin riesgo. |

## 3. Diseño: cómo debería funcionar

Con el análisis y los escenarios se definió el comportamiento:

1. **Verificar la firma** del aviso (HMAC) antes de hacer cualquier cosa.
2. **Registrar el aviso** en una tabla con un índice único: si ya existe, es repetido y no se procesa.
3. **Marcar el pedido como pagado con una sola sentencia** `UPDATE ... WHERE estado = pendiente AND monto = ... AND moneda = ...`. SQL Server pone en fila los avisos simultáneos y solo uno puede ganar.
4. **Registrar la ganancia**, con un índice único por pedido como red de seguridad.
5. **No llamar al restaurante dentro de la transacción.** Se deja un "mensaje pendiente" en la misma transacción (patrón *Outbox*), y un proceso en segundo plano lo envía con reintentos.
6. **Responder 200 en los casos raros** y marcarlos para revisión, porque reintentar no los arregla.
7. Los pasos 2 a 5 van en **una sola transacción**: o se guarda todo, o nada.

## 4. Arquitectura indicada a la IA

Se le explicó a la IA cómo debía estar organizado el código:

- **Controller → Service → Repository**, en un solo proyecto, con carpetas claras.
- **Nombres técnicos en inglés** (`Controller`, `Service`, `Repository`, `UnitOfWork`) y **lo de negocio en español** (`Pedido`, `Ganancia`, `ConfirmarPagoAsync`).
- Un **`UnitOfWork` que exponga todos los repositorios** (`_unitOfWork.Pedidos`, `_unitOfWork.Ganancias`...), para que cada servicio reciba un solo objeto y todos compartan la misma transacción.
- La verificación de la firma en un **filtro** (`[ValidarFirmaPasarela]`), para que el controlador solo se ocupe de HTTP.
- Código **entendible para un desarrollador junior**: nombres que se expliquen solos y comentarios en español en cada clase y método.

> La primera versión que generó la IA estaba organizada en cuatro proyectos (Domain, Application, Infrastructure, Api) y con nombres en inglés. Al revisarla, la estructura resultaba difícil de seguir, así que se le pidió **reescribirla** con la arquitectura de arriba, conservando las mismas garantías y las mismas pruebas.

## 5. Generación del código

Con el análisis, los escenarios, el diseño y la arquitectura, la IA escribió:

- La API (.NET 8, Entity Framework Core, SQL Server).
- Los scripts SQL de migración, que primero **verifican los datos existentes** y se detienen si encuentran ganancias duplicadas, en vez de borrarlas.
- Las pruebas: unitarias, y de integración contra un **SQL Server real**, incluidas pruebas que **reproducen los errores del código original** como evidencia.
- La documentación: README, `db/README.md`, `CLAUDE.md` y las skills del proyecto.

## 6. Revisión, comentarios y correcciones

El código se revisó por partes, preguntando el porqué de cada decisión hasta entenderla. Cada vez que algo no estaba claro o no era consistente, se le pidió a la IA corregirlo y volver a comentarlo:

| Qué se revisó | Qué se pidió o se corrigió |
|---|---|
| Estructura general | Reescribir en Controller → Service → Repository, con nombres en español. |
| Nombres | Términos técnicos en inglés y negocio en español, de forma consistente en todo el proyecto. |
| Servicios con muchas dependencias | Que el `UnitOfWork` expusiera los repositorios: el constructor pasó de 9 parámetros a 5. |
| Controlador con lógica de seguridad | Mover la verificación de la firma a un filtro. |
| Archivos sin comentarios | Agregar comentarios en español simple en las 32 clases y métodos que no tenían, y en las pruebas. |
| Explicaciones difíciles de seguir | Reescribirlas con ejemplos simples (la firma como un "sello secreto", el `CancellationToken`...). |

Además, **las pruebas encontraron errores en el propio código generado por la IA**, que se corrigieron:

- **Precisión de fechas:** la diferencia entre `datetime` y `datetime2(3)` hacía que, a veces, un mensaje recién creado no se enviara a tiempo. Se corrigió en el origen y se confirmó con 10 ejecuciones seguidas.
- **Hilos bloqueados en las pruebas de concurrencia:** la forma de lanzar las tareas al mismo tiempo frenaba el sistema. Se cambió por una alternativa asíncrona.
- **En otra máquina:** el SDK instalado era .NET 9 (se ajustó `global.json`), la base de datos no existía al arrancar (ahora se crea sola en desarrollo) y el script de PowerShell no funcionaba en la versión de Windows.

## 7. Revisión final (code review)

Al final se pidió una revisión "como la haría un senior". Encontró y corrigió:

- Un paquete de pruebas con una **vulnerabilidad de seguridad alta** (Testcontainers / SSH.NET), y parches de seguridad pendientes en EF Core.
- La **contraseña de la base de datos** en la configuración base: se movió a la configuración de desarrollo.
- Que la API **arrancaba sin los secretos de firma**: ahora no inicia si falta configuración.
- Que el aviso al restaurante **no tenía tiempo límite**, lo que podía causar envíos dobles.
- Que el `/health` decía "sano" con la base de datos caída: ahora responde 503 en máximo 3 segundos.

Cada corrección tiene su prueba automática.

## 8. Verificación

- **92 pruebas automáticas en verde**, ejecutadas varias veces seguidas. Las de integración usan SQL Server real (Docker), no una base simulada.
- **Prueba manual de punta a punta** en la máquina local: Docker con SQL Server, la API en Visual Studio y los datos en SQL Server Management Studio, enviando avisos firmados para cada escenario (pago normal, repetido, cobro doble, monto incorrecto, pedido cancelado, firma falsa y 10 avisos simultáneos).
- **Clon limpio desde GitHub**: el repositorio compila y pasa todas las pruebas por sí solo.
- Herramientas: 0 advertencias de compilación, 0 cambios de formato (`dotnet format`) y 0 paquetes vulnerables.

## Responsabilidad

La IA aceleró el análisis y la escritura, pero las decisiones de diseño, la arquitectura, los escenarios a cubrir y la aceptación final de cada parte se revisaron y validaron antes de entregar. Todo el código está comentado y puede explicarse línea por línea.
