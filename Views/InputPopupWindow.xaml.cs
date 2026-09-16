using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EasyAlarm.Models;
using EasyAlarm.Services;
using EasyAlarm.Utils;

namespace EasyAlarm.Views
{
    /// <summary>
    /// 단축키(Alt+Shift+Z 또는 Shift 2연타) 및 '나중에 다시' 버튼으로 호출되는 알람 입력 팝업.
    /// Enter = 등록 / 빈 채로 Enter·Esc·다른 곳 클릭 = 닫기 / 실패 시 빨간 테두리 + 흔들림
    /// 뜨는 즉시 키보드 입력을 받도록 최전면 포커스를 강제로 확보합니다(마우스 클릭 불필요).
    /// </summary>
    public partial class InputPopupWindow : Window
    {
        private readonly AlarmParserService _parser = new AlarmParserService();
        private readonly StorageService _storage = new StorageService();

        // 문이 이미 닫히는 중인지 표시하는 깃발 (이중 닫힘 크래시 방지)
        private bool _isClosing;

        // 확인창이 떠 있는 동안 '포커스 잃으면 닫힘'을 잠시 잠그는 깃발
        private bool _suppressAutoClose;

        // 자동 닫힘을 켜도 되는 시점이 되었는지 (뜨자마자 닫히는 사고 방지)
        private bool _autoCloseArmed;
        private DispatcherTimer? _graceTimer;

        // 팝업이 뜨기 직전에 사용자가 쓰고 있던 창 (모니터 판별용)
        private readonly IntPtr _anchorWindow;

        /// <summary>창이 뜬 뒤 이만큼은 자동 닫힘을 꺼 둡니다 (밀리초)</summary>
        private const int AutoCloseGraceMs = 700;

