using System;
using System.Text.RegularExpressions;
using EasyAlarm.Models;

namespace EasyAlarm.Services
{
    public class ParseResult
    {
        public bool Success { get; init; }
        public string ErrorMessage { get; init; } = string.Empty;
        public AlarmModel? Alarm { get; init; }
        public bool NoTimeDetected { get; init; }
    }

    /// <summary>
    /// 자연어 문장에서 [시간]과 [할 일]을 분리하는 두뇌.
    /// (숫자/한글 시간, 시간대 힌트, 시간 범위("~부터~까지"), 이번주/다음주,
    ///  매일/매주/매월/간격/평일 반복, 타이머/알람 인식어, 정오/자정, 있다가/이따)
    /// </summary>
    public class AlarmParserService
    {
        private const string Eul = @"(?:에(?![가-힣]))?";

        private static readonly Regex TimerHour = new(
            @"(\d{1,3})\s*시간\s*(?:(\d{1,2})\s*분\s*)?(?:타이머|알람)(?![가-힣])",
            RegexOptions.Compiled);

        private static readonly Regex TimerMinute = new(
            @"(\d{1,3})\s*분\s*(?:타이머|알람)(?![가-힣])",
            RegexOptions.Compiled);

        private static readonly Regex RelativeHour = new(
            @"(\d{1,3})\s*시간\s*(?:(\d{1,2})\s*분\s*)?(?:뒤|후|있다가)\s*" + Eul,
            RegexOptions.Compiled);

        private static readonly Regex RelativeMinute = new(
            @"(\d{1,3})\s*분\s*(?:뒤|후|있다가)\s*" + Eul,
            RegexOptions.Compiled);

        private static readonly Regex RelativeDayNumber = new(
            @"(\d{1,3})\s*일\s*(?:뒤|후|있다가)\s*" + Eul,
            RegexOptions.Compiled);

        private static readonly Regex RelativeDayWord = new(
            @"(내일모레|내일|모레|글피|사흘|나흘|닷새|일주일|(?<![가-힣])낼(?![가-힣]))\s*(?:뒤|후)?\s*" + Eul,
            RegexOptions.Compiled);

        private static readonly Regex ExplicitDate = new(
            @"(\d{1,2})\s*[월/.]\s*(\d{1,2})\s*일?\s*" + Eul,
            RegexOptions.Compiled);

        private static readonly Regex Weekday = new(
            @"(?:(다다음\s*주|다음\s*주|이번\s*주)\s*)?(월|화|수|목|금|토|일)요일\s*" + Eul,
            RegexOptions.Compiled);

        private static readonly Regex RepeatInterval = new(
            @"(\d{1,3})\s*(분|시간|일)\s*마다\s*" + Eul,
            RegexOptions.Compiled);

        private static readonly Regex RepeatWeekdays = new(
            @"평일\s*(?:매일)?\s*",
            RegexOptions.Compiled);

        private static readonly Regex RepeatWeekly = new(
            @"매주\s*(월|화|수|목|금|토|일)요일\s*(?:마다)?\s*" + Eul,
            RegexOptions.Compiled);

        private static readonly Regex WeekdayMada = new(
            @"(월|화|수|목|금|토|일)요일\s*마다\s*" + Eul,
            RegexOptions.Compiled);

        private static readonly Regex RepeatMonthly = new(
            @"매(?:월|달)\s*(\d{1,2})\s*일\s*" + Eul,
            RegexOptions.Compiled);

        private static readonly Regex RepeatEveryClock = new(
            @"매\s*(?=\d{1,2}\s*시|(?:새벽|아침|오전|낮|오후|저녁|밤))",
            RegexOptions.Compiled);

        private static readonly Regex RepeatDaily = new(
            @"매일\s*",
            RegexOptions.Compiled);

        private static readonly Regex NoonMidnight = new(
            @"(정오|자정)\s*" + Eul,
            RegexOptions.Compiled);

