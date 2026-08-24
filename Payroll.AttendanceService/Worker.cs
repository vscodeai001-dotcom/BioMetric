using Microsoft.EntityFrameworkCore;
using Payroll.Shared;
using Payroll.AttendanceService.Services;

namespace Payroll.AttendanceService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IZkDevice _zkem;
        private readonly IConfiguration _configuration;

        // Configurable fields are now initialized dynamically inside ExecuteAsync
        private string _deviceIP = string.Empty;
        private int _devicePort;
        private int _machineNumber;
        private readonly int _pollInterval;
        private readonly bool _clearLogs;

        public Worker(
            ILogger<Worker> logger,
            IConfiguration config,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _configuration = config;
            // Use fallback implementation when COM interop is not available at build time.
            _zkem = new ZkDeviceFallback();

            // Read service rules from appsettings (these are NOT in DB, only service config)
            _pollInterval = config.GetValue<int>("AttendanceServiceRules:PollIntervalSeconds", 60);
            _clearLogs = config.GetValue<bool>("AttendanceServiceRules:ClearLogsAfterDownload", false);
        }

        private async Task<bool> LoadDeviceSettingsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var settings = await dbContext.CompanySettings.AsNoTracking().FirstOrDefaultAsync(s => s.SettingID == 1);

            if (settings == null || string.IsNullOrEmpty(settings.ZktecoIP))
            {
                _logger.LogError("CRITICAL: Device settings (IP) not found in CompanySettings table. Worker cannot connect.");
                return false;
            }

            _deviceIP = settings.ZktecoIP!;
            _devicePort = settings.ZktecoPort;
            _machineNumber = settings.ZktecoMachineNumber;

            _logger.LogInformation("Worker configured from DB: {ip}:{port} (MachineNo: {num})", _deviceIP, _devicePort, _machineNumber);

            return true;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Attendance Service starting up...");

            while (!stoppingToken.IsCancellationRequested)
            {
                // Check if device settings are loaded and valid on every loop
                if (!await LoadDeviceSettingsAsync())
                {
                    _logger.LogWarning("Waiting {seconds} seconds for device settings to be configured...", _pollInterval);
                    await Task.Delay(TimeSpan.FromSeconds(_pollInterval), stoppingToken);
                    continue;
                }

                bool isConnected = false;
                try
                {
                    // --- 1. Connect to the Device ---
                    _logger.LogInformation("Attempting to connect to device at {ip}...", _deviceIP);
                    isConnected = _zkem.Connect_Net(_deviceIP, _devicePort);

                    if (isConnected)
                    {
                        _logger.LogInformation("Successfully connected to device.");

                        // --- 2. Download Logs ---
                        if (_zkem.ReadAllGLogData(_machineNumber))
                        {
                            _logger.LogInformation("Successfully downloaded logs. Processing...");

                            // --- 3. Process and Save Logs to Database ---
                            await ProcessLogs(stoppingToken);
                        }
                        else
                        {
                            _logger.LogWarning("Failed to download logs from device.");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Failed to connect to device.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while connecting or processing logs.");
                }
                finally
                {
                    // Disconnect after each attempt
                    if (isConnected)
                    {
                        _zkem.Disconnect();
                        _logger.LogInformation("Disconnected from device.");
                    }
                }

                _logger.LogInformation("Waiting {seconds} seconds for next poll...", _pollInterval);
                await Task.Delay(TimeSpan.FromSeconds(_pollInterval), stoppingToken);
            }
        }

        private async Task ProcessLogs(CancellationToken stoppingToken)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var lastLog = await dbContext.AttendanceLogs.OrderByDescending(log => log.PunchTime).FirstOrDefaultAsync(stoppingToken);
                DateTime lastLogTime = lastLog?.PunchTime ?? DateTime.MinValue;

                _logger.LogInformation("Getting logs since {time}", lastLogTime);

                string biometricID;
                int verifyMode, inOutMode, year, month, day, hour, minute, second, workCode;
                workCode = 0;

                int newLogCount = 0;
                int unmatchedLogCount = 0;

                while (_zkem.SSR_GetGeneralLogData(_machineNumber, out biometricID, out verifyMode,
                           out inOutMode, out year, out month, out day, out hour, out minute, out second, ref workCode))
                {
                    if (year <= 2000) continue;

                    var punchTime = new DateTime(year, month, day, hour, minute, second);

                    if (punchTime > lastLogTime)
                    {
                        var employee = await dbContext.Employees
                            .FirstOrDefaultAsync(e => e.BiometricID == biometricID, stoppingToken);

                        if (employee != null)
                        {
                            newLogCount++;

                            var newLog = new AttendanceLog
                            {
                                BiometricID = biometricID,
                                PunchTime = punchTime,
                                EmployeeID = employee.EmployeeID,
                                DeviceID = $"ZKTeco_{_deviceIP}",
                                LogType = "Punch"
                            };

                            await dbContext.AttendanceLogs.AddAsync(newLog, stoppingToken);
                        }
                        else
                        {
                            unmatchedLogCount++;
                            _logger.LogWarning("Discarding punch for unmatched BiometricID: {BioId} at {Time}", biometricID, punchTime);
                        }
                    }
                }

                if (newLogCount > 0)
                {
                    await dbContext.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation("Successfully saved {count} new attendance logs.", newLogCount);

                    // Notify the running Web application only after the
                    // existing biometric database save has succeeded.
                    await NotifyWebApplicationAsync();
                }
                else
                {
                    _logger.LogInformation("No new logs found.");
                }

                if (unmatchedLogCount > 0)
                {
                    _logger.LogWarning("Ignored {count} punches due to no matching Employee BiometricID.", unmatchedLogCount);
                }

                if (newLogCount > 0 && _clearLogs)
                {
                    _logger.LogInformation("ClearLogsAfterDownload is TRUE. Attempting to clear device logs...");
                    if (_zkem.ClearGLog(_machineNumber))
                    {
                        _logger.LogInformation("Successfully cleared logs from device memory.");
                    }
                    else
                    {
                        _logger.LogWarning("Failed to clear logs from device memory.");
                    }
                }
            }
        }

        private async Task NotifyWebApplicationAsync()
        {
            try
            {
                var webBaseUrl =
                    _configuration["AttendanceRefresh:WebBaseUrl"];

                var secret =
                    _configuration["AttendanceRefresh:Secret"];

                if (string.IsNullOrWhiteSpace(webBaseUrl) ||
                    string.IsNullOrWhiteSpace(secret))
                {
                    _logger.LogWarning(
                        "Attendance refresh Web configuration is missing. " +
                        "Biometric attendance was saved successfully, but live UI notification was skipped.");

                    return;
                }

                using var client =
                    new HttpClient
                    {
                        Timeout = TimeSpan.FromSeconds(10)
                    };

                using var request =
                    new HttpRequestMessage(
                        HttpMethod.Post,
                        $"{webBaseUrl.TrimEnd('/')}/api/internal/attendance-refresh");

                request.Headers.Add(
                    "X-Attendance-Refresh-Secret",
                    secret);

                using var response =
                    await client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Web attendance refresh notification failed. HTTP {StatusCode}.",
                        (int)response.StatusCode);

                    return;
                }

                _logger.LogInformation(
                    "Web application notified about new biometric attendance.");
            }
            catch (Exception ex)
            {
                // A UI notification failure must NEVER break the biometric
                // download/save flow.
                _logger.LogWarning(
                    ex,
                    "Unable to notify Web application about biometric attendance change.");
            }
        }
    }
}