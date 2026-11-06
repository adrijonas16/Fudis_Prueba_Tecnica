---
name: revision-codigo
description: Lista de verificación de nivel senior para revisar un cambio en Fudis Pagos antes de entregarlo o aprobarlo, con foco en las garantías de dinero, la concurrencia, la seguridad y las convenciones del proyecto.
---

# Revisión de código

Recorrer cada punto. Si alguno falla, corregirlo (con su prueba) antes de entregar.

## 1. Herramientas automáticas

```bash
dotnet build                                            # 0 advertencias (TreatWarningsAsErrors)
dotnet test                                             # todo en verde; 3 veces si se tocó concurrencia u Outbox
dotnet format --verify-no-changes                       # 0 cambios
dotnet list package --vulnerable --include-transitive   # 0 vulnerabilidades
dotnet list package --outdated --highest-patch          # aplicar parches de seguridad de la misma versión mayor
```

## 2. Garantías de dinero (ver "Reglas que NO se pueden romper" en CLAUDE.md)

- [ ] ¿Algún cambio de estado de un pedido usa "leer, preguntar con un if y guardar" en vez de un `UPDATE ... WHERE <condición>`?
- [ ] ¿Todo lo del flujo de pago pasa por el mismo `UnitOfWork` y la misma transacción?
- [ ] ¿Se llama a algo externo (HTTP, cola, correo) dentro de una transacción? Debe ir por el Outbox.
- [ ] ¿Siguen existiendo los índices únicos de `PaymentNotifications` y `DriverEarnings`?
- [ ] ¿Hay pruebas de concurrencia (N llamadas a la vez con `EjecutarAlMismoTiempoAsync`) para lo nuevo que escribe dinero?
- [ ] Montos: `decimal`, redondeo explícito (`MidpointRounding.AwayFromZero`), columnas `DECIMAL(18,2)`.

## 3. Seguridad

- [ ] ¿Hay contraseñas, tokens o secretos en archivos versionados? Solo valores de desarrollo en `appsettings.Development.json`.
- [ ] ¿El webhook sigue verificando la firma sobre el cuerpo crudo antes de deserializar?
- [ ] ¿Las consultas SQL a mano usan parámetros (`SqlParameter`), nunca concatenación?
- [ ] ¿Se registra en el log algún dato sensible (tarjetas, secretos)?
- [ ] Si se agregó configuración obligatoria: ¿la API se niega a arrancar si falta?

## 4. Fallos y reintentos

- [ ] ¿Qué pasa si este código se ejecuta dos veces con los mismos datos? Debe ser idempotente.
- [ ] ¿Qué pasa si se cae a la mitad? No debe quedar nada a medias.
- [ ] ¿Las llamadas externas tienen tiempo límite?
- [ ] ¿Los casos que reintentar no arregla devuelven 200 + `RequiereRevision` y no 4xx/5xx?

## 5. Base de datos

- [ ] Los cambios de esquema están en un script nuevo de `db/migrations/` (ver skill `nueva-migracion`).
- [ ] Las fechas usan `TimeProvider.AhoraUtc()` y los parámetros de fecha a mano son `datetime2(3)`.

## 6. Legibilidad y convenciones

- [ ] Términos técnicos en inglés y negocio en español.
- [ ] `/// <summary>` en español simple en toda clase y método nuevo, explicando el por qué.
- [ ] Nombres que se entienden sin leer el cuerpo del método.
- [ ] Los servicios reciben `UnitOfWork`, no repositorios sueltos.

## 7. Documentación

- [ ] README actualizado si cambió el comportamiento, el contrato, las respuestas o la configuración.
- [ ] `db/README.md` actualizado si se agregó un script.
- [ ] Lo que queda pendiente está anotado en "Limitaciones y trabajo pendiente" del README.
