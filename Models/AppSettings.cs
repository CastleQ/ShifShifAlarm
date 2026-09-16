namespace EasyAlarm.Models
{
    /// <summary>
    /// 앱 환경설정 스키마. %appdata%\EasyAlarm\settings.json 에 보관됩니다.
    /// </summary>
    public class AppSettings
    {
        // ─────────────── 구글 캘린더 연동 ───────────────

        /// <summary>구글 캘린더 동기화 사용 여부 (신규 사용자는 꺼진 상태로 시작)</summary>
        public bool GoogleSyncEnabled { get; set; } = false;

        /// <summary>동기화 대상 캘린더의 고유 ID (빈 값이면 기본 캘린더 사용)</summary>
        public string GoogleCalendarId { get; set; } = string.Empty;

        /// <summary>선택된 캘린더의 표시 이름 (설정 창에 보여주기용)</summary>
        public string GoogleCalendarName { get; set; } = string.Empty;

        // ─────────────── 일반 ───────────────

        /// <summary>윈도우 시작 시 자동 실행 (기본 켜짐)</summary>
        public bool StartWithWindows { get; set; } = true;

        /// <summary>24시간제 표기 사용 (기본 켜짐 / 끄면 오전·오후 표기)</summary>
        public bool Use24HourClock { get; set; } = true;

        // ─────────────── 위젯 위치 ───────────────
        // 사용자가 드래그해 옮긴 자리를 기억합니다.
        // null이면 '아직 옮긴 적 없음' → 기본 위치(우측 하단)에 자리 잡습니다.

        /// <summary>위젯의 화면 왼쪽 좌표</summary>
        public double? WidgetLeft { get; set; } = null;

        /// <summary>위젯의 화면 위쪽 좌표</summary>
        public double? WidgetTop { get; set; } = null;
    }
}