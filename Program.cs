using System.Diagnostics;

namespace PenPulse;

internal static class Program
{
    private static readonly string InstanceName = @"Local\PenPulse-" + Environment.UserName;
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var mutex = new Mutex(true, InstanceName, out bool first);
        using var show = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + "-show");
        using var quit = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + "-quit");
        if (args.Contains("--quit"))
        {
            quit.Set();
            return;
        }

        if (!first)
        {
            show.Set();
            return;
        }

        try
        {
            using var context = new PulseContext(args.Contains("--silent"), show, quit);
            Application.Run(context);
        }
        catch (Exception e)
        {
            MessageBox.Show("PenPulse could not start.\n\n" + e.Message, "PenPulse", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal sealed class PulseContext : ApplicationContext
{
    private readonly MainWindow form;
    private readonly RegisteredWaitHandle showWait, quitWait;
    public PulseContext(bool silent, EventWaitHandle show, EventWaitHandle quit)
    {
        form = new MainWindow();
        form.ExitRequested += ExitThread;
        _ = form.Handle;
        showWait = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => Post(form.ShowSettings), null, -1, false);
        quitWait = ThreadPool.RegisterWaitForSingleObject(quit, (_, _) => Post(ExitThread), null, -1, false);
        if (!silent)
            form.ShowSettings();
        void Post(Action action)
        {
            try
            {
                if (!form.IsDisposed)
                    form.BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    protected override void ExitThreadCore()
    {
        form.Shutdown();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            showWait.Unregister(null);
            quitWait.Unregister(null);
            form.Dispose();
        }

        base.Dispose(disposing);
    }
}
