using Payroll.Shared;
using Payroll.Shared.Data;
using System.Globalization;
using System.Text;

namespace Payroll.Web.Services
{
    public class BankExportService
    {
        public string GenerateSalaryUploadCsv(List<PayrollHistory> payrolls, List<Employee> employees)
        {
            var sb = new StringBuilder();
            var inrCulture = new CultureInfo("en-IN");

            // Standard Header for Bulk Upload (Generic Format compatible with HDFC/ICICI/SBI)
            // Format: PaymentType, BeneAccountNo, Amount, BeneName, IFSC, Reference
            sb.AppendLine("Payment Mode,Beneficiary Account No,Amount,Beneficiary Name,IFSC Code,Narration,Employee ID");

            foreach (var pay in payrolls)
            {
                if (pay.NetSalary <= 0) continue; // Skip zero payouts

                var emp = employees.FirstOrDefault(e => e.EmployeeID == pay.EmployeeID);
                if (emp == null || string.IsNullOrWhiteSpace(emp.BankAccountNumber)) continue; // Skip missing bank details

                string paymentMode = "NEFT"; // Default to NEFT or INTRA
                string narration = $"Salary {new DateTime(pay.PayYear, pay.PayMonth, 1):MMM yyyy}";

                // CSV Safe Name
                string safeName = emp.Name.Replace(",", " ").Trim();

                sb.AppendLine(string.Join(",",
                    paymentMode,
                    emp.BankAccountNumber,
                    pay.NetSalary.ToString("F2", CultureInfo.InvariantCulture), // Banks usually expect pure decimal (no commas)
                    safeName,
                    emp.BankIfscCode,
                    narration,
                    emp.EmployeeID
                ));
            }

            return sb.ToString();
        }
    }
}