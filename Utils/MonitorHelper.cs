using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace EasyAlarm.Utils
{
    /// <summary>
    /// "지금 사용자가 보고 있는 모니터"를 알아내는 담당.
    ///
    /// 왜 필요한가: WPF의 기본 설정(CenterScreen)은 **항상 주 모니터** 중앙에 창을 띄웁니다.
    /// 모니터를 두 대 이상 쓰면, 작업 중인 화면이 아닌 엉뚱한 화면에 팝업이 떠서
    /// "마우스 없이 바로 타이핑"이라는 목적이 깨집니다.
    /// 그래서 사용자가 쓰고 있던 창을 기준으로 그 창이 놓인 모니터를 찾아냅니다.
    /// </summary>
    public static class MonitorHelper
    {
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

        /// <summary>창이 어느 모니터에도 안 걸쳐 있으면 '가장 가까운' 모니터로 친다</summary>
        private const uint MonitorDefaultToNearest = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;    // 모니터 전체 영역
            public NativeRect Work;       // 작업 영역 (작업표시줄 제외)
            public uint Flags;
        }

        /// <summary>
        /// 기준 창이 놓인 모니터 중앙에 창을 놓습니다.
        /// 모니터를 알아내지 못하면 주 모니터 중앙으로 갑니다(기존 동작).
        ///
        /// ⚠️ 창 핸들이 만들어진 뒤(SourceInitialized) 불러야 화면 배율 변환이 정확합니다.
        ///    그리고 창이 그려지기 전에 불러야 자리가 튀는 깜빡임이 없습니다.
        /// </summary>
        /// <param name="target">자리를 잡을 창</param>
        /// <param name="anchorWindow">기준이 될 창의 핸들 (사용자가 쓰고 있던 창)</param>
        public static void CenterOnAnchorMonitor(Window target, IntPtr anchorWindow)
        {
            Rect area = GetWorkArea(anchorWindow, target) ?? SystemParameters.WorkArea;

            target.Left = area.Left + ((area.Width - target.Width) / 2);
            target.Top = area.Top + ((area.Height - target.Height) / 2);
        }

        /// <summary>
        /// 창이 놓인 모니터의 '전체 영역'을 실제 픽셀 그대로 돌려줍니다.
        ///
        /// 작업 영역(Work)이 아니라 전체 영역(Monitor)이고, 화면 배율 변환도 하지 않습니다.
        /// 창 크기와 직접 견주어 '전체화면인지' 따지는 용도라, 양쪽 다 실제 픽셀이어야 하기 때문입니다.
        /// </summary>
        public static Int32Rect? GetMonitorBoundsInPixels(IntPtr window)
        {
            if (window == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                IntPtr monitor = MonitorFromWindow(window, MonitorDefaultToNearest);

                if (monitor == IntPtr.Zero)
                {
                    return null;
                }

                MonitorInfo info = new MonitorInfo();
                info.Size = Marshal.SizeOf<MonitorInfo>();

                if (!GetMonitorInfo(monitor, ref info))
                {
                    return null;
                }

                return new Int32Rect(
                    info.Monitor.Left,
                    info.Monitor.Top,
                    info.Monitor.Right - info.Monitor.Left,
                    info.Monitor.Bottom - info.Monitor.Top);
            }
            catch (Exception ex)
            {
                Services.LogService.LogError("모니터 전체 영역 조회", ex);
                return null;
            }
        }

        /// <summary>
        /// 기준 창이 놓인 모니터의 '작업 영역'을 WPF 좌표로 돌려줍니다.
        /// 알아내지 못하면 null (부르는 쪽에서 기본값으로 처리).
        /// </summary>
        /// <param name="anchorWindow">기준이 될 창의 핸들 (사용자가 쓰고 있던 창)</param>
        /// <param name="target">자리를 잡을 우리 창 (화면 배율 변환에 필요)</param>
        public static Rect? GetWorkArea(IntPtr anchorWindow, Window target)
        {
            if (anchorWindow == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                IntPtr monitor = MonitorFromWindow(anchorWindow, MonitorDefaultToNearest);

                if (monitor == IntPtr.Zero)
                {
                    return null;
                }

                MonitorInfo info = new MonitorInfo();
                info.Size = Marshal.SizeOf<MonitorInfo>();

                if (!GetMonitorInfo(monitor, ref info))
                {
                    return null;
                }

                // ⚠️ 윈도우가 알려주는 좌표는 '실제 픽셀'이고 WPF가 쓰는 좌표는 '배율을 적용한 값'입니다.
                //    125%·150% 확대를 쓰거나 모니터마다 배율이 다르면 이 변환 없이는 자리가 어긋납니다.
                Matrix toWpf = Matrix.Identity;
                PresentationSource? source = PresentationSource.FromVisual(target);

                if (source?.CompositionTarget != null)
                {
                    toWpf = source.CompositionTarget.TransformFromDevice;
                }

                Point topLeft = toWpf.Transform(new Point(info.Work.Left, info.Work.Top));
                Point bottomRight = toWpf.Transform(new Point(info.Work.Right, info.Work.Bottom));

                return new Rect(topLeft, bottomRight);
            }
            catch (Exception ex)
            {
                Services.LogService.LogError("모니터 영역 조회", ex);
                return null;
            }
        }
    }
}
