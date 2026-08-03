using System;
using System.Globalization;

namespace EasyAlarm.Utils
{
    /// <summary>
    /// 시각 표기를 한 곳에서 관리하는 서식 담당.
    /// 설정(24시간제/12시간제)에 따라 앱 전체의 시각 표기가 함께 바뀝니다.
    /// (표기 규칙이 여러 파일에 흩어지면 나중에 한 곳만 고쳐 어긋나므로 중앙화)
    /// </summary>
    public static class TimeFormatter
    {
        /// <summary>
        /// 날짜·요일 표기가 항상 한국식으로 나오도록 문화권을 고정합니다.
        /// (OS 언어가 달라도 "08-04 (화) 오후 2:30" 형태 유지)
        /// </summary>
        private static readonly CultureInfo Korean = new CultureInfo("ko-KR");

        /// <summary>true면 14:30, false면 오후 2:30 형태로 표기</summary>
        public static bool Use24Hour { get; set; } = true;

        /// <summary>날짜 + 요일 + 시각 (예: "08-04 (화) 14:30")</summary>
        public static string Format(DateTime time)
        {
            return Use24Hour
                ? time.ToString("MM/dd (ddd) HH:mm", Korean)
                : time.ToString("MM/dd (ddd) tt h:mm", Korean);
        }

        /// <summary>시각만 (예: "14:30" 또는 "오후 2:30")</summary>
        public static string FormatTimeOnly(DateTime time)
        {
            return Use24Hour
                ? time.ToString("HH:mm", Korean)
                : time.ToString("tt h:mm", Korean);
        }
    }
}