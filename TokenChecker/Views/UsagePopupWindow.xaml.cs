using System.Windows;
using System.Windows.Media;
using TokenChecker.Models;
using TokenChecker.Services;
using TokenChecker.Utilities;
using TokenChecker.ViewModels;
using MediaColor = System.Windows.Media.Color;

namespace TokenChecker.Views;

public partial class UsagePopupWindow : Window
{
    private readonly UsageViewModel _vm;
    private bool _pickerReady;
    private bool _transparencyPickerReady;

    public event Action? MonitorSwitchRequested;
    public event Action? PlacementSwitchRequested;

    public UsagePopupWindow(UsageViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        ApplySystemAppearance();
        SetupIntervalPicker();
        SetupTransparencyPicker();
        StartupChk.IsChecked = vm.StartupEnabled;
        vm.PropertyChanged  += (_, _) => Dispatcher.Invoke(Refresh);

        Refresh();
    }

    private void ApplySystemAppearance()
    {
        Resources["PrimaryText"]   = new SolidColorBrush(MediaColor.FromRgb(0x1A, 0x1A, 0x1A));
        Resources["SecondaryText"] = new SolidColorBrush(MediaColor.FromRgb(0x45, 0x45, 0x45));
        Resources["TertiaryText"]  = new SolidColorBrush(MediaColor.FromRgb(0x70, 0x70, 0x70));
        Resources["BorderBrush2"]  = new SolidColorBrush(MediaColor.FromArgb(0x35, 0x00, 0x00, 0x00));
        Resources["SurfaceBrush"] = new SolidColorBrush(
            MediaColor.FromArgb(_vm.PopupTransparency.ToAlpha(), 0xFF, 0xFF, 0xFF));

        var hover = new SolidColorBrush(MediaColor.FromArgb(0x18, 0x00, 0x00, 0x00));
        Resources["HoverBg"] = hover;
    }

    // ── Binding helpers ──────────────────────────────────────────────────

    private void Refresh()
    {
        RefreshClaude();
        RefreshCodex();
        RefreshFooter();
    }

    private void RefreshClaude()
    {
        var snap = _vm.Snapshot;

        if (snap.ClaudeUsage == null && snap.ClaudeError == null)
        {
            ClaudeLoading.Visibility = Visibility.Visible;
            ClaudeContent.Visibility = Visibility.Collapsed;
            ClaudeError.Visibility   = Visibility.Collapsed;
            return;
        }

        ClaudeLoading.Visibility = Visibility.Collapsed;

        if (snap.ClaudeError != null)
        {
            // 直前値があれば常に表示し続ける（待機/一時エラー中も数字を消さない）。
            var showLastValue = snap.ClaudeUsage != null;
            ClaudeContent.Visibility = showLastValue ? Visibility.Visible : Visibility.Collapsed;
            ClaudeError.Visibility   = Visibility.Visible;
            ClaudeErrorTitle.Text    = showLastValue || snap.ClaudeError.Kind == DomainErrorKind.AnthropicRateLimited
                ? "⏳ 更新待機中"
                : "⚠ 取得失敗";
            ClaudeErrorMsg.Text      = snap.ClaudeError.Kind == DomainErrorKind.AnthropicRateLimited &&
                                       _vm.ClaudeNextRetryAt is { } retryAt
                ? $"{snap.ClaudeError.Message}\n次回の自動取得: {retryAt:HH:mm}"
                : snap.ClaudeError.Message;
            if (!showLastValue) return;
        }
        else
        {
            ClaudeContent.Visibility = Visibility.Visible;
            ClaudeError.Visibility   = Visibility.Collapsed;
        }
        var usage = snap.ClaudeUsage!;

        if (usage.FiveHour is { } fh)
        {
            Claude5hPanel.Visibility = Visibility.Visible;
            Claude5hBar.Value        = fh.Utilization;
            Claude5hPct.Text         = $"{fh.Percent}%";
            Claude5hPct.Foreground   = UtilBrush(fh.Utilization);
            Claude5hReset.Text       = ResetLabel(fh.ResetsAt);
        }
        else { Claude5hPanel.Visibility = Visibility.Collapsed; }

        if (usage.Weekly is { } w)
        {
            ClaudeWeeklyRow.Visibility = Visibility.Visible;
            ClaudeWeeklyPct.Text       = $"{w.Percent}%";
            ClaudeWeeklyPct.Foreground = UtilBrush(w.Utilization);
            ClaudeWeeklyBar.Value      = w.Utilization;
            ClaudeWeeklyReset.Text     = ResetLabel(w.ResetsAt);
        }
        else { ClaudeWeeklyRow.Visibility = Visibility.Collapsed; }

        if (usage.WeeklySonnet is { } ws)
        {
            ClaudeSonnetRow.Visibility = Visibility.Visible;
            ClaudeSonnetPct.Text       = $"{ws.Percent}%";
            ClaudeSonnetPct.Foreground = UtilBrush(ws.Utilization);
        }
        else { ClaudeSonnetRow.Visibility = Visibility.Collapsed; }
    }

    private void RefreshCodex()
    {
        var snap = _vm.Snapshot;

        if (snap.CodexUsage == null && snap.CodexError == null)
        {
            CodexLoading.Visibility = Visibility.Visible;
            CodexContent.Visibility = Visibility.Collapsed;
            CodexError.Visibility   = Visibility.Collapsed;
            return;
        }

        CodexLoading.Visibility = Visibility.Collapsed;

        if (snap.CodexError != null)
        {
            // 直前値があれば常に表示し続ける（待機/一時エラー中も数字を消さない）。
            var showLastValue = snap.CodexUsage != null;
            CodexContent.Visibility = showLastValue ? Visibility.Visible : Visibility.Collapsed;
            CodexError.Visibility   = Visibility.Visible;
            CodexErrorTitle.Text    = showLastValue ? "⏳ 更新待機中" : "⚠ 取得失敗";
            CodexErrorMsg.Text      = snap.CodexError.Message;
            if (!showLastValue) return;
        }
        else
        {
            CodexContent.Visibility = Visibility.Visible;
            CodexError.Visibility   = Visibility.Collapsed;
        }
        var usage = snap.CodexUsage!;

        if (usage.FiveHour is { } fh)
        {
            Codex5hPanel.Visibility = Visibility.Visible;
            Codex5hBar.Value        = fh.Utilization;
            Codex5hPct.Text         = $"{fh.Percent}%";
            Codex5hPct.Foreground   = UtilBrush(fh.Utilization);
            Codex5hReset.Text       = ResetLabel(fh.ResetsAt);
        }
        else { Codex5hPanel.Visibility = Visibility.Collapsed; }

        if (usage.Weekly is { } w)
        {
            CodexWeeklyRow.Visibility = Visibility.Visible;
            CodexWeeklyPct.Text       = $"{w.Percent}%";
            CodexWeeklyPct.Foreground = UtilBrush(w.Utilization);
            CodexWeeklyBar.Value      = w.Utilization;
            CodexWeeklyReset.Text     = ResetLabel(w.ResetsAt);
        }
        else { CodexWeeklyRow.Visibility = Visibility.Collapsed; }
    }

    private void RefreshFooter()
    {
        if (_vm.Snapshot.FetchedAt > DateTime.MinValue)
            FetchedAtLabel.Text = $"更新: {_vm.Snapshot.FetchedAt:HH:mm:ss}";

        RefreshBtn.IsEnabled = !_vm.IsLoading;
    }

    // ── Interval picker ──────────────────────────────────────────────────

    private void SetupIntervalPicker()
    {
        _pickerReady = false;
        IntervalPicker.ItemsSource       = PollingIntervalExtensions.All
            .Select(p => new IntervalItem(p)).ToList();
        IntervalPicker.DisplayMemberPath = "Label";
        SyncIntervalPicker();
        _pickerReady = true;
    }

    private void SyncIntervalPicker()
    {
        var items = (IList<IntervalItem>?)IntervalPicker.ItemsSource;
        IntervalPicker.SelectedItem = items?.FirstOrDefault(i => i.Value == _vm.PollingInterval);
    }

    private void IntervalPicker_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_pickerReady && IntervalPicker.SelectedItem is IntervalItem item)
            _vm.PollingInterval = item.Value;
    }

    private sealed record IntervalItem(PollingInterval Value)
    {
        public string Label { get; } = Value.ToLabel();
    }

    private void SetupTransparencyPicker()
    {
        _transparencyPickerReady = false;
        TransparencyPicker.ItemsSource = PopupTransparencyExtensions.All
            .Select(p => new TransparencyItem(p)).ToList();
        TransparencyPicker.DisplayMemberPath = "Label";
        var items = (IList<TransparencyItem>?)TransparencyPicker.ItemsSource;
        TransparencyPicker.SelectedItem = items?.FirstOrDefault(i => i.Value == _vm.PopupTransparency);
        _transparencyPickerReady = true;
    }

    private void TransparencyPicker_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_transparencyPickerReady || TransparencyPicker.SelectedItem is not TransparencyItem item) return;
        _vm.PopupTransparency = item.Value;
        ApplySystemAppearance();
    }

    private sealed record TransparencyItem(PopupTransparency Value)
    {
        public string Label { get; } = Value.ToLabel();
    }

    // ── Event handlers ───────────────────────────────────────────────────

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await _vm.RefreshAsync(force: true);

    private void Close_Click(object sender, RoutedEventArgs e)
        => Hide();

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show(
                "Token Checker を終了しますか？", "確認",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes)
            System.Windows.Application.Current.Shutdown();
    }

    private void ClaudeLogin_Click(object sender, RoutedEventArgs e)
    {
        var win = new LoginWindow("Claude Code", "claude auth login", _vm, new WindowsTokenSource());
        win.Show();
    }

    private void CodexLogin_Click(object sender, RoutedEventArgs e)
    {
        var win = new LoginWindow("Codex", "codex login", _vm);
        win.Show();
    }

    private void StartupChk_Changed(object sender, RoutedEventArgs e)
        => _vm.StartupEnabled = StartupChk.IsChecked == true;

    private void Monitor_Click(object sender, RoutedEventArgs e)
        => MonitorSwitchRequested?.Invoke();

    private void Placement_Click(object sender, RoutedEventArgs e)
        => PlacementSwitchRequested?.Invoke();

    // ── Helpers ──────────────────────────────────────────────────────────

    private static SolidColorBrush UtilBrush(double v) => new(
        v < 0.75 ? MediaColor.FromRgb(0x4C, 0xAF, 0x50) :
        v < 0.90 ? MediaColor.FromRgb(0xFF, 0xC1, 0x07) :
                   MediaColor.FromRgb(0xF4, 0x43, 0x36));

    private static string ResetLabel(DateTime resetsAt)
    {
        var local = resetsAt.Kind == DateTimeKind.Utc ? resetsAt.ToLocalTime() : resetsAt;
        var now   = DateTime.Now;
        if (local <= now.AddMinutes(1)) return "まもなくリセット";

        var diff = local - now;
        if (diff.TotalDays >= 1)
            return $"あと {(int)diff.TotalDays}日{diff.Hours}時間 ({local:M/d HH:mm} リセット)";

        var h   = (int)diff.TotalHours;
        var m   = diff.Minutes;
        var rel = h > 0 ? $"{h}時間{m}分" : $"{m}分";
        return $"あと {rel} ({local:HH:mm} リセット)";
    }

    public void UpdateMonitorLabel(int index, int total)
    {
        MonitorBtn.Content = total > 1 ? $"⇄ {index + 1}/{total}" : "⇄ 切替";
    }

    public void UpdatePlacementLabel(WidgetPlacement placement)
    {
        PlacementBtn.Content = placement == WidgetPlacement.Right ? "右" : "左";
    }

}
