BEGIN;

CREATE TABLE IF NOT EXISTS public.employee_device_locks
(
    "Id" uuid NOT NULL,
    "UserId" character varying(450) NOT NULL,
    "DeviceId" character varying(200) NOT NULL,
    "CreatedAtUtc" timestamp without time zone NOT NULL,
    "LastSeenAtUtc" timestamp without time zone NOT NULL,
    CONSTRAINT "PK_employee_device_locks" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_employee_device_locks_DeviceId"
    ON public.employee_device_locks ("DeviceId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_employee_device_locks_UserId"
    ON public.employee_device_locks ("UserId");

INSERT INTO public."__EFMigrationsHistory"
    ("MigrationId", "ProductVersion")
SELECT
    '20260826065938_AddEmployeeDeviceLock',
    '8.0.21'
WHERE NOT EXISTS
(
    SELECT 1
    FROM public."__EFMigrationsHistory"
    WHERE "MigrationId" = '20260826065938_AddEmployeeDeviceLock'
);

COMMIT;
