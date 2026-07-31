using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EasyAlarm.Utils
{
    /// <summary>
    /// 위젯 창의 특수 동작(도구 창 전환, 실물 상태 점검/복구)을 담당.
    /// WPF의 장부(WindowState)가 아니라 운영체제의 실제 상태를 직접 다룹니다.
    /// </summary>
    public static class DesktopWindowHelper
    {
        // ── 윈도우 운영체제의 저수준 기능(Win32 API)을 빌려오는 선언 ──

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        /// <summary>창이 '실제로' 최소화되어 있는지 운영체제에 직접 묻습니다.</summary>
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        /// <summary>창의 표시 상태를 운영체제 레벨에서 직접 바꿉니다.</summary>
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        // 창의 '확장 속성' 서랍 번호
        private const int GWL_EXSTYLE = -20;

        // 도구 창 속성: Win+M 방송 수신 제외 + Alt+Tab 목록에서도 제외
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        // ShowWindow 명령어: 활성화(포커스 뺏기) 없이 원래 크기로 복구
        private const int SW_SHOWNOACTIVATE = 4;

        /// <summary>
        /// 창을 도구 창으로 전환합니다. (Win+M 생존용)
        /// 반드시 창이 화면에 표시된 이후(Loaded)에 호출해야 합니다.
        /// </summary>
        public static void PinToDesktop(Window window)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
        }

        /// <summary>
        /// [감시견용] 창이 실제로 넘어져 있으면 일으켜 세웁니다.
        /// WPF 장부가 아닌 운영체제 실물 상태를 기준으로 판단하므로,
        /// Win+D처럼 장부에 기록을 안 남기는 강제 최소화도 잡아냅니다.
        /// 복구 시 포커스를 뺏지 않아(NOACTIVATE) 사용자의 작업을 방해하지 않습니다.
        /// </summary>
        public static void RestoreIfMinimized(Window window)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, SW_SHOWNOACTIVATE);

                // WPF 장부도 실물과 일치하도록 동기화
                window.WindowState = WindowState.Normal;
            }
        }
    }
}