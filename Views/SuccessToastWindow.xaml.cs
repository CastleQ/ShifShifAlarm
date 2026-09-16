using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using EasyAlarm.Models;
using EasyAlarm.Utils;

namespace EasyAlarm.Views
{
    /// <summary>
    /// 알람 등록 성공 시 잠깐 나타났다 사라지는 확인 카드.
    /// 0.5초 유지 → 0.6초 페이드아웃 → 자동 소멸. 클릭/포커스 불필요.
    /// </summary>
    public partial class SuccessToastWindow : Window
    {
        private readonly DispatcherTimer _lifeTimer;

        /// <param name="alarm">방금 등록된 알람</param>
        /// <param name="anchorWindow">
        /// 기준이 될 창의 핸들. 이 창이 놓인 모니터 중앙에 토스트가 뜹니다.
        /// 넘기지 않으면 주 모니터 중앙(기존 동작).
        /// </param>
        public SuccessToastWindow(AlarmModel alarm, IntPtr anchorWindow = default)
        {
            InitializeComponent();

            // 팝업과 같은 모니터에 떠야 합니다. 안 그러면 모니터가 2대 이상일 때
            // 입력은 이쪽 화면에서 했는데 "등록 완료!"는 저쪽 화면에 떠서 못 보고 지나칩니다.
            SourceInitialized += (s, e) => MonitorHelper.CenterOnAnchorMonitor(this, anchorWindow);

            // 무시간 할일은 예약 시각이 의미 없으므로 문구를 달리 표시
            if (alarm.IsUntimed)
            {
                MessageText.Text = "✅ 할일 추가 완료! 알람 없이 목록에 담았어요";
            }
            else
            {
                // 올해가 아닌 알람은 연도를 굵게 띄워 한눈에 들어오게 한다.
                // (날짜를 착각해 지난 달을 적으면 내년으로 잡히는데, 그걸 놓치지 않도록)
                (string year, string rest) = TimeFormatter.SplitRange(alarm.AlarmTime, alarm.DurationMinutes);

                MessageText.Inlines.Clear();
                MessageText.Inlines.Add(new Run("✅ 등록 완료! "));

                if (year.Length > 0)
                {
                    MessageText.Inlines.Add(new Run(year) { FontWeight = FontWeights.Bold });
                }

                MessageText.Inlines.Add(new Run($"{rest}에 알려드릴게요"));
            }

            // 0.5초 동안 보여준 뒤 퇴장 시작
            _lifeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _lifeTimer.Tick += (s, e) =>
            {
                _lifeTimer.Stop();
                FadeOutAndClose();
            };

            Loaded += (s, e) => _lifeTimer.Start();
        }

        /// <summary>0.6초에 걸쳐 서서히 투명해진 뒤 창을 닫습니다.</summary>
        private void FadeOutAndClose()
        {
            DoubleAnimation fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(600),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            fadeOut.Completed += (s, e) => Close();

            BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}