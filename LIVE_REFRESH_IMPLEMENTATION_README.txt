BioMetric Live Refresh - Full Source Replacement
Generated: 2026-08-22

IMPORTANT
This package is a source-only replacement. bin, obj, .git and Visual Studio generated folders are intentionally excluded.

GOAL
Add non-invasive real-time UI refresh for attendance-related changes without changing:
- database schema
- existing application layout
- existing attendance calculation rules
- existing Re-Process behavior
- GPS/session lifecycle
- login/logout flow
- biometric punch processing rules

WHAT WAS CHANGED

1. Payroll.Web/Components/App.razor
   - Loads SignalR browser client.
   - Loads wwwroot/js/attendance-refresh.js.

2. Payroll.Web/wwwroot/js/attendance-refresh.js
   - Connects to /hubs/attendance-refresh.
   - Supports AttendanceChanged, LocationChanged and RegularizationChanged.
   - Supports page registration/unregistration.
   - Automatically reconnects.

3. Payroll.Web/Components/UI/Attendance/AttendanceRefreshListener.razor
   - Reusable Blazor listener for relevant pages.
   - Does not change page layout.

4. Payroll.Web/Components/Pages/Attendance/AttendanceLogViewer.razor
   - Existing LoadLogs() is called when AttendanceChanged arrives.
   - Existing Re-Process remains unchanged.
   - No attendance calculation was added to the notification path.

5. Payroll.Web/Services/GeoLocationService.cs
   - After an existing successful mobile AttendanceLog save, sends AttendanceChanged.
   - Notification failure cannot invalidate a successful punch.

6. Payroll.Web/Services/RegularizationService.cs
   - Existing regularization save remains unchanged.
   - Pending request sends RegularizationChanged.
   - Approved injected punch sends AttendanceChanged and RegularizationChanged.

7. Payroll.Web/Components/Pages/Attendance/ManualPunchCorrection.razor
   - Existing add/delete/edit/recalculate behavior remains.
   - Existing calculation is still used.
   - Adds refresh notification after successful existing operation.

8. Payroll.Web/Components/Pages/Attendance/PunchCorrectionApproval.razor
   - Existing approve/reject/recalculate behavior remains.
   - Adds refresh notification after successful existing operation.

9. Relevant attendance/regularization pages
   - CompanyAttendanceReport
   - MyAttendanceViewer
   - RegularizationApproval
   - MyRegularization
   now listen for the appropriate refresh events.

10. Payroll.Web/Controllers/InternalAttendanceRefreshController.cs
    - Internal endpoint for the separate biometric Worker.
    - Protected by a shared secret.
    - Endpoint: POST /api/internal/attendance-refresh

11. Payroll.AttendanceService/Worker.cs
    - Existing biometric download and DB save logic remains unchanged.
    - After a successful existing SaveChangesAsync(), it sends a notification to Payroll.Web.
    - Notification failure never breaks biometric processing.

12. Payroll.AttendanceService/Program.cs
    - No database or biometric registration changes.

13. appsettings
    Web:
      AttendanceRefresh:WorkerSecret
    Worker:
      AttendanceRefresh:WebBaseUrl
      AttendanceRefresh:Secret

SHARED INTERNAL SECRET
The Web and Worker configs in this package already contain the same generated internal refresh secret.
Do not publish this value publicly. If you change it, change it in BOTH appsettings files.

NETWORK
The Worker uses:
  http://localhost:5050

This assumes Payroll.Web and Payroll.AttendanceService run on the same Windows machine.
If the Worker runs on a different computer, change Worker:
  AttendanceRefresh:WebBaseUrl
to the Payroll.Web server address, for example:
  http://192.168.x.x:5050

IMPORTANT
This refresh layer does NOT automatically run Re-Process after a punch.
That is intentional because the requirement was to preserve the existing calculation/business rules.
Existing Re-Process remains the calculation authority.

BUILD NOTE
The environment used to prepare this package did not have executable access to the dotnet CLI, so a local compile could not be performed here.
After replacement, run:
  dotnet build Payroll.Web/Payroll.Web.csproj
  dotnet build Payroll.AttendanceService/Payroll.AttendanceService.csproj

