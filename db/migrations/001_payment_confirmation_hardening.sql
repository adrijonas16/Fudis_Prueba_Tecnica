/*
  001 - Endurecimiento de la confirmación de pagos.

  Idempotente: puede ejecutarse más de una vez. Ejecutar con sqlcmd -b (detener ante el primer error):
      sqlcmd -S <server> -d <db> -b -i 001_payment_confirmation_hardening.sql

  Antes de crear restricciones se VERIFICA la calidad de los datos existentes. Si hay ganancias duplicadas,
  sin repartidor o huérfanas, el script se detiene: borrar o fusionar registros de dinero automáticamente
  no es aceptable; deben conciliarse con finanzas (ver db/queries/reconciliation.sql).
*/
SET XACT_ABORT ON;
GO

/* ---------- 1. Verificaciones previas de datos ---------- */
IF EXISTS (SELECT 1 FROM dbo.DriverEarnings GROUP BY OrderId HAVING COUNT(*) > 1)
    THROW 50001, N'Hay pedidos con más de una ganancia de repartidor. Concilie antes de migrar (db/queries/reconciliation.sql).', 1;

IF EXISTS (SELECT 1 FROM dbo.DriverEarnings WHERE DriverId IS NULL)
    THROW 50002, N'Hay ganancias sin repartidor (DriverId NULL). Concilie antes de migrar.', 1;

IF EXISTS (SELECT 1 FROM dbo.DriverEarnings e WHERE NOT EXISTS (SELECT 1 FROM dbo.Orders o WHERE o.Id = e.OrderId))
    THROW 50003, N'Hay ganancias que referencian pedidos inexistentes. Concilie antes de migrar.', 1;
GO

/* ---------- 2. Orders: datos necesarios para validar el pago ---------- */
IF COL_LENGTH(N'dbo.Orders', N'TotalAmount') IS NULL ALTER TABLE dbo.Orders ADD TotalAmount DECIMAL(18,2) NULL;
IF COL_LENGTH(N'dbo.Orders', N'Currency')    IS NULL ALTER TABLE dbo.Orders ADD Currency    CHAR(3)       NULL;
IF COL_LENGTH(N'dbo.Orders', N'PaymentId')   IS NULL ALTER TABLE dbo.Orders ADD PaymentId   NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.Orders', N'PaidAt')      IS NULL ALTER TABLE dbo.Orders ADD PaidAt      DATETIME2(3)  NULL;
GO

/* ---------- 3. DriverEarnings: unicidad por pedido y trazabilidad ---------- */
IF COL_LENGTH(N'dbo.DriverEarnings', N'SharePercent')    IS NULL ALTER TABLE dbo.DriverEarnings ADD SharePercent    DECIMAL(5,2)  NULL;
IF COL_LENGTH(N'dbo.DriverEarnings', N'SourcePaymentId') IS NULL ALTER TABLE dbo.DriverEarnings ADD SourcePaymentId NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.DriverEarnings', N'CreatedAt')       IS NULL
    ALTER TABLE dbo.DriverEarnings ADD CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_DriverEarnings_CreatedAt DEFAULT SYSUTCDATETIME();
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.DriverEarnings') AND name = N'DriverId' AND is_nullable = 1)
    ALTER TABLE dbo.DriverEarnings ALTER COLUMN DriverId INT NOT NULL;
GO

-- Red de seguridad final: aunque falle cualquier otra capa, la BD no admite dos ganancias para un pedido.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_DriverEarnings_OrderId' AND object_id = OBJECT_ID(N'dbo.DriverEarnings'))
    CREATE UNIQUE INDEX UX_DriverEarnings_OrderId ON dbo.DriverEarnings (OrderId);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DriverEarnings_Orders')
    ALTER TABLE dbo.DriverEarnings WITH CHECK ADD CONSTRAINT FK_DriverEarnings_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders (Id);

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_DriverEarnings_Amount_NonNegative')
    ALTER TABLE dbo.DriverEarnings WITH CHECK ADD CONSTRAINT CK_DriverEarnings_Amount_NonNegative CHECK (Amount >= 0);
GO

/* ---------- 4. Inbox de notificaciones de la pasarela ---------- */
IF OBJECT_ID(N'dbo.PaymentNotifications') IS NULL
BEGIN
    CREATE TABLE dbo.PaymentNotifications (
        Id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentNotifications PRIMARY KEY,
        Provider       NVARCHAR(50)   NOT NULL,
        EventId        NVARCHAR(100)  NOT NULL,
        EventType      NVARCHAR(50)   NOT NULL,
        PaymentId      NVARCHAR(100)  NOT NULL,
        OrderId        INT            NOT NULL,   -- sin FK a propósito: se registran también eventos de pedidos inexistentes
        Amount         DECIMAL(18,2)  NOT NULL,
        Currency       CHAR(3)        NOT NULL,
        OccurredAt     DATETIME2(3)   NOT NULL,
        ReceivedAt     DATETIME2(3)   NOT NULL,
        RawPayload     NVARCHAR(MAX)  NOT NULL,
        Outcome        NVARCHAR(40)   NOT NULL,
        OutcomeDetail  NVARCHAR(400)  NULL,
        RequiresReview BIT            NOT NULL CONSTRAINT DF_PaymentNotifications_RequiresReview DEFAULT 0
    );

    -- Idempotencia: un evento de la pasarela se procesa una sola vez.
    CREATE UNIQUE INDEX UX_PaymentNotifications_Provider_EventId ON dbo.PaymentNotifications (Provider, EventId);
    CREATE INDEX IX_PaymentNotifications_PaymentId ON dbo.PaymentNotifications (PaymentId);
    CREATE INDEX IX_PaymentNotifications_OrderId   ON dbo.PaymentNotifications (OrderId);
    -- Cola de revisión manual / conciliación.
    CREATE INDEX IX_PaymentNotifications_Review ON dbo.PaymentNotifications (ReceivedAt) WHERE RequiresReview = 1;
END
GO

/* ---------- 5. Outbox transaccional ---------- */
IF OBJECT_ID(N'dbo.OutboxMessages') IS NULL
BEGIN
    CREATE TABLE dbo.OutboxMessages (
        Id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_OutboxMessages PRIMARY KEY NONCLUSTERED,
        Type           NVARCHAR(100)    NOT NULL,
        Payload        NVARCHAR(MAX)    NOT NULL,
        CreatedAt      DATETIME2(3)     NOT NULL,
        NextAttemptAt  DATETIME2(3)     NOT NULL,
        Attempts       INT              NOT NULL CONSTRAINT DF_OutboxMessages_Attempts DEFAULT 0,
        LockedUntil    DATETIME2(3)     NULL,
        ProcessedAt    DATETIME2(3)     NULL,
        DeadLetteredAt DATETIME2(3)     NULL,
        LastError      NVARCHAR(2000)   NULL
    );

    -- Clustered por fecha (inserciones secuenciales; el GUID aleatorio fragmentaría el clustered).
    CREATE CLUSTERED INDEX CIX_OutboxMessages_CreatedAt ON dbo.OutboxMessages (CreatedAt);
    -- Solo mensajes pendientes: el índice se mantiene pequeño aunque la tabla crezca.
    CREATE INDEX IX_OutboxMessages_Pending ON dbo.OutboxMessages (NextAttemptAt) INCLUDE (LockedUntil)
        WHERE ProcessedAt IS NULL AND DeadLetteredAt IS NULL;
END
GO
