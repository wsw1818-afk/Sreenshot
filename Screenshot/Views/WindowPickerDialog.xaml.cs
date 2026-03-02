using System.Windows;
using Screenshot.Services;

namespace Screenshot.Views;

public partial class WindowPickerDialog : Window
{
    private readonly WindowCaptureService _windowCaptureService;
    private List<WindowInfoViewModel> _allWindows = new();

    public WindowCaptureService.WindowInfo? SelectedWindow { get; private set; }

    public WindowPickerDialog(WindowCaptureService windowCaptureService)
    {
        _windowCaptureService = windowCaptureService;
        InitializeComponent();
        Loaded += (_, _) => LoadWindows();
    }

    private void LoadWindows()
    {
        _allWindows = _windowCaptureService.GetVisibleWindows()
            .Select(w => new WindowInfoViewModel(w))
            .ToList();

        ApplyFilter(SearchBox.Text);
        SearchBox.Focus();
    }

    private void ApplyFilter(string keyword)
    {
        var filtered = string.IsNullOrWhiteSpace(keyword)
            ? _allWindows
            : _allWindows.Where(w => w.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();

        WindowList.ItemsSource = filtered;
        CountText.Text = $"{filtered.Count}개";
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ApplyFilter(SearchBox.Text);
    }

    private void WindowList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ConfirmSelection();
    }

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        ConfirmSelection();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ConfirmSelection()
    {
        if (WindowList.SelectedItem is WindowInfoViewModel vm)
        {
            SelectedWindow = vm.Source;
            DialogResult = true;
            Close();
        }
    }
}

internal class WindowInfoViewModel
{
    public WindowCaptureService.WindowInfo Source { get; }
    public string Title => Source.Title;
    public string SizeText => $"{Source.Bounds.Width} × {Source.Bounds.Height}";

    public WindowInfoViewModel(WindowCaptureService.WindowInfo source)
    {
        Source = source;
    }
}