        // "14시부터 16시까지", "14시~16시", "14:00-16:00", "오후 2시부터 4시까지",
        // "다섯시부터 일곱시까지" 등 시간 범위 통째로 인식.
        // 앞뒤에 반드시 "시" 또는 ":" 표기가 있어야 매치되므로 전화번호("010-1234") 등은 안전.
        private static readonly Regex ClockRange = new(
            @"(?:(?<hint1>새벽|아침|오전|낮|오후|저녁|밤)\s*)?" +
            @"(?:" +
                @"(?<colonH1>\d{1,2})\s*:\s*(?<colonM1>\d{1,2})" +
                @"|(?<digH1>\d{1,2})\s*시(?:\s*(?<digM1>\d{1,2})\s*분|\s*(?<digHalf1>반)|\s*정각)?" +
                @"|(?<hanW1>열두|열한|열|아홉|여덟|일곱|여섯|다섯|네|세|두|한)\s*시(?:\s*(?<hanM1>\d{1,2})\s*분|\s*(?<hanHalf1>반)|\s*정각)?" +
            @")\s*" +
            @"(?<startMarker>부터|에서)?\s*(?<sep>~|-|–|—|ㅡ|to)?\s*" +
            @"(?:(?<hint2>새벽|아침|오전|낮|오후|저녁|밤)\s*)?" +
            @"(?:" +
                @"(?<colonH2>\d{1,2})\s*:\s*(?<colonM2>\d{1,2})" +
                @"|(?<digH2>\d{1,2})\s*시(?:\s*(?<digM2>\d{1,2})\s*분|\s*(?<digHalf2>반)|\s*정각)?" +
                @"|(?<hanW2>열두|열한|열|아홉|여덟|일곱|여섯|다섯|네|세|두|한)\s*시(?:\s*(?<hanM2>\d{1,2})\s*분|\s*(?<hanHalf2>반)|\s*정각)?" +
            @")\s*" +
            @"(?<endMarker>까지|끝|종료)?",
            RegexOptions.Compiled);

        private static readonly Regex ClockDigit = new(
            @"(?:(새벽|아침|오전|낮|오후|저녁|밤)\s*)?(\d{1,2})\s*시(?:\s*(\d{1,2})\s*분|\s*(반)|\s*(정각))?\s*" + Eul,
            RegexOptions.Compiled);

        private static readonly Regex ClockColon = new(
            @"(?:(새벽|아침|오전|낮|오후|저녁|밤)\s*)?(\d{1,2})\s*:\s*(\d{1,2})\s*" + Eul,
            RegexOptions.Compiled);

        private static readonly Regex ClockHangul = new(
            @"(?:(새벽|아침|오전|낮|오후|저녁|밤)\s*)?(열두|열한|열|아홉|여덟|일곱|여섯|다섯|네|세|두|한)\s*시(?:\s*(\d{1,2})\s*분|\s*(반)|\s*(정각))?\s*" + Eul,
            RegexOptions.Compiled);

        private static readonly Regex LaterWord = new(
            @"(?<![가-힣])이따가?(?![가-힣])",
            RegexOptions.Compiled);

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

        private static readonly Regex MalformedTime = new(
            @"\d{1,2}시\d{1,2}(?!\d)(?!\s*분)" +
            @"|\d[ㄱ-ㅎㅏ-ㅣ]" +
            @"|\d{1,2}\s*[ㄱ-ㅎㅏ-ㅣ]\s*분",
            RegexOptions.Compiled);

        public ParseResult Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return Fail("내용을 입력해주세요");
            }

            if (MalformedTime.IsMatch(input))
            {
                return Fail("시간 표기를 확인해주세요");
            }

            string working = input.Trim();
            DateTime now = DateTime.Now;

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
                    _ => n * 1440
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
                repeat = RepeatType.Daily;
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

            // ── 4단계: 시각(몇 시 몇 분) 찾기 — 범위 우선 시도 ──
            int hour = 0;
            int minute = 0;
            bool hasHint = false;
            bool isHangul = false;
            bool clockFound = false;
            int? rangeEndHour = null;
            int? rangeEndMinute = null;

            int? ComputeDuration(DateTime start)
            {
                if (!rangeEndHour.HasValue)
                {
                    return null;
                }
                DateTime end = start.Date.AddHours(rangeEndHour.Value).AddMinutes(rangeEndMinute ?? 0);
                if (end <= start)
                {
                    end = end.AddDays(1);   // 자정 넘는 구간("23시부터 1시까지") 자동 익일 처리
                }
                return (int)(end - start).TotalMinutes;
            }

            m = ClockRange.Match(working);
            bool rangeValid = m.Success &&
                (m.Groups["startMarker"].Success || m.Groups["sep"].Success || m.Groups["endMarker"].Success);

