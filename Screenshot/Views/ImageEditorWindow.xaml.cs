using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Screenshot.Views;

public partial class ImageEditorWindow : Window
{
    private Bitmap _originalImage;
    private Bitmap _editedImage;
    private string? _savePath;

    private enum EditTool { Arrow, Rectangle, Ellipse, Line, Text, Highlight, Mosaic }
    private EditTool _currentTool = EditTool.Arrow;

    private Point _startPoint;
    private bool _isDrawing;
    private Shape? _currentShape;

    private readonly Stack<Bitmap> _undoStack = new();
    private readonly Stack<Bitmap> _redoStack = new();

    public Bitmap? ResultImage { get; private set; }
    public Bitmap? EditedImage => ResultImage ?? _editedImage;

    public ImageEditorWindow(Bitmap image, string? savePath = null)
    {
        InitializeComponent();

        _originalImage = (Bitmap)image.Clone();
        _editedImage = (Bitmap)image.Clone();
        _savePath = savePath;

        // 이미지 표시
        DisplayImage(_editedImage);

        // 캔버스 크기 설정
        EditCanvas.Width = image.Width;
        EditCanvas.Height = image.Height;

        // 사이즈 표시
        SizeText.Text = $"크기: {image.Width} x {image.Height}";

        // 두께 슬라이더 이벤트
        ThicknessSlider.ValueChanged += (s, e) =>
        {
            ThicknessText.Text = ((int)ThicknessSlider.Value).ToString();
        };

        // 키보드 단축키
        KeyDown += Window_KeyDown;

        // 기본 도구 선택
        BtnArrow.IsChecked = true;
    }

    private void DisplayImage(Bitmap bitmap)
    {
        var bitmapSource = ConvertToBitmapSource(bitmap);
        SourceImage.Source = bitmapSource;
        SourceImage.Width = bitmap.Width;
        SourceImage.Height = bitmap.Height;
    }

    private BitmapSource ConvertToBitmapSource(Bitmap bitmap)
    {
        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        memory.Position = 0;

        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.StreamSource = memory;
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.EndInit();
        bitmapImage.Freeze();

        return bitmapImage;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Undo_Click(sender, e);
        }
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Redo_Click(sender, e);
        }
        else if (e.Key == Key.A) { SelectTool(EditTool.Arrow); BtnArrow.IsChecked = true; }
        else if (e.Key == Key.R) { SelectTool(EditTool.Rectangle); BtnRect.IsChecked = true; }
        else if (e.Key == Key.E) { SelectTool(EditTool.Ellipse); BtnEllipse.IsChecked = true; }
        else if (e.Key == Key.L) { SelectTool(EditTool.Line); BtnLine.IsChecked = true; }
        else if (e.Key == Key.T) { SelectTool(EditTool.Text); BtnText.IsChecked = true; }
        else if (e.Key == Key.H) { SelectTool(EditTool.Highlight); BtnHighlight.IsChecked = true; }
        else if (e.Key == Key.M) { SelectTool(EditTool.Mosaic); BtnMosaic.IsChecked = true; }
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton btn && btn.Tag is string toolName)
        {
            // 모든 버튼 해제
            BtnArrow.IsChecked = false;
            BtnRect.IsChecked = false;
            BtnEllipse.IsChecked = false;
            BtnLine.IsChecked = false;
            BtnText.IsChecked = false;
            BtnHighlight.IsChecked = false;
            BtnMosaic.IsChecked = false;

            btn.IsChecked = true;

            _currentTool = toolName switch
            {
                "Arrow" => EditTool.Arrow,
                "Rectangle" => EditTool.Rectangle,
                "Ellipse" => EditTool.Ellipse,
                "Line" => EditTool.Line,
                "Text" => EditTool.Text,
                "Highlight" => EditTool.Highlight,
                "Mosaic" => EditTool.Mosaic,
                _ => EditTool.Arrow
            };
        }
    }

    private void SelectTool(EditTool tool)
    {
        _currentTool = tool;
    }

    private Brush GetSelectedColor()
    {
        if (ColorRed.IsChecked == true) return Brushes.Red;
        if (ColorGreen.IsChecked == true) return Brushes.Green;
        if (ColorBlue.IsChecked == true) return Brushes.Blue;
        if (ColorYellow.IsChecked == true) return Brushes.Yellow;
        if (ColorWhite.IsChecked == true) return Brushes.White;
        if (ColorBlack.IsChecked == true) return Brushes.Black;
        return Brushes.Red;
    }

    private System.Drawing.Color GetSelectedDrawingColor()
    {
        if (ColorRed.IsChecked == true) return System.Drawing.Color.Red;
        if (ColorGreen.IsChecked == true) return System.Drawing.Color.Green;
        if (ColorBlue.IsChecked == true) return System.Drawing.Color.Blue;
        if (ColorYellow.IsChecked == true) return System.Drawing.Color.Yellow;
        if (ColorWhite.IsChecked == true) return System.Drawing.Color.White;
        if (ColorBlack.IsChecked == true) return System.Drawing.Color.Black;
        return System.Drawing.Color.Red;
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(EditCanvas);
        _isDrawing = true;

        if (_currentTool == EditTool.Text)
        {
            ShowTextInput(_startPoint);
            return;
        }

        // 미리보기 도형 생성
        var color = GetSelectedColor();
        var thickness = (int)ThicknessSlider.Value;

        switch (_currentTool)
        {
            case EditTool.Arrow:
            case EditTool.Line:
                _currentShape = new Line
                {
                    Stroke = color,
                    StrokeThickness = thickness,
                    X1 = _startPoint.X,
                    Y1 = _startPoint.Y,
                    X2 = _startPoint.X,
                    Y2 = _startPoint.Y
                };
                break;

            case EditTool.Rectangle:
            case EditTool.Mosaic:
                _currentShape = new Rectangle
                {
                    Stroke = _currentTool == EditTool.Mosaic ? Brushes.Gray : color,
                    StrokeThickness = _currentTool == EditTool.Mosaic ? 2 : thickness,
                    StrokeDashArray = _currentTool == EditTool.Mosaic ? new DoubleCollection { 4, 2 } : null,
                    Fill = Brushes.Transparent
                };
                Canvas.SetLeft(_currentShape, _startPoint.X);
                Canvas.SetTop(_currentShape, _startPoint.Y);
                break;

            case EditTool.Ellipse:
                _currentShape = new Ellipse
                {
                    Stroke = color,
                    StrokeThickness = thickness,
                    Fill = Brushes.Transparent
                };
                Canvas.SetLeft(_currentShape, _startPoint.X);
                Canvas.SetTop(_currentShape, _startPoint.Y);
                break;

            case EditTool.Highlight:
                _currentShape = new Rectangle
                {
                    Fill = new SolidColorBrush(Color.FromArgb(100, 255, 255, 0)),
                    Stroke = Brushes.Transparent
                };
                Canvas.SetLeft(_currentShape, _startPoint.X);
                Canvas.SetTop(_currentShape, _startPoint.Y);
                break;
        }

        if (_currentShape != null)
        {
            EditCanvas.Children.Add(_currentShape);
        }

        EditCanvas.CaptureMouse();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing || _currentShape == null) return;

        var currentPoint = e.GetPosition(EditCanvas);

        switch (_currentTool)
        {
            case EditTool.Arrow:
            case EditTool.Line:
                if (_currentShape is Line line)
                {
                    line.X2 = currentPoint.X;
                    line.Y2 = currentPoint.Y;
                }
                break;

            case EditTool.Rectangle:
            case EditTool.Mosaic:
            case EditTool.Highlight:
                if (_currentShape is Rectangle rect)
                {
                    var x = Math.Min(_startPoint.X, currentPoint.X);
                    var y = Math.Min(_startPoint.Y, currentPoint.Y);
                    var width = Math.Abs(currentPoint.X - _startPoint.X);
                    var height = Math.Abs(currentPoint.Y - _startPoint.Y);

                    Canvas.SetLeft(rect, x);
                    Canvas.SetTop(rect, y);
                    rect.Width = width;
                    rect.Height = height;
                }
                break;

            case EditTool.Ellipse:
                if (_currentShape is Ellipse ellipse)
                {
                    var x = Math.Min(_startPoint.X, currentPoint.X);
                    var y = Math.Min(_startPoint.Y, currentPoint.Y);
                    var width = Math.Abs(currentPoint.X - _startPoint.X);
                    var height = Math.Abs(currentPoint.Y - _startPoint.Y);

                    Canvas.SetLeft(ellipse, x);
                    Canvas.SetTop(ellipse, y);
                    ellipse.Width = width;
                    ellipse.Height = height;
                }
                break;
        }
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing) return;

        _isDrawing = false;
        EditCanvas.ReleaseMouseCapture();

        if (_currentShape == null) return;

        var endPoint = e.GetPosition(EditCanvas);

        // 실행 취소를 위해 현재 상태 저장
        SaveUndoState();

        // 이미지에 그리기 적용
        using (var g = Graphics.FromImage(_editedImage))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var color = GetSelectedDrawingColor();
            var thickness = (int)ThicknessSlider.Value;

            switch (_currentTool)
            {
                case EditTool.Arrow:
                    DrawArrow(g, _startPoint, endPoint, color, thickness);
                    break;

                case EditTool.Line:
                    using (var pen = new System.Drawing.Pen(color, thickness))
                    {
                        g.DrawLine(pen, (float)_startPoint.X, (float)_startPoint.Y,
                            (float)endPoint.X, (float)endPoint.Y);
                    }
                    break;

                case EditTool.Rectangle:
                    var rectX = (int)Math.Min(_startPoint.X, endPoint.X);
                    var rectY = (int)Math.Min(_startPoint.Y, endPoint.Y);
                    var rectW = (int)Math.Abs(endPoint.X - _startPoint.X);
                    var rectH = (int)Math.Abs(endPoint.Y - _startPoint.Y);
                    using (var pen = new System.Drawing.Pen(color, thickness))
                    {
                        g.DrawRectangle(pen, rectX, rectY, rectW, rectH);
                    }
                    break;

                case EditTool.Ellipse:
                    var ellipseX = (int)Math.Min(_startPoint.X, endPoint.X);
                    var ellipseY = (int)Math.Min(_startPoint.Y, endPoint.Y);
                    var ellipseW = (int)Math.Abs(endPoint.X - _startPoint.X);
                    var ellipseH = (int)Math.Abs(endPoint.Y - _startPoint.Y);
                    using (var pen = new System.Drawing.Pen(color, thickness))
                    {
                        g.DrawEllipse(pen, ellipseX, ellipseY, ellipseW, ellipseH);
                    }
                    break;

                case EditTool.Highlight:
                    var hlX = (int)Math.Min(_startPoint.X, endPoint.X);
                    var hlY = (int)Math.Min(_startPoint.Y, endPoint.Y);
                    var hlW = (int)Math.Abs(endPoint.X - _startPoint.X);
                    var hlH = (int)Math.Abs(endPoint.Y - _startPoint.Y);
                    using (var brush = new SolidBrush(System.Drawing.Color.FromArgb(100, 255, 255, 0)))
                    {
                        g.FillRectangle(brush, hlX, hlY, hlW, hlH);
                    }
                    break;

                case EditTool.Mosaic:
                    ApplyMosaic(_editedImage, _startPoint, endPoint);
                    break;
            }
        }

        // 미리보기 도형 제거
        EditCanvas.Children.Remove(_currentShape);
        _currentShape = null;

        // 이미지 갱신
        DisplayImage(_editedImage);
    }

    private void DrawArrow(Graphics g, Point start, Point end, System.Drawing.Color color, int thickness)
    {
        using var pen = new System.Drawing.Pen(color, thickness)
        {
            EndCap = LineCap.ArrowAnchor,
            CustomEndCap = new AdjustableArrowCap(thickness + 2, thickness + 2)
        };
        g.DrawLine(pen, (float)start.X, (float)start.Y, (float)end.X, (float)end.Y);
    }

    private void ApplyMosaic(Bitmap image, Point start, Point end)
    {
        var x = (int)Math.Min(start.X, end.X);
        var y = (int)Math.Min(start.Y, end.Y);
        var width = (int)Math.Abs(end.X - start.X);
        var height = (int)Math.Abs(end.Y - start.Y);

        if (width <= 0 || height <= 0) return;

        // 클리핑
        x = Math.Max(0, x);
        y = Math.Max(0, y);
        width = Math.Min(width, image.Width - x);
        height = Math.Min(height, image.Height - y);

        const int blockSize = 10;

        for (int by = y; by < y + height; by += blockSize)
        {
            for (int bx = x; bx < x + width; bx += blockSize)
            {
                int blockW = Math.Min(blockSize, x + width - bx);
                int blockH = Math.Min(blockSize, y + height - by);

                // 블록 평균 색상 계산
                int r = 0, g = 0, b = 0, count = 0;
                for (int py = by; py < by + blockH && py < image.Height; py++)
                {
                    for (int px = bx; px < bx + blockW && px < image.Width; px++)
                    {
                        var pixel = image.GetPixel(px, py);
                        r += pixel.R;
                        g += pixel.G;
                        b += pixel.B;
                        count++;
                    }
                }

                if (count == 0) continue;

                var avgColor = System.Drawing.Color.FromArgb(r / count, g / count, b / count);

                // 블록 채우기
                for (int py = by; py < by + blockH && py < image.Height; py++)
                {
                    for (int px = bx; px < bx + blockW && px < image.Width; px++)
                    {
                        image.SetPixel(px, py, avgColor);
                    }
                }
            }
        }
    }

    private void ShowTextInput(Point position)
    {
        var dialog = new Window
        {
            Title = "텍스트 입력",
            Width = 300,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            ResizeMode = ResizeMode.NoResize
        };

        var stack = new StackPanel { Margin = new Thickness(15) };

        var textBox = new TextBox
        {
            FontSize = 14,
            Padding = new Thickness(8),
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80))
        };
        stack.Children.Add(textBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 15, 0, 0)
        };

        var okButton = new Button
        {
            Content = "확인",
            Padding = new Thickness(20, 8, 20, 8),
            Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        okButton.Click += (s, e) =>
        {
            if (!string.IsNullOrEmpty(textBox.Text))
            {
                SaveUndoState();
                DrawText(position, textBox.Text);
            }
            dialog.Close();
        };

        var cancelButton = new Button
        {
            Content = "취소",
            Padding = new Thickness(20, 8, 20, 8),
            Margin = new Thickness(10, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(51, 51, 51)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        cancelButton.Click += (s, e) => dialog.Close();

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        stack.Children.Add(buttonPanel);

        dialog.Content = stack;
        textBox.Focus();
        dialog.ShowDialog();
    }

    private void DrawText(Point position, string text)
    {
        using var g = Graphics.FromImage(_editedImage);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        var color = GetSelectedDrawingColor();
        var fontSize = 12 + (int)ThicknessSlider.Value * 2;

        using var font = new Font("맑은 고딕", fontSize, System.Drawing.FontStyle.Bold);
        using var brush = new SolidBrush(color);

        g.DrawString(text, font, brush, (float)position.X, (float)position.Y);

        DisplayImage(_editedImage);
    }

    private void SaveUndoState()
    {
        _undoStack.Push((Bitmap)_editedImage.Clone());
        _redoStack.Clear();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undoStack.Count == 0) return;

        _redoStack.Push((Bitmap)_editedImage.Clone());
        _editedImage.Dispose();
        _editedImage = _undoStack.Pop();
        DisplayImage(_editedImage);
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_redoStack.Count == 0) return;

        _undoStack.Push((Bitmap)_editedImage.Clone());
        _editedImage.Dispose();
        _editedImage = _redoStack.Pop();
        DisplayImage(_editedImage);
    }

    private void CopyToClipboard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetImage(ConvertToBitmapSource(_editedImage));
            System.Media.SystemSounds.Asterisk.Play();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"클립보드 복사 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PNG 이미지|*.png|JPEG 이미지|*.jpg|BMP 이미지|*.bmp",
            DefaultExt = ".png",
            FileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}"
        };

        if (dialog.ShowDialog() == true)
        {
            SaveImage(dialog.FileName);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_savePath))
        {
            SaveAs_Click(sender, e);
            return;
        }

        SaveImage(_savePath);
    }

    private void SaveImage(string path)
    {
        try
        {
            var format = System.IO.Path.GetExtension(path).ToLower() switch
            {
                ".jpg" or ".jpeg" => ImageFormat.Jpeg,
                ".bmp" => ImageFormat.Bmp,
                _ => ImageFormat.Png
            };

            _editedImage.Save(path, format);
            ResultImage = (Bitmap)_editedImage.Clone();
            _savePath = path;

            System.Media.SystemSounds.Asterisk.Play();
            MessageBox.Show($"저장 완료: {path}", "저장", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Undo/Redo 스택 정리
        foreach (var bitmap in _undoStack) bitmap.Dispose();
        foreach (var bitmap in _redoStack) bitmap.Dispose();
        _undoStack.Clear();
        _redoStack.Clear();

        // 에디터 내부 이미지 정리
        _originalImage?.Dispose();
        _editedImage?.Dispose();

        base.OnClosed(e);
    }
}
