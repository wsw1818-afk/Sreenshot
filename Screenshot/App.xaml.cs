using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Media;

namespace Screenshot;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    [STAThread]
    public static void Main()
    {
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // 소프트웨어 렌더링 제거 - 캡처 검은화면 원인
        // RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        base.OnStartup(e);

        var mainWindow = new MainWindow();
        mainWindow.Show();
    }
}
