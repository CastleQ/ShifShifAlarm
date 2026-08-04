using System;
using System.Collections.Generic;
using System.Windows.Threading;
using EasyAlarm.Models;

namespace EasyAlarm.Services
{
    /// <summary>
    /// 주방 타이머 감시자.
    /// 10초마다 알람 장부를 확인해서, 시간이 된 알람이 있으면
    /// ① 알림을 쏘고 ② 일반 알람은 '완료 도장' / 반복 알람은 다음 회차로 갱신합니다.
    /// </summary>
    public class AlarmSchedulerService
    {
        private readonly StorageService _storage;
        private readonly NotificationService _notification;
        private readonly DispatcherTimer _timer;

        /// <summary>
        /// 장부 내용이 바뀌었을 때 울리는 알림 종.
        /// 감시자는 누가 듣는지 모르고 종만 울립니다(부품 독립성 유지).
        /// 사령탑이 이 종에 귀를 연결해, 열려 있는 목록 창만 다시 그리도록 지시합니다.
        /// </summary>
        public event Action? AlarmsChanged;

        public AlarmSchedulerService(StorageService storage, NotificationService notification)
        {
            _storage = storage;
            _notification = notification;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _timer.Tick += (s, e) => CheckAlarms();
        }

        /// <summary>감시 시작. 프로그램 시작 시 1회 호출합니다.</summary>
        public void Start()
        {
            CheckAlarms();
            _timer.Start();
        }

        /// <summary>장부를 열어 시간이 된 알람을 처리합니다.</summary>
        private void CheckAlarms()
        {
            DateTime now = DateTime.Now;
            List<AlarmModel> alarms = _storage.LoadAlarms();
            bool changed = false;

            foreach (AlarmModel alarm in alarms)
            {
                if (alarm.IsCompleted)
                {
                    continue;
                }

                if (alarm.AlarmTime > now)
                {
                    continue;
                }

                // ── 시간 도래! 알림 발사 ──
                // 단, 등록 시점에 이미 시작 시각이 지나 있던 '진행 중 구간'은 알림을 생략한다.
                // (예: 15:40에 "11시부터 20시까지" 등록 → 캘린더엔 오늘 구간으로 남기고, 지난 시각 알림은 무의미)
                bool alreadyStartedWhenCreated =
                    alarm.Repeat == RepeatType.None &&
                    alarm.DurationMinutes.HasValue &&
                    alarm.AlarmTime <= alarm.CreatedAt;

                if (!alreadyStartedWhenCreated)
                {
                    _notification.ShowAlarmNotification(alarm);
                }

                if (alarm.Repeat == RepeatType.None)
                {
                    alarm.IsCompleted = true;
                    alarm.CompletedAt = now;
                }
                else
                {
                    alarm.AlarmTime = CalcNextOccurrence(alarm, now);
                }

                changed = true;
            }

            if (changed)
            {
                _storage.SaveAlarms(alarms);

                // 장부가 바뀌었으니 종을 울린다 → 열려 있는 목록 창이 실시간으로 갱신됨
                // (예정된 알람 → 지난 알람 이동이 즉시 반영)
                AlarmsChanged?.Invoke();
            }
        }

        /// <summary>
        /// 반복 알람의 다음 울림 시각을 계산합니다.
        /// 프로그램이 꺼져 있어서 여러 회차를 놓쳤어도, 반드시 '미래'의 시각이 나올 때까지 전진합니다.
        /// </summary>
        private static DateTime CalcNextOccurrence(AlarmModel alarm, DateTime now)
        {
            DateTime next = alarm.AlarmTime;

            switch (alarm.Repeat)
            {
                case RepeatType.Daily:
                    while (next <= now)
                    {
                        next = next.AddDays(1);
                    }
                    break;

                case RepeatType.Weekly:
                    while (next <= now)
                    {
                        next = next.AddDays(7);
                    }
                    break;

                case RepeatType.Monthly:
                    int targetDay = alarm.RepeatDayOfMonth ?? next.Day;
                    while (next <= now)
                    {
                        DateTime cursor = new DateTime(next.Year, next.Month, 1).AddMonths(1);
                        while (targetDay > DateTime.DaysInMonth(cursor.Year, cursor.Month))
                        {
                            cursor = cursor.AddMonths(1);
                        }
                        next = new DateTime(cursor.Year, cursor.Month, targetDay,
                                            next.Hour, next.Minute, 0);
                    }
                    break;

                case RepeatType.Interval:
                    int interval = Math.Max(1, alarm.RepeatIntervalMinutes ?? 1);
                    while (next <= now)
                    {
                        next = next.AddMinutes(interval);
                    }
                    break;

                case RepeatType.Weekdays:
                    while (next <= now ||
                           next.DayOfWeek == DayOfWeek.Saturday ||
                           next.DayOfWeek == DayOfWeek.Sunday)
                    {
                        next = next.AddDays(1);
                    }
                    break;
            }

            return next;
        }
    }
}