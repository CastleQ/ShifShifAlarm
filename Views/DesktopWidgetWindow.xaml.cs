using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
        private readonly SettingsService _settings = new SettingsService();
        private readonly DispatcherTimer _feedbackTimer;
        private readonly DispatcherTimer _watchdogTimer;

        /// <summary>기본 위치에서 화면 가장자리와 띄울 간격(픽셀)</summary>
        private const double DefaultMargin = 16;

        /// <summary>이만큼도 안 보이면 '화면 밖'으로 판단해 기본 위치로 되돌립니다(픽셀)</summary>
        private const double MinVisibleSize = 40;

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

            // 우클릭 → 위젯 숨기기 (입력칸 위에서는 입력칸 고유의 복사·붙여넣기 메뉴가 그대로 뜸)
            // 늘 떠 있는 게 거슬리는 사람이 그 자리에서 바로 치울 수 있게 합니다.
            MenuItem hideItem = new MenuItem { Header = "위젯 숨기기 (트레이 아이콘 메뉴에서 다시 켤 수 있어요)" };
            hideItem.Click += (s, e) => ((App)Application.Current).SetWidgetVisible(false);
            ContextMenu = new ContextMenu();
            ContextMenu.Items.Add(hideItem);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RestorePosition();

            // 도구 창으로 전환 → Win+M 생존 + Alt+Tab 목록에서 숨김
            DesktopWindowHelper.PinToDesktop(this);
        }

        // ── 위젯 위치 기억하기 ──

        /// <summary>
        /// 지난번에 드래그해 둔 자리로 돌아갑니다.
        /// 저장된 자리가 없거나, 모니터 구성이 바뀌어 화면 밖이 되었으면 기본 위치로 갑니다.
        /// </summary>
        private void RestorePosition()
        {
            AppSettings settings = _settings.Load();

            if (settings.WidgetLeft.HasValue && settings.WidgetTop.HasValue &&
                IsVisibleOnScreen(settings.WidgetLeft.Value, settings.WidgetTop.Value))
            {
                Left = settings.WidgetLeft.Value;
                Top = settings.WidgetTop.Value;
                return;
            }

            MoveToDefaultCorner();
        }

        /// <summary>기본 자리: 화면 우측 하단(작업표시줄 살짝 위)</summary>
        private void MoveToDefaultCorner()
        {
            Left = SystemParameters.WorkArea.Right - Width - DefaultMargin;
            Top = SystemParameters.WorkArea.Bottom - Height - DefaultMargin;
        }

        /// <summary>드래그가 끝난 자리를 기억해 둡니다.</summary>
        private void SavePosition()
        {
            // ⚠️ 통째로 덮어쓰면 그 사이 설정 창에서 바꾼 값이 날아갑니다.
            //    반드시 최신 설정을 다시 읽어와 좌표만 갈아끼웁니다.
            AppSettings settings = _settings.Load();

            settings.WidgetLeft = Left;
            settings.WidgetTop = Top;

            _settings.Save(settings);
        }

        /// <summary>
        /// 그 자리에 뒀을 때 위젯이 실제로 보이는지 확인합니다.
        /// 노트북에서 외부 모니터를 빼면 저장된 좌표가 허공이 되어 위젯이 사라지는데,
        /// 그때 기본 자리로 되돌리기 위한 안전장치입니다.
        /// </summary>
        private bool IsVisibleOnScreen(double left, double top)
        {
            Rect widget = new Rect(left, top, Width, Height);

            Rect allScreens = new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);

            widget.Intersect(allScreens);   // 겹치는 부분만 남는다 (안 겹치면 빈 사각형)

            return widget.Width >= MinVisibleSize && widget.Height >= MinVisibleSize;
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
                bool answer = ConfirmDialogWindow.Ask(
                    this,
                    "알람 없이 등록",
                    "시간을 찾지 못했어요. 알람 없이 할일 목록에만 담을까요?",
                    text.Trim());

                if (answer)
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
            // ⚠️ 저장 실패를 조용히 넘기면 사용자가 '등록됐다'고 착각합니다.
            //    입력 내용은 그대로 두고 오류만 표시해, 바로 다시 시도할 수 있게 합니다.
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

            InputBox.Clear();
            ShowSuccess(alarm);
        }

        /// <summary>성공 피드백: 초록 문구로 안내</summary>
        private void ShowSuccess(AlarmModel alarm)
        {
            OuterBorder.BorderBrush = NormalBorder;
            HintText.Foreground = SuccessGreen;

            if (alarm.IsUntimed)
            {
                HintText.Text = "✅ 할일 추가! 알람 없이 목록에 담았어요";
            }
            else
            {
                // 올해가 아닌 알람은 연도를 굵게 띄워 한눈에 들어오게 한다.
                // (날짜를 착각해 지난 달을 적으면 내년으로 잡히는데, 그걸 놓치지 않도록)
                (string year, string rest) = TimeFormatter.SplitRange(alarm.AlarmTime, alarm.DurationMinutes);

                HintText.Inlines.Clear();
                HintText.Inlines.Add(new Run("✅ 등록 완료! "));

                if (year.Length > 0)
                {
                    HintText.Inlines.Add(new Run(year) { FontWeight = FontWeights.Bold });
                }

                HintText.Inlines.Add(new Run($"{rest}에 알려드릴게요"));
            }

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
                // DragMove는 '마우스를 놓을 때까지' 여기서 멈춰 있다가 돌아옵니다.
                // 그래서 바로 다음 줄이 곧 '드래그가 끝난 시점'입니다.
                DragMove();
                SavePosition();
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