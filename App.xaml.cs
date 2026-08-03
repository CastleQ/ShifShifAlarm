using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EasyAlarm.Models;
using EasyAlarm.Services;
using EasyAlarm.Views;
using H.NotifyIcon;
using Microsoft.Toolkit.Uwp.Notifications;
using NHotkey;
using NHotkey.Wpf;

namespace EasyAlarm;

/// <summary>
/// 프로그램의 사령탑.
/// 시작 시: ① 위젯 ② 단축키 ③ 알람 감시자 ④ 알림 버튼 우체통 ⑤ 트레이 아이콘 ⑥ Shift 2연타
/// 부가: 설정 보관, 구글 캘린더 동기화 배달부(실패분 자동 재시도)
/// </summary>
public partial class App : Application
{
    private DesktopWidgetWindow? _widget;
    private InputPopupWindow? _popup;
    private AlarmListWindow? _listWindow;
    private AlarmSchedulerService? _scheduler;
    private TaskbarIcon? _trayIcon;
    private StorageService? _storage;
    private ShiftDoubleTapService? _shiftTap;
    private SettingsService? _settings;
    private SyncService? _sync;
    private DispatcherTimer? _syncRetryTimer;

    /// <summary>
    /// [진단 장치] 예기치 못한 오류를 조용히 로그 파일에 기록합니다.
    /// 위치: %appdata%\EasyAlarm\error.log
    /// </summary>
    private void SetupCrashReporter()
    {
        DispatcherUnhandledException += (s, ex) =>
        {
            LogService.LogError("UI 스레드", ex.Exception);
            ex.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
        {
            if (ex.ExceptionObject is Exception exception)
            {
                LogService.LogError("백그라운드 스레드", exception);
            }
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SetupCrashReporter();
        _storage = new StorageService();
        _storage.CleanupOldCompleted();   // 30일 지난 완료 알람 자동 정리

        // 설정 + 구글 동기화 배달부 준비 (결과가 나오면 목록 창 갱신)
        _settings = new SettingsService();
        _sync = new SyncService(_storage, _settings);
        _sync.SyncStateChanged += () => Current.Dispatcher.Invoke(NotifyAlarmsChanged);

        // 지난번 통신 실패로 남은 일감 정리 (삭제분 + 업로드분)
        _sync.RetryPendingDeletes();
        _sync.RetryFailedUploads();

        // 5분마다 실패분 재시도 — 할 일이 없으면 즉시 반환되어 부담이 없습니다
        _syncRetryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        _syncRetryTimer.Tick += (s, args) =>
        {
            _sync?.RetryPendingDeletes();
            _sync?.RetryFailedUploads();
        };
        _syncRetryTimer.Start();

        // ① 상주 위젯 가동
        _widget = new DesktopWidgetWindow();
        _widget.Show();

        // ② 전역 단축키 등록
        HotkeyManager.Current.AddOrReplace(
            "ShowAlarmInput",
            Key.Z,
            ModifierKeys.Alt | ModifierKeys.Shift,
            OnHotkeyPressed);

        // ③ 알람 감시자 가동 (장부가 바뀌면 목록 창에 알리도록 종을 연결)
        _scheduler = new AlarmSchedulerService(_storage, new NotificationService());
        _scheduler.AlarmsChanged += NotifyAlarmsChanged;
        _scheduler.Start();

        // ④ 알림 버튼 우체통
        ToastNotificationManagerCompat.OnActivated += OnToastActivated;

        // ⑤ 트레이 아이콘 설치
        SetupTrayIcon();

        // ⑥ Shift 2연타 감지기 가동 (쉽쉽알람 시그니처 단축키)
        _shiftTap = new ShiftDoubleTapService();
        _shiftTap.DoubleTapDetected += () => Current.Dispatcher.Invoke(() => ShowPopup(string.Empty));
        _shiftTap.Start();
    }

    /// <summary>Alt+Shift+Z가 눌리면 호출됩니다.</summary>
    private void OnHotkeyPressed(object? sender, HotkeyEventArgs e)
    {
        ShowPopup(string.Empty);
        e.Handled = true;
    }

    /// <summary>입력 팝업을 띄웁니다. (이미 있으면 앞으로)</summary>
    private void ShowPopup(string prefillText)
    {
        if (_popup != null && _popup.IsLoaded)
        {
            _popup.Activate();
            return;
        }

        _popup = new InputPopupWindow(prefillText);
        _popup.Show();
    }

    /// <summary>
    /// 할일 목록 창을 띄웁니다. 단일 창 원칙:
    /// 이미 열려 있으면 새로 만들지 않고, 최신 데이터로 갱신해서 앞으로 데려옵니다.
    /// </summary>
    public void ShowAlarmList()
    {
        if (_listWindow != null && _listWindow.IsLoaded)
        {
            _listWindow.Refresh();
            BringToFront(_listWindow);
            return;
        }

        _listWindow = new AlarmListWindow();
        _listWindow.Show();
        BringToFront(_listWindow);
    }

    /// <summary>
    /// 장부(알람 목록)가 바뀌었음을 알리는 방송 창구.
    /// 목록 창이 열려 있을 때만 다시 그리도록 지시합니다.
    /// </summary>
    public void NotifyAlarmsChanged()
    {
        if (_listWindow != null && _listWindow.IsLoaded)
        {
            _listWindow.Refresh();
        }
    }

    /// <summary>
    /// 새로 등록된 알람을 구글 캘린더로 보냅니다.
    /// 즉시 반환되며 실제 전송은 백그라운드에서 처리됩니다(입력 속도 보호).
    /// </summary>
    public void SyncNewAlarm(AlarmModel alarm)
    {
        _sync?.QueueUpload(alarm);
    }

    /// <summary>
    /// 삭제된 알람의 구글 일정도 지웁니다.
    /// ⚠️ 로컬에서 지우기 '전에' 호출해야 합니다 (일정 번호가 필요하므로).
    /// </summary>
    public void SyncDeletedAlarm(AlarmModel alarm)
    {
        _sync?.QueueDelete(alarm);
    }

    /// <summary>여러 알람의 구글 일정을 한 번에 지웁니다. (전체삭제용)</summary>
    public void SyncDeletedAlarms(System.Collections.Generic.IEnumerable<AlarmModel> alarms)
    {
        _sync?.QueueDeleteMany(alarms);
    }

    /// <summary>
    /// 창을 확실하게 화면 맨 앞으로 끌어옵니다.
    /// '한 프레임만 최상단으로 올린 뒤 즉시 해제'하는 표준 기법.
    /// </summary>
    private static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    /// <summary>
    /// Toast 알림에서 버튼이 클릭되면 호출되는 우체통.
    /// ⚠️ 별도 스레드에서 배달되므로 UI 작업은 Dispatcher를 통해 넘깁니다.
    /// </summary>
    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        ToastArguments args = ToastArguments.Parse(e.Argument);

        string action = args.TryGetValue("action", out string? value) ? value : string.Empty;

        if (action == "snooze")
        {
            string prefill = string.Empty;

            if (args.TryGetValue("originalInput", out string? original))
            {
                prefill = original;
            }

            Current.Dispatcher.Invoke(() => ShowPopup(prefill));
        }
        // "confirm"은 알림만 닫히면 되므로 처리 불필요
    }

    /// <summary>트레이 아이콘과 우클릭 메뉴를 구성합니다.</summary>
    private void SetupTrayIcon()
    {
        ContextMenu menu = new ContextMenu();

        MenuItem newAlarmItem = new MenuItem { Header = "새 알람 (Alt+Shift+Z)" };
        newAlarmItem.Click += (s, e) => ShowPopup(string.Empty);

        MenuItem listItem = new MenuItem { Header = "📋 할일 목록" };
        listItem.Click += (s, e) => ShowAlarmList();

        // ── 구글 캘린더 동기화 스위치 (정식 설정 창으로 이전 예정) ──
        AppSettings current = _settings?.Load() ?? new AppSettings();

        MenuItem syncToggle = new MenuItem
        {
            Header = "☁ 구글 캘린더 동기화",
            IsCheckable = true,
            IsChecked = current.GoogleSyncEnabled
        };
        syncToggle.Click += (s, e) =>
        {
            if (_settings == null)
            {
                return;
            }

            AppSettings settings = _settings.Load();
            settings.GoogleSyncEnabled = syncToggle.IsChecked;
            _settings.Save(settings);

            if (!settings.GoogleSyncEnabled)
            {
                return;
            }

            if (_sync != null && !_sync.IsReady())
            {
                MessageBox.Show(
                    "동기화를 켰지만 구글 로그인이 아직 안 되어 있습니다.\n\n" +
                    "'☁ 구글 연결 테스트'를 한 번 실행해 로그인해주세요.",
                    "구글 캘린더 동기화");
                return;
            }

            // 켜는 순간, 그동안 못 올라간 알람들을 바로 밀어 올린다
            _sync?.RetryFailedUploads();
        };

        // ── 구글 연동 임시 진단 메뉴 (정식 설정 창 완성 후 제거 예정) ──
        MenuItem googleTestItem = new MenuItem { Header = "☁ 구글 연결 테스트 (임시)" };
        googleTestItem.Click += async (s, e) => await TestGoogleConnectionAsync();

        MenuItem googleEventItem = new MenuItem { Header = "☁ 테스트 일정 올리기 (10분 뒤, 임시)" };
        googleEventItem.Click += async (s, e) => await TestCreateEventAsync();

        MenuItem exitItem = new MenuItem { Header = "종료" };
        exitItem.Click += (s, e) => ExitApplication();

        menu.Items.Add(newAlarmItem);
        menu.Items.Add(listItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(syncToggle);
        menu.Items.Add(googleTestItem);
        menu.Items.Add(googleEventItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        // 아이콘 이미지 불러오기 (실패 시 기본 ℹ️ 아이콘으로 대체하는 안전장치)
        System.Drawing.Icon trayIconImage;
        try
        {
            trayIconImage = new System.Drawing.Icon(
                System.IO.Path.Combine(AppContext.BaseDirectory, "EasyAlarm.ico"));
        }
        catch (Exception ex)
        {
            LogService.LogError("트레이 아이콘 로드", ex);
            trayIconImage = System.Drawing.SystemIcons.Information;
        }

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "쉽쉽알람 — 우클릭으로 메뉴",
            ContextMenu = menu,
            Icon = trayIconImage
        };

        // 아이콘 더블클릭 → 새 알람 입력창
        _trayIcon.TrayMouseDoubleClick += (s, e) => ShowPopup(string.Empty);

        // 트레이에 '실제로 등록'하는 명령 (H.NotifyIcon 2.x 필수)
        _trayIcon.ForceCreate();
    }

    /// <summary>우아한 종료: 자원을 정리하고 프로그램을 끝냅니다.</summary>
    private void ExitApplication()
    {
        _syncRetryTimer?.Stop();
        ToastNotificationManagerCompat.Uninstall();
        _trayIcon?.Dispose();
        _shiftTap?.Dispose();                          // 키보드 감지기 철거
        Shutdown();
    }

    /// <summary>
    /// [임시 진단] 구글 로그인 + 캘린더 목록 조회 테스트.
    /// 정식 설정 창 완성 후 이 메서드와 트레이 메뉴 항목은 제거 예정.
    /// </summary>
    private async System.Threading.Tasks.Task TestGoogleConnectionAsync()
    {
        try
        {
            GoogleAuthService auth = new GoogleAuthService();

            if (!auth.HasSecretFile)
            {
                MessageBox.Show(
                    "구글 인증 파일(client_secret.json)을 찾을 수 없습니다.\n\n" +
                    "실행 파일과 같은 폴더에 있어야 합니다.",
                    "구글 연결 테스트");
                return;
            }

            GoogleCalendarService calendars = new GoogleCalendarService(auth);
            System.Collections.Generic.List<CalendarInfo> list = await calendars.GetWritableCalendarsAsync();

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"✅ 연결 성공! 쓰기 가능한 캘린더 {list.Count}개");
            sb.AppendLine();

            foreach (CalendarInfo c in list)
            {
                sb.AppendLine($"{(c.IsPrimary ? "★ " : "· ")}{c.Name}");
                sb.AppendLine($"   ({c.Id})");
            }

            MessageBox.Show(sb.ToString(), "구글 연결 테스트");
        }
        catch (Exception ex)
        {
            LogService.LogError("구글 연결 테스트", ex);
            MessageBox.Show(
                $"연결에 실패했습니다.\n\n{ex.GetType().Name}\n{ex.Message}",
                "구글 연결 테스트");
        }
    }

    /// <summary>
    /// [임시 진단] 10분 뒤 시각으로 구글 캘린더에 테스트 일정 1건을 만듭니다.
    /// 정식 연동 완성 후 이 메서드와 메뉴 항목은 제거 예정.
    /// </summary>
    private async System.Threading.Tasks.Task TestCreateEventAsync()
    {
        try
        {
            GoogleAuthService auth = new GoogleAuthService();
            GoogleCalendarService calendar = new GoogleCalendarService(auth);

            DateTime when = DateTime.Now.AddMinutes(10);

            AlarmModel test = new AlarmModel
            {
                TaskContent = "구글 연동 테스트",
                AlarmTime = when,
                OriginalInput = "10분 뒤 구글 연동 테스트"
            };

            string eventId = await calendar.CreateEventAsync(test, "primary");

            MessageBox.Show(
                $"✅ 일정 생성 성공!\n\n{when:MM/dd (ddd) HH:mm}\n구글 연동 테스트\n\n" +
                $"일정 번호: {eventId}\n\n구글 캘린더(웹·모바일)에서 확인해보세요.",
                "테스트 일정 올리기");
        }
        catch (Exception ex)
        {
            LogService.LogError("테스트 일정 생성", ex);
            MessageBox.Show(
                $"일정 생성에 실패했습니다.\n\n{ex.GetType().Name}\n{ex.Message}",
                "테스트 일정 올리기");
        }
    }
}