using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyAlarm.Services;
using EasyAlarm.Views;
using H.NotifyIcon;
using Microsoft.Toolkit.Uwp.Notifications;
using NHotkey;
using NHotkey.Wpf;

namespace EasyAlarm;

/// <summary>
/// 프로그램의 사령탑.
/// 시작 시: ① 위젯 가동 ② 단축키 등록 ③ 알람 감시자 가동 ④ 알림 버튼 우체통 개설 ⑤ 트레이 아이콘 설치
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
            _listWindow.Refresh();       // 그 사이 바뀐 알람 반영
            BringToFront(_listWindow);
            return;
        }

        _listWindow = new AlarmListWindow();
        _listWindow.Show();
        BringToFront(_listWindow);
    }
    /// <summary>
    /// 장부(알람 목록)가 바뀌었음을 알리는 방송 창구.
    /// 목록 창이 열려 있을 때만 다시 그리도록 지시하고, 닫혀 있으면 아무 일도 하지 않습니다.
    /// (주기적 감시나 추가 타이머 없이 이벤트 1회 전달 = 가벼움 유지)
    /// </summary>
    public void NotifyAlarmsChanged()
    {
        if (_listWindow != null && _listWindow.IsLoaded)
        {
            _listWindow.Refresh();
        }
    }

    /// <summary>
    /// 창을 확실하게 화면 맨 앞으로 끌어옵니다.
    /// Show()만으로는 다른 프로그램 창 뒤로 밀리는 경우가 있어,
    /// '한 프레임만 최상단으로 올린 뒤 즉시 해제'하는 표준 기법을 씁니다.
    /// (계속 Topmost로 두면 다른 창을 영구히 가려 방해가 되므로 바로 해제)
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

        MenuItem exitItem = new MenuItem { Header = "종료" };
        exitItem.Click += (s, e) => ExitApplication();

        menu.Items.Add(newAlarmItem);
        menu.Items.Add(listItem);
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
        ToastNotificationManagerCompat.Uninstall();
        _trayIcon?.Dispose();
        _shiftTap?.Dispose();                          // 키보드 감지기 철거
        Shutdown();
    }
}