using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace EasyAlarm.Utils
{
    /// <summary>
    /// 부르르 흔들림 효과 담당 (파싱 실패 시 사용)
    /// 창 자체가 아니라 창 '내용물'을 흔드는 방식이라, 위젯과 팝업 모두에서 재사용 가능합니다.
    /// </summary>
    public static class AnimationHelper
    {
        /// <summary>
        /// 화면 요소를 좌우로 빠르게 흔듭니다. (틀렸다고 고개를 젓는 듯한 효과)
        /// </summary>
        public static void Shake(FrameworkElement element)
        {
            TranslateTransform transform = new TranslateTransform();
            element.RenderTransform = transform;

            DoubleAnimation shake = new DoubleAnimation
            {
                From = -8,    // 왼쪽으로 8픽셀
                To = 8,       // 오른쪽으로 8픽셀
                Duration = TimeSpan.FromMilliseconds(50),
                AutoReverse = true,                      // 갔다가 돌아오기
                RepeatBehavior = new RepeatBehavior(3)   // 3번 왕복
            };

            // 흔들기가 끝나면 원위치로 정확히 복귀
            shake.Completed += (s, e) =>
            {
                transform.BeginAnimation(TranslateTransform.XProperty, null);
                transform.X = 0;
            };

            transform.BeginAnimation(TranslateTransform.XProperty, shake);
        }
    }
}