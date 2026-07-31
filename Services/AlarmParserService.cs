using System;
using System.Text.RegularExpressions;
using EasyAlarm.Models;

namespace EasyAlarm.Services
{
    /// <summary>
    /// 파싱 결과 보고서.
    /// </summary>
    public class ParseResult
    {
        public bool Success { get; init; }
        public string ErrorMessage { get; init; } = string.Empty;
        public AlarmModel? Alarm { get; init; }

        /// <summary>
        /// 시간처럼 보이는 표현이 '아예 없는' 입력이었음을 알리는 표식.
        /// (예: "회의 자료 준비") → 호출자가 "시간 없이 할일로 등록할까요?"를 물을 수 있습니다.
        /// 반대로 시간 흔적은 있는데 해석 실패한 경우(오타 등)는 이 표식 없이 일반 오류로 보고합니다.
        /// </summary>
        public bool NoTimeDetected { get; init; }
    }

    /// <summary>
    /// 자연어 문장에서 [시간]과 [할 일]을 분리하는 두뇌.
    /// (숫자/한글 시간, 시간대 힌트, 이번주/다음주, 매일/매주/매월/간격/평일 반복,
    ///  타이머/알람 인식어, 정오/자정, 있다가/이따)
    /// </summary>
    public class AlarmParserService
    {
        // ───────────── 공용 조각 ─────────────
        // 조사 '에' 삼키기: 단, '에' 바로 뒤에 다른 한글이 붙어 있으면
        // ("에스엘"의 '에'처럼) 조사가 아니므로 건드리지 않는다. [버그 수정의 핵심]
        private const string Eul = @"(?:에(?![가-힣]))?";

        // ───────────── 패턴 탐지기(정규식) 목록 ─────────────

        // "30분 타이머", "1시간 30분 타이머", "5분 알람" — 무텍스트 알람 인식어
        private static readonly Regex TimerHour = new(
            @"(\d{1,3})\s*시간\s*(?:(\d{1,2})\s*분\s*)?(?:타이머|알람)(?![가-힣])",
            RegexOptions.Compiled);

        private static readonly Regex TimerMinute = new(
            @"(\d{1,3})\s*분\s*(?:타이머|알람)(?![가-힣])",
            RegexOptions.Compiled);

        // "1시간 뒤", "2시간 30분 후", "1시간 있다가"
        private static readonly Regex RelativeHour = new(
            @"(\d{1,3})\s*시간\s*(?:(\d{1,2})\s*분\s*)?(?:뒤|후|있다가)\s*" + Eul,
            RegexOptions.Compiled);

        // "30분 뒤", "5분 후", "10분 있다가"
        private static readonly Regex RelativeMinute = new(
            @"(\d{1,3})\s*분\s*(?:뒤|후|있다가)\s*" + Eul,
            RegexOptions.Compiled);

        // "3일 뒤", "10일 후"
        private static readonly Regex RelativeDayNumber = new(
            @"(\d{1,3})\s*일\s*(?:뒤|후|있다가)\s*" + Eul,
            RegexOptions.Compiled);

        // "내일", "낼", "모레", "사흘 뒤", "일주일 뒤" 등
        private static readonly Regex RelativeDayWord = new(
            @"(내일모레|내일|모레|글피|사흘|나흘|닷새|일주일|(?<![가-힣])낼(?![가-힣]))\s*(?:뒤|후)?\s*" + Eul,
            RegexOptions.Compiled);

        // "7/7", "7월 7일", "12.25"
        private static readonly Regex ExplicitDate = new(
            @"(\d{1,2})\s*[월/.]\s*(\d{1,2})\s*일?\s*" + Eul,
            RegexOptions.Compiled);

        // "금요일", "이번주 금요일", "다음주 월요일", "다다음주 화요일"
        private static readonly Regex Weekday = new(
            @"(?:(다다음\s*주|다음\s*주|이번\s*주)\s*)?(월|화|수|목|금|토|일)요일\s*" + Eul,
            RegexOptions.Compiled);

