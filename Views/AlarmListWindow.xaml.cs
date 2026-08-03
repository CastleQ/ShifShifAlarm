using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EasyAlarm.Models;
using EasyAlarm.Services;
using EasyAlarm.Utils;

namespace EasyAlarm.Views
{
    /// <summary>
    /// 등록된 알람을 한눈에 보는 목록 창.
    /// 위 = 예정(시간 알람 임박순 → 무시간 할일) / 아래 = 지난 알람(최신순).
    /// 알람 추가·울림·동기화 결과가 생기면 사령탑의 지시로 실시간 갱신됩니다.
    /// </summary>
    public partial class AlarmListWindow : Window
    {
        private readonly StorageService _storage = new StorageService();

        // 직전에 그린 장부의 '지문'. 내용이 그대로면 다시 그리지 않아 화면 깜빡임을 막습니다.
        private string _lastSignature = string.Empty;
        private bool _rendered;

        // 색상 팔레트
        private static readonly Brush UpcomingTime = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0));
        private static readonly Brush UntimedTime = new SolidColorBrush(Color.FromRgb(0x8D, 0x6E, 0x63));
        private static readonly Brush CompletedTime = new SolidColorBrush(Color.FromRgb(0x75, 0x75, 0x75));
        private static readonly Brush TaskBlack = new SolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21));
        private static readonly Brush TaskGray = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
        private static readonly Brush CardWhite = new SolidColorBrush(Colors.White);
        private static readonly Brush EmptyGray = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));

        public AlarmListWindow()
        {
            InitializeComponent();
            Refresh();
        }

        /// <summary>
        /// 장부를 다시 읽어 화면을 새로 그립니다.
        /// 내용이 바뀌지 않았으면 아무 일도 하지 않고, 다시 그릴 때는 스크롤 위치를 보존합니다.
        /// </summary>
        public void Refresh()
        {
            List<AlarmModel> alarms = _storage.LoadAlarms();

            // 변경 없음 → 재그리기 생략 (읽던 위치와 화면이 그대로 유지됨)
            string signature = BuildSignature(alarms);
            if (_rendered && signature == _lastSignature)
            {
                return;
            }
            _lastSignature = signature;
            _rendered = true;

            Render(alarms);
        }

        /// <summary>
        /// 데이터가 같아도 무조건 다시 그립니다.
        /// (시각 표기 형식 변경처럼 '데이터 밖의 이유'로 화면이 달라져야 할 때 사용)
        /// </summary>
        public void ForceRefresh()
        {
            _rendered = false;
            Refresh();
        }

        private void Render(List<AlarmModel> alarms)
        {
            // 다시 그리면 스크롤이 맨 위로 튕기므로, 현재 위치를 미리 기억해둔다
            double upcomingOffset = UpcomingScroll.VerticalOffset;
            double completedOffset = CompletedScroll.VerticalOffset;

            // 예정: 시간이 정해진 알람이 위(임박순), 무시간 할일은 항상 그 아래
            List<AlarmModel> upcoming = alarms
                .Where(a => !a.IsCompleted)
                .OrderBy(a => a.IsUntimed ? 1 : 0)
                .ThenBy(a => a.AlarmTime)
                .ToList();

            List<AlarmModel> completed = alarms
                .Where(a => a.IsCompleted)
                .OrderByDescending(a => a.CompletedAt ?? DateTime.MinValue)
                .ToList();

            UpcomingHeader.Text = $"예정된 알람 ({upcoming.Count})";
            CompletedHeader.Text = $"지난 알람 ({completed.Count})";

            RenderSection(UpcomingList, upcoming, isCompletedSection: false);
            RenderSection(CompletedList, completed, isCompletedSection: true);

            // 목록이 새로 배치된 다음(Loaded 단계)에 기억해둔 위치로 되돌린다
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpcomingScroll.ScrollToVerticalOffset(upcomingOffset);
                CompletedScroll.ScrollToVerticalOffset(completedOffset);
            }), DispatcherPriority.Loaded);
        }

        /// <summary>
        /// 장부의 '지문'을 만듭니다. 항목의 고유번호·완료여부·시각·내용·동기화 상태가
        /// 모두 같으면 같은 지문 → 다시 그리지 않습니다.
        /// </summary>
        private static string BuildSignature(List<AlarmModel> alarms)
        {
            StringBuilder sb = new StringBuilder();

            foreach (AlarmModel alarm in alarms)
            {
                sb.Append(alarm.Id).Append('|')
                  .Append(alarm.IsCompleted ? '1' : '0').Append('|')
                  .Append(alarm.AlarmTime.Ticks).Append('|')
                  .Append((int)alarm.Sync).Append('|')
                  .Append(alarm.TaskContent).Append(';');
            }

            return sb.ToString();
        }

        /// <summary>한 구역(예정 또는 지난)의 목록을 그립니다.</summary>
        private void RenderSection(StackPanel panel, List<AlarmModel> alarms, bool isCompletedSection)
        {
            panel.Children.Clear();

            if (alarms.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = isCompletedSection ? "지난 알람이 없습니다" : "예정된 알람이 없습니다",
                    FontSize = 13,
                    Foreground = EmptyGray,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0)
                });
                return;
            }

            foreach (AlarmModel alarm in alarms)
            {
                panel.Children.Add(BuildRow(alarm, isCompletedSection));
            }
        }

        /// <summary>
        /// 복사(블록지정) 가능한 텍스트 줄을 만듭니다.
        /// 겉모습은 일반 글자와 같지만, 속은 '읽기전용 입력칸'이라 드래그 선택과 Ctrl+C가 됩니다.
        /// </summary>
        private static TextBox MakeSelectableText(string text, double fontSize, Brush color, FontWeight weight)
        {
            return new TextBox
            {
                Text = text,
                FontSize = fontSize,
                Foreground = color,
                FontWeight = weight,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                TextWrapping = TextWrapping.Wrap
            };
        }

        /// <summary>알람 1개 = 흰색 카드 한 줄 (시간/내용 + 삭제 버튼)</summary>
        private Border BuildRow(AlarmModel alarm, bool isCompletedSection)
        {
            // 첫 줄 표기: 무시간 할일은 시각 대신 "알람없음" 배지
            string timeText;
            Brush timeColor;

            if (alarm.IsUntimed)
            {
                timeText = "🔔 알람없음 · 매일 정오 알림";
                timeColor = isCompletedSection ? CompletedTime : UntimedTime;
            }
            else
            {
                timeText = TimeFormatter.Format(alarm.AlarmTime);

                if (alarm.Repeat != RepeatType.None)
                {
                    string repeatDesc = alarm.Repeat switch
                    {
                        RepeatType.Daily => "매일",
                        RepeatType.Weekly => $"매주 {ToKoreanDay(alarm.RepeatDayOfWeek)}",
                        RepeatType.Monthly => $"매월 {alarm.RepeatDayOfMonth}일",
                        RepeatType.Interval => $"매 {FormatInterval(alarm.RepeatIntervalMinutes ?? 0)}",
                        RepeatType.Weekdays => "평일 매일",
                        _ => "반복"
                    };
                    timeText = $"🔁 {repeatDesc} · 다음 {timeText}";
                }

                timeColor = isCompletedSection ? CompletedTime : UpcomingTime;
            }

            // 구글 동기화 상태 표식
            timeText += SyncBadge(alarm);

            TextBox timeBlock = MakeSelectableText(timeText, 12, timeColor, FontWeights.SemiBold);

            TextBox taskBlock = MakeSelectableText(
                string.IsNullOrWhiteSpace(alarm.TaskContent) ? "(알람)" : alarm.TaskContent,
                14,
                isCompletedSection ? TaskGray : TaskBlack,
                FontWeights.Normal);
            taskBlock.Margin = new Thickness(0, 3, 0, 0);

            StackPanel textStack = new StackPanel();
            textStack.Children.Add(timeBlock);
            textStack.Children.Add(taskBlock);

            Button deleteButton = new Button
            {
                Content = "삭제",
                Style = (Style)FindResource("DeleteButtonStyle"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            deleteButton.Click += (s, e) =>
            {
                // 예정된 알람은 아직 안 울린 '약속'이므로, 실수 방지를 위해
                // 무엇을 지우는지 보여주며 한 번 확인한다. (지난 알람은 기록일 뿐이라 즉시 삭제)
                if (!isCompletedSection)
                {
                    string when = alarm.IsUntimed
                        ? "알람없음 · 매일 정오 알림"
                        : TimeFormatter.Format(alarm.AlarmTime);

                    string what = string.IsNullOrWhiteSpace(alarm.TaskContent)
                        ? "(알람)"
                        : alarm.TaskContent;

                    string title = alarm.IsUntimed ? "할일 삭제" : "알람 삭제";
                    string question = alarm.IsUntimed
                        ? "이 할일을 삭제할까요?"
                        : "이 예정된 알람을 삭제할까요?";

                    MessageBoxResult answer = MessageBox.Show(
                        this,
                        $"{question}\n\n{when}\n{what}",
                        title,
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (answer != MessageBoxResult.Yes)
                    {
                        return;   // 취소 → 아무 일도 없음
                    }
                }

                // ⚠️ 순서 중요: 로컬에서 지우기 전에 구글 삭제를 예약해야 일정 번호가 살아있다
                ((App)Application.Current).SyncDeletedAlarm(alarm);

                _storage.DeleteAlarm(alarm.Id);
                Refresh();
            };

            Grid rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(textStack, 0);
            Grid.SetColumn(deleteButton, 1);
            rowGrid.Children.Add(textStack);
            rowGrid.Children.Add(deleteButton);

            return new Border
            {
                Background = CardWhite,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 6, 0, 0),
                Child = rowGrid
            };
        }

        /// <summary>지난 알람 전체삭제 (확인 다이얼로그 1회 + 구글 일정 동시 삭제)</summary>
        private void DeleteAllButton_Click(object sender, RoutedEventArgs e)
        {
            List<AlarmModel> completed = _storage.LoadAlarms().Where(a => a.IsCompleted).ToList();

            if (completed.Count == 0)
            {
                return;
            }

            MessageBoxResult answer = MessageBox.Show(
                this,
                $"지난 알람 {completed.Count}개를 모두 삭제할까요?\n\n" +
                "구글 캘린더에 등록된 일정도 함께 삭제됩니다.",
                "전체 삭제",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer == MessageBoxResult.Yes)
            {
                // 로컬에서 지우기 전에 구글 삭제 예약 (일정 번호 확보)
                ((App)Application.Current).SyncDeletedAlarms(completed);

                _storage.DeleteAllCompleted();
                Refresh();
            }
        }

        /// <summary>설정 창 열기</summary>
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ((App)Application.Current).ShowSettings();
        }

        /// <summary>DayOfWeek를 한글 요일로 번역합니다.</summary>
        private static string ToKoreanDay(DayOfWeek? day)
        {
            return day switch
            {
                DayOfWeek.Monday => "월요일",
                DayOfWeek.Tuesday => "화요일",
                DayOfWeek.Wednesday => "수요일",
                DayOfWeek.Thursday => "목요일",
                DayOfWeek.Friday => "금요일",
                DayOfWeek.Saturday => "토요일",
                DayOfWeek.Sunday => "일요일",
                _ => ""
            };
        }

        /// <summary>분 단위 간격을 읽기 좋은 한글로 바꿉니다. (4320→"3일", 120→"2시간", 30→"30분")</summary>
        private static string FormatInterval(int minutes)
        {
            if (minutes >= 1440 && minutes % 1440 == 0)
            {
                return $"{minutes / 1440}일";
            }
            if (minutes >= 60 && minutes % 60 == 0)
            {
                return $"{minutes / 60}시간";
            }
            return $"{minutes}분";
        }

        /// <summary>
        /// 구글 캘린더 동기화 상태를 짧은 표식으로 바꿉니다.
        /// ☁ 완료 / ⏳ 대기 / ⚠ 실패 / 💻 PC 전용(구글 미지원 형식)
        /// </summary>
        private static string SyncBadge(AlarmModel alarm)
        {
            if (alarm.Sync == SyncState.Skipped && !GoogleCalendarService.IsSyncable(alarm))
            {
                return "  💻 PC 전용";
            }

            return alarm.Sync switch
            {
                SyncState.Synced => "  ☁",
                SyncState.Pending => "  ⏳",
                SyncState.Failed => "  ⚠",
                _ => string.Empty
            };
        }
    }
}