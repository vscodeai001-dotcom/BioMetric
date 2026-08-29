# Deployment Runbook

This runbook covers safe deployment and rollback for the current production-ready Biometric Payroll application without changing the existing app flow, layout, database design, or business logic.

---

## 1. Deployment Goals

- Keep the app stable and production-safe.
- Apply all required database migrations safely.
- Validate required schema and critical tables before startup.
- Verify the application is healthy after deployment.
- Roll back quickly if a deployment introduces a blocker.

---

## 2. Pre-Deployment Checklist

### Required checks

- [ ] Build succeeds.
- [ ] No pending compile errors.
- [ ] Latest code is committed to the target branch.
- [ ] Migration file is present and included in source control.
- [ ] Target database backup is created.
- [ ] All required environment variables are configured.
- [ ] PostgreSQL connection string is validated.
- [ ] App health endpoint is reachable after deployment.

### Build command

```bash
dotnet build BiometricPayroll.sln --nologo -v minimal
```

### Migration command

```bash
dotnet ef database update --project Payroll.Web/Payroll.Web.csproj --startup-project Payroll.Web/Payroll.Web.csproj --context AppDbContext
```

---

## 3. Database Deployment Steps

### Option A: EF Core migration

Run the migration command against the target PostgreSQL database:

```bash
dotnet ef database update --project Payroll.Web/Payroll.Web.csproj --startup-project Payroll.Web/Payroll.Web.csproj --context AppDbContext
```

### Option B: SQL script fallback

If the environment cannot run the EF tool or the target DB is already drifted, run the provided DB script manually.

Use one of the following:

- [neon-user-theme-preferences.sql](neon-user-theme-preferences.sql)
- [pgadmin-user-theme-preferences.sql](pgadmin-user-theme-preferences.sql)

---

## 4. Post-Migration Validation

### Verify schema exists

Run the following in PostgreSQL:

```sql
SELECT table_schema, table_name
FROM information_schema.tables
WHERE table_schema = 'public'
  AND table_name IN ('__EFMigrationsHistory', 'user_theme_preferences', 'employees', 'AspNetUsers');
```

### Verify migration record exists

```sql
SELECT "MigrationId", "ProductVersion"
FROM public."__EFMigrationsHistory";
```

### Check required startup tables

```sql
SELECT EXISTS (
    SELECT 1
    FROM information_schema.tables
    WHERE table_schema = 'public'
      AND table_name = 'user_theme_preferences'
);
```

---

## 5. Application Startup Validation

After deployment, verify:

- app starts normally
- health endpoint responds successfully
- no startup database errors appear in logs
- no `42P01` missing-table errors are thrown
- the app loads the main login/admin screen successfully

### Health endpoint

```text
https://<your-app-url>/health
```

Expected: HTTP 200 response and status information in JSON.

---

## 6. Smoke Tests

Run the following checks after deployment:

### Authentication
- [ ] User login works.
- [ ] Admin login works.
- [ ] SuperAdmin login works.
- [ ] Employee access is restricted correctly.

### Core flows
- [ ] Employee list loads.
- [ ] Employee details open correctly.
- [ ] Attendance page loads.
- [ ] Payroll page loads.
- [ ] Leave management loads.
- [ ] Settings page loads.

### Critical data checks
- [ ] No missing-table runtime errors in logs.
- [ ] No DB connection failures.
- [ ] Theme persistence works without warnings.
- [ ] No broken filters or missing data on critical pages.

---

## 7. Rollback Plan

If deployment fails or causes a critical issue:

### Rollback steps

1. Stop the app service/container.
2. Restore the previous database backup.
3. Revert the application version to the previous stable build.
4. Restart the app.
5. Validate the health endpoint again.
6. Re-run smoke tests.

### Rollback command example

```bash
dotnet ef database update <previous-migration-name> --project Payroll.Web/Payroll.Web.csproj --startup-project Payroll.Web/Payroll.Web.csproj --context AppDbContext
```

If the environment is using a database snapshot or recovery point, restore that backup instead.

---

## 8. Operational Notes

- Do not deploy without validating the database schema.
- Do not skip migration checks in staging or production.
- Do not continue if the health endpoint is failing.
- Do not assume local DB state matches remote DB state.
- Treat migration drift as a deployment blocker.

---

## 9. Release Approval Gate

Deployment is approved only when all the following are true:

- [ ] Build succeeded.
- [ ] Migrations applied successfully.
- [ ] Critical tables exist in the live DB.
- [ ] App starts and health check passes.
- [ ] Smoke tests pass.
- [ ] No runtime database exceptions appear in logs.
- [ ] Rollback plan is ready.

---

## 10. Support Contact / Reference Files

- [PRODUCTION_READINESS_CHECKLIST.md](PRODUCTION_READINESS_CHECKLIST.md)
- [DEPLOYMENT_FIXES_GUIDE.md](DEPLOYMENT_FIXES_GUIDE.md)
- [VERIFICATION_CHECKLIST.md](VERIFICATION_CHECKLIST.md)
- [Payroll.Web/Program.cs](Payroll.Web/Program.cs)
