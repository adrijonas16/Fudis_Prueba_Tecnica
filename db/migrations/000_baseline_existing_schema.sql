/*
  000 - Esquema EXISTENTE supuesto (línea base).
  Representa lo que el enunciado declara: Orders(Id, Status, DriverId, DeliveryFee) y
  DriverEarnings(OrderId, DriverId, Amount). Sin restricciones de unicidad: así es como el código
  original permite ganancias duplicadas.
  En producción este script NO se ejecuta (las tablas ya existen); solo crea el entorno local/pruebas.
*/
IF OBJECT_ID(N'dbo.Orders') IS NULL
    CREATE TABLE dbo.Orders (
        Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Orders PRIMARY KEY,
        Status      NVARCHAR(30)      NOT NULL,
        DriverId    INT               NULL,
        DeliveryFee DECIMAL(18,2)     NOT NULL
    );
GO

IF OBJECT_ID(N'dbo.DriverEarnings') IS NULL
    CREATE TABLE dbo.DriverEarnings (
        Id       BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DriverEarnings PRIMARY KEY,
        OrderId  INT                  NOT NULL,
        DriverId INT                  NULL,
        Amount   DECIMAL(18,2)        NOT NULL
    );
GO
