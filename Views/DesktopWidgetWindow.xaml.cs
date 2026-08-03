using System;
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
    /// 바탕화면 우측 하단에 상주하는 입력 위젯.
    /// Win+M을 눌러도 사라지지 않으며(실물 점검 감시견), Enter로 즉시 알람을 등록합니다.
    /// </summary>
    public partial class DesktopWidgetWindow : Window
    {
        private readonly AlarmParserService _parser = new AlarmParserService();
        private readonly StorageService _storage = new StorageService();
        private readonly DispatcherTimer _feedbackTimer;
        private readonly DispatcherTimer _watchdogTimer;

        // 색상 팔레트
        private static readonly Brush NormalBorder = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
        private static readonly Brush ErrorBorder = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
        private static readonly Brush HintGray = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
        private static readonly Brush SuccessGreen = new SolidColorBrush(Color.FromRgb(0x43, 0xA0, 0x47));
        private static readonly Brush ErrorRed = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));

        private const string DefaultHint = "⏰ 쉽쉽알람 — 여기에 입력 (드래그로 이동)";

        public DesktopWidgetWindow()
        {
            InitializeComponent();

            // 피드백 문구("등록 완료!" 등)를 2.5초 뒤 원래대로 되돌리는 타이머
            _feedbackTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.5)
            };
            _feedbackTimer.Tick += (s, e) =>
            {
                _feedbackTimer.Stop();
                ResetFeedback();
            };

            // ── 감시견 (실물 점검 방식) ──
            // 0.3초마다 운영체제의 실제 상태를 확인해서, 넘어져 있으면 일으켜 세운다.
            _watchdogTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _watchdogTimer.Tick += (s, e) => DesktopWindowHelper.RestoreIfMinimized(this);
            _watchdogTimer.Start();

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 화면 우측 하단(작업표시줄 살짝 위)에 자리 잡기
            Left = SystemParameters.WorkArea.Right - Width - 16;
            Top = SystemParameters.WorkArea.Bottom - Height - 16;

            // 도구 창으로 전환 → Win+M 생존 + Alt+Tab 목록에서 숨김
            DesktopWindowHelper.PinToDesktop(this);
        }

        // ── 입력 처리 ──

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                InputBox.Clear();
                ResetFeedback();
                return;
            }

            if (e.Key != Key.Enter)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(InputBox.Text))
            {
                return;   // 빈 입력은 조용히 무시
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
                MessageBoxResult answer = MessageBox.Show(
                    this,
                    $"시간 없이 할일을 등록할까요?\n\n{text.Trim()}",
                    "알람 없이 등록",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (answer == MessageBoxResult.Yes)
                {
                    Register(_parser.CreateUntimedTodo(text));
                }
                else
                {
                    InputBox.Focus();   // 취소: 내용 유지, 커서 복귀
                }

                return;
            }

            ShowError(result.ErrorMessage);
        }

        /// <summary>알람을 저장하고 입력칸을 비운 뒤 성공 피드백을 표시합니다.</summary>
        private void Register(AlarmModel alarm)
        {
            _storage.AddAlarm(alarm);

            App app = (App)Application.Current;
            app.NotifyAlarmsChanged();   // 열려 있는 목록 창 즉시 갱신
            app.SyncNewAlarm(alarm);     // 구글 캘린더로 백그라운드 전송

            InputBox.Clear();
            ShowSuccess(alarm);
        }

        /// <summary>성공 피드백: 초록 문구로 안내</summary>
        private void ShowSuccess(AlarmModel alarm)
        {
            OuterBorder.BorderBrush = NormalBorder;
            HintText.Foreground = SuccessGreen;
            HintText.Text = alarm.IsUntimed
                ? "✅ 할일 추가! 알람 없이 목록에 담았어요"
                : $"✅ 등록 완료! {TimeFormatter.Format(alarm.AlarmTime)}에 알려드릴게요";
            _feedbackTimer.Stop();
            _feedbackTimer.Start();
        }

        /// <summary>실패 피드백: 빨간 테두리 + 흔들림 + 오류 문구</summary>
        private void ShowError(string message)
        {
            OuterBorder.BorderBrush = ErrorBorder;
            HintText.Foreground = ErrorRed;
            HintText.Text = $"❌ {message}";
            AnimationHelper.Shake(OuterBorder);
            _feedbackTimer.Stop();
            _feedbackTimer.Start();
        }

        /// <summary>피드백 표시를 평소 상태로 되돌립니다.</summary>
        private void ResetFeedback()
        {
            OuterBorder.BorderBrush = NormalBorder;
            HintText.Foreground = HintGray;
            HintText.Text = DefaultHint;
        }

        // ── 반투명 효과: 평소 85%, 마우스 올리거나 입력 중엔 100% ──

        private void Widget_MouseEnter(object sender, MouseEventArgs e)
        {
            Opacity = 1.0;
        }

        private void Widget_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!InputBox.IsKeyboardFocused)
            {
                Opacity = 0.85;
            }
        }

        // ── 드래그 이동: 안내 문구를 잡고 끌면 위치 변경 ──

        private void HintText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // 마우스 버튼 상태가 어긋난 순간에 호출되면 예외가 날 수 있음 → 무시해도 안전
            }
        }

        // ── 목록 버튼: 할일 목록 창 열기 ──

        private void ListButton_Click(object sender, RoutedEventArgs e)
        {
            ((App)Application.Current).ShowAlarmList();
        }
    }
}