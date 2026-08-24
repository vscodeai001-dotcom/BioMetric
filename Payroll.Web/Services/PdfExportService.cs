using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Payroll.Shared.Data;
using Payroll.Shared;
using System.Globalization;

namespace Payroll.Web.Services
{
    public class PdfExportService
    {
        public byte[] GeneratePayslipPdf(PayrollHistory payslip, Employee employee, CompanySetting company)
        {
            // Enable Community License
            QuestPDF.Settings.License = LicenseType.Community;

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                    page.Header().Element(header => ComposeHeader(header, company));
                    page.Content().Element(content => ComposeContent(content, payslip, employee));
                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Page ");
                        x.CurrentPageNumber();
                        x.Span(" of ");
                        x.TotalPages();
                        x.Span($" | Generated on {DateTime.Now:dd-MMM-yyyy}");
                    });
                });
            });

            return document.GeneratePdf();
        }

        void ComposeHeader(IContainer container, CompanySetting company)
        {
            container.Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().Text(company.CompanyName).FontSize(20).SemiBold().FontColor(Colors.Blue.Medium);
                    column.Item().Text(company.AddressLine1);
                    column.Item().Text(company.CityStatePincode);
                });

                row.ConstantItem(100).AlignRight().Text("PAYSLIP").FontSize(18).SemiBold().FontColor(Colors.Grey.Medium);
            });
        }

        void ComposeContent(IContainer container, PayrollHistory payslip, Employee employee)
        {
            var culture = new CultureInfo("en-IN");

            // --- PAYSLIP VIEW MODEL RECREATION ---
            decimal earnedHours = payslip.TotalHoursWorked ?? 0m;
            decimal earnedPay = earnedHours * payslip.HourlyRate;
            decimal otPay = payslip.OvertimePay ?? 0m;
            decimal totalBonus = payslip.Bonus ?? 0m;
            decimal totalShiftAllowance = payslip.TotalShiftAllowance;

            // Gross Earnings (Basis for Statutory)
            decimal grossEarnings = earnedPay + otPay + totalBonus + totalShiftAllowance;

            // Total Deductions
            // INCLUDES: Statutory (PF/ESI/PT) + TDS + Penalties + Advances
            decimal statutoryDeductions = payslip.PfDeduction + payslip.EsiDeduction + payslip.PtDeduction;
            decimal tdsDeduction = payslip.TdsDeduction; // <-- NEW: TDS
            decimal otherDeductions = (payslip.Deductions_Hours ?? 0) + (payslip.Deductions_Advance ?? 0);

            decimal totalDeductionsDisplay = statutoryDeductions + tdsDeduction + otherDeductions;

            // --- PDF CONTENT START ---
            container.PaddingVertical(20).Column(column =>
            {
                // Employee Details Box
                column.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text($"Name: {employee.Name}").FontSize(11).SemiBold();
                        c.Item().Text($"Role: {employee.Role}");
                        c.Item().Text($"Pay Period: {new DateTime(payslip.PayYear, payslip.PayMonth, 1):MMM yyyy}");
                    });

                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text($"Employee ID: {employee.EmployeeID}");
                        c.Item().Text($"Bio ID: {employee.BiometricID ?? "N/A"}");
                        c.Item().Text($"PF UAN: {employee.UAN ?? "N/A"}");
                        if (employee.TdsRatePercent > 0)
                        {
                            c.Item().Text($"TDS Rate: {employee.TdsRatePercent:F1}%");
                        }
                    });
                });

                column.Item().PaddingVertical(10);

                // Earnings & Deductions Table
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        // 5 Columns: Desc, Amount (Earnings) | Desc, Amount (Deductions)
                        columns.RelativeColumn(3); // Earnings Description
                        columns.RelativeColumn(1); // Earnings Amount
                        columns.ConstantColumn(10); // Spacer
                        columns.RelativeColumn(3); // Deduction Description
                        columns.RelativeColumn(1); // Deduction Amount
                    });

                    // Row 1: Headers
                    table.Header(header =>
                    {
                        header.Cell().ColumnSpan(2).Element(HeaderStyle).Text("A. EARNINGS").SemiBold().FontColor(Colors.Green.Medium);
                        header.Cell().ColumnSpan(1).Element(SpacerStyle);
                        header.Cell().ColumnSpan(2).Element(HeaderStyle).Text("B. DEDUCTIONS").SemiBold().FontColor(Colors.Red.Medium);
                    });

                    // Row 2: Earned Pay vs Statutory PF
                    table.Cell().Element(CellStyle).Text("Earned Pay (Standard Hours)");
                    table.Cell().Element(CellStyle).AlignRight().Text(earnedPay.ToString("N2", culture));
                    table.Cell().Element(SpacerStyle);

                    table.Cell().Element(CellStyle).Text("Provident Fund (PF)");
                    table.Cell().Element(CellStyle).AlignRight().Text(payslip.PfDeduction > 0 ? payslip.PfDeduction.ToString("N2", culture) : "-");

                    // Row 3: Overtime vs ESI
                    table.Cell().Element(CellStyle).Text("Overtime Pay");
                    table.Cell().Element(CellStyle).AlignRight().Text(otPay > 0 ? otPay.ToString("N2", culture) : "-");
                    table.Cell().Element(SpacerStyle);

                    table.Cell().Element(CellStyle).Text("ESI");
                    table.Cell().Element(CellStyle).AlignRight().Text(payslip.EsiDeduction > 0 ? payslip.EsiDeduction.ToString("N2", culture) : "-");

                    // Row 4: Bonus vs Professional Tax
                    table.Cell().Element(CellStyle).Text("Bonus");
                    table.Cell().Element(CellStyle).AlignRight().Text(totalBonus > 0 ? totalBonus.ToString("N2", culture) : "-");
                    table.Cell().Element(SpacerStyle);

                    table.Cell().Element(CellStyle).Text("Professional Tax (PT)");
                    table.Cell().Element(CellStyle).AlignRight().Text(payslip.PtDeduction > 0 ? payslip.PtDeduction.ToString("N2", culture) : "-");

                    // Row 5: Shift Allowance vs TDS
                    // LOGIC: Only print the cell text if value > 0. Otherwise leave blank or print something else.

                    // Left Side (Shift Allowance)
                    if (totalShiftAllowance > 0)
                    {
                        table.Cell().Element(CellStyle).Text("Night Shift Allowance");
                        table.Cell().Element(CellStyle).AlignRight().Text(totalShiftAllowance.ToString("N2", culture));
                    }
                    else
                    {
                        // Fill with empty space to keep table alignment if Shift Allowance is OFF
                        table.Cell().Element(CellStyle).Text("");
                        table.Cell().Element(CellStyle).AlignRight().Text("");
                    }

                    table.Cell().Element(SpacerStyle);

                    // Right Side (TDS) - STRICT GATING
                    if (tdsDeduction > 0)
                    {
                        table.Cell().Element(CellStyle).Text("Income Tax (TDS)");
                        table.Cell().Element(CellStyle).AlignRight().Text(tdsDeduction.ToString("N2", culture));
                    }
                    else
                    {
                        // If TDS is disabled or 0, this cell becomes invisible/empty
                        table.Cell().Element(CellStyle).Text("");
                        table.Cell().Element(CellStyle).AlignRight().Text("");
                    }

                    // Row 6: Empty vs Penalty Hours
                    table.Cell().Element(CellStyle).Text("");
                    table.Cell().Element(CellStyle).AlignRight().Text("");
                    table.Cell().Element(SpacerStyle);

                    table.Cell().Element(CellStyle).Text("Attendance Penalty (Loss Hours)");
                    table.Cell().Element(CellStyle).AlignRight().Text(((payslip.Deductions_Hours ?? 0) > 0)
                        ? (payslip.Deductions_Hours!.Value).ToString("N2", culture)
                        : "-");

                    // Row 7: Empty vs Advance Deduction
                    table.Cell().Element(CellStyle).Text("");
                    table.Cell().Element(CellStyle).AlignRight().Text("");
                    table.Cell().Element(SpacerStyle);

                    table.Cell().Element(CellStyle).Text("Salary Advance Deduction");
                    table.Cell().Element(CellStyle).AlignRight().Text(((payslip.Deductions_Advance ?? 0) > 0)
                        ? (payslip.Deductions_Advance!.Value).ToString("N2", culture)
                        : "-");


                    // Totals Row
                    table.Cell().ColumnSpan(5).PaddingTop(10).BorderTop(1).BorderColor(Colors.Black).Row(totals =>
                    {
                        // Gross Earnings Left
                        totals.RelativeItem(3).Text("Gross Earnings (A):").SemiBold();
                        totals.RelativeItem(1).AlignRight().Text(grossEarnings.ToString("N2", culture)).SemiBold();

                        totals.ConstantItem(10); // Spacer

                        // Total Deductions Right
                        totals.RelativeItem(3).Text("Total Deductions (B):").SemiBold().FontColor(Colors.Red.Medium);
                        totals.RelativeItem(1).AlignRight().Text(totalDeductionsDisplay.ToString("N2", culture)).SemiBold();
                    });

                    // Net Pay Row
                    table.Cell().ColumnSpan(5).PaddingVertical(10).Row(netPay =>
                    {
                        netPay.RelativeItem(3).Text("NET SALARY PAYABLE:").FontSize(14).SemiBold().FontColor(Colors.Green.Darken2);
                        netPay.RelativeItem(1).AlignRight().Text(payslip.NetSalary.ToString("C", culture)).FontSize(14).SemiBold().FontColor(Colors.Green.Darken2);
                        netPay.ConstantItem(10); // Spacer
                        netPay.RelativeItem(4); // Empty on the right
                    });
                });


                column.Item().PaddingTop(20).Text("This is a computer-generated payslip. Please contact payroll for details.").FontSize(8).Italic().AlignCenter();
            });

            static IContainer CellStyle(IContainer container) => container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5);
            static IContainer HeaderStyle(IContainer container) => container.Padding(5).BorderBottom(1).BorderColor(Colors.Grey.Medium);
            static IContainer SpacerStyle(IContainer container) => container.Background(Colors.Grey.Lighten4);
        }
    }
}