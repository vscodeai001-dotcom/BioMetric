using System;

namespace Payroll.AttendanceService.Services
{
    // Minimal abstraction for ZK device used by Worker.
    // Provides safe no-op fallback implementation when COM interop is not available.
    public interface IZkDevice
    {
        bool Connect_Net(string ip, int port);
        bool ReadAllGLogData(int machineNumber);
        bool SSR_GetGeneralLogData(int machineNumber, out string biometricID, out int verifyMode,
            out int inOutMode, out int year, out int month, out int day, out int hour, out int minute, out int second, ref int workCode);
        bool ClearGLog(int machineNumber);
        void Disconnect();
    }

    public class ZkDeviceFallback : IZkDevice
    {
        public bool Connect_Net(string ip, int port)
        {
            // No device available in fallback mode
            return false;
        }

        public bool ReadAllGLogData(int machineNumber)
        {
            return false;
        }

        public bool SSR_GetGeneralLogData(int machineNumber, out string biometricID, out int verifyMode,
            out int inOutMode, out int year, out int month, out int day, out int hour, out int minute, out int second, ref int workCode)
        {
            biometricID = string.Empty;
            verifyMode = 0;
            inOutMode = 0;
            year = 0; month = 0; day = 0; hour = 0; minute = 0; second = 0;
            return false;
        }

        public bool ClearGLog(int machineNumber)
        {
            return false;
        }

        public void Disconnect()
        {
            // nothing to do
        }
    }
}
