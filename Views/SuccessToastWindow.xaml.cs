using System;
using System.Windows;
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

        public SuccessToastWindow(AlarmModel alarm)
        {
            InitializeComponent();

            // 무시간 할일은 예약 시각이 의미 없으므로 문구를 달리 표시
            MessageText.Text = alarm.IsUntimed
                ? "✅ 할일 추가 완료! 알람 없이 목록에 담았어요"
                : $"✅ 등록 완료! {TimeFormatter.FormatRange(alarm.AlarmTime, alarm.DurationMinutes)}에 알려드릴게요";

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