using System;
using System.Collections.Concurrent;
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

        /// <summary>
        /// 캘린더별 기본 알림 설정 캐시.
        ///
        /// 일정을 1건 올릴 때마다 "이 캘린더의 기본 알림이 뭐냐"를 구글에 매번 물어보고 있었습니다.
        /// 거의 바뀌지 않는 값이라, 앱이 떠 있는 동안 한 번만 물어보고 재사용합니다.
        /// (실패분 10건을 재시도하면 왕복 20회 → 11회로 줄어듭니다)
        ///
        /// ※ 구글 쪽에서 기본 알림을 바꾸면 앱을 다시 켤 때까지 옛 값을 씁니다.
        ///   알림 시각 자체는 항상 '정각'이 따로 붙으므로 알람을 놓칠 일은 없습니다.
        /// ※ 업로드와 재시도가 서로 다른 스레드에서 돌 수 있어 ConcurrentDictionary를 씁니다.
        ///   (운 나쁘면 두 번 조회할 뿐, 어긋나지는 않습니다)
        /// </summary>
        private readonly ConcurrentDictionary<string, List<(string Method, int? Minutes)>> _reminderCache = new();

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

            List<CalendarInfo> result = new List<CalendarInfo>();

            foreach (CalendarListEntry entry in list.Items)
            {
                if (entry.AccessRole != "owner" && entry.AccessRole != "writer")
                {
                    continue;   // 구독만 한 캘린더는 일정을 넣을 수 없어 제외
                }

                result.Add(new CalendarInfo
                {
                    Id = entry.Id,
                    Name = await ResolveDisplayNameAsync(service, entry),
                    IsPrimary = entry.Primary == true
                });
            }

            return result.OrderByDescending(c => c.IsPrimary).ToList();
        }

        /// <summary>
        /// 캘린더의 표시 이름을 찾아냅니다.
        /// ⚠️ 구글은 캘린더 이름을 세 곳에 나눠 보관합니다.
        ///    ① SummaryOverride(내가 붙인 별칭) ② CalendarList.Summary(목록 캐시)
        ///    ③ Calendars.Summary(캘린더 본체의 실제 이름)
        ///    특히 기본 캘린더는 ②가 '이메일 주소'로 채워져 있어서,
        ///    이메일처럼 보이면 다음 후보를 찾아가야 사용자가 보는 이름과 일치합니다.
        /// </summary>
        private static async Task<string> ResolveDisplayNameAsync(
            CalendarService service, CalendarListEntry entry)
        {
            if (IsRealName(entry.SummaryOverride))
            {
                return entry.SummaryOverride;
            }

            if (IsRealName(entry.Summary))
            {
                return entry.Summary;
            }

            // 목록에 담긴 이름이 이메일뿐이면, 캘린더 본체를 한 번 더 조회해 실제 이름을 확인
            try
            {
                Google.Apis.Calendar.v3.Data.Calendar detail =
                    await service.Calendars.Get(entry.Id).ExecuteAsync();

                if (IsRealName(detail.Summary))
                {
                    return detail.Summary;
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("캘린더 이름 조회", ex);
            }

            // 끝까지 못 찾으면 있는 값이라도 보여준다 (이메일 표기)
            if (!string.IsNullOrWhiteSpace(entry.SummaryOverride))
            {
                return entry.SummaryOverride;
            }

            if (!string.IsNullOrWhiteSpace(entry.Summary))
            {
                return entry.Summary;
            }

            return entry.Id;
        }

        /// <summary>비어있지 않고 이메일 주소 형태도 아닌 '사람이 붙인 이름'인지 판단합니다.</summary>
        private static bool IsRealName(string? name)
        {
            return !string.IsNullOrWhiteSpace(name) && !name.Contains('@');
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
                    // 시간 범위 입력("~까지")이 있으면 실제 구간 길이로, 없으면 기본 5분
                    DateTimeDateTimeOffset = start.AddMinutes(alarm.DurationMinutes ?? EventLengthMinutes),
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

                    // 분·시간 단위는 구글이 거부하므로 여기까지 오지 않지만(IsSyncable에서 차단),
                    // 만약을 위해 12시간 종료 조건을 붙여 둡니다.
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
        /// ⚠️ 구글 API는 '기본 알림 사용'과 '직접 지정'을 동시에 쓸 수 없어 이렇게 합칩니다.
        /// </summary>
        private async Task<Event.RemindersData> BuildRemindersAsync(
            CalendarService service, string calendarId)
        {
            if (!_reminderCache.TryGetValue(calendarId, out List<(string Method, int? Minutes)>? plan))
            {
                plan = await FetchReminderPlanAsync(service, calendarId);
                _reminderCache[calendarId] = plan;
            }

            // ⚠️ 캐시에는 '설계도'(방식·분)만 담고, 실제 알림 객체는 일정마다 새로 만듭니다.
            //    같은 객체를 여러 일정이 공유하면 라이브러리가 내부 값을 덧쓸 때 서로 간섭할 수 있습니다.
            return new Event.RemindersData
            {
                UseDefault = false,
                Overrides = plan
                    .Select(r => new EventReminder { Method = r.Method, Minutes = r.Minutes })
                    .ToList()
            };
        }

        /// <summary>캘린더의 기본 알림을 실제로 조회해 '설계도'로 정리합니다. (캐시가 비었을 때만 호출)</summary>
        private static async Task<List<(string Method, int? Minutes)>> FetchReminderPlanAsync(
            CalendarService service, string calendarId)
        {
            List<(string Method, int? Minutes)> reminders = new List<(string, int?)>();

            try
            {
                CalendarListEntry entry = await service.CalendarList.Get(calendarId).ExecuteAsync();

                if (entry.DefaultReminders != null)
                {
                    foreach (EventReminder r in entry.DefaultReminders)
                    {
                        reminders.Add((r.Method, r.Minutes));
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
                reminders.Add(("popup", 0));
            }

            if (reminders.Count > MaxReminders)
            {
                reminders = reminders
                    .Where(r => r.Minutes == 0)
                    .Concat(reminders.Where(r => r.Minutes != 0))
                    .Take(MaxReminders)
                    .ToList();
            }

            return reminders;
        }
    }
}