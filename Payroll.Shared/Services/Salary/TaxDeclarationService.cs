using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Payroll.Shared.Data;
using System;
using System.Threading.Tasks;

namespace Payroll.Shared.Services
{
    public class TaxDeclarationService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ILogger<TaxDeclarationService> _logger;

        public TaxDeclarationService(IDbContextFactory<AppDbContext> dbFactory, ILogger<TaxDeclarationService> logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
        }

        public async Task<TaxDeclaration> GetOrCreateDeclarationAsync(int employeeId, int financialYear)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var decl = await db.TaxDeclarations
                .FirstOrDefaultAsync(t => t.EmployeeId == employeeId && t.FinancialYear == financialYear);

            if (decl == null)
            {
                decl = new TaxDeclaration
                {
                    EmployeeId = employeeId,
                    FinancialYear = financialYear,
                    Regime = "New", // Default to New Regime (Govt Standard)
                    Status = "Draft"
                };
                db.TaxDeclarations.Add(decl);
                await db.SaveChangesAsync();
            }
            return decl;
        }

        public async Task SubmitAsync(TaxDeclaration model)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var existing = await db.TaxDeclarations.FindAsync(model.DeclarationId);

            if (existing != null)
            {
                if (existing.Status == "Approved")
                    throw new InvalidOperationException("Cannot modify an approved declaration.");

                // Update fields
                existing.Regime = model.Regime;
                existing.Section80C = model.Section80C;
                existing.Section80D = model.Section80D;
                existing.HraRentPaid = model.HraRentPaid;
                existing.OtherExemptions = model.OtherExemptions;

                existing.Status = "Pending"; // Send for approval
                existing.SubmissionDate = DateTime.Now;

                await db.SaveChangesAsync();
            }
        }

        public async Task ApproveAsync(int declarationId, string remarks)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var existing = await db.TaxDeclarations.FindAsync(declarationId);
            if (existing != null)
            {
                existing.Status = "Approved";
                existing.AdminRemarks = remarks;
                existing.ApprovalDate = DateTime.Now;
                await db.SaveChangesAsync();
            }
        }

        public async Task RejectAsync(int declarationId, string remarks)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var existing = await db.TaxDeclarations.FindAsync(declarationId);
            if (existing != null)
            {
                existing.Status = "Rejected"; // Sends back to employee
                existing.AdminRemarks = remarks;
                await db.SaveChangesAsync();
            }
        }
    }
}