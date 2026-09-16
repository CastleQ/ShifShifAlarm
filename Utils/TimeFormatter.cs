using System;
using System.Globalization;

namespace EasyAlarm.Utils
{
    public static class TimeFormatter
    {
        private static readonly CultureInfo Korean = new CultureInfo("ko-KR");

        public static bool Use24Hour { get; set; } = true;

        /// <summary>
        /// 올해가 아닌 알람에만 "2027년 " 같은 연도 표시를 붙입니다. 올해면 빈 문자열.
        ///
        /// 평소에는 아무것도 안 붙으므로, 연도가 보인다는 것 자체가
        /// "이건 올해가 아니다"라는 신호가 됩니다.
        /// (지난 날짜를 착각해서 입력했을 때 알아차리게 하는 장치)
        /// </summary>
        public static string YearPrefix(DateTime time)
        {
            return time.Year == DateTime.Now.Year ? string.Empty : $"{time.Year}년 ";
        }

        public static string Format(DateTime time)
        {
            return YearPrefix(time) + (Use24Hour
                ? time.ToString("MM/dd (ddd) HH:mm", Korean)
                : time.ToString("MM/dd (ddd) tt h:mm", Korean));
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

        /// <summary>
        /// 등록 성공 안내처럼 '연도만 굵게' 보여줘야 하는 화면을 위해 두 토막으로 나눠 돌려줍니다.
        /// 올해 알람이면 Year는 빈 문자열이라 굵게 칠할 것이 없습니다.
        /// </summary>
        public static (string Year, string Detail) SplitRange(DateTime start, int? durationMinutes)
        {
            string full = FormatRange(start, durationMinutes);
            string year = YearPrefix(start);

            return (year, full.Substring(year.Length));
        }
    }
}