            if (rangeValid &&
                TryReadClockSide(m, "1", out int h1, out int min1, out bool hangul1, out string hint1) &&
                TryReadClockSide(m, "2", out int h2, out int min2, out bool hangul2, out string hint2))
            {
                string effectiveHint1 = !string.IsNullOrEmpty(hint1) ? hint1 : hint2;
                string effectiveHint2 = !string.IsNullOrEmpty(hint2) ? hint2 : hint1;

                if (!string.IsNullOrEmpty(effectiveHint1) || !string.IsNullOrEmpty(effectiveHint2))
                {
                    h1 = ApplyHint(h1, effectiveHint1);
                    h2 = ApplyHint(h2, effectiveHint2);
                }
                else if (hangul1 || hangul2)
                {
                    // 힌트가 전혀 없는 한글 구간: 시작은 '가장 가까운 미래'로, 끝은 시작과 같은 오전/오후로 맞춘다
                    DateTime startGuess = NearestFuture(now, h1, min1);
                    bool startIsPm = startGuess.Hour >= 12;
                    h1 = startGuess.Hour;
                    min1 = startGuess.Minute;
                    h2 = (h2 % 12) + (startIsPm ? 12 : 0);
                }

                if (h1 > 23 || min1 > 59 || h2 > 23 || min2 > 59)
                {
                    return Fail("시간 형식을 확인해주세요");
                }

                hour = h1;
                minute = min1;
                rangeEndHour = h2;
                rangeEndMinute = min2;
                hasHint = true;   // 이미 완전히 해석했으므로 하위 단계의 재해석(NearestFuture 재적용)을 막는다
                isHangul = false;
                clockFound = true;

                working = RemoveMatch(working, m);
            }
            else
            {
                m = NoonMidnight.Match(working);
                if (m.Success)
                {
                    hour = m.Groups[1].Value == "정오" ? 12 : 0;
                    minute = 0;
                    hasHint = true;
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
            }

            if (!clockFound && repeat != RepeatType.Interval)
            {
                if (TimeTrace.IsMatch(input))
                {
                    return Fail("시간을 입력해주세요");
                }

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
                    _ => NextWeekdaysTime(now, hour, minute)
                };

                return Ok(first, task, input, repeat,
                    repeat == RepeatType.Weekly ? repeatWeekday : null,
                    repeat == RepeatType.Monthly ? repeatMonthDay : null,
                    repeat == RepeatType.Interval ? intervalMinutes : null,
                    ComputeDuration(first));
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

                    // 시간 범위 입력이고, 종료 시각이 아직 오늘 안에 남아 있으면
                    // '이미 진행 중인 구간'이므로 내일로 밀지 않고 오늘로 유지한다.
                    // (예: 15:40에 "11시부터 20시까지 코어타임" → 오늘 11:00~20:00)
                    bool rangeStillOngoing =
                        rangeEndHour.HasValue &&
                        alarmTime.Date
                            .AddHours(rangeEndHour.Value)
                            .AddMinutes(rangeEndMinute ?? 0) > now;

                    if (!rangeStillOngoing)
                    {
                        alarmTime = alarmTime.AddDays(1);
                    }
                }
            }
            return Ok(alarmTime, task, input, duration: ComputeDuration(alarmTime));
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

        private static int ReadMinute(Match m)
        {
            return m.Groups[3].Success ? int.Parse(m.Groups[3].Value)
                 : m.Groups[4].Success ? 30
                 : 0;
        }

        /// <summary>범위 정규식(ClockRange)의 한쪽(1 또는 2)에서 시·분을 읽어냅니다.</summary>
        private static bool TryReadClockSide(Match m, string side, out int hour, out int minute, out bool isHangul, out string hint)
        {
            hint = m.Groups["hint" + side].Success ? m.Groups["hint" + side].Value : string.Empty;
            isHangul = false;

            if (m.Groups["colonH" + side].Success)
            {
                hour = int.Parse(m.Groups["colonH" + side].Value);
                minute = int.Parse(m.Groups["colonM" + side].Value);
                return true;
            }

            if (m.Groups["digH" + side].Success)
            {
                hour = int.Parse(m.Groups["digH" + side].Value);
                minute = m.Groups["digM" + side].Success ? int.Parse(m.Groups["digM" + side].Value)
                       : m.Groups["digHalf" + side].Success ? 30
                       : 0;
                return true;
            }

            if (m.Groups["hanW" + side].Success)
            {
                hour = ToHangulHour(m.Groups["hanW" + side].Value);
                minute = m.Groups["hanM" + side].Success ? int.Parse(m.Groups["hanM" + side].Value)
                       : m.Groups["hanHalf" + side].Success ? 30
                       : 0;
                isHangul = true;
                return true;
            }

            hour = 0;
            minute = 0;
            return false;
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

        private static string RemoveAndClean(string text, Match match)
        {
            return CleanTask(RemoveMatch(text, match));
        }

        private static string CleanTask(string text)
        {
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

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
            int? intervalMinutes = null,
            int? duration = null)
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
                    RepeatIntervalMinutes = intervalMinutes,
                    DurationMinutes = duration
                }
            };
        }

        private static ParseResult Fail(string message)
        {
            return new ParseResult { Success = false, ErrorMessage = message };
        }

        /// <summary>
        /// [무시간 할일] 시간 없이 할일만 등록할 때 쓰는 알람을 만듭니다.
        /// </summary>
        public AlarmModel CreateUntimedTodo(string input)
        {
            DateTime now = DateTime.Now;
            DateTime noon = now.Date.AddHours(12);
            if (noon <= now)
            {
                noon = noon.AddDays(1);
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