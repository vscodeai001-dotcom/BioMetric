using System;
using System.Collections.Generic;
using System.Linq;
using Payroll.Shared.Data;

namespace Payroll.Shared.Services
{
    public class SalaryStructureService
    {
        private const decimal PfWageCeiling = 15000.00m;
        // Updated Signature: Now accepts 'ptSlabs'
        public SalaryBreakdown CalculateBreakdown(decimal grossEarned, Employee emp, CompanySetting settings, 
            List<ProfessionalTaxSlab> ptSlabs, FeatureSettings featureSettings)
        {
            var result = new SalaryBreakdown();

            // --- 1. PF & ESI Logic (Existing) ---
            if (settings.EnablePfEsiSystem)
            {
                decimal basicCalculationBasis;

                // CRITICAL FEATURE GATE: Use structured component ONLY if feature is enabled.
                if (featureSettings.EnableSalaryStructuring == true && emp.BasicSalaryComponent > 0) // <-- FIX: Use 'featureSettings' parameter directly
                {
                    basicCalculationBasis = emp.BasicSalaryComponent;
                }
                else
                {
                    // FALLBACK: Use CompanySetting percentage of gross (existing logic)
                    decimal basicPercent = settings.BasicSalaryPercentage > 0 ? settings.BasicSalaryPercentage / 100m : 0.40m;
                    basicCalculationBasis = Math.Round(grossEarned * basicPercent, 0);
                }

                result.BasicSalary = basicCalculationBasis; // Store the actual basis used

                if (emp.EnablePF)
                {
                    // CRITICAL FIX: Apply PF Wage Ceiling (usually on Basic, capped at 15000)
                    decimal pfBasis = Math.Min(result.BasicSalary, PfWageCeiling);

                    // PF Deduction (Employee Share)
                    result.PfDeduction = Math.Round(pfBasis * (settings.EmployeePfPercentage / 100m), 0);

                    // Employer PF Contribution
                    result.EmployerPfContribution = Math.Round(pfBasis * (settings.EmployerPfPercentage / 100m), 0);
                }

                if (emp.EnableESI)
                {
                    if (grossEarned <= settings.EsiWageLimit)
                    {
                        result.EsiDeduction = Math.Ceiling(grossEarned * (settings.EmployerEsiPercentage / 100m));
                        result.EmployerEsiContribution = Math.Ceiling(grossEarned * (settings.EmployerEsiPercentage / 100m));
                    }
                }
            }

            // --- 2. Professional Tax Logic (NEW) ---
            if (settings.EnableProfessionalTax && ptSlabs != null && ptSlabs.Any())
            {
                // Find the slab that matches the Gross Earned
                var match = ptSlabs.FirstOrDefault(s => grossEarned >= s.MinSalary && grossEarned <= s.MaxSalary);
                if (match != null)
                {
                    result.PtDeduction = match.TaxAmount;
                }
            }

            // --- 3. Final Totals ---
            result.TotalDeductions = result.PfDeduction + result.EsiDeduction + result.PtDeduction;
            result.NetPayable = grossEarned - result.TotalDeductions;

            return result;
        }
    }

    public class SalaryBreakdown
    {
        public decimal BasicSalary { get; set; }
        public decimal PfDeduction { get; set; }
        public decimal EsiDeduction { get; set; }
        public decimal PtDeduction { get; set; } // NEW

        public decimal EmployerPfContribution { get; set; }
        public decimal EmployerEsiContribution { get; set; }

        public decimal TotalDeductions { get; set; }
        public decimal NetPayable { get; set; }
    }
}