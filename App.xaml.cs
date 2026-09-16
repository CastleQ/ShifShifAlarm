using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EasyAlarm.Models;
using EasyAlarm.Services;
using EasyAlarm.Utils;
using EasyAlarm.Views;
using H.NotifyIcon;
using Microsoft.Toolkit.Uwp.Notifications;
using NHotkey;
using NHotkey.Wpf;

namespace EasyAlarm;

/// <summary>
/// 프로그램의 사령탑.
/// 시작 시: ① 설정·동기화 준비 ② 위젯 ③ 단축키 ④ 알람 감시자 ⑤ 알림 우체통 ⑥ 트레이 ⑦ Shift 2연타
/// </summary>
public partial class App : Application
{
    private DesktopWidgetWindow? _widget;
    private InputPopupWindow? _popup;
    private AlarmListWindow? _listWindow;
    private SettingsWindow? _settingsWindow;
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

        // ① 설정 적용 + 구글 동기화 배달부 준비
        _settings = new SettingsService();
        AppSettings settings = _settings.Load();

        TimeFormatter.Use24Hour = settings.Use24HourClock;
        StartupService.Synchronize(settings.StartWithWindows);   // 자동 시작 등록 상태 일치시키기

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

        // ② 상주 위젯 가동
        _widget = new DesktopWidgetWindow();
        _widget.Show();

        // ③ 전역 단축키 등록
        HotkeyManager.Current.AddOrReplace(
            "ShowAlarmInput",
            Key.Z,
            ModifierKeys.Alt | ModifierKeys.Shift,
            OnHotkeyPressed);

        // ④ 알람 감시자 가동 (장부가 바뀌면 목록 창에 알리도록 종을 연결)
        _scheduler = new AlarmSchedulerService(_storage, new NotificationService());
        _scheduler.AlarmsChanged += NotifyAlarmsChanged;
        _scheduler.Start();

        // ⑤ 알림 버튼 우체통
        ToastNotificationManagerCompat.OnActivated += OnToastActivated;

        // ⑥ 트레이 아이콘 설치
        SetupTrayIcon();

        // ⑦ Shift 2연타 감지기 가동 (쉽쉽알람 시그니처 단축키)
        _shiftTap = new ShiftDoubleTapService();

        // ⚠️ 반드시 BeginInvoke(비동기)여야 합니다. Invoke(동기)를 쓰면 안 됩니다.
        //
        // 키보드 감지기의 알림은 '화면 스레드'에서 직접 옵니다. 그래서 Invoke를 쓰면
        // 기다리지 않고 그 자리에서 실행되어, **Shift를 뗀 키 이벤트가 원래 창에 배달되기도 전에**
        // 팝업이 떠 버립니다. 그 직후 키 이벤트가 배달되면 원래 창이 전경으로 돌아오고,
        // 포커스를 빼앗긴 팝업은 자동 닫힘 규칙에 걸려 곧바로 사라집니다.
        // (Alt+Shift+Z는 윈도우가 키를 미리 소비하는 방식이라 이 문제가 없었습니다)
        //
        // BeginInvoke로 넘기면 감지기가 즉시 할 일을 마치고, 키 처리가 다 끝난 뒤에 팝업이 뜹니다.
        // 덤으로, 저수준 키보드 감지기가 300ms 안에 응답하지 못하면 윈도우가 감지기를 조용히
        // 해제해 버리는 위험도 함께 사라집니다.
        _shiftTap.DoubleTapDetected += () => Current.Dispatcher.BeginInvoke(
            new Action(ShowPopupUnlessFullScreen),
            DispatcherPriority.Input);

        _shiftTap.Start();
    }

    /// <summary>Alt+Shift+Z가 눌리면 호출됩니다.</summary>
    private void OnHotkeyPressed(object? sender, HotkeyEventArgs e)
    {
        e.Handled = true;
        ShowPopupUnlessFullScreen();
    }

    /// <summary>
    /// 전체화면으로 몰입 중이 아닐 때만 입력 팝업을 띄웁니다.
    ///
    /// 게임·영상처럼 전체화면으로 뭔가 하는 중이면 조용히 무시합니다.
    /// 게임에서 Shift 연타는 '달리기 토글'로 흔히 쓰여 오작동이 잦고,
    /// 전체화면 위에 창이 뜨면 게임이 전체화면 모드에서 튕겨 나가기까지 합니다.
    ///
    /// Shift 2연타와 Alt+Shift+Z **두 경로 모두** 이 문을 거칩니다.
    /// (트레이 메뉴·위젯처럼 사용자가 직접 눌러 부르는 경로는 막지 않습니다)
    /// </summary>
    private void ShowPopupUnlessFullScreen()
    {
        if (FullScreenDetector.IsFullScreenAppActive())
        {
            return;
        }

        ShowPopup(string.Empty);
    }

    /// <summary>
    /// 입력 팝업을 띄웁니다. (이미 열려 있으면 그 창을 다시 전면으로)
    /// 뜬 직후 키보드 포커스까지 확보되므로 마우스 클릭 없이 바로 타이핑할 수 있습니다.
    /// </summary>
    private void ShowPopup(string prefillText)
    {
        if (_popup != null && _popup.IsLoaded)
        {
            _popup.TakeFocus();   // 이미 열려 있던 창도 확실히 전면 + 입력 포커스
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

    /// <summary>설정 창을 띄웁니다. (단일 창 원칙 동일 적용)</summary>
    public void ShowSettings()
    {
        if (_settingsWindow != null && _settingsWindow.IsLoaded)
        {
            BringToFront(_settingsWindow);
            return;
        }

        _settingsWindow = new SettingsWindow();
        _settingsWindow.Show();
        BringToFront(_settingsWindow);
    }

    /// <summary>
    /// 설정이 바뀌었을 때 앱 전체에 반영합니다. (설정 창이 저장 직후 호출)
    /// </summary>
    public void ApplySettings(AppSettings settings)
    {
        TimeFormatter.Use24Hour = settings.Use24HourClock;

        // 시각 표기가 바뀌었을 수 있으니 열려 있는 목록 창을 다시 그린다
        _listWindow?.ForceRefresh();
    }

    /// <summary>구글 동기화 실패분을 즉시 재시도합니다. (설정 창에서 호출)</summary>
    public void RetrySync()
    {
        _sync?.RetryPendingDeletes();
        _sync?.RetryFailedUploads();
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

        MenuItem settingsItem = new MenuItem { Header = "⚙ 설정" };
        settingsItem.Click += (s, e) => ShowSettings();

        MenuItem exitItem = new MenuItem { Header = "종료" };
        exitItem.Click += (s, e) => ExitApplication();

        menu.Items.Add(newAlarmItem);
        menu.Items.Add(listItem);
        menu.Items.Add(settingsItem);
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
}