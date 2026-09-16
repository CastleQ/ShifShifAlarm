using System;
using System.Runtime.InteropServices;
using System.Windows;
using EasyAlarm.Services;

namespace EasyAlarm.Utils
{
    /// <summary>
    /// "지금 사용자가 전체화면으로 뭔가에 몰입해 있는가"를 판단하는 담당.
    ///
    /// 왜 필요한가: 게임에서 Shift 연타는 '달리기 토글'로 아주 흔하게 쓰입니다.
    /// 그때마다 입력창이 튀어나오면 성가실 뿐 아니라, 전체화면 위에 창이 뜨면서
    /// 게임이 전체화면 모드에서 튕겨 나가기까지 합니다.
    ///
    /// 두 겹으로 판단합니다.
    ///  ① 윈도우에게 직접 물어본다 — 윈도우가 알림을 띄울지 말지 정할 때 쓰는 바로 그 기준
    ///  ② 전경 창이 모니터를 통째로 덮는 '테두리 없는 창'인지 직접 확인 — ①이 놓치는 경우 보완
    ///
    /// ⚠️ 이 판단은 Shift 2연타에만 씁니다. Alt+Shift+Z는 막지 않습니다.
    ///    3개 키를 동시에 누르는 건 우연히 될 일이 없고, 전체화면 중에도 정말 필요할 때
    ///    쓸 수 있는 탈출구를 남겨두기 위해서입니다.
    /// </summary>
    public static class FullScreenDetector
    {
        // ── ① 윈도우 자체 판정 ──

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int state);

        /// <summary>전체화면 앱이 실행 중</summary>
        private const int StateBusy = 2;

        /// <summary>전체화면 게임(DirectX)이 실행 중</summary>
        private const int StateD3DFullScreen = 3;

        /// <summary>발표 모드 (화면 공유·프레젠테이션)</summary>
        private const int StatePresentation = 4;

        /// <summary>스토어 앱이 전체화면으로 실행 중</summary>
        private const int StateStoreApp = 7;

        // ── ② 직접 확인 ──

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>창의 생김새 정보가 들어 있는 서랍 번호</summary>
        private const int WindowStyleIndex = -16;

        /// <summary>제목 표시줄이 있다는 표시</summary>
        private const int HasTitleBar = 0x00C00000;

        /// <summary>테두리 두께 오차 허용치(픽셀). 창이 화면보다 몇 px 크거나 작아도 전체화면으로 본다</summary>
        private const int EdgeTolerance = 2;

        /// <summary>
        /// 지금 전체화면으로 몰입 중이라 방해하면 안 되는 상태인지 알려줍니다.
        /// 판단에 실패하면 false — 확신이 없을 때 기능을 막아버리는 쪽이 더 나쁘기 때문입니다.
        /// </summary>
        public static bool IsFullScreenAppActive()
        {
            try
            {
                return IsWindowsReportingFullScreen() || IsForegroundWindowCoveringMonitor();
            }
            catch (Exception ex)
            {
                LogService.LogError("전체화면 감지", ex);
                return false;
            }
        }

        /// <summary>① 윈도우에게 "지금 알림 띄워도 되나?"를 물어봅니다.</summary>
        private static bool IsWindowsReportingFullScreen()
        {
            if (SHQueryUserNotificationState(out int state) != 0)
            {
                return false;   // 물어보기 자체가 실패 → 막지 않는다
            }

            return state is StateBusy or StateD3DFullScreen or StatePresentation or StateStoreApp;
        }

        /// <summary>
        /// ② 전경 창이 '제목 표시줄 없이 모니터를 통째로 덮는' 상태인지 확인합니다.
        ///
        /// ⚠️ 판정 기준을 '최대화 아님'이 아니라 '제목 표시줄 없음'으로 잡은 이유:
        ///    일부 게임·영상 플레이어는 테두리 없는 창을 **최대화**해서 전체화면을 만듭니다.
        ///    '최대화 아님'을 기준으로 삼으면 바로 그 경우를 놓칩니다. (실측으로 확인)
        ///    반대로 최대화된 평범한 창(브라우저 등)은 제목 표시줄이 있어 걸러집니다.
        /// </summary>
        private static bool IsForegroundWindowCoveringMonitor()
        {
            IntPtr foreground = GetForegroundWindow();

            if (foreground == IntPtr.Zero)
            {
                return false;
            }

            // 바탕화면은 원래 화면 전체 크기라 전체화면으로 오해하기 쉽다 → 먼저 제외
            if (foreground == GetShellWindow() || foreground == GetDesktopWindow())
            {
                return false;
            }

            if ((GetWindowLong(foreground, WindowStyleIndex) & HasTitleBar) == HasTitleBar)
            {
                return false;   // 제목 표시줄이 있는 평범한 창
            }

            if (!GetWindowRect(foreground, out NativeRect window))
            {
                return false;
            }

            Int32Rect? bounds = MonitorHelper.GetMonitorBoundsInPixels(foreground);

            if (bounds == null)
            {
                return false;
            }

            Int32Rect monitor = bounds.Value;

            return window.Left <= monitor.X + EdgeTolerance
                && window.Top <= monitor.Y + EdgeTolerance
                && window.Right >= monitor.X + monitor.Width - EdgeTolerance
                && window.Bottom >= monitor.Y + monitor.Height - EdgeTolerance;
        }
    }
}
