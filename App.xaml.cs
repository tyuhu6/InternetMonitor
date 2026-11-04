using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace InternetMonitor;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            MessageBox.Show("程序遇到错误：" + args.Exception.Message, "网络监控", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        _singleInstance = new Mutex(true, @"Local\InternetMonitor.SingleInstance", out bool created);
        if (!created)
        {
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static void LogError(Exception exception)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "InternetMonitor");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // 日志写入失败时忽略
        }
        catch (UnauthorizedAccessException)
        {
            // 无写入权限时忽略
        }
    }
}
