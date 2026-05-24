using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using TokenChecker.Models;
using TokenChecker.Services;
using TokenChecker.Utilities;
using TokenChecker.ViewModels;
using TokenChecker.Views;

namespace TokenChecker;

public partial class App : System.Windows.Application
{
    private NotifyIcon?              _tray;
    private TaskbarWidget?           _widget;
    private UsagePopupWindow?        _popup;
    private UsageViewModel?          _vm;
    private CancellationTokenSource? _pollCts;
    private Icon?                    _prevIcon;
    private Mutex?                   _singleInstanceMutex;
    private DateTime                 _popupHiddenAt;
    private int                      _targetScreenIndex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var instanceMutex = new Mutex(initiallyOwned: true, @"Local\TokenChecker", out var isFirstInstance);
        if (!isFirstInstance)
        {
            instanceMutex.Dispose();
            Shutdown();
            return;
        }
        _singleInstanceMutex = instanceMutex;

        DispatcherUnhandledException += (_, ex) =>
        {
            // フルスタック（ファイルパス等の内部情報を含む）は表示せず、要約のみ出す。
            System.Windows.MessageBox.Show(ex.Exception.Message,
                "Token Checker - 起動エラー",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            ex.Handled = true;
            Shutdown(1);
        };

        try
        {
            _vm = new UsageViewModel();
            _targetScreenIndex = ResolveSavedScreenIndex(_vm.MonitorDeviceName);
            _vm.MonitorDeviceName = System.Windows.Forms.Screen.AllScreens[_targetScreenIndex].DeviceName;

            // ── コンパクトウィジェット（常時表示）
            _widget = new TaskbarWidget(_vm, _targetScreenIndex);
            _widget.PopupToggleRequested += TogglePopup;
            _widget.Show();

            // ── 詳細ポップアップ（クリックで開閉）
            _popup = new UsagePopupWindow(_vm);
            _popup.MonitorSwitchRequested += CycleMonitor;
            _popup.PlacementSwitchRequested += ToggleWidgetPlacement;
            _popup.UpdatePlacementLabel(_vm.WidgetPlacement);
            _popup.UpdateMonitorLabel(_targetScreenIndex, System.Windows.Forms.Screen.AllScreens.Length);
            _popup.SizeChanged += (_, _) =>
            {
                if (_popup.IsVisible)
                    PositionPopup();
            };

            // フォーカスが外れたら（外側クリック等）自動で閉じる
            _popup.Deactivated += (_, _) =>
            {
                if (_popup is { IsVisible: true })
                {
                    _popup.Hide();
                    _popupHiddenAt = DateTime.UtcNow;
                }
            };

            // ── トレイアイコン（右クリックメニュー用）
            _tray = new NotifyIcon
            {
                Visible     = true,
                Text        = "Token Checker",
                Icon        = TrayIconRenderer.CreateIcon(null, null),
                ContextMenuStrip = BuildContextMenu(),
            };
            _tray.MouseClick += OnTrayClick;

            _tray.ShowBalloonTip(
                timeout: 4000,
                tipTitle: "Token Checker 起動中",
                tipText: "タスクバー右端のウィジェットをクリックすると詳細が開きます。",
                tipIcon: ToolTipIcon.Info);

            _vm.SnapshotChanged += UpdateTrayIcon;

            // 初回フェッチ完了後に一度だけ、ログイン確認→ポップアップ自動表示を行う。
            var loginChecked = false;
            _vm.SnapshotChanged += () =>
            {
                if (loginChecked || _vm!.Snapshot.FetchedAt == DateTime.MinValue) return;
                loginChecked = true;
                Dispatcher.BeginInvoke(() =>
                {
                    PromptLoginIfNeeded();
                    PositionPopup();
                    _popup!.Show();
                    _popup.Activate();
                });
            };

            _pollCts = new CancellationTokenSource();
            _ = _vm.RunPollingLoopAsync(_pollCts.Token);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message,
                "Token Checker - 起動エラー",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    // ── 初回ログイン案内 ─────────────────────────────────────────────────

    private void PromptLoginIfNeeded()
    {
        if (_vm!.LoginPrompted) return;
        _vm.LoginPrompted = true; // 初回のみ。以降は手動ログインボタンを使う。

        var snap = _vm.Snapshot;
        if (snap.ClaudeError?.Kind == DomainErrorKind.TokenMissing)
            new LoginWindow("Claude Code", "claude auth login", _vm, new WindowsTokenSource()).ShowDialog();
        if (snap.CodexError?.Kind is DomainErrorKind.CodexRpcError or DomainErrorKind.CodexUnauthorized)
            new LoginWindow("Codex", "codex login", _vm).ShowDialog();
    }

    // ── ポップアップ開閉 ─────────────────────────────────────────────────

    private void TogglePopup()
    {
        if (_popup!.IsVisible) { _popup.Hide(); _popupHiddenAt = DateTime.UtcNow; return; }

        // 直前に Deactivated で閉じられた直後は再表示しない（同一クリック操作の二重発火防止）
        if ((DateTime.UtcNow - _popupHiddenAt).TotalMilliseconds < 250) return;

        PositionPopup();
        _popup.Show();
        _popup.Dispatcher.BeginInvoke(PositionPopup);
        _popup.Activate();
    }

    private void PositionPopup()
    {
        using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
        var dpi     = g.DpiX / 96.0;

        var screens = System.Windows.Forms.Screen.AllScreens;
        var screen  = _targetScreenIndex < screens.Length
                      ? screens[_targetScreenIndex]
                      : (System.Windows.Forms.Screen.PrimaryScreen ?? screens[0]);
        var wa      = screen.WorkingArea;
        var popupH  = _popup!.ActualHeight > 10 ? _popup.ActualHeight : 480;
        var taskbarTop = TaskbarPosition.Get(_targetScreenIndex)?.TaskbarTop ?? wa.Bottom / dpi;

        double left = _widget != null
            ? _widget.Left + _widget.ActualWidth / 2 - _popup.Width / 2
            : wa.Right / dpi - _popup.Width - 12;
        double popupBottom = _widget != null
            ? _widget.Top - 8
            : wa.Bottom / dpi - 8;
        popupBottom = Math.Min(popupBottom, taskbarTop - 8);
        double top = popupBottom - popupH;

        // 画面右端・上端の補正
        if (left + _popup.Width > wa.Right / dpi) left = wa.Right / dpi - _popup.Width - 4;
        if (left < wa.Left  / dpi)                left = wa.Left  / dpi + 4;
        if (top  < wa.Top   / dpi)                top  = wa.Top   / dpi + 4;

        _popup.Left = left;
        _popup.Top  = top;
    }

    private void CycleMonitor()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length <= 1) return;

        var requestedScreenIndex = (_targetScreenIndex + 1) % screens.Length;
        _widget?.SnapToTaskbar(requestedScreenIndex, _vm!.WidgetPlacement);
        _targetScreenIndex = _widget?.CurrentScreenIndex ?? requestedScreenIndex;
        _vm!.MonitorDeviceName = screens[_targetScreenIndex].DeviceName;
        _popup?.UpdateMonitorLabel(_targetScreenIndex, screens.Length);
        if (_popup?.IsVisible == true)
            PositionPopup();
    }

    private static int ResolveSavedScreenIndex(string? deviceName)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0) return 0;
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            var primary = Array.FindIndex(screens, screen => screen.Primary);
            return primary >= 0 ? primary : 0;
        }

        var saved = Array.FindIndex(screens,
            screen => string.Equals(screen.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
        return saved >= 0 ? saved : 0;
    }

    private void ToggleWidgetPlacement()
    {
        _vm!.WidgetPlacement = _vm.WidgetPlacement == WidgetPlacement.Right
            ? WidgetPlacement.Left
            : WidgetPlacement.Right;
        _widget?.SnapToTaskbar(_targetScreenIndex, _vm.WidgetPlacement);
        _targetScreenIndex = _widget?.CurrentScreenIndex ?? _targetScreenIndex;
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (_targetScreenIndex >= 0 && _targetScreenIndex < screens.Length)
            _vm.MonitorDeviceName = screens[_targetScreenIndex].DeviceName;
        _popup?.UpdateMonitorLabel(_targetScreenIndex, System.Windows.Forms.Screen.AllScreens.Length);
        _popup?.UpdatePlacementLabel(_vm.WidgetPlacement);

        if (_popup?.IsVisible == true)
            PositionPopup();
    }

    // ── トレイアイコン ───────────────────────────────────────────────────

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("詳細を表示/非表示",   null, (_, _) => Dispatcher.Invoke(TogglePopup));
        menu.Items.Add("今すぐ更新",           null, (_, _) => _ = _vm!.RefreshAsync(force: true));
        menu.Items.Add("モニター切替",         null, (_, _) => Dispatcher.Invoke(CycleMonitor));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了",                 null, (_, _) => Dispatcher.Invoke(() => Shutdown()));
        return menu;
    }

    private void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            Dispatcher.Invoke(TogglePopup);
    }

    private void UpdateTrayIcon()
    {
        Dispatcher.Invoke(() =>
        {
            var snap       = _vm!.Snapshot;
            var claudeUtil = snap.ClaudeUsage?.FiveHour?.Utilization ?? snap.ClaudeUsage?.Weekly?.Utilization;
            var codexUtil  = snap.CodexUsage?.FiveHour?.Utilization  ?? snap.CodexUsage?.Weekly?.Utilization;

            var newIcon = TrayIconRenderer.CreateIcon(claudeUtil, codexUtil);
            var old = _prevIcon;
            _tray!.Icon = newIcon;
            _prevIcon = newIcon;
            old?.Dispose();

            _tray.Text = BuildTooltip();
        });
    }

    private string BuildTooltip()
    {
        var snap = _vm?.Snapshot;
        if (snap == null) return "Token Checker";
        var sb = new System.Text.StringBuilder("Token Checker");
        var cu = snap.ClaudeUsage;
        if (cu?.FiveHour is { } cf)       sb.Append($"\nClaude 5h: {cf.Percent}%");
        else if (cu?.Weekly is { } cw)    sb.Append($"\nClaude 7d: {cw.Percent}%");
        var xu = snap.CodexUsage;
        if (xu?.FiveHour is { } xf)       sb.Append($"\nCodex  5h: {xf.Percent}%");
        else if (xu?.Weekly is { } xw)    sb.Append($"\nCodex  7d: {xw.Percent}%");
        if (snap.FetchedAt > DateTime.MinValue)    sb.Append($"\n更新: {snap.FetchedAt:HH:mm:ss}");
        return sb.ToString();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _pollCts?.Cancel();
        _tray?.Dispose();
        _prevIcon?.Dispose();
        if (_vm != null) await _vm.DisposeAsync();
        try { _singleInstanceMutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