        // ── 반복 탐지기 ──

        // "30분마다", "2시간마다", "3일마다" — 간격 반복
        private static readonly Regex RepeatInterval = new(
            @"(\d{1,3})\s*(분|시간|일)\s*마다\s*" + Eul,
            RegexOptions.Compiled);

        // "평일 매일 9시", "평일 9시" — 평일(월~금) 반복
        private static readonly Regex RepeatWeekdays = new(
            @"평일\s*(?:매일)?\s*",
            RegexOptions.Compiled);

        // "매주 수요일", "매주 수요일마다"
        private static readonly Regex RepeatWeekly = new(
            @"매주\s*(월|화|수|목|금|토|일)요일\s*(?:마다)?\s*" + Eul,
            RegexOptions.Compiled);

        // "수요일마다", "일요일마다" — '매주' 없이도 반복으로 인식
        private static readonly Regex WeekdayMada = new(
            @"(월|화|수|목|금|토|일)요일\s*마다\s*" + Eul,
            RegexOptions.Compiled);

        // "매월 1일", "매달 15일"
        private static readonly Regex RepeatMonthly = new(
            @"매(?:월|달)\s*(\d{1,2})\s*일\s*" + Eul,
            RegexOptions.Compiled);

        // "매 3시에", "매 오후 3시" — 시각 앞의 '매' = 매일 반복
        // 소비하는 건 '매'라는 글자뿐이고, 시각 자체는 아래 시각 탐지기가 처리
        private static readonly Regex RepeatEveryClock = new(
            @"매\s*(?=\d{1,2}\s*시|(?:새벽|아침|오전|낮|오후|저녁|밤))",
            RegexOptions.Compiled);

        // "매일"
        private static readonly Regex RepeatDaily = new(
            @"매일\s*",
            RegexOptions.Compiled);

        // ── 시각 탐지기 ──

        // "정오", "자정" — 한 단어 시각
        private static readonly Regex NoonMidnight = new(
            @"(정오|자정)\s*" + Eul,
            RegexOptions.Compiled);

        // "오후 2시", "14시 30분", "5시반", "15시 정각"
        private static readonly Regex ClockDigit = new(
            @"(?:(새벽|아침|오전|낮|오후|저녁|밤)\s*)?(\d{1,2})\s*시(?:\s*(\d{1,2})\s*분|\s*(반)|\s*(정각))?\s*" + Eul,
            RegexOptions.Compiled);

        // "14:30" 형식
        private static readonly Regex ClockColon = new(
            @"(?:(새벽|아침|오전|낮|오후|저녁|밤)\s*)?(\d{1,2})\s*:\s*(\d{1,2})\s*" + Eul,
            RegexOptions.Compiled);

        // "다섯시반", "밤 열한시", "열시 정각" (한글 숫자)
        private static readonly Regex ClockHangul = new(
            @"(?:(새벽|아침|오전|낮|오후|저녁|밤)\s*)?(열두|열한|열|아홉|여덟|일곱|여섯|다섯|네|세|두|한)\s*시(?:\s*(\d{1,2})\s*분|\s*(반)|\s*(정각))?\s*" + Eul,
            RegexOptions.Compiled);

