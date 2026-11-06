---
name: nueva-migracion
description: Agregar un cambio de esquema de base de datos (columna, tabla, índice) al proyecto Fudis Pagos de forma segura e idempotente. Usar cuando se pida modificar tablas, agregar índices o cambiar la estructura de SQL Server.
---

# Nueva migración de base de datos

Los cambios de esquema se hacen **solo** con scripts SQL en `db/migrations/`. No se usan EF Migrations.

## Pasos

1. **Crear el archivo** con el siguiente número: `db/migrations/002_descripcion_corta.sql` (snake_case, en inglés, como las tablas).

2. **Encabezado** explicando qué hace, por qué y cómo ejecutarlo:
   ```sql
   /*
     002 - <qué cambia y por qué>
     Idempotente. Ejecutar con: sqlcmd -S <server> -d <db> -b -i 002_....sql
   */
   SET XACT_ABORT ON;
   GO
   ```

3. **Si el cambio depende de la calidad de los datos**, verificarla primero y detenerse con `THROW`. Nunca corregir datos de dinero automáticamente:
   ```sql
   IF EXISTS (SELECT 1 FROM dbo.Tabla WHERE <dato inválido>)
       THROW 50010, N'Explicación de qué conciliar antes de migrar.', 1;
   GO
   ```

4. **Cada cambio, idempotente:**
   ```sql
   IF COL_LENGTH(N'dbo.Orders', N'NuevaColumna') IS NULL
       ALTER TABLE dbo.Orders ADD NuevaColumna NVARCHAR(50) NULL;
   GO
   IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_...' AND object_id = OBJECT_ID(N'dbo.Orders'))
       CREATE INDEX IX_... ON dbo.Orders (NuevaColumna);
   GO
   ```
   Separar con `GO` el bloque que agrega una columna del bloque que la usa.

5. **Columnas nuevas en tablas existentes:** `NULL`, o `NOT NULL` con `DEFAULT`, para no romper los datos que ya existen.

6. **Reflejar el cambio en C#:** propiedad en español en la entidad (`Models/Entities/`) y mapeo con `HasColumnName("...")` en `Database/FudisDbContext.cs`.

7. **Prueba** en `tests/Fudis.Pagos.IntegrationTests/DatabaseMigratorTests.cs`:
   - Que se puede ejecutar dos veces (borrar `dbo.SchemaVersions` y volver a aplicar).
   - Si tiene verificación de datos: que se detiene con el número de `THROW` esperado y **no cambia nada**.

8. **Documentar** el script en la tabla de `db/README.md`.

9. Verificar: `dotnet build` y `dotnet test` en verde.

## No hacer

- No renombrar tablas ni columnas existentes (`Orders`, `DriverEarnings`...): producción depende de ellas.
- No quitar `UX_DriverEarnings_OrderId` ni `UX_PaymentNotifications_Provider_EventId`: son las redes de seguridad contra pagos dobles.
- No editar un script que ya se aplicó en algún entorno: crear uno nuevo.
