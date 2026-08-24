using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Payroll.Shared.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "attendance_regularizations",
                columns: table => new
                {
                    regularization_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employee_id = table.Column<int>(type: "integer", nullable: false),
                    date_of_punch = table.Column<DateOnly>(type: "date", nullable: false),
                    is_in_punch = table.Column<bool>(type: "boolean", nullable: false),
                    punch_time_new = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    submission_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    approved_by_id = table.Column<string>(type: "text", nullable: true),
                    admin_remarks = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_regularizations", x => x.regularization_id);
                });

            migrationBuilder.CreateTable(
                name: "attendancelogs",
                columns: table => new
                {
                    logid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employeeid = table.Column<int>(type: "integer", nullable: true),
                    biometricid = table.Column<string>(type: "text", nullable: false),
                    punchtime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DeviceID = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    LogType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_approved = table.Column<bool>(type: "boolean", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendancelogs", x => x.logid);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    logid = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    timestamp = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    user_email = table.Column<string>(type: "text", nullable: false),
                    action_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<string>(type: "text", nullable: true),
                    details = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.logid);
                });

            migrationBuilder.CreateTable(
                name: "bonus_records",
                columns: table => new
                {
                    bonusid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employeeid = table.Column<int>(type: "integer", nullable: false),
                    bonus_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    payrollid_paid = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bonus_records", x => x.bonusid);
                });

            migrationBuilder.CreateTable(
                name: "CompanySettings",
                schema: "public",
                columns: table => new
                {
                    SettingID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyName = table.Column<string>(type: "text", nullable: false),
                    AddressLine1 = table.Column<string>(type: "text", nullable: false),
                    CityStatePincode = table.Column<string>(type: "text", nullable: false),
                    SalaryCalculationMethod = table.Column<string>(type: "text", nullable: false),
                    ZktecoIP = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ZktecoPort = table.Column<int>(type: "integer", nullable: false),
                    ZktecoMachineNumber = table.Column<int>(type: "integer", nullable: false),
                    work_day_cutoff_hour = table.Column<int>(type: "integer", nullable: false),
                    EndTimeGraceMinutes = table.Column<int>(type: "integer", nullable: false),
                    LateGraceMinutes = table.Column<int>(type: "integer", nullable: false),
                    EnablePfEsiSystem = table.Column<bool>(type: "boolean", nullable: false),
                    EsiWageLimit = table.Column<decimal>(type: "numeric", nullable: false),
                    basic_salary_percentage = table.Column<decimal>(type: "numeric", nullable: false),
                    EmployeePfPercentage = table.Column<decimal>(type: "numeric", nullable: false),
                    EmployeeEsiPercentage = table.Column<decimal>(type: "numeric", nullable: false),
                    EmployerPfPercentage = table.Column<decimal>(type: "numeric", nullable: false),
                    EmployerEsiPercentage = table.Column<decimal>(type: "numeric", nullable: false),
                    EnableProfessionalTax = table.Column<bool>(type: "boolean", nullable: false),
                    EnableEmailNotifications = table.Column<bool>(type: "boolean", nullable: false),
                    SmtpHost = table.Column<string>(type: "text", nullable: true),
                    SmtpPort = table.Column<int>(type: "integer", nullable: false),
                    SmtpUser = table.Column<string>(type: "text", nullable: true),
                    SmtpPass = table.Column<string>(type: "text", nullable: true),
                    SmtpFromEmail = table.Column<string>(type: "text", nullable: true),
                    EnableSsl = table.Column<bool>(type: "boolean", nullable: false),
                    enable_shift_allowance = table.Column<bool>(type: "boolean", nullable: false),
                    EnableLeaveAccrual = table.Column<bool>(type: "boolean", nullable: false),
                    LeaveAccrualRate = table.Column<decimal>(type: "numeric", nullable: false),
                    EnableSandwichRule = table.Column<bool>(type: "boolean", nullable: false),
                    enable_leave_management = table.Column<bool>(type: "boolean", nullable: false),
                    enable_tds_deduction = table.Column<bool>(type: "boolean", nullable: false),
                    office_latitude = table.Column<double>(type: "double precision", nullable: false),
                    office_longitude = table.Column<double>(type: "double precision", nullable: false),
                    geo_radius_meters = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanySettings", x => x.SettingID);
                });

            migrationBuilder.CreateTable(
                name: "daily_summaries",
                columns: table => new
                {
                    summaryid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employeeid = table.Column<int>(type: "integer", nullable: false),
                    shiftdate = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    earned_standard_hours = table.Column<decimal>(type: "numeric", nullable: false),
                    total_overtime_duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    total_penalty_duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    total_lateness = table.Column<TimeSpan>(type: "interval", nullable: false),
                    total_break_penalty = table.Column<TimeSpan>(type: "interval", nullable: false),
                    scheduled_shift_duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    shift_allowance_earned = table.Column<decimal>(type: "numeric", nullable: false),
                    is_manual_override = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_summaries", x => x.summaryid);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                schema: "public",
                columns: table => new
                {
                    employeeid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dob = table.Column<DateOnly>(type: "date", nullable: true),
                    role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    monthlysalary = table.Column<decimal>(type: "numeric", nullable: false),
                    basic_salary_component = table.Column<decimal>(type: "numeric", nullable: false),
                    hra_component = table.Column<decimal>(type: "numeric", nullable: false),
                    da_component = table.Column<decimal>(type: "numeric", nullable: false),
                    standardhours = table.Column<int>(type: "integer", nullable: false),
                    ot_rule = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ot_flatrate = table.Column<decimal>(type: "numeric", nullable: false),
                    biometricid = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    standardbreakminutes = table.Column<int>(type: "integer", nullable: false),
                    shiftstarttime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    shiftendtime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    HireDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TerminationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    comp_off_day = table.Column<int>(type: "integer", nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    payroll_type_override = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SalaryCalculationMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DirectHourlyWage = table.Column<decimal>(type: "numeric", nullable: true),
                    AspNetUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    enable_pf = table.Column<bool>(type: "boolean", nullable: false),
                    enable_esi = table.Column<bool>(type: "boolean", nullable: false),
                    uan_number = table.Column<string>(type: "text", nullable: true),
                    esi_number = table.Column<string>(type: "text", nullable: true),
                    PaidLeaveBalance = table.Column<decimal>(type: "numeric", nullable: false),
                    SickLeaveBalance = table.Column<decimal>(type: "numeric", nullable: false),
                    NightShiftAllowance = table.Column<decimal>(type: "numeric", nullable: false),
                    tds_rate_percent = table.Column<decimal>(type: "numeric", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    bank_account_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    bank_ifsc_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    bank_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    enable_shift_rotation = table.Column<bool>(type: "boolean", nullable: false),
                    rotation_group = table.Column<string>(type: "text", nullable: true),
                    shift_rotation_pattern = table.Column<string>(type: "text", nullable: true),
                    last_rotated_date = table.Column<DateOnly>(type: "date", nullable: true),
                    current_shift_index = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employees", x => x.employeeid);
                });

            migrationBuilder.CreateTable(
                name: "fbp_components",
                columns: table => new
                {
                    component_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    max_annual_limit = table.Column<decimal>(type: "numeric", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_tax_exempt = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fbp_components", x => x.component_id);
                });

            migrationBuilder.CreateTable(
                name: "feature_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    enable_payroll = table.Column<bool>(type: "boolean", nullable: false),
                    enable_salary_advance = table.Column<bool>(type: "boolean", nullable: false),
                    enable_bonus_management = table.Column<bool>(type: "boolean", nullable: false),
                    enable_salary_structuring = table.Column<bool>(type: "boolean", nullable: false),
                    employee_can_view_advance = table.Column<bool>(type: "boolean", nullable: false),
                    employee_can_view_bonus = table.Column<bool>(type: "boolean", nullable: false),
                    enable_tds_deduction = table.Column<bool>(type: "boolean", nullable: false),
                    enable_shift_scheduling = table.Column<bool>(type: "boolean", nullable: false),
                    enable_leave_management = table.Column<bool>(type: "boolean", nullable: false),
                    enable_punch_correction = table.Column<bool>(type: "boolean", nullable: false),
                    enable_employee_management = table.Column<bool>(type: "boolean", nullable: false),
                    enable_company_reports = table.Column<bool>(type: "boolean", nullable: false),
                    enable_statutory_compliance = table.Column<bool>(type: "boolean", nullable: false),
                    admin_can_view_dashboard = table.Column<bool>(type: "boolean", nullable: false),
                    admin_can_manage_employees = table.Column<bool>(type: "boolean", nullable: false),
                    admin_can_view_attendance = table.Column<bool>(type: "boolean", nullable: false),
                    admin_can_run_payroll = table.Column<bool>(type: "boolean", nullable: false),
                    admin_can_edit_settings = table.Column<bool>(type: "boolean", nullable: false),
                    admin_can_manage_shifts = table.Column<bool>(type: "boolean", nullable: false),
                    admin_can_manage_punch_approvals = table.Column<bool>(type: "boolean", nullable: false),
                    admin_can_view_reports = table.Column<bool>(type: "boolean", nullable: false),
                    employee_can_view_dashboard = table.Column<bool>(type: "boolean", nullable: false),
                    employee_can_view_payslip = table.Column<bool>(type: "boolean", nullable: false),
                    employee_can_view_attendance = table.Column<bool>(type: "boolean", nullable: false),
                    employee_can_view_leave = table.Column<bool>(type: "boolean", nullable: false),
                    employee_can_view_leave_history = table.Column<bool>(type: "boolean", nullable: false),
                    employee_tools_visible = table.Column<bool>(type: "boolean", nullable: false),
                    show_theme_toggle = table.Column<bool>(type: "boolean", nullable: false),
                    admin_can_manage_employee_permissions = table.Column<bool>(type: "boolean", nullable: false),
                    enable_professional_tax = table.Column<bool>(type: "boolean", nullable: false),
                    enable_email_notifications = table.Column<bool>(type: "boolean", nullable: false),
                    enable_leave_accrual = table.Column<bool>(type: "boolean", nullable: false),
                    enable_sandwich_rule = table.Column<bool>(type: "boolean", nullable: false),
                    enable_shift_allowance = table.Column<bool>(type: "boolean", nullable: false),
                    enable_audit_log = table.Column<bool>(type: "boolean", nullable: false),
                    employee_can_view_shifts = table.Column<bool>(type: "boolean", nullable: false),
                    enable_year_end_summary = table.Column<bool>(type: "boolean", nullable: false),
                    enable_recycle_bin = table.Column<bool>(type: "boolean", nullable: false),
                    enable_tax_declarations = table.Column<bool>(type: "boolean", nullable: false),
                    enable_geo_fencing = table.Column<bool>(type: "boolean", nullable: false),
                    enable_resignation_module = table.Column<bool>(type: "boolean", nullable: false),
                    employee_can_view_resignation = table.Column<bool>(type: "boolean", nullable: false),
                    employee_can_view_tax = table.Column<bool>(type: "boolean", nullable: false),
                    enable_custom_reporting = table.Column<bool>(type: "boolean", nullable: false),
                    employee_can_view_reports = table.Column<bool>(type: "boolean", nullable: false),
                    enable_regularization_req = table.Column<bool>(type: "boolean", nullable: false),
                    enable_auto_shift_rotation = table.Column<bool>(type: "boolean", nullable: false),
                    enable_flexible_benefits = table.Column<bool>(type: "boolean", nullable: false),
                    enable_in_app_notifications = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feature_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "flexible_benefit_declarations",
                columns: table => new
                {
                    declaration_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employee_id = table.Column<int>(type: "integer", nullable: false),
                    financial_year = table.Column<int>(type: "integer", nullable: false),
                    component_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    annual_allocated_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    monthly_allocated_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    submission_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    admin_remarks = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flexible_benefit_declarations", x => x.declaration_id);
                });

            migrationBuilder.CreateTable(
                name: "fnf_settlements",
                columns: table => new
                {
                    settlement_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employee_id = table.Column<int>(type: "integer", nullable: false),
                    resignation_request_id = table.Column<int>(type: "integer", nullable: false),
                    settlement_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    unpaid_salary = table.Column<decimal>(type: "numeric", nullable: false),
                    leave_encashment_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    gratuity_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    bonus_payable = table.Column<decimal>(type: "numeric", nullable: false),
                    notice_period_recovery = table.Column<decimal>(type: "numeric", nullable: false),
                    asset_recovery_cost = table.Column<decimal>(type: "numeric", nullable: false),
                    outstanding_advances = table.Column<decimal>(type: "numeric", nullable: false),
                    net_payable = table.Column<decimal>(type: "numeric", nullable: false),
                    is_finalized = table.Column<bool>(type: "boolean", nullable: false),
                    payment_reference = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fnf_settlements", x => x.settlement_id);
                });

            migrationBuilder.CreateTable(
                name: "holidays",
                schema: "public",
                columns: table => new
                {
                    holidayid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    holidaydate = table.Column<DateOnly>(type: "date", nullable: false),
                    holidayname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_holidays", x => x.holidayid);
                });

            migrationBuilder.CreateTable(
                name: "leaverequests",
                columns: table => new
                {
                    leaverequestid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employeeid = table.Column<int>(type: "integer", nullable: false),
                    leavedate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    leavetype = table.Column<string>(type: "text", nullable: false),
                    is_half_day = table.Column<bool>(type: "boolean", nullable: false),
                    isapproved = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leaverequests", x => x.leaverequestid);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    notification_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    url = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    is_read = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.notification_id);
                });

            migrationBuilder.CreateTable(
                name: "payrollhistory",
                columns: table => new
                {
                    payrollid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employeeid = table.Column<int>(type: "integer", nullable: false),
                    paymonth = table.Column<int>(type: "integer", nullable: false),
                    payyear = table.Column<int>(type: "integer", nullable: false),
                    TotalLateness = table.Column<TimeSpan>(type: "interval", nullable: true),
                    TotalBreakPenalty = table.Column<TimeSpan>(type: "interval", nullable: true),
                    ScheduledShiftDuration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    basesalary = table.Column<decimal>(type: "numeric", nullable: true),
                    totalhoursworked = table.Column<decimal>(type: "numeric", nullable: true),
                    overtimepay = table.Column<decimal>(type: "numeric", nullable: true),
                    deductions_hours = table.Column<decimal>(type: "numeric", nullable: true),
                    deductions_advance = table.Column<decimal>(type: "numeric", nullable: true),
                    Bonus = table.Column<decimal>(type: "numeric", nullable: true),
                    netsalary = table.Column<decimal>(type: "numeric", nullable: false),
                    manualleavedays = table.Column<int>(type: "integer", nullable: false),
                    absentdays = table.Column<int>(type: "integer", nullable: false),
                    totalpenaltyduration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    totalovertimeduration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    HourlyRate = table.Column<decimal>(type: "numeric", nullable: false),
                    basic_salary_component = table.Column<decimal>(type: "numeric", nullable: false),
                    pf_deduction = table.Column<decimal>(type: "numeric", nullable: false),
                    esi_deduction = table.Column<decimal>(type: "numeric", nullable: false),
                    employer_pf_contribution = table.Column<decimal>(type: "numeric", nullable: false),
                    employer_esi_contribution = table.Column<decimal>(type: "numeric", nullable: false),
                    pt_deduction = table.Column<decimal>(type: "numeric", nullable: false),
                    tds_deduction = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalShiftAllowance = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payrollhistory", x => x.payrollid);
                });

            migrationBuilder.CreateTable(
                name: "professional_tax_slabs",
                columns: table => new
                {
                    slab_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    min_salary = table.Column<decimal>(type: "numeric", nullable: false),
                    max_salary = table.Column<decimal>(type: "numeric", nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_professional_tax_slabs", x => x.slab_id);
                });

            migrationBuilder.CreateTable(
                name: "report_definitions",
                columns: table => new
                {
                    report_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    report_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    report_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    target_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_definitions", x => x.report_id);
                });

            migrationBuilder.CreateTable(
                name: "resignation_requests",
                columns: table => new
                {
                    request_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employee_id = table.Column<int>(type: "integer", nullable: false),
                    submission_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    desired_last_working_day = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    approved_last_working_day = table.Column<DateOnly>(type: "date", nullable: true),
                    admin_remarks = table.Column<string>(type: "text", nullable: true),
                    is_settled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resignation_requests", x => x.request_id);
                });

            migrationBuilder.CreateTable(
                name: "salaryadvances",
                columns: table => new
                {
                    advanceid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employeeid = table.Column<int>(type: "integer", nullable: false),
                    advancedate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    advancetype = table.Column<string>(type: "text", nullable: true),
                    payrollid_paid = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_salaryadvances", x => x.advanceid);
                });

            migrationBuilder.CreateTable(
                name: "shiftschedules",
                columns: table => new
                {
                    scheduleid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employeeid = table.Column<int>(type: "integer", nullable: false),
                    shiftdate = table.Column<DateOnly>(type: "date", nullable: false),
                    starttime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    endtime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    is_recurring_pattern = table.Column<bool>(type: "boolean", nullable: false),
                    pattern_duration_days = table.Column<int>(type: "integer", nullable: false),
                    applies_to_day_of_week = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shiftschedules", x => x.scheduleid);
                });

            migrationBuilder.CreateTable(
                name: "tax_declarations",
                columns: table => new
                {
                    declaration_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employee_id = table.Column<int>(type: "integer", nullable: false),
                    financial_year = table.Column<int>(type: "integer", nullable: false),
                    regime = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    section_80c = table.Column<decimal>(type: "numeric", nullable: false),
                    section_80d = table.Column<decimal>(type: "numeric", nullable: false),
                    hra_rent_paid = table.Column<decimal>(type: "numeric", nullable: false),
                    other_exemptions = table.Column<decimal>(type: "numeric", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    admin_remarks = table.Column<string>(type: "text", nullable: true),
                    submission_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    approval_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_declarations", x => x.declaration_id);
                });

            migrationBuilder.CreateTable(
                name: "year_end_summaries",
                columns: table => new
                {
                    summary_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employeeid = table.Column<int>(type: "integer", nullable: false),
                    tax_year = table.Column<int>(type: "integer", nullable: false),
                    gross_taxable_salary = table.Column<decimal>(type: "numeric", nullable: false),
                    total_tds_deducted = table.Column<decimal>(type: "numeric", nullable: false),
                    total_pf_contribution_employee = table.Column<decimal>(type: "numeric", nullable: false),
                    total_annual_absent_days = table.Column<int>(type: "integer", nullable: false),
                    total_annual_ot_pay = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_year_end_summaries", x => x.summary_id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "attendance_regularizations");

            migrationBuilder.DropTable(
                name: "attendancelogs");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "bonus_records");

            migrationBuilder.DropTable(
                name: "CompanySettings",
                schema: "public");

            migrationBuilder.DropTable(
                name: "daily_summaries");

            migrationBuilder.DropTable(
                name: "employees",
                schema: "public");

            migrationBuilder.DropTable(
                name: "fbp_components");

            migrationBuilder.DropTable(
                name: "feature_settings");

            migrationBuilder.DropTable(
                name: "flexible_benefit_declarations");

            migrationBuilder.DropTable(
                name: "fnf_settlements");

            migrationBuilder.DropTable(
                name: "holidays",
                schema: "public");

            migrationBuilder.DropTable(
                name: "leaverequests");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "payrollhistory");

            migrationBuilder.DropTable(
                name: "professional_tax_slabs");

            migrationBuilder.DropTable(
                name: "report_definitions");

            migrationBuilder.DropTable(
                name: "resignation_requests");

            migrationBuilder.DropTable(
                name: "salaryadvances");

            migrationBuilder.DropTable(
                name: "shiftschedules");

            migrationBuilder.DropTable(
                name: "tax_declarations");

            migrationBuilder.DropTable(
                name: "year_end_summaries");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");
        }
    }
}
