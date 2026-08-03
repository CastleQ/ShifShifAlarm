using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EasyAlarm.Models;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;

namespace EasyAlarm.Services
{
    /// <summary>캘린더 1개의 요약 정보 (설정 창 목록 표시용)</summary>
    public class CalendarInfo
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public bool IsPrimary { get; init; }
    }

    /// <summary>
    /// 구글 캘린더와 실제로 주고받는 창구.
    /// 규칙(확정): 시작=입력 시각, 길이 5분, 알림=캘린더 기본 알림 + 정각 추가,
    /// 반복은 국제 표준 규칙(RRULE), 무시간 할일은 '등록일 정오' 1건.
    /// </summary>
    public class GoogleCalendarService
    {
        private readonly GoogleAuthService _auth;

        /// <summary>일정 길이(분)</summary>
        private const int EventLengthMinutes = 5;

        /// <summary>구글이 허용하는 일정당 알림 최대 개수</summary>
        private const int MaxReminders = 5;

        /// <summary>분·시간 간격 반복의 기본 종료 시간(시간). 알림 폭주 방지 (확정: 12시간)</summary>
        private const int IntervalSyncHours = 12;

        public GoogleCalendarService(GoogleAuthService auth)
        {
            _auth = auth;
        }

        /// <summary>쓰기 권한이 있는 캘린더 목록을 가져옵니다.</summary>
        public async Task<List<CalendarInfo>> GetWritableCalendarsAsync()
        {
            using CalendarService service = await _auth.CreateCalendarServiceAsync();

            CalendarList list = await service.CalendarList.List().ExecuteAsync();

            if (list.Items == null)
            {
                return new List<CalendarInfo>();
            }

            return list.Items
                .Where(c => c.AccessRole == "owner" || c.AccessRole == "writer")
                .Select(c => new CalendarInfo
                {
                    Id = c.Id,
                    Name = string.IsNullOrWhiteSpace(c.Summary) ? c.Id : c.Summary,
                    IsPrimary = c.Primary == true
                })
                .OrderByDescending(c => c.IsPrimary)
                .ToList();
        }

        /// <summary>
        /// 알람 1건을 구글 캘린더 일정으로 만듭니다.
        /// 반환값은 생성된 일정의 고유번호(삭제 연동에 사용).
        /// </summary>
        public async Task<string> CreateEventAsync(AlarmModel alarm, string calendarId)
        {
            using CalendarService service = await _auth.CreateCalendarServiceAsync();

            string target = string.IsNullOrWhiteSpace(calendarId) ? "primary" : calendarId;
            string zone = GetIanaTimeZone();

            // 무시간 할일은 '등록한 날짜의 정오'로 올립니다.
            // (로컬 AlarmTime은 '다가오는 정오'라서 오후에 등록하면 내일이 되어버리므로 별도 계산)
            DateTime localStart = alarm.IsUntimed
                ? alarm.CreatedAt.Date.AddHours(12)
                : alarm.AlarmTime;

            DateTimeOffset start = new DateTimeOffset(localStart);

            Event ev = new Event
            {
                Summary = string.IsNullOrWhiteSpace(alarm.TaskContent) ? "쉽쉽알람" : alarm.TaskContent,
                Description = string.IsNullOrWhiteSpace(alarm.OriginalInput)
                    ? null
                    : $"쉽쉽알람 등록: {alarm.OriginalInput}",

                // ⚠️ TimeZone은 반복 일정에 필수입니다.
                // 없으면 구글이 "매일 22시"를 어느 지역 기준으로 반복할지 몰라 요청을 거부합니다.
                Start = new EventDateTime { DateTimeDateTimeOffset = start, TimeZone = zone },
                End = new EventDateTime
                {
                    DateTimeDateTimeOffset = start.AddMinutes(EventLengthMinutes),
                    TimeZone = zone
                },

                Reminders = await BuildRemindersAsync(service, target)
            };

            // 무시간 할일은 반복 없이 1건만 (확정된 나-1안)
            if (!alarm.IsUntimed)
            {
                string? rule = BuildRecurrenceRule(alarm);
                if (rule != null)
                {
                    ev.Recurrence = new List<string> { rule };
                }
            }

            Event created = await service.Events.Insert(ev, target).ExecuteAsync();
            return created.Id;
        }

        /// <summary>
        /// 이 PC의 시간대를 구글이 이해하는 국제 표기(예: Asia/Seoul)로 바꿉니다.
        /// 윈도우는 자체 표기("Korea Standard Time")를 쓰므로 변환이 필요합니다.
        /// </summary>
        private static string GetIanaTimeZone()
        {
            try
            {
                if (TimeZoneInfo.Local.HasIanaId)
                {
                    return TimeZoneInfo.Local.Id;
                }

                if (TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out string? iana)
                    && !string.IsNullOrWhiteSpace(iana))
                {
                    return iana;
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("시간대 변환", ex);
            }

            return "UTC";   // 변환 실패 시 안전한 기본값
        }
        /// <summary>
        /// 이 알람을 구글 캘린더로 보낼 수 있는지 판단합니다.
        /// ⚠️ 분·시간 단위 간격 반복("20분마다")은 구글이 지원하지 않습니다.
        ///    구글 반복 규칙은 일·주·월·년 단위만 허용하며,
        ///    FREQ=MINUTELY/HOURLY는 "Invalid recurrence rule"로 거부됩니다.
        ///    → 이런 알람은 PC 전용으로 남깁니다 (확정된 '나안').
        /// </summary>
        public static bool IsSyncable(AlarmModel alarm)
        {
            if (alarm.Repeat != RepeatType.Interval)
            {
                return true;
            }

            int minutes = alarm.RepeatIntervalMinutes ?? 0;

            // 일 단위 간격(3일마다 등)만 동기화 가능
            return minutes > 0 && minutes % 1440 == 0;
        }
        /// <summary>
        /// 구글 캘린더에서 일정 1건을 삭제합니다.
        /// 반복 일정의 대표 번호를 지우면 모든 회차가 함께 사라집니다.
        /// 이미 지워진 일정(404/410)은 결과가 같으므로 성공으로 간주합니다.
        /// </summary>
        public async Task DeleteEventAsync(string eventId, string calendarId)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                return;
            }

            using CalendarService service = await _auth.CreateCalendarServiceAsync();

            string target = string.IsNullOrWhiteSpace(calendarId) ? "primary" : calendarId;

            try
            {
                await service.Events.Delete(target, eventId).ExecuteAsync();
            }
            catch (Google.GoogleApiException ex) when (
                ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound ||
                ex.HttpStatusCode == System.Net.HttpStatusCode.Gone)
            {
                // 사용자가 구글에서 먼저 지운 경우 → 목표 달성이므로 조용히 넘어간다
            }
        }

        /// <summary>반복 정보를 국제 표준 반복 규칙(RRULE)으로 번역합니다. 반복이 아니면 null.</summary>
        private static string? BuildRecurrenceRule(AlarmModel alarm)
        {
            switch (alarm.Repeat)
            {
                case RepeatType.Daily:
                    return "RRULE:FREQ=DAILY";

                case RepeatType.Weekly:
                    DayOfWeek day = alarm.RepeatDayOfWeek ?? alarm.AlarmTime.DayOfWeek;
                    return $"RRULE:FREQ=WEEKLY;BYDAY={ToRruleDay(day)}";

                case RepeatType.Monthly:
                    int monthDay = alarm.RepeatDayOfMonth ?? alarm.AlarmTime.Day;
                    return $"RRULE:FREQ=MONTHLY;BYMONTHDAY={monthDay}";

                case RepeatType.Weekdays:
                    return "RRULE:FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR";

                case RepeatType.Interval:
                    int minutes = Math.Max(1, alarm.RepeatIntervalMinutes ?? 1);

                    // 일 단위 간격은 하루 1번이라 종료 조건 없이 안전
                    if (minutes % 1440 == 0)
                    {
                        return $"RRULE:FREQ=DAILY;INTERVAL={minutes / 1440}";
                    }

                    // 분·시간 단위는 12시간 후 종료 (모바일 알림 폭주 방지)
                    string until = alarm.AlarmTime
                        .AddHours(IntervalSyncHours)
                        .ToUniversalTime()
                        .ToString("yyyyMMdd'T'HHmmss'Z'");

                    if (minutes % 60 == 0)
                    {
                        return $"RRULE:FREQ=HOURLY;INTERVAL={minutes / 60};UNTIL={until}";
                    }

                    return $"RRULE:FREQ=MINUTELY;INTERVAL={minutes};UNTIL={until}";

                default:
                    return null;
            }
        }

        /// <summary>요일을 반복 규칙 표기(MO, TU...)로 바꿉니다.</summary>
        private static string ToRruleDay(DayOfWeek day)
        {
            return day switch
            {
                DayOfWeek.Monday => "MO",
                DayOfWeek.Tuesday => "TU",
                DayOfWeek.Wednesday => "WE",
                DayOfWeek.Thursday => "TH",
                DayOfWeek.Friday => "FR",
                DayOfWeek.Saturday => "SA",
                _ => "SU"
            };
        }

        /// <summary>
        /// 알림 구성: 캘린더 기본 알림을 그대로 복사하고 정각(0분 전) 알림을 추가합니다.
        /// </summary>
        private static async Task<Event.RemindersData> BuildRemindersAsync(
            CalendarService service, string calendarId)
        {
            List<EventReminder> reminders = new List<EventReminder>();

            try
            {
                CalendarListEntry entry = await service.CalendarList.Get(calendarId).ExecuteAsync();

                if (entry.DefaultReminders != null)
                {
                    foreach (EventReminder r in entry.DefaultReminders)
                    {
                        reminders.Add(new EventReminder { Method = r.Method, Minutes = r.Minutes });
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("캘린더 기본 알림 조회", ex);
            }

            bool hasExact = reminders.Any(r => r.Minutes == 0 && r.Method == "popup");
            if (!hasExact)
            {
                reminders.Add(new EventReminder { Method = "popup", Minutes = 0 });
            }

            if (reminders.Count > MaxReminders)
            {
                reminders = reminders
                    .Where(r => r.Minutes == 0)
                    .Concat(reminders.Where(r => r.Minutes != 0))
                    .Take(MaxReminders)
                    .ToList();
            }

            return new Event.RemindersData
            {
                UseDefault = false,
                Overrides = reminders
            };
        }
    }
}