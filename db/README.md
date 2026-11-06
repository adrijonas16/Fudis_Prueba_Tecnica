# Base de datos

Todo el SQL del proyecto está aquí. Las tablas y columnas están en **inglés** porque `Orders` y `DriverEarnings` ya existen en producción. Las clases de C# están en español y `src/Fudis.Pagos.Api/Database/FudisDbContext.cs` dice qué propiedad va con qué columna.

| Archivo | ¿Para qué sirve? | ¿Se ejecuta en producción? |
|---|---|---|
| `migrations/000_baseline_existing_schema.sql` | Crea `Orders` y `DriverEarnings` **como suponemos que existen hoy** (sin restricciones). Lo necesitan el entorno local y las pruebas, incluidas las que demuestran los errores del código original. | **No.** En producción esas tablas ya existen. |
| `migrations/001_payment_confirmation_hardening.sql` | **El cambio real.** Revisa que no haya datos inválidos, agrega columnas a `Orders`, el índice único en `DriverEarnings` y las tablas nuevas `PaymentNotifications` y `OutboxMessages`. | **Sí.** |
| `queries/reconciliation.sql` | Consultas de solo lectura para operaciones: ganancias duplicadas (las que bloquean la migración 001), avisos que requieren revisión, notificaciones sin enviar y pedidos pagados sin ganancia. | Opcional, se ejecuta a mano cuando hace falta. |
| `demo/seed-demo.sql` | Tres pedidos de ejemplo (normal, sin repartidor, cancelado) para probar la API a mano. | **No.** Solo desarrollo y demostraciones. |

## Cómo se aplican

- **Desarrollo:** la API los aplica sola al arrancar (`BaseDeDatos:AplicarMigracionesAlIniciar = true` en `appsettings.Development.json`). Quedan anotados en `dbo.SchemaVersions`.
- **Pruebas:** cada prueba crea su propia base y ejecuta los **mismos** scripts. Así se prueba el SQL real, no una copia.
- **Producción:** los ejecuta el DBA o el pipeline, en horario de poco tráfico, deteniéndose ante el primer error:
  ```bash
  sqlcmd -S <servidor> -d <base> -b -i db/migrations/001_payment_confirmation_hardening.sql
  ```

## Reglas para scripts nuevos

1. Nombre con número correlativo: `002_descripcion_corta.sql`.
2. **Idempotentes:** se tienen que poder ejecutar dos veces sin error (`IF NOT EXISTS`, `IF COL_LENGTH(...) IS NULL`).
3. **Nunca borrar ni modificar datos de dinero automáticamente.** Si hay datos inválidos, el script se detiene con `THROW` y explica qué conciliar.
4. Separar con `GO` los bloques que agregan columnas de los que las usan.
5. Agregar una prueba en `tests/Fudis.Pagos.IntegrationTests/DatabaseMigratorTests.cs`.
