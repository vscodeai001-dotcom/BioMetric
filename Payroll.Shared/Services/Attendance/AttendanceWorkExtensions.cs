using System;
using System.Collections.Generic;
using Payroll.Shared;

namespace Payroll.Shared.Services
{
    /// <summary>
    /// Helpers to compute gross worked time from paired punches.
    /// </summary>
    public static class AttendanceWorkExtensions
    {
        public static TimeSpan GrossWorked(this (List<AttendanceLog> Ordered, DateTime? FirstIn, DateTime? LastOut) pr)
        {
            var list = pr.Ordered;
            if (list == null || list.Count < 2) return TimeSpan.Zero;

            TimeSpan total = TimeSpan.Zero;
            // Pair strictly by index: [0]=IN, [1]=OUT, [2]=IN, [3]=OUT, ...
            for (int i = 0; i + 1 < list.Count; i += 2)
            {
                var start = list[i].PunchTime;
                var end = list[i + 1].PunchTime;
                if (end > start) total += (end - start);
            }
            return total;
        }
    }
}
