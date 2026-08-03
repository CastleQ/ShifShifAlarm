namespace EasyAlarm.Models
{
    /// <summary>
    /// 앱 환경설정 스키마. %appdata%\EasyAlarm\settings.json 에 보관됩니다.
    /// (구글 연동 + 추후 설정 창에서 쓸 옵션들을 한 곳에 모음)
    /// </summary>
    public class AppSettings
    {
        // ─────────────── 구글 캘린더 연동 ───────────────

        /// <summary>구글 캘린더 동기화 사용 여부</summary>
        public bool GoogleSyncEnabled { get; set; } = false;

        /// <summary>동기화 대상 캘린더의 고유 ID (빈 값이면 아직 선택 안 함)</summary>
        public string GoogleCalendarId { get; set; } = string.Empty;

        /// <summary>선택된 캘린더의 표시 이름 (설정 창에 보여주기용)</summary>
        public string GoogleCalendarName { get; set; } = string.Empty;

        // ─────────────── 추후 설정 창에서 사용 (지금은 자리만) ───────────────

        /// <summary>부팅 시 자동 시작</summary>
        public bool StartWithWindows { get; set; } = false;

        /// <summary>24시간제 표기 사용 (false면 오전/오후 표기)</summary>
        public bool Use24HourClock { get; set; } = true;
    }
}