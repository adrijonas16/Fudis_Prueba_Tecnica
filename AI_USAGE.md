# Uso de herramientas de IA

> Borrador que debe completar el postulante: la sección "Qué verifiqué yo" es responsabilidad de quien entrega.

**Herramienta:** Claude (Anthropic), como asistente de análisis y programación.

**Para qué se usó**
- Analizar el código original y ordenar los problemas por impacto.
- Proponer el diseño: registro de avisos para detectar repetidos, UPDATE condicional, índices únicos y Outbox.
- Escribir el código, los scripts SQL, las pruebas y el README.
- Reescribir la primera versión (4 proyectos, nombres en inglés) en **Controller → Service → Repository con nombres en español**, para que fuera más fácil de leer y explicar.

**Qué se verificó o corrigió durante el desarrollo**
- Todo se probó contra **SQL Server 2022 real**, no contra una base en memoria, porque esa no tiene transacciones ni índices únicos.
- Los errores del código original se **demuestran con pruebas**, no solo se describen.
- Las pruebas encontraron un error en el código generado: por una diferencia de precisión entre `datetime` y `datetime2(3)`, a veces un mensaje recién creado no se enviaba a tiempo. Se corrigió y se confirmó con 10 corridas repetidas.
- Al ejecutar en otra máquina aparecieron dos problemas que se corrigieron: el SDK instalado era .NET 9 (se ajustó `global.json`) y la base de datos no existía al arrancar (ahora se crea sola en desarrollo).

- En una revisión final tipo code review se encontró y corrigió: una vulnerabilidad en un paquete de pruebas, parches de seguridad pendientes, una contraseña en la configuración base, la falta de validación al arrancar, la falta de tiempo límite en el aviso al restaurante y un health check que no revisaba la base de datos. Cada corrección tiene su prueba.

**Qué verifiqué yo** *(completar)*
- [ ] Ejecuté `dotnet test` en mi máquina (deben pasar 92): resultado ___
- [ ] Puedo explicar por qué dos avisos simultáneos no generan dos ganancias (`PedidoRepository.IntentarMarcarComoPagadoAsync`).
- [ ] Puedo explicar qué es el Outbox y por qué el aviso al restaurante no se envía dentro de la transacción.
- [ ] Revisé el script `001` y sé qué pasa si hay datos duplicados.
- [ ] Probé la API con `scripts/send-notification.ps1`.
- [ ] Cambios que pedí o con los que no estuve de acuerdo: pedí reescribir la estructura en Controller-Service-Repository, con lo de negocio en español y los términos técnicos en inglés, y que el UnitOfWork expusiera los repositorios para que cada servicio reciba un solo objeto; ___
