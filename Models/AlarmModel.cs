using System;

namespace EasyAlarm.Models
{
    public enum RepeatType
    {
        None = 0,
        Daily = 1,
        Weekly = 2,
        Monthly = 3,
        Interval = 4,
        Weekdays = 5
    }

    public enum SyncState
    {
        NotSynced = 0,
        Pending = 1,
        Synced = 2,
        Failed = 3,
        Skipped = 4
    }

    public class AlarmModel
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string TaskContent { get; set; } = string.Empty;
        public DateTime AlarmTime { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string OriginalInput { get; set; } = string.Empty;

        public RepeatType Repeat { get; set; } = RepeatType.None;
        public DayOfWeek? RepeatDayOfWeek { get; set; } = null;
        public int? RepeatDayOfMonth { get; set; } = null;
        public int? RepeatIntervalMinutes { get; set; } = null;

        public bool IsUntimed { get; set; } = false;

        /// <summary>
        /// "14시부터 16시까지" 같은 시간 범위 입력 시의 길이(분).
        /// 절대 종료시각 대신 '길이'로 저장 → 반복 알람이 다음 회차로 갱신돼도
        /// 시작+길이 계산만으로 항상 정확한 종료시각이 나옵니다(동기화 어긋남 없음).
        /// 범위가 아닌 일반 알람은 null.
        /// </summary>
        public int? DurationMinutes { get; set; } = null;

        public string? GoogleEventId { get; set; } = null;
        public SyncState Sync { get; set; } = SyncState.NotSynced;
        public DateTime? SyncAttemptedAt { get; set; } = null;

        public bool IsCompleted { get; set; } = false;
        public DateTime? CompletedAt { get; set; } = null;
    }
}