# Production Readiness Checklist

This checklist is for hardening the existing application without changing the app flow, layout, database design, or business logic. The goal is to validate that the current working solution is safe for production deployment.

---

## 1. Migration Validation

- [ ] Confirm all pending EF Core migrations are applied in every environment.
- [ ] Validate the database schema matches the model snapshot.
- [ ] Confirm the `__EFMigrationsHistory` table is consistent for each environment.
- [ ] Verify the required theme table exists in the live PostgreSQL database.
- [ ] Validate that startup migration execution succeeds without schema drift.
- [ ] Confirm no migration has been skipped across dev, staging, and production.

### Required database checks

- [ ] Check all required tables exist in the public schema.
- [ ] Confirm the user theme preferences table exists:
  - public.user_theme_preferences
- [ ] Validate the schema owner and privileges are correct for the application user.
- [ ] Confirm application login has read/write access to required tables.

---

## 2. Environment Verification

- [ ] Validate connection string for each environment.
- [ ] Confirm the correct PostgreSQL database is connected in staging and production.
- [ ] Check that the app is pointed to the correct environment-specific database.
- [ ] Confirm app settings are not using stale or old connection details.
- [ ] Validate Data Protection keys path is persisted in container deployments.
- [ ] Verify environment variables are set correctly for deployment.

---

## 3. Startup Health Checks

- [ ] Confirm the app health endpoint responds successfully.
- [ ] Verify database health check passes before app becomes fully available.
- [ ] Ensure missing-table or schema-drift failures generate clear startup errors.
- [ ] Check app startup does not silently continue when the DB is invalid.
- [ ] Confirm Hangfire and DB services are healthy after startup.

---

## 4. Smoke Tests for Critical Flows

Run these checks before release.

### Authentication and access
- [ ] User can log in successfully.
- [ ] Employee can access employee-only screens.
- [ ] Admin can access admin screens.
- [ ] SuperAdmin can access restricted screens.
- [ ] Role-based access blocks unauthorized users.

### Employee flow
- [ ] Employee list loads correctly.
- [ ] Employee details open correctly.
- [ ] Employee records can be created or updated without errors.
- [ ] Employee-specific data remains isolated per employee.

### Attendance flow
- [ ] Attendance logs load correctly.
- [ ] Manual punch correction functions without errors.
- [ ] Attendance approval flow works as expected.

### Payroll flow
- [ ] Payroll generation process runs successfully.
- [ ] Payroll summary totals display correctly.
- [ ] Payslip generation succeeds.
- [ ] Salary advances remain correctly tied to the employee.

### Leave and benefits
- [ ] Leave request submission works.
- [ ] Leave approval flow works.
- [ ] Feature toggles still work correctly.
- [ ] PF / ESI / PT / shift allowance toggles work without breaking the existing logic.

---

## 5. Role-Based Access Validation

- [ ] Admin access is restricted to admin-allowed pages.
- [ ] SuperAdmin can reach elevated screens only intended for them.
- [ ] Employee users cannot access admin-only pages.
- [ ] URL tampering does not bypass authorization.
- [ ] Access control remains consistent across page refreshes.

---

## 6. Payroll Calculation Validation

- [ ] Re-test payroll calculations with representative real-world data.
- [ ] Validate overtime calculations remain unchanged.
- [ ] Validate late/early deduction logic remains unchanged.
- [ ] Validate PF, ESI, PT, and shift allowance logic remains consistent.
- [ ] Check salary components are calculated against the correct employee data.
- [ ] Confirm payroll totals match expected values for test scenarios.

---

## 7. Deployment and Rollback Documentation

- [ ] Document deployment steps for each environment.
- [ ] Document exact migration execution order.
- [ ] Document backup and restore instructions.
- [ ] Document rollback steps if deployment fails.
- [ ] Document how to restore the previous database state if needed.
- [ ] Maintain a release log with environment names and timestamps.

---

## 8. Monitoring and Operational Readiness

- [ ] Configure log monitoring for database connection failures.
- [ ] Configure alerting for migration or startup errors.
- [ ] Monitor unexpected access errors after deployment.
- [ ] Verify application health endpoint remains healthy after release.
- [ ] Review logs for post-deploy exceptions and warnings.

---

## 9. Release Gate

Do not release until all items below are checked:

- [ ] All database migrations applied successfully.
- [ ] Required tables validated in the target database.
- [ ] Startup validation passes.
- [ ] Critical smoke tests pass.
- [ ] Role-based access checks pass.
- [ ] Payroll scenarios validate correctly.
- [ ] Rollback plan is documented and tested.

---

## 10. Recommended Next Actions

1. Apply pending migrations to the target PostgreSQL environment.
2. Validate the table set in Neon or pgAdmin.
3. Run smoke tests for login, employee, attendance, payroll, and leave flows.
4. Confirm role access and authorization checks.
5. Verify payroll values using live-style sample scenarios.
6. Perform production deployment only after all checks pass.

---

## Final Note

These are production-readiness controls and not feature additions. They are intended to harden the current working application while preserving the existing flow, layout, database design, and calculation rules.