        // "이따", "이따가" — 시간 정보가 아니라 그냥 '나중에'라는 추임새 → 조용히 제거
        private static readonly Regex LaterWord = new(
            @"(?<![가-힣])이따가?(?![가-힣])",
            RegexOptions.Compiled);
            // "시간처럼 보이는 흔적"이 문장에 있는지만 판단하는 탐지기.
        // 해석에는 쓰지 않고, 실패 원인이 '오타'인지 '아예 시간을 안 적었는지' 구분하는 용도입니다.
        private static readonly Regex TimeTrace = new(
            @"\d\s*[:시분]" +
            @"|(?:새벽|아침|오전|낮|오후|저녁|밤|정오|자정)" +
            @"|(?:매일|매주|매월|매달|마다|평일|정각)" +
            @"|(?:내일모레|내일|모레|글피|사흘|나흘|닷새|일주일|(?<![가-힣])낼(?![가-힣]))" +
            @"|[월화수목금토일]요일" +
            @"|(?:뒤|후|있다가)(?![가-힣])" +
            @"|(?:타이머|알람)" +
            @"|(?:열두|열한|열|아홉|여덟|일곱|여섯|다섯|네|세|두|한)\s*시",
            RegexOptions.Compiled);
            // 오타로 깨진 시간 표기 탐지기.
        // 예: "14시30ㅇ분"(14시30 뒤가 '분'이 아님), "3시30 회의"(분 누락),
        //     숫자에 낱자 자음·모음이 달라붙은 경우("30ㅇ")
        // → 절반만 해석되어 엉뚱한 할일로 등록되는 사고를 막습니다.
        private static readonly Regex MalformedTime = new(
            @"\d{1,2}시\d{1,2}(?!\d)(?!\s*분)" +
            @"|\d[ㄱ-ㅎㅏ-ㅣ]" +
            @"|\d{1,2}\s*[ㄱ-ㅎㅏ-ㅣ]\s*분",
            RegexOptions.Compiled);

        /// <summary>
        /// 입력 문장을 해석해서 알람으로 변환합니다.
        /// </summary>
        public ParseResult Parse(string input)
        {
            // 시간 표기가 오타로 깨진 경우 → 조용히 할일로 넘기지 않고 즉시 오류 피드백
            if (MalformedTime.IsMatch(input))
            {
                return Fail("시간 표기를 확인해주세요");
            }

            string working = input.Trim();
            DateTime now = DateTime.Now;

            // ── 0단계: 추임새 제거 ("이따 3시에" → "3시에") ──
            working = LaterWord.Replace(working, " ");

            // ── 1단계: 타이머/알람 인식어 + 상대 시간 ──
            Match m = TimerHour.Match(working);
            if (m.Success)
            {
                int h = int.Parse(m.Groups[1].Value);
                int min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
                return Ok(now.AddHours(h).AddMinutes(min), FinishTask(RemoveMatch(working, m)), input);
            }

            m = TimerMinute.Match(working);
            if (m.Success)
            {
                return Ok(now.AddMinutes(int.Parse(m.Groups[1].Value)), FinishTask(RemoveMatch(working, m)), input);
            }

            m = RelativeHour.Match(working);
            if (m.Success)
            {
                int hours = int.Parse(m.Groups[1].Value);
                int minutes = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
                return Ok(now.AddHours(hours).AddMinutes(minutes), FinishTask(RemoveMatch(working, m)), input);
            }

            m = RelativeMinute.Match(working);
            if (m.Success)
            {
                return Ok(now.AddMinutes(int.Parse(m.Groups[1].Value)), FinishTask(RemoveMatch(working, m)), input);
            }

            // ── 2단계: 반복 알람 검사 ──
            RepeatType repeat = RepeatType.None;
            DayOfWeek repeatWeekday = DayOfWeek.Monday;
            int repeatMonthDay = 1;
            int intervalMinutes = 0;

            m = RepeatInterval.Match(working);
            if (m.Success)
            {
                int n = int.Parse(m.Groups[1].Value);
                intervalMinutes = m.Groups[2].Value switch
                {
                    "분" => n,
                    "시간" => n * 60,
                    _ => n * 1440   // "일"
                };
                if (intervalMinutes < 1)
                {
                    return Fail("반복 간격을 확인해주세요");
                }
                repeat = RepeatType.Interval;
                working = RemoveMatch(working, m);
            }
            else if ((m = RepeatWeekdays.Match(working)).Success)
            {
                repeat = RepeatType.Weekdays;
                working = RemoveMatch(working, m);
            }
            else if ((m = RepeatWeekly.Match(working)).Success)
            {
                repeat = RepeatType.Weekly;
                repeatWeekday = ToDayOfWeek(m.Groups[1].Value);
                working = RemoveMatch(working, m);
            }
            else if ((m = WeekdayMada.Match(working)).Success)
            {
                repeat = RepeatType.Weekly;
                repeatWeekday = ToDayOfWeek(m.Groups[1].Value);
                working = RemoveMatch(working, m);
            }
            else if ((m = RepeatMonthly.Match(working)).Success)
            {
                repeatMonthDay = int.Parse(m.Groups[1].Value);
                if (repeatMonthDay < 1 || repeatMonthDay > 31)
                {
                    return Fail("날짜 형식을 확인해주세요");
                }
                repeat = RepeatType.Monthly;
                working = RemoveMatch(working, m);
            }
            else if ((m = RepeatDaily.Match(working)).Success)
            {
                repeat = RepeatType.Daily;
                working = RemoveMatch(working, m);
            }
            else if ((m = RepeatEveryClock.Match(working)).Success)
            {
                repeat = RepeatType.Daily;   // "매 3시" = 매일 3시
                working = RemoveMatch(working, m);
            }

            // ── 3단계: 날짜 단서 수집 (반복 알람이 아닐 때만) ──
            DateTime baseDate = now.Date;
            bool dateSpecified = false;
            bool weekdaySpecified = false;
            DayOfWeek targetWeekday = DayOfWeek.Monday;
            string weekPrefix = string.Empty;

            if (repeat == RepeatType.None)
            {
                m = ExplicitDate.Match(working);
                if (m.Success)
                {
                    int month = int.Parse(m.Groups[1].Value);
                    int day = int.Parse(m.Groups[2].Value);

                    if (month < 1 || month > 12 ||
                        day < 1 || day > DateTime.DaysInMonth(now.Year, month))
                    {
                        return Fail("날짜 형식을 확인해주세요");
                    }

                    baseDate = new DateTime(now.Year, month, day);
                    dateSpecified = true;
                    working = RemoveMatch(working, m);
                }

                if (!dateSpecified)
                {
                    m = RelativeDayWord.Match(working);
                    if (m.Success)
                    {
                        int offset = m.Groups[1].Value switch
                        {
                            "낼" or "내일" => 1,
                            "모레" or "내일모레" => 2,
                            "글피" or "사흘" => 3,
                            "나흘" => 4,
                            "닷새" => 5,
                            "일주일" => 7,
                            _ => 0
                        };
                        baseDate = now.Date.AddDays(offset);
                        dateSpecified = true;
                        working = RemoveMatch(working, m);
                    }
                }

                if (!dateSpecified)
                {
                    m = RelativeDayNumber.Match(working);
                    if (m.Success)
                    {
                        baseDate = now.Date.AddDays(int.Parse(m.Groups[1].Value));
                        dateSpecified = true;
                        working = RemoveMatch(working, m);
                    }
                }

                if (!dateSpecified)
                {
                    m = Weekday.Match(working);
                    if (m.Success)
                    {
                        weekPrefix = m.Groups[1].Success
                            ? m.Groups[1].Value.Replace(" ", "")
                            : string.Empty;
                        targetWeekday = ToDayOfWeek(m.Groups[2].Value);
                        weekdaySpecified = true;
                        working = RemoveMatch(working, m);
                    }
                }
            }

            // ── 4단계: 시각(몇 시 몇 분) 찾기 ──
            int hour = 0;
            int minute = 0;
            bool hasHint = false;
            bool isHangul = false;
            bool clockFound = false;

            m = NoonMidnight.Match(working);
            if (m.Success)
            {
                hour = m.Groups[1].Value == "정오" ? 12 : 0;
                minute = 0;
                hasHint = true;   // 정오/자정은 오전·오후가 확정된 시각
                clockFound = true;
                working = RemoveMatch(working, m);
            }
            else if ((m = ClockDigit.Match(working)).Success)
            {
                hour = int.Parse(m.Groups[2].Value);
                minute = ReadMinute(m);
                hasHint = m.Groups[1].Success;
                hour = ApplyHint(hour, m.Groups[1].Value);
                clockFound = true;
                working = RemoveMatch(working, m);
            }
            else if ((m = ClockColon.Match(working)).Success)
            {
                hour = int.Parse(m.Groups[2].Value);
                minute = int.Parse(m.Groups[3].Value);
                hasHint = m.Groups[1].Success;
                hour = ApplyHint(hour, m.Groups[1].Value);
                clockFound = true;
                working = RemoveMatch(working, m);
            }
            else if ((m = ClockHangul.Match(working)).Success)
            {
                hour = ToHangulHour(m.Groups[2].Value);
                minute = ReadMinute(m);
                hasHint = m.Groups[1].Success;
                isHangul = true;
                hour = ApplyHint(hour, m.Groups[1].Value);
                clockFound = true;
                working = RemoveMatch(working, m);
            }

            // 시각이 없어도 되는 유일한 예외: 간격 반복 ("30분마다 물마시기")
            if (!clockFound && repeat != RepeatType.Interval)
            {
                // 시간 흔적이 있는데 해석 실패 → 오타 가능성이 크므로 기존처럼 오류 피드백
                if (TimeTrace.IsMatch(input))
                {
                    return Fail("시간을 입력해주세요");
                }

                // 시간 흔적이 아예 없음 → 무시간 할일 후보로 보고 (호출자가 사용자에게 확인)
                return new ParseResult
                {
                    Success = false,
                    NoTimeDetected = true,
                    ErrorMessage = "시간을 입력해주세요"
                };
            }

            if (clockFound && (hour > 23 || minute > 59))
            {
                return Fail("시간 형식을 확인해주세요");
            }

            string task = FinishTask(working);

            // ── 5단계: 반복 알람이면 '첫 울림 시각' 계산 후 종료 ──
            if (repeat != RepeatType.None)
            {
                if (isHangul && !hasHint)
                {
                    hour = NearestFuture(now, hour, minute).Hour;
                }

                DateTime first = repeat switch
                {
                    RepeatType.Daily => NextDaily(now, hour, minute),
                    RepeatType.Weekly => NextWeekly(now, repeatWeekday, hour, minute),
                    RepeatType.Monthly => NextMonthly(now, repeatMonthDay, hour, minute),
                    RepeatType.Interval => NextInterval(now, clockFound, hour, minute, intervalMinutes),
                    _ => NextWeekdaysTime(now, hour, minute)   // Weekdays
                };

                return Ok(first, task, input, repeat,
                    repeat == RepeatType.Weekly ? repeatWeekday : null,
                    repeat == RepeatType.Monthly ? repeatMonthDay : null,
                    repeat == RepeatType.Interval ? intervalMinutes : null);
            }

            // ── 6단계: 일반 알람 날짜+시각 조립, 과거 시간 처리 ──
            DateTime alarmTime;

            if (weekdaySpecified)
            {
                int daysSinceMonday = ((int)now.DayOfWeek + 6) % 7;
                DateTime thisMonday = now.Date.AddDays(-daysSinceMonday);
                int offsetInWeek = ((int)targetWeekday + 6) % 7;

                if (weekPrefix == "이번주")
                {
                    alarmTime = thisMonday.AddDays(offsetInWeek).AddHours(hour).AddMinutes(minute);
                    if (alarmTime <= now)
                    {
                        return Fail("이미 지난 시간이에요");
                    }
                }
                else if (weekPrefix == "다음주")
                {
                    alarmTime = thisMonday.AddDays(7 + offsetInWeek).AddHours(hour).AddMinutes(minute);
                }
                else if (weekPrefix == "다다음주")
                {
                    alarmTime = thisMonday.AddDays(14 + offsetInWeek).AddHours(hour).AddMinutes(minute);
                }
                else
                {
                    int diff = ((int)targetWeekday - (int)now.DayOfWeek + 7) % 7;
                    alarmTime = now.Date.AddDays(diff).AddHours(hour).AddMinutes(minute);

                    if (alarmTime <= now)
                    {
                        alarmTime = alarmTime.AddDays(7);
                    }
                }
            }
            else if (!dateSpecified && isHangul && !hasHint)
            {
                alarmTime = NearestFuture(now, hour, minute);
            }
            else
            {
                alarmTime = baseDate.AddHours(hour).AddMinutes(minute);

                if (alarmTime <= now)
                {
                    if (dateSpecified)
                    {
                        return Fail("이미 지난 시간이에요");
                    }
                    alarmTime = alarmTime.AddDays(1);
                }
            }

            return Ok(alarmTime, task, input);
        }

        // ───────────── 보조 도구들 ─────────────

        private static int ApplyHint(int hour, string hint)
        {
            bool isAfternoon = hint is "오후" or "낮" or "저녁" or "밤";
            if (isAfternoon && hour >= 1 && hour <= 11)
            {
                return hour + 12;
            }
            return hour;
        }

        private static int ToHangulHour(string word)
        {
            return word switch
            {
                "한" => 1, "두" => 2, "세" => 3, "네" => 4,
                "다섯" => 5, "여섯" => 6, "일곱" => 7, "여덟" => 8,
                "아홉" => 9, "열" => 10, "열한" => 11, _ => 12
            };
        }

        private static DayOfWeek ToDayOfWeek(string word)
        {
            return word switch
            {
                "월" => DayOfWeek.Monday,
                "화" => DayOfWeek.Tuesday,
                "수" => DayOfWeek.Wednesday,
                "목" => DayOfWeek.Thursday,
                "금" => DayOfWeek.Friday,
                "토" => DayOfWeek.Saturday,
                _ => DayOfWeek.Sunday
            };
        }

        /// <summary>정규식 결과에서 분(minute)을 읽습니다. "반"=30분, "정각"=0분.</summary>
        private static int ReadMinute(Match m)
        {
            return m.Groups[3].Success ? int.Parse(m.Groups[3].Value)
                 : m.Groups[4].Success ? 30
                 : 0;   // 분 표기 없음 또는 "정각" → 0분
        }

        private static DateTime NearestFuture(DateTime now, int hour, int minute)
        {
            int amHour = hour % 12;
            int pmHour = amHour + 12;

            DateTime best = DateTime.MaxValue;
            foreach (int dayOffset in new[] { 0, 1 })
            {
                foreach (int h in new[] { amHour, pmHour })
                {
                    DateTime candidate = now.Date.AddDays(dayOffset).AddHours(h).AddMinutes(minute);
                    if (candidate > now && candidate < best)
                    {
                        best = candidate;
                    }
                }
            }
            return best;
        }

        private static DateTime NextDaily(DateTime now, int hour, int minute)
        {
            DateTime t = now.Date.AddHours(hour).AddMinutes(minute);
            return t > now ? t : t.AddDays(1);
        }

        private static DateTime NextWeekly(DateTime now, DayOfWeek weekday, int hour, int minute)
        {
            int diff = ((int)weekday - (int)now.DayOfWeek + 7) % 7;
            DateTime t = now.Date.AddDays(diff).AddHours(hour).AddMinutes(minute);
            return t > now ? t : t.AddDays(7);
        }

        private static DateTime NextMonthly(DateTime now, int day, int hour, int minute)
        {
            DateTime cursor = new DateTime(now.Year, now.Month, 1);

            for (int i = 0; i < 24; i++)
            {
                if (day <= DateTime.DaysInMonth(cursor.Year, cursor.Month))
                {
                    DateTime t = new DateTime(cursor.Year, cursor.Month, day, hour, minute, 0);
                    if (t > now)
                    {
                        return t;
                    }
                }
                cursor = cursor.AddMonths(1);
            }
            return now.AddMonths(1);
        }

        /// <summary>
        /// [간격 반복] 첫 울림 시각.
        /// 시각 지정이 있으면("3일마다 9시") 오늘 그 시각부터, 지났으면 간격만큼 전진.
        /// 시각 지정이 없으면("30분마다") 지금부터 간격만큼 뒤.
        /// </summary>
        private static DateTime NextInterval(DateTime now, bool clockFound, int hour, int minute, int intervalMinutes)
        {
            if (!clockFound)
            {
                return now.AddMinutes(intervalMinutes);
            }

            DateTime t = now.Date.AddHours(hour).AddMinutes(minute);
            while (t <= now)
            {
                t = t.AddMinutes(intervalMinutes);
            }
            return t;
        }

        /// <summary>[평일 반복] 첫 울림: 오늘(평일이고 미래면) 그 시각, 아니면 다음 평일.</summary>
        private static DateTime NextWeekdaysTime(DateTime now, int hour, int minute)
        {
            DateTime t = now.Date.AddHours(hour).AddMinutes(minute);
            while (t <= now ||
                   t.DayOfWeek == DayOfWeek.Saturday ||
                   t.DayOfWeek == DayOfWeek.Sunday)
            {
                t = t.AddDays(1);
            }
            return t;
        }

        private static string RemoveMatch(string text, Match match)
        {
            return text.Remove(match.Index, match.Length);
        }

        /// <summary>
        /// 남은 문장을 '할 일' 텍스트로 최종 정리합니다.
        /// 남은 게 인식어("타이머"/"알람")뿐이면 → 울림 자체가 목적이므로 빈 내용으로.
        /// </summary>
        private static string FinishTask(string text)
        {
            string cleaned = Regex.Replace(text, @"\s+", " ").Trim();

            if (cleaned == "타이머" || cleaned == "알람")
            {
                return string.Empty;
            }

            return cleaned;
        }

        private static ParseResult Ok(
            DateTime time, string task, string original,
            RepeatType repeat = RepeatType.None,
            DayOfWeek? repeatWeekday = null,
            int? repeatMonthDay = null,
            int? intervalMinutes = null)
        {
            return new ParseResult
            {
                Success = true,
                Alarm = new AlarmModel
                {
                    TaskContent = task,
                    AlarmTime = time,
                    OriginalInput = original,
                    Repeat = repeat,
                    RepeatDayOfWeek = repeatWeekday,
                    RepeatDayOfMonth = repeatMonthDay,
                    RepeatIntervalMinutes = intervalMinutes
                }
            };
        }

        private static ParseResult Fail(string message)
        {
            return new ParseResult { Success = false, ErrorMessage = message };
        }
        /// <summary>
        /// [무시간 할일] 시간 없이 할일만 등록할 때 쓰는 알람을 만듭니다.
        /// 내부적으로는 '다가오는 정오 + 매일 반복'이라 스케줄러 수정 없이
        /// 매일 12:00 알림이 자동으로 성립합니다. (가벼움 유지)
        /// </summary>
        public AlarmModel CreateUntimedTodo(string input)
        {
            DateTime now = DateTime.Now;
            DateTime noon = now.Date.AddHours(12);
            if (noon <= now)
            {
                noon = noon.AddDays(1);   // 정오가 지났으면 내일 정오
            }

            return new AlarmModel
            {
                TaskContent = Regex.Replace(input, @"\s+", " ").Trim(),
                AlarmTime = noon,
                OriginalInput = input,
                Repeat = RepeatType.Daily,
                IsUntimed = true
            };
        }
    }
}