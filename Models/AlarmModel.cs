using System;

namespace EasyAlarm.Models
{
    /// <summary>
    /// 알람의 반복 종류 (객관식 선택지)
    /// </summary>
    public enum RepeatType
    {
        /// <summary>반복 없음 (한 번만 울리는 일반 알람)</summary>
        None = 0,

        /// <summary>매일 반복 (예: "매일 09시", "매 3시에")</summary>
        Daily = 1,

        /// <summary>매주 특정 요일 반복 (예: "매주 수요일 14시", "일요일마다")</summary>
        Weekly = 2,

        /// <summary>매월 특정 일 반복 (예: "매월 1일 10시")</summary>
        Monthly = 3,

        /// <summary>일정 간격 반복 (예: "30분마다", "2시간마다", "3일마다")</summary>
        Interval = 4,

        /// <summary>평일(월~금)만 매일 반복 (예: "평일 매일 9시")</summary>
        Weekdays = 5
    }

    /// <summary>
    /// 알람 1개의 데이터 구조 (주문서 양식)
    /// </summary>
    public class AlarmModel
    {
        /// <summary>알람의 고유번호 (주민등록번호처럼 절대 겹치지 않는 값)</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// 할 일 내용 (예: "세탁기 돌리기")
        /// "30분 타이머"처럼 울림 자체가 목적인 경우 빈 문자열일 수 있습니다.
        /// </summary>
        public string TaskContent { get; set; } = string.Empty;

        /// <summary>
        /// 알람이 울릴 정확한 날짜와 시간
        /// 반복 알람의 경우: '다음번에 울릴 시각'이 담깁니다.
        /// </summary>
        public DateTime AlarmTime { get; set; }

        /// <summary>알람이 만들어진 시각 (기록용)</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>사용자가 입력했던 원본 문장 (예: "30분 뒤 세탁기 돌리기")</summary>
        public string OriginalInput { get; set; } = string.Empty;

        // ─────────────── 반복 알람 정보 ───────────────

        /// <summary>반복 종류 (None이면 한 번만 울리는 일반 알람)</summary>
        public RepeatType Repeat { get; set; } = RepeatType.None;

        /// <summary>[매주 반복 전용] 무슨 요일에 울릴지</summary>
        public DayOfWeek? RepeatDayOfWeek { get; set; } = null;

        /// <summary>[매월 반복 전용] 며칠에 울릴지 (1~31)</summary>
        public int? RepeatDayOfMonth { get; set; } = null;

        /// <summary>
        /// [간격 반복 전용] 몇 분 간격으로 울릴지.
        /// 예: "30분마다" = 30, "2시간마다" = 120, "3일마다" = 4320
        /// </summary>
        public int? RepeatIntervalMinutes { get; set; } = null;

        // ─────────────── 무시간 할일 ("알람없음") ───────────────

        /// <summary>
        /// 사용자가 시간을 지정하지 않은 할일인지 여부.
        /// true면 목록에서 시각 대신 "알람없음" 배지로 표시되고,
        /// 내부적으로는 매일 정오(12:00) 반복(Daily)으로 동작합니다.
        /// 기존 JSON에 이 칸이 없으면 자동으로 false(일반 알람) 취급.
        /// </summary>
        public bool IsUntimed { get; set; } = false;

        // ─────────────── 완료 도장 (할일 목록 기능용) ───────────────

        /// <summary>울림 완료 여부. true면 '지난 알람' 목록에 표시됩니다.</summary>
        public bool IsCompleted { get; set; } = false;

        /// <summary>울림이 완료된 시각</summary>
        public DateTime? CompletedAt { get; set; } = null;
    }
}