using System.Windows;
using GridAlignment.Core;
using GridAlignment.UI;

namespace GridAlignment;

public partial class App : Application
{
    private Extension? ext;
    private MainWindow? window;
    private bool running;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Task.Run(start);
    }

    private async void start()
    {
        try
        {
            ext = new Extension();

            ext.Activated += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (window == null)
                    {
                        window = new MainWindow(ext);
                        window.Closing += onclose;
                    }
                    window.Show();
                    window.Activate();
                });
            };

            running = true;
            ext.Run();
        }
        catch { }
        finally
        {
            running = false;
            await Task.Delay(2000);
            Dispatcher.Invoke(() => Shutdown());
        }
    }

    private void onclose(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!running)
        {
            Shutdown();
        }
        else
        {
            e.Cancel = true;
            if (sender is Window w)
                w.Hide();
        }
    }
}
