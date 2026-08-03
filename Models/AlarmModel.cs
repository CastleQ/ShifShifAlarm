using System;

namespace EasyAlarm.Models
{
    /// <summary>알람의 반복 종류 (객관식 선택지)</summary>
    public enum RepeatType
    {
        None = 0,
        Daily = 1,
        Weekly = 2,
        Monthly = 3,
        Interval = 4,
        Weekdays = 5
    }

    /// <summary>구글 캘린더 동기화 상태</summary>
    public enum SyncState
    {
        /// <summary>아직 시도하지 않음</summary>
        NotSynced = 0,

        /// <summary>전송 대기 중 (통신 진행 또는 재시도 대기)</summary>
        Pending = 1,

        /// <summary>구글 캘린더에 등록 완료</summary>
        Synced = 2,

        /// <summary>전송 실패 (나중에 재시도)</summary>
        Failed = 3,

        /// <summary>동기화 대상 아님 (연동 꺼짐 등)</summary>
        Skipped = 4
    }

    /// <summary>알람 1개의 데이터 구조 (주문서 양식)</summary>
    public class AlarmModel
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>할 일 내용. 무텍스트 알람("30분 타이머")은 빈 문자열</summary>
        public string TaskContent { get; set; } = string.Empty;

        /// <summary>알람이 울릴 시각 (반복 알람은 '다음 회차' 시각)</summary>
        public DateTime AlarmTime { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string OriginalInput { get; set; } = string.Empty;

        // ─────────────── 반복 알람 정보 ───────────────

        public RepeatType Repeat { get; set; } = RepeatType.None;
        public DayOfWeek? RepeatDayOfWeek { get; set; } = null;
        public int? RepeatDayOfMonth { get; set; } = null;

        /// <summary>[간격 반복] 몇 분 간격 (30분=30, 2시간=120, 3일=4320)</summary>
        public int? RepeatIntervalMinutes { get; set; } = null;

        // ─────────────── 무시간 할일 ("알람없음") ───────────────

        /// <summary>시간을 지정하지 않은 할일. 로컬은 매일 정오, 구글은 종일 일정</summary>
        public bool IsUntimed { get; set; } = false;

        // ─────────────── 구글 캘린더 동기화 ───────────────

        /// <summary>구글 캘린더에 만들어진 일정의 고유번호 (삭제 연동에 사용)</summary>
        public string? GoogleEventId { get; set; } = null;

        /// <summary>동기화 상태</summary>
        public SyncState Sync { get; set; } = SyncState.NotSynced;

        /// <summary>마지막 전송 시도 시각 (재시도 간격 제어용)</summary>
        public DateTime? SyncAttemptedAt { get; set; } = null;

        // ─────────────── 완료 도장 ───────────────

        public bool IsCompleted { get; set; } = false;
        public DateTime? CompletedAt { get; set; } = null;
    }
}