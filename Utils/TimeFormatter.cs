using System;
using System.Globalization;

namespace EasyAlarm.Utils
{
    public static class TimeFormatter
    {
        private static readonly CultureInfo Korean = new CultureInfo("ko-KR");

        public static bool Use24Hour { get; set; } = true;

        public static string Format(DateTime time)
        {
            return Use24Hour
                ? time.ToString("MM/dd (ddd) HH:mm", Korean)
                : time.ToString("MM/dd (ddd) tt h:mm", Korean);
        }

        public static string FormatTimeOnly(DateTime time)
        {
            return Use24Hour
                ? time.ToString("HH:mm", Korean)
                : time.ToString("tt h:mm", Korean);
        }

        /// <summary>
        /// 시작 시각 + (있다면) 길이를 함께 표시합니다.
        /// 길이가 없으면 시작 시각만, 있으면 "시작 ~ 끝시각" 형태로.
        /// </summary>
        public static string FormatRange(DateTime start, int? durationMinutes)
        {
            if (!durationMinutes.HasValue || durationMinutes.Value <= 0)
            {
                return Format(start);
            }

            DateTime end = start.AddMinutes(durationMinutes.Value);
            return $"{Format(start)} ~ {FormatTimeOnly(end)}";
        }
    }
}