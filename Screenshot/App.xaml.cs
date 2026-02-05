using System.Windows;
using Screenshot.Services.Capture;

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
        // 미처리 예외 시 프로세스 확실히 종료
        DispatcherUnhandledException += (s, args) =>
        {
            CaptureLogger.Error("App", "미처리 예외 발생", args.Exception);
            CaptureLogger.FlushToFile();
            args.Handled = true;
            Environment.Exit(1);
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                CaptureLogger.Error("App", "AppDomain 미처리 예외", ex);
                CaptureLogger.FlushToFile();
            }
            Environment.Exit(1);
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            CaptureLogger.Error("App", "Task 미처리 예외", args.Exception);
            args.SetObserved(); // 프로세스 종료 방지, 로깅만
        };

        base.OnStartup(e);

        var mainWindow = new MainWindow();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CaptureLogger.Info("App", "OnExit - 프로세스 종료");
        CaptureLogger.FlushToFile();
        base.OnExit(e);

        // 모든 리소스 정리 후에도 잔류 스레드가 있을 수 있으므로 강제 종료
        Environment.Exit(0);
    }
}
