using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EasyAlarm.Services
{
    /// <summary>
    /// Shift 키 2연타(톡, 톡)를 감지하는 저수준 키보드 감지기.
    /// 쉽쉽알람의 시그니처 단축키 담당.
    ///
    /// 탭 인정 규칙:
    ///  - Shift를 눌렀다 뗀 것 = 1탭 (꾹 누르고 있는 건 탭 아님)
    ///  - 누르고 있는 동안 다른 키가 끼면 그 탭은 무효 (Shift+A 같은 일반 타이핑 보호)
    ///  - 두 탭 사이에 다른 키가 끼어도 무효
    ///  - 1탭 → 400ms 이내 → 2탭 = 발동!
    /// </summary>
    public class ShiftDoubleTapService : IDisposable
    {
        /// <summary>2연타로 인정할 최대 간격 (밀리초)</summary>
        private const int DoubleTapWindowMs = 220;

        /// <summary>2연타 감지 시 호출되는 이벤트</summary>~
        public event Action? DoubleTapDetected;

        // ── 윈도우 저수준 기능(Win32 API) 선언 ──

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const int WH_KEYBOARD_LL = 13;      // 저수준 키보드 감지기 종류 번호
        private const int WM_KEYDOWN = 0x0100;      // 키 눌림
        private const int WM_KEYUP = 0x0101;        // 키 뗌
        private const int WM_SYSKEYDOWN = 0x0104;   // (Alt 조합) 키 눌림
        private const int WM_SYSKEYUP = 0x0105;     // (Alt 조합) 키 뗌
        private const int VK_LSHIFT = 0xA0;         // 왼쪽 Shift
        private const int VK_RSHIFT = 0xA1;         // 오른쪽 Shift

        private IntPtr _hookHandle = IntPtr.Zero;

        // ⚠️ 감지기 함수를 필드로 붙잡아 두는 이유:
        // 지역변수로만 넘기면 청소부(GC)가 "아무도 안 쓰네?" 하고 수거해가서
        // 어느 순간 프로그램이 죽는 유명한 함정이 있습니다. 필드로 소유권을 명시!
        private readonly LowLevelKeyboardProc _proc;

        // ── 탭 판정 상태 ──
        private bool _shiftIsDown;              // 지금 Shift가 눌려있는가
        private bool _otherKeyDuringHold;       // 이번 Shift 누름 중에 다른 키가 끼었는가
        private long _lastTapAtMs = long.MinValue;   // 직전 유효 탭의 시각
        private readonly Stopwatch _clock = Stopwatch.StartNew();   // 시각 측정용 (밀리초)

        public ShiftDoubleTapService()
        {
            _proc = HookCallback;
        }

        /// <summary>감지 시작. 프로그램 시작 시 1회 호출합니다.</summary>
        public void Start()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                return;   // 이미 가동 중
            }

            _hookHandle = SetWindowsHookEx(
                WH_KEYBOARD_LL,
                _proc,
                GetModuleHandle(null),
                0);

            if (_hookHandle == IntPtr.Zero)
            {
                // 감지기 설치 실패: 치명적이지 않으므로(기존 Alt+Shift+Z는 살아있음) 기록만 남긴다
                LogService.LogError("Shift 2연타 감지기 설치",
                    new InvalidOperationException($"SetWindowsHookEx 실패, 오류코드 {Marshal.GetLastWin32Error()}"));
            }
        }

        /// <summary>
        /// 모든 키 입력이 지나가는 관문. 최대한 가볍게 판정만 하고 즉시 통과시킵니다.
        /// (핵심 가치 '가벼움' 준수: 키 하나당 몇 번의 비교 연산이 전부)
        /// </summary>
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                // lParam이 가리키는 구조의 첫 4바이트 = 가상 키 번호
                int vkCode = Marshal.ReadInt32(lParam);
                bool isShift = vkCode == VK_LSHIFT || vkCode == VK_RSHIFT;

                int msg = wParam.ToInt32();
                bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

                if (isShift)
                {
                    if (isDown && !_shiftIsDown)
                    {
                        // Shift 새로 눌림 (꾹 누르면 눌림 신호가 반복되지만, 플래그로 첫 신호만 인정)
                        _shiftIsDown = true;
                        _otherKeyDuringHold = false;
                    }
                    else if (isUp && _shiftIsDown)
                    {
                        // Shift 뗌 → 탭 판정
                        _shiftIsDown = false;

                        if (!_otherKeyDuringHold)
                        {
                            long nowMs = _clock.ElapsedMilliseconds;

                            // ⚠️ 반드시 '직전 탭이 실제로 있었는지'부터 확인!
                            // 없음 표시(long.MinValue)와 뺄셈하면 숫자 저울이 한 바퀴 돌아
                            // 음수가 되어 첫 탭부터 발동하는 버그가 있었음 (오버플로우)
                            bool hasPreviousTap = _lastTapAtMs != long.MinValue;

                            if (hasPreviousTap && nowMs - _lastTapAtMs <= DoubleTapWindowMs)
                            {
                                // 🎉 2연타 성립!
                                _lastTapAtMs = long.MinValue;   // 3연타가 또 발동하지 않게 초기화
                                DoubleTapDetected?.Invoke();
                            }
                            else
                            {
                                _lastTapAtMs = nowMs;   // 1탭째로 기록
                            }
                        }
                        else
                        {
                            _lastTapAtMs = long.MinValue;   // 조합 타이핑이었음 → 무효
                        }
                    }
                }
                else if (isDown)
                {
                    // 다른 키가 눌림 → 진행 중이던 연타 시도는 전부 무효
                    _otherKeyDuringHold = true;
                    _lastTapAtMs = long.MinValue;
                }
            }

            // 키 입력을 절대 가로막지 않고 다음으로 통과
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        /// <summary>감지기 철거. 프로그램 종료 시 호출합니다.</summary>
        public void Dispose()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
        }
    }
}