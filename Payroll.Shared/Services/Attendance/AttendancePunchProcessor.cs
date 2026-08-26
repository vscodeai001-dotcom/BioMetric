using System;
using System.Collections.Generic;
using System.Linq;
using Payroll.Shared;

namespace Payroll.Shared.Services
{
    
    public class AttendancePunchProcessor
    {
        public (List<AttendanceLog> Ordered, DateTime? FirstIn, DateTime? LastOut)
            ProcessPunches(List<AttendanceLog> punches, DateTime day)
        {
            if (punches == null || punches.Count == 0)
                return (new List<AttendanceLog>(), null, null);

            var ordered = punches.OrderBy(p => p.PunchTime).ToList();

            // First punch treated as IN, next as OUT, etc.
            DateTime? firstIn = ordered.FirstOrDefault()?.PunchTime;
            DateTime? lastOut = null;

            if (ordered.Count >= 2)
                lastOut = ordered.Last().PunchTime;

            // If odd count, close the last IN with a dummy OUT at the same time
            if (ordered.Count % 2 == 1)
            {
                var last = ordered.Last();
                ordered.Add(new AttendanceLog
                {
                    PunchTime = last.PunchTime,
                    BiometricID = last.BiometricID,
                    EmployeeID = last.EmployeeID,
                    DeviceID = last.DeviceID,
                    LogType = last.LogType
                });
                lastOut = last.PunchTime;
            }

            return (ordered, firstIn, lastOut);
        }
    }
}
