using System;
using Microsoft.Win32;

namespace EasyAlarm.Services
{
    /// <summary>
    /// 윈도우 시작 시 자동 실행 담당.
    /// 윈도우의 '시작 프로그램 명단'(레지스트리 Run 키)에 우리 exe 경로를 적거나 지웁니다.
    /// 관리자 권한이 필요 없는 사용자 전용 영역입니다.
    /// </summary>
    public static class StartupService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "ShifShifAlarm";

        /// <summary>
        /// 개발 빌드로 실행 중인지 판단합니다.
        /// ⚠️ 개발 빌드 경로(bin\Debug…)를 자동 시작에 등록하면
        ///    재빌드 때마다 경로가 깨지거나 구버전이 부팅 시 살아나는 '유령' 사고가 납니다.
        ///    그래서 개발 빌드에서는 자동 시작 설정을 막습니다.
        /// </summary>
        public static bool IsDevBuild =>
            AppContext.BaseDirectory.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase);

        /// <summary>현재 자동 시작이 등록되어 있는지</summary>
        public static bool IsEnabled()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                return key?.GetValue(ValueName) != null;
            }
            catch (Exception ex)
            {
                LogService.LogError("자동 시작 상태 확인", ex);
                return false;
            }
        }

        /// <summary>자동 시작을 켜거나 끕니다. 성공 여부를 돌려줍니다.</summary>
        public static bool SetEnabled(bool enabled)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);

                if (key == null)
                {
                    return false;
                }

                if (!enabled)
                {
                    key.DeleteValue(ValueName, false);
                    return true;
                }

                if (IsDevBuild)
                {
                    return false;   // 개발 빌드는 등록하지 않음
                }

                string? exePath = Environment.ProcessPath;

                if (string.IsNullOrWhiteSpace(exePath))
                {
                    return false;
                }

                // 경로에 공백이 있어도 안전하도록 큰따옴표로 감쌉니다
                key.SetValue(ValueName, $"\"{exePath}\"");
                return true;
            }
            catch (Exception ex)
            {
                LogService.LogError("자동 시작 설정", ex);
                return false;
            }
        }

        /// <summary>
        /// 설정값과 실제 등록 상태를 일치시킵니다. (프로그램 시작 시 1회 호출)
        /// 예: 설정은 '켜짐'인데 exe를 옮겨 등록이 끊긴 경우 → 새 경로로 다시 등록
        /// </summary>
        public static void Synchronize(bool desired)
        {
            if (IsDevBuild)
            {
                return;   // 개발 빌드에서는 아무것도 건드리지 않음
            }

            bool actual = IsEnabled();

            if (desired && !actual)
            {
                SetEnabled(true);
            }
            else if (!desired && actual)
            {
                SetEnabled(false);
            }
            else if (desired)
            {
                SetEnabled(true);   // 경로가 바뀐 경우 대비해 항상 최신 경로로 덮어쓰기
            }
        }
    }
}