        private static readonly Brush ErrorBorder = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));

        /// <param name="prefillText">입력칸에 미리 채워둘 문장 (예: '나중에 다시'로 호출 시 원본 입력)</param>
        public InputPopupWindow(string prefillText = "")
        {
            InitializeComponent();

            InputBox.Text = prefillText;

            // 팝업이 뜨기 직전에 사용자가 쓰고 있던 창을 기억해 둡니다.
            // ① 그 창이 있는 모니터 중앙에 팝업을 띄우고 ② 포커스를 넘겨받는 데 씁니다.
            _anchorWindow = ForegroundHelper.GetActiveWindowHandle();

            // 창 핸들이 만들어진 직후 — 아직 화면에 그려지기 전에 자리를 잡아야 깜빡임이 없습니다.
            SourceInitialized += (s, e) => MonitorHelper.CenterOnAnchorMonitor(this, _anchorWindow);

            // 창이 실제로 그려진 다음(ContentRendered) 포커스를 강제해야 확실히 먹습니다.
            // Loaded 시점은 아직 창 핸들 준비가 덜 된 경우가 있어 한 박자 늦춥니다.
            ContentRendered += (s, e) =>
            {
                TakeFocus();
                StartAutoCloseGrace();
            };

            // 포커스를 잃으면(다른 곳 클릭) 조용히 닫힘
            // 단, 이미 닫히는 중이거나 확인창이 떠 있거나, 아직 유예 시간 중이면 아무것도 안 함
            Deactivated += (s, e) =>
            {
                if (!_isClosing && !_suppressAutoClose && _autoCloseArmed)
                {
                    Close();
                }
            };
        }

        /// <summary>
        /// 뜨자마자 닫혀 버리는 것을 막는 유예 장치.
        ///
        /// 창이 뜨는 그 순간은 '누가 앞에 설지' 다투는 시간이라, 포커스를 잠깐 빼앗겼다가
        /// 되찾는 일이 흔합니다. 이때 자동 닫힘이 발동하면 사용자 눈에는
        /// "떴다가 그냥 사라졌다"로 보입니다. 그래서 잠깐(0.7초) 자동 닫힘을 꺼 둡니다.
        ///
        /// 유예가 끝날 때까지도 포커스를 못 잡았으면 한 번 더 시도합니다
        /// (상대 창이 전경을 붙잡고 있다가 놓아준 경우를 위한 두 번째 기회).
        /// </summary>
        private void StartAutoCloseGrace()
        {
            _graceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AutoCloseGraceMs)
            };

            _graceTimer.Tick += (s, e) =>
            {
                _graceTimer?.Stop();
                _graceTimer = null;

                if (!_isClosing && !IsActive)
                {
                    TakeFocus();
                }

                _autoCloseArmed = true;
            };

            _graceTimer.Start();
        }


        /// <summary>
        /// 최전면 + 키보드 포커스를 확보하고 입력칸 내용을 전체 선택합니다.
        /// (Shift 2연타처럼 백그라운드에서 열린 경우에도 마우스 없이 바로 타이핑 가능)
        /// </summary>
        public void TakeFocus()
        {
            ForegroundHelper.ForceForeground(this, InputBox);

            InputBox.Focus();
            InputBox.SelectAll();
        }

        /// <summary>닫힘 절차가 시작되는 순간 깃발을 올립니다.</summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            _isClosing = true;

            // 유예 타이머가 살아 있으면 정리 (닫힌 창에 대고 포커스를 잡으려 들지 않도록)
            _graceTimer?.Stop();
            _graceTimer = null;

            base.OnClosing(e);
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                return;
            }

            if (e.Key != Key.Enter)
            {
                return;
            }

            // 빈 채로 Enter = "잘못 열었으니 닫을래" → 오류 대신 조용히 닫기
            if (string.IsNullOrWhiteSpace(InputBox.Text))
            {
                Close();
                return;
            }

            string text = InputBox.Text;
            ParseResult result = _parser.Parse(text);

            if (result.Success && result.Alarm != null)
            {
                Register(result.Alarm);
                return;
            }

            // 시간 표현이 아예 없는 입력 → 무시간 할일로 등록할지 물어본다
            if (result.NoTimeDetected)
            {
                _suppressAutoClose = true;

                bool answer = ConfirmDialogWindow.Ask(
                    this,
                    "알람 없이 등록",
                    "시간을 찾지 못했어요. 알람 없이 할일 목록에만 담을까요?",
                    text.Trim());

                _suppressAutoClose = false;

                if (answer)
                {
                    Register(_parser.CreateUntimedTodo(text));
                }
                else
                {
                    // 취소: 입력 내용을 남겨 수정할 수 있게 다시 커서를 돌려준다
                    Activate();
                    InputBox.Focus();
                    InputBox.CaretIndex = InputBox.Text.Length;
                }

                return;
            }

            ShowError(result.ErrorMessage);
        }

        /// <summary>알람을 저장하고, 확인 카드를 띄운 뒤 팝업을 닫습니다.</summary>
        private void Register(AlarmModel alarm)
        {
            // ⚠️ 저장에 실패했는데 팝업이 닫히면 사용자는 등록된 줄 압니다.
            //    창을 닫지 않고 오류만 표시해, 입력 내용을 지킨 채 다시 시도하게 합니다.
            try
            {
                _storage.AddAlarm(alarm);
            }
            catch (Exception ex)
            {
                LogService.LogError("알람 저장", ex);
                ShowError("저장하지 못했어요. 잠시 뒤 다시 시도해주세요");
                return;
            }

            App app = (App)Application.Current;
            app.NotifyAlarmsChanged();   // 열려 있는 목록 창 즉시 갱신
            app.SyncNewAlarm(alarm);     // 구글 캘린더로 백그라운드 전송

            // 팝업이 떠 있던 그 모니터에 토스트도 띄운다 (입력한 화면에서 결과를 보도록)
            SuccessToastWindow toast = new SuccessToastWindow(alarm, _anchorWindow);
            toast.Show();

            Close();
        }

        /// <summary>실패 피드백: 빨간 테두리 + 흔들림 + 오류 문구</summary>
        private void ShowError(string message)
        {
            OuterBorder.BorderBrush = ErrorBorder;
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
            AnimationHelper.Shake(OuterBorder);
        }

        // ── 목록 버튼: 할일 목록 창 열기 ──

        private void ListButton_Click(object sender, RoutedEventArgs e)
        {
            // ⚠️ 순서가 중요합니다.
            // 팝업을 먼저 닫고, 닫힘 처리가 완전히 끝난 다음(ApplicationIdle) 목록 창을 띄웁니다.
            Close();

            Dispatcher.BeginInvoke(
                new Action(() => ((App)Application.Current).ShowAlarmList()),
                DispatcherPriority.ApplicationIdle);
        }
    }
}