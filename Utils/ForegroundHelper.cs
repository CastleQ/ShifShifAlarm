using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace EasyAlarm.Utils
{
    /// <summary>
    /// 팝업 입력창을 '키보드 입력까지 확실히 받는 상태'로 최전면에 띄우는 담당.
    ///
    /// 왜 필요한가: 백그라운드 키보드 감지기에서 띄운 창은 윈도우 보안 정책상
    /// 자동으로 입력 포커스를 얻지 못합니다(화면엔 보이지만 타이핑이 원래 창으로 감).
    /// 그래서 현재 활성 창의 입력 처리 줄에 잠깐 편승(AttachThreadInput)해
    /// 포커스를 정당하게 넘겨받고 즉시 분리합니다.
    ///
    /// ⚠️ 이 파일은 '팝업 전용'입니다. 목록 창·설정 창은 기존 방식(BringToFront)을 그대로 쓰며,
    ///    과거 '목록 창이 다른 창 뒤로 가던' 문제의 재발 경로를 만들지 않기 위해 완전히 분리했습니다.
    /// </summary>
    public static class ForegroundHelper
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_SHOW = 5;

        /// <summary>
        /// 창을 최전면으로 끌어올리고 키보드 입력 포커스까지 가져옵니다.
        /// 창이 화면에 표시된 이후(Loaded 이후)에 호출해야 합니다.
        /// </summary>
        /// <param name="focusTarget">포커스를 최종적으로 받을 요소 (예: 입력칸)</param>
        public static void ForceForeground(Window window, IInputElement? focusTarget = null)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;

            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            uint currentThread = GetCurrentThreadId();
            uint foregroundThread = 0;
            bool attached = false;

            try
            {
                IntPtr foreground = GetForegroundWindow();

                if (foreground != IntPtr.Zero && foreground != hwnd)
                {
                    foregroundThread = GetWindowThreadProcessId(foreground, out _);

                    if (foregroundThread != 0 && foregroundThread != currentThread)
                    {
                        // 현재 활성 창의 입력 줄에 잠깐 편승 → 포커스 이전이 허용된다
                        attached = AttachThreadInput(currentThread, foregroundThread, true);
                    }
                }

                ShowWindow(hwnd, SW_SHOW);
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
            }
            catch (Exception ex)
            {
                // 포커스 확보 실패는 치명적이지 않음(창은 이미 보이는 상태) → 기록만
                Services.LogService.LogError("팝업 전면화", ex);
            }
            finally
            {
                if (attached)
                {
                    // 편승은 반드시 즉시 해제 — 남겨두면 두 프로그램의 입력이 뒤엉킵니다
                    AttachThreadInput(currentThread, foregroundThread, false);
                }
            }

            // WPF 내부 포커스도 맞춰준다 (창 활성화 ≠ 입력칸 포커스)
            window.Activate();

            if (focusTarget != null)
            {
                Keyboard.Focus(focusTarget);
            }
        }
    }
}