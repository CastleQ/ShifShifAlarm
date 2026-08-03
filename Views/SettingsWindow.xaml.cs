using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using EasyAlarm.Models;
using EasyAlarm.Services;
using EasyAlarm.Utils;

namespace EasyAlarm.Views
{
    /// <summary>
    /// 환경설정 창. 변경 즉시 저장되며(확인 버튼 없음),
    /// 저장 후 사령탑에 알려 앱 전체에 반영합니다.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly SettingsService _settingsService = new SettingsService();
        private readonly GoogleAuthService _auth = new GoogleAuthService();

        private AppSettings _settings;

        /// <summary>화면을 코드로 채우는 동안 이벤트가 저장을 중복 호출하지 않게 막는 깃발</summary>
        private bool _loading = true;

        public SettingsWindow()
        {
            InitializeComponent();

            _settings = _settingsService.Load();

            Loaded += async (s, e) =>
            {
                LoadToScreen();
                _loading = false;

                await RefreshConnectionAsync();
            };
        }

        /// <summary>설정값을 화면 요소에 채워 넣습니다.</summary>
        private void LoadToScreen()
        {
            SyncEnabledCheck.IsChecked = _settings.GoogleSyncEnabled;
            Clock24Check.IsChecked = _settings.Use24HourClock;
            UpdateClockHint();

            // 자동 시작: 개발 빌드에서는 조작 불가 (경로 오염 방지)
            if (StartupService.IsDevBuild)
            {
                StartupCheck.IsChecked = false;
                StartupCheck.IsEnabled = false;
                StartupHint.Text = "개발 빌드에서는 설정할 수 없습니다. 포장된 exe에서 사용해주세요.";
            }
            else
            {
                StartupCheck.IsChecked = StartupService.IsEnabled();
                StartupHint.Text = "PC를 켜면 쉽쉽알람이 자동으로 대기합니다.";
            }
        }

        // ─────────────── 구글 연동 ───────────────

        /// <summary>연결 상태를 확인하고, 연결되어 있으면 캘린더 목록을 불러옵니다.</summary>
        private async Task RefreshConnectionAsync()
        {
            if (!_auth.HasSecretFile)
            {
                StatusText.Text = "⚠ 구글 인증 파일(client_secret.json)이 없습니다.\n실행 파일과 같은 폴더에 두어야 합니다.";
                ConnectButton.IsEnabled = false;
                DisconnectButton.IsEnabled = false;
                CalendarCombo.IsEnabled = false;
                return;
            }

            if (!_auth.IsConnected)
            {
                StatusText.Text = "연결되지 않음 — [계정 연결]을 눌러 로그인해주세요.";
                ConnectButton.IsEnabled = true;
                DisconnectButton.IsEnabled = false;
                CalendarCombo.IsEnabled = false;
                CalendarCombo.ItemsSource = null;
                return;
            }

            ConnectButton.IsEnabled = false;
            DisconnectButton.IsEnabled = true;
            StatusText.Text = "연결 정보를 불러오는 중...";

            await LoadCalendarsAsync();
        }

        /// <summary>쓰기 가능한 캘린더 목록을 가져와 드롭다운을 채웁니다.</summary>
        private async Task LoadCalendarsAsync()
        {
            try
            {
                GoogleCalendarService calendar = new GoogleCalendarService(_auth);
                List<CalendarInfo> list = await calendar.GetWritableCalendarsAsync();

                _loading = true;

                CalendarCombo.ItemsSource = list;
                CalendarCombo.DisplayMemberPath = nameof(CalendarInfo.Name);
                CalendarCombo.IsEnabled = true;

                // 저장된 선택이 있으면 그것을, 없으면 기본 캘린더를 고른다
                CalendarInfo? selected = list.Find(c => c.Id == _settings.GoogleCalendarId)
                                        ?? list.Find(c => c.IsPrimary);

                CalendarCombo.SelectedItem = selected;

                _loading = false;

                // 기본 캘린더의 ID가 곧 계정 이메일이라 계정 확인용으로 쓸 수 있습니다
                CalendarInfo? primary = list.Find(c => c.IsPrimary);
                string account = primary != null ? primary.Id : "알 수 없음";

                StatusText.Text = $"✅ 연결됨 — {account}";

                // 첫 연결이라 저장된 캘린더가 없으면 지금 선택된 것을 기록해 둔다
                if (string.IsNullOrWhiteSpace(_settings.GoogleCalendarId) && selected != null)
                {
                    SaveCalendarSelection(selected);
                }
            }
            catch (Exception ex)
            {
                _loading = false;
                LogService.LogError("설정 창 캘린더 목록", ex);

                StatusText.Text = $"⚠ 캘린더 목록을 가져오지 못했습니다.\n{ex.Message}";
                CalendarCombo.IsEnabled = false;
            }
        }

        private void SyncEnabledCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            _settings.GoogleSyncEnabled = SyncEnabledCheck.IsChecked == true;
            Save();

            if (!_settings.GoogleSyncEnabled)
            {
                return;
            }

            if (!_auth.IsConnected)
            {
                MessageBox.Show(
                    this,
                    "동기화를 켰지만 구글 계정이 연결되지 않았습니다.\n\n[계정 연결]을 눌러 로그인해주세요.",
                    "구글 캘린더 연동",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // 켜는 순간, 그동안 못 올라간 알람들을 밀어 올린다
            ((App)Application.Current).RetrySync();
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectButton.IsEnabled = false;
            StatusText.Text = "브라우저에서 구글 로그인을 진행해주세요...";

            try
            {
                // 캘린더 목록 조회가 곧 로그인 절차입니다 (토큰이 없으면 브라우저가 열립니다)
                await LoadCalendarsAsync();

                DisconnectButton.IsEnabled = _auth.IsConnected;

                if (_auth.IsConnected && _settings.GoogleSyncEnabled)
                {
                    ((App)Application.Current).RetrySync();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("구글 계정 연결", ex);
                StatusText.Text = $"⚠ 연결에 실패했습니다.\n{ex.Message}";
                ConnectButton.IsEnabled = true;
            }
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult answer = MessageBox.Show(
                this,
                "구글 계정 연결을 해제할까요?\n\n" +
                "이미 캘린더에 등록된 일정은 그대로 남습니다.\n다시 연결하려면 로그인이 필요합니다.",
                "연결 해제",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            _auth.Disconnect();

            _settings.GoogleCalendarId = string.Empty;
            _settings.GoogleCalendarName = string.Empty;
            Save();

            await RefreshConnectionAsync();
        }

        private void CalendarCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            if (CalendarCombo.SelectedItem is CalendarInfo info)
            {
                SaveCalendarSelection(info);
            }
        }

        private void SaveCalendarSelection(CalendarInfo info)
        {
            _settings.GoogleCalendarId = info.Id;
            _settings.GoogleCalendarName = info.Name;
            Save();
        }

        // ─────────────── 일반 ───────────────

        private void StartupCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            bool desired = StartupCheck.IsChecked == true;

            if (!StartupService.SetEnabled(desired) && desired)
            {
                MessageBox.Show(
                    this,
                    "자동 시작을 등록하지 못했습니다.\n포장된 exe로 실행 중인지 확인해주세요.",
                    "자동 시작",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                _loading = true;
                StartupCheck.IsChecked = false;
                _loading = false;
                return;
            }

            _settings.StartWithWindows = desired;
            Save();
        }

        private void Clock24Check_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            _settings.Use24HourClock = Clock24Check.IsChecked == true;
            Save();
            UpdateClockHint();
        }

        private void UpdateClockHint()
        {
            bool use24 = Clock24Check.IsChecked == true;
            DateTime sample = DateTime.Today.AddHours(14).AddMinutes(30);

            ClockHint.Text = use24
                ? $"예: {sample:HH:mm}"
                : $"예: {sample.ToString("tt h:mm", new System.Globalization.CultureInfo("ko-KR"))}";
        }

        // ─────────────── 공통 ───────────────

        /// <summary>설정을 파일에 쓰고, 앱 전체에 반영하도록 사령탑에 알립니다.</summary>
        private void Save()
        {
            _settingsService.Save(_settings);
            ((App)Application.Current).ApplySettings(_settings);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}