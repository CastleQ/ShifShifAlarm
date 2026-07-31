using System;
using System.IO;

namespace EasyAlarm.Services
{
    /// <summary>
    /// 오류를 조용히 일지 파일에 기록하는 서기.
    /// 위치: %appdata%\EasyAlarm\error.log (alarms.json 옆)
    /// </summary>
    public static class LogService
    {
        private static readonly string _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EasyAlarm",
            "error.log");

        // 일지가 무한정 커지는 것을 막는 상한선 (1MB ≈ 오류 수천 건 분량)
        private const long MaxLogSizeBytes = 1_000_000;

        /// <summary>
        /// 예외를 일지에 기록합니다. 기록 자체가 실패해도 프로그램에 영향을 주지 않습니다.
        /// </summary>
        public static void LogError(string source, Exception exception)
        {
            try
            {
                string? folder = Path.GetDirectoryName(_logPath);
                if (folder != null)
                {
                    Directory.CreateDirectory(folder);
                }

                // 일지가 상한선을 넘으면 새로 시작 (오래된 기록보다 프로그램 건강이 우선)
                if (File.Exists(_logPath) && new FileInfo(_logPath).Length > MaxLogSizeBytes)
                {
                    File.Delete(_logPath);
                }

                string entry =
                    $"────────────────────────────────────{Environment.NewLine}" +
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}" +
                    $"{exception.GetType().Name}: {exception.Message}{Environment.NewLine}" +
                    $"{exception.StackTrace}{Environment.NewLine}";

                File.AppendAllText(_logPath, entry);
            }
            catch
            {
                // 서기가 펜을 놓쳐도(디스크 꽉 참 등) 프로그램은 멈추지 않는다.
                // 로그 기록 실패는 어떤 경우에도 새 오류를 일으켜선 안 됨.
            }
        }
    }
}