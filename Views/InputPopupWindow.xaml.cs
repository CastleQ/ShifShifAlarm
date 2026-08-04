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

        private static readonly Brush ErrorBorder = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));

        /// <param name="prefillText">입력칸에 미리 채워둘 문장 (예: '나중에 다시'로 호출 시 원본 입력)</param>
        public InputPopupWindow(string prefillText = "")
        {
            InitializeComponent();

            InputBox.Text = prefillText;

            // 창이 실제로 그려진 다음(ContentRendered) 포커스를 강제해야 확실히 먹습니다.
            // Loaded 시점은 아직 창 핸들 준비가 덜 된 경우가 있어 한 박자 늦춥니다.
            ContentRendered += (s, e) => TakeFocus();

            // 포커스를 잃으면(다른 곳 클릭) 조용히 닫힘
            // 단, 이미 닫히는 중이거나 확인창이 떠 있으면 아무것도 안 함
            Deactivated += (s, e) =>
            {
                if (!_isClosing && !_suppressAutoClose)
                {
                    Close();
                }
            };
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

                MessageBoxResult answer = MessageBox.Show(
                    this,
                    $"시간 없이 할일을 등록할까요?\n\n{text.Trim()}",
                    "알람 없이 등록",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                _suppressAutoClose = false;

                if (answer == MessageBoxResult.Yes)
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
            _storage.AddAlarm(alarm);

            App app = (App)Application.Current;
            app.NotifyAlarmsChanged();   // 열려 있는 목록 창 즉시 갱신
            app.SyncNewAlarm(alarm);     // 구글 캘린더로 백그라운드 전송

            SuccessToastWindow toast = new SuccessToastWindow(alarm);
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