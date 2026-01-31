using System.Windows;

namespace Screenshot.Views;

public partial class UrlInputDialog : Window
{
    public string Url => UrlTextBox.Text;

    public UrlInputDialog()
    {
        InitializeComponent();
        UrlTextBox.Focus();
        UrlTextBox.SelectAll();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UrlTextBox.Text) || UrlTextBox.Text == "https://")
        {
            System.Windows.MessageBox.Show("URL을 입력해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // URL 형식 검증
        if (!UrlTextBox.Text.StartsWith("http://") && !UrlTextBox.Text.StartsWith("https://"))
        {
            UrlTextBox.Text = "https://" + UrlTextBox.Text;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
