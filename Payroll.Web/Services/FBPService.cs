using Microsoft.EntityFrameworkCore;
using Payroll.Shared.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Payroll.Web.Services
{
    public class FBPService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public FBPService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // --- ADMIN: MANAGE COMPONENTS ---

        public async Task<List<FBPComponent>> GetActiveComponentsAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.FBPComponents.Where(c => c.IsActive).ToListAsync();
        }

        public async Task SaveComponentAsync(FBPComponent component)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            if (component.ComponentId == 0)
            {
                db.FBPComponents.Add(component);
            }
            else
            {
                db.FBPComponents.Update(component);
            }
            await db.SaveChangesAsync();
        }

        // --- EMPLOYEE: DECLARATION LOGIC ---

        public async Task<List<FlexibleBenefitDeclaration>> GetDeclarationsAsync(int employeeId, int financialYear)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.FlexibleBenefitDeclarations
                .Where(d => d.EmployeeId == employeeId && d.FinancialYear == financialYear)
                .ToListAsync();
        }

        public async Task SaveDeclarationsAsync(int employeeId, int financialYear, List<FlexibleBenefitDeclaration> declarations)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            // Delete existing drafts/submissions for the year to replace them
            var existing = await db.FlexibleBenefitDeclarations
                .Where(d => d.EmployeeId == employeeId && d.FinancialYear == financialYear && d.Status != "Locked")
                .ToListAsync();
            db.FlexibleBenefitDeclarations.RemoveRange(existing);

            // Calculate monthly amount for new submissions
            int remainingMonths = 12; // Simplified: Assume declaration is done at the start of the year

            foreach (var declaration in declarations)
            {
                if (declaration.AnnualAllocatedAmount > 0)
                {
                    declaration.EmployeeId = employeeId;
                    declaration.FinancialYear = financialYear;
                    declaration.Status = "Submitted";
                    declaration.SubmissionDate = DateTime.Now;

                    // CRITICAL CALCULATION: Pro-rata monthly distribution
                    declaration.MonthlyAllocatedAmount = Math.Round(declaration.AnnualAllocatedAmount / remainingMonths, 2);

                    db.FlexibleBenefitDeclarations.Add(declaration);
                }
            }
            await db.SaveChangesAsync();
        }

        // --- PAYROLL INTEGRATION LOGIC ---

        /// <summary>
        /// Retrieves the active FBP monthly allocation for an employee.
        /// </summary>
        public async Task<Dictionary<string, decimal>> GetActiveMonthlyAllocationsAsync(int employeeId, int financialYear)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            // Only consider declarations that have been APPROVED or are currently ACTIVE and Locked
            var declarations = await db.FlexibleBenefitDeclarations
                .Where(d => d.EmployeeId == employeeId &&
                            d.FinancialYear == financialYear &&
                            d.Status == "Approved" &&
                            d.IsActive)
                .ToDictionaryAsync(d => d.ComponentName, d => d.MonthlyAllocatedAmount);

            return declarations;
        }
    }
}