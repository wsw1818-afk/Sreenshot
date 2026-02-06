using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace Screenshot.Services;

/// <summary>
/// 캡처 완료 알림 서비스 (토스트 + 소리)
/// </summary>
public class NotificationService
{
    private Window? _toastWindow;
    private DispatcherTimer? _hideTimer;

    /// <summary>
    /// 캡처 성공 알림
    /// </summary>
    public void ShowCaptureSuccess(string message, string? filePath = null)
    {
        // 소리 재생
        SystemSounds.Asterisk.Play();

        // 토스트 알림 표시
        if (Application.Current?.Dispatcher == null) return;
        Application.Current.Dispatcher.Invoke(() =>
        {
            ShowToast("✓ " + message, ToastType.Success, filePath);
        });
    }

    /// <summary>
    /// 캡처 실패 알림
    /// </summary>
    public void ShowCaptureError(string message)
    {
        SystemSounds.Hand.Play();

        if (Application.Current?.Dispatcher == null) return;
        Application.Current.Dispatcher.Invoke(() =>
        {
            ShowToast("✗ " + message, ToastType.Error);
        });
    }

    /// <summary>
    /// 클립보드 복사 알림
    /// </summary>
    public void ShowClipboardCopy()
    {
        SystemSounds.Asterisk.Play();

        if (Application.Current?.Dispatcher == null) return;
        Application.Current.Dispatcher.Invoke(() =>
        {
            ShowToast("✓ 클립보드에 복사됨", ToastType.Info);
        });
    }

    private enum ToastType { Success, Error, Info }

    private void ShowToast(string message, ToastType type, string? filePath = null)
    {
        // 기존 토스트 닫기
        _toastWindow?.Close();
        _hideTimer?.Stop();

        // 토스트 윈도우 생성
        _toastWindow = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            Width = 300,
            Height = 80,
            ResizeMode = ResizeMode.NoResize
        };

        // 배경색 결정
        var bgColor = type switch
        {
            ToastType.Success => Color.FromRgb(76, 175, 80),
            ToastType.Error => Color.FromRgb(244, 67, 54),
            _ => Color.FromRgb(33, 150, 243)
        };

        // 컨텐츠 생성
        var border = new Border
        {
            Background = new SolidColorBrush(bgColor),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(15),
            Margin = new Thickness(10),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 10,
                ShadowDepth = 3,
                Opacity = 0.3
            }
        };

        var stack = new StackPanel();

        var text = new TextBlock
        {
            Text = message,
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        stack.Children.Add(text);

        if (!string.IsNullOrEmpty(filePath))
        {
            var pathText = new TextBlock
            {
                Text = filePath,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 5, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            pathText.MouseLeftButtonDown += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                }
                catch { }
            };
            stack.Children.Add(pathText);
        }

        border.Child = stack;
        _toastWindow.Content = border;

        // 화면 우측 하단에 위치
        var workArea = SystemParameters.WorkArea;
        _toastWindow.Left = workArea.Right - _toastWindow.Width - 10;
        _toastWindow.Top = workArea.Bottom - _toastWindow.Height - 10;

        // 페이드 인 애니메이션
        _toastWindow.Opacity = 0;
        _toastWindow.Show();

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        _toastWindow.BeginAnimation(Window.OpacityProperty, fadeIn);

        // 자동 숨김 타이머
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideTimer.Tick += (s, e) =>
        {
            _hideTimer.Stop();
            HideToast();
        };
        _hideTimer.Start();

        // 클릭하면 바로 닫기
        _toastWindow.MouseLeftButtonDown += (s, e) =>
        {
            _hideTimer?.Stop();
            HideToast();
        };
    }

    private void HideToast()
    {
        if (_toastWindow == null) return;

        try
        {
            var window = _toastWindow;
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, e) =>
            {
                try { window.Close(); } catch { }
                if (_toastWindow == window) _toastWindow = null;
            };
            window.BeginAnimation(Window.OpacityProperty, fadeOut);
        }
        catch
        {
            // 애니메이션 시작 실패 시 직접 닫기
            try { _toastWindow?.Close(); } catch { }
            _toastWindow = null;
        }
    }
}
