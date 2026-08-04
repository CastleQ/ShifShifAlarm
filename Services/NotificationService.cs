using EasyAlarm.Models;
using EasyAlarm.Utils;
using Microsoft.Toolkit.Uwp.Notifications;

namespace EasyAlarm.Services
{
    /// <summary>
    /// 윈도우 네이티브 Toast 알림을 쏘는 방송 스피커.
    /// [확인 ✅] = 그냥 닫기 / [나중에 다시] = 원본 문장이 채워진 입력창 재호출
    /// </summary>
    public class NotificationService
    {
        /// <summary>알람 시간이 도래했을 때 Toast 알림을 표시합니다.</summary>
        public void ShowAlarmNotification(AlarmModel alarm)
        {
            // 할 일이 비어있으면("30분 타이머" 같은 경우) 기본 문구로
            string title = string.IsNullOrWhiteSpace(alarm.TaskContent)
                ? "⏰ 알람 시간입니다!"
                : alarm.TaskContent;

            // 무시간 할일은 예약 시각이 의미가 없으므로 성격을 대신 표시
            string subtitle = alarm.IsUntimed
                ? "알람 미지정 할일 · 매일 정오 알림"
                : $"예약: {TimeFormatter.FormatRange(alarm.AlarmTime, alarm.DurationMinutes)}";

            new ToastContentBuilder()
                .AddArgument("alarmId", alarm.Id.ToString())
                .AddArgument("originalInput", alarm.OriginalInput)
                .AddText(title)
                .AddText(subtitle)
                .AddButton(new ToastButton()
                    .SetContent("확인 ✅")
                    .AddArgument("action", "confirm"))
                .AddButton(new ToastButton()
                    .SetContent("나중에 다시")
                    .AddArgument("action", "snooze"))
                .Show();
        }
    }
}