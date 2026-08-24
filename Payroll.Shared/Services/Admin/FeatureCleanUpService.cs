using Microsoft.EntityFrameworkCore;
using Payroll.Shared.Data;
using System.Threading.Tasks;

namespace Payroll.Shared.Services
{
    public class FeatureCleanUpService
    {
        private readonly AppDbContext _dbContext;

        public FeatureCleanUpService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // --- THIS IS THE NEW CONSOLIDATED METHOD ---
        public async Task ResetAllTogglesAsync()
        {
            // Find the existing settings (Id = 1)
            var entity = await _dbContext.FeatureSettings.FindAsync(1);
            if (entity != null)
            {
                _dbContext.FeatureSettings.Remove(entity);
                await _dbContext.SaveChangesAsync();
            }

            // Add a new default record
            await InsertDefaultSettingsAsync();
        }

        // --- Helper Method to Re-Insert Defaults ---
        private async Task InsertDefaultSettingsAsync()
        {
            if (!await _dbContext.FeatureSettings.AnyAsync(t => t.Id == 1))
            {
                _dbContext.FeatureSettings.Add(new FeatureSettings());
                await _dbContext.SaveChangesAsync();
            }
        }

        // --- THIS METHOD IS UNCHANGED ---
        public async Task WipeAllTransactionalDataAsync()
        {
            // 1. Truncate tables not linked by foreign key
            await _dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE public.shiftschedules, public.daily_summaries RESTART IDENTITY;");

            // 2. Truncate employees (which cascades to logs, payroll, advances, leave)
            await _dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE public.employees RESTART IDENTITY CASCADE;");

            // 3. Truncate all user accounts (which cascades to their role links)
            await _dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE public.\"AspNetUsers\" RESTART IDENTITY CASCADE;");
        }


        // --- OLD METHODS (NOW DELETED) ---
        // public async Task ResetClientTogglesAsync()...
        // public async Task ResetAdminTogglesAsync()...
        // public async Task ResetEmployeeTogglesAsync()...
    }
}