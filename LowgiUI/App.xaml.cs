using LowgiCore;
using LowgiCore.Managers;
using LowgiPrimitives;
using LowgiPrimitives.IPC;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Tommy.Extensions.Configuration;

using static LowgiUI.AppExtensions;

namespace LowgiUI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\LowgiUI.SingleInstance";
    private Mutex? _singleInstanceMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        CrashLog.SetEnabled(LowgiUI.Properties.Settings.Default.EnableLogging, "Lowgi.exe");

        if (!TryAcquireSingleInstance())
        {
            CrashLog.WriteRunEvent("duplicate instance exit");
            Shutdown();
            return;
        }

        base.OnStartup(e);

        RuntimeSettings.SetBatteryPollingIntervalMinutes(LowgiUI.Properties.Settings.Default.BatteryPollingIntervalMinutes);
        CrashLog.WriteRunEvent("startup");
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            CrashLog.WriteRunEvent("process exit");
            CrashLog.StopHeartbeat();
        };
        AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CrashHandler);
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLog.Write(args.Exception, "DispatcherUnhandledException");
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLog.Write(args.Exception, "UnobservedTaskException");
            args.SetObserved();
        };

        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            EnableEfficiencyMode();

            var builder = Host.CreateEmptyApplicationBuilder(null);
            await LoadAppSettings(builder.Configuration);

            builder.Services.Configure<AppSettings>(builder.Configuration);
            builder.Services.AddLowgiMessagePipe(enableInterprocess: false);
            builder.Services.AddSingleton<UserSettingsWrapper>();

            builder.Services.AddSingleton<LogiDeviceIconFactory>();
            builder.Services.AddSingleton<LogiDeviceViewModelFactory>();
            builder.Services.AddSingleton<LowBatteryNotificationService>();

            if (builder.Configuration.Get<AppSettings>()?.Native.Enabled == true)
            {
                builder.Services.AddSingleton<LowgiNativeDeviceManager>();
                builder.Services.AddSingleton<IDeviceManager>(p => p.GetRequiredService<LowgiNativeDeviceManager>());
                builder.Services.AddSingleton<IHostedService>(p => p.GetRequiredService<LowgiNativeDeviceManager>());
            }
            builder.Services.AddIDeviceManager<GHubManager>(builder.Configuration);
            builder.Services.AddSingleton<ILogiDeviceCollection, LogiDeviceCollection>();

            builder.Services.AddSingleton<MainTaskbarIconWrapper>();
            builder.Services.AddHostedService<NotifyIconViewModel>();

            var host = builder.Build();
            await host.RunAsync();
            CrashLog.WriteRunEvent("host stopped");
            Dispatcher.InvokeShutdown();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "Startup");
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private bool TryAcquireSingleInstance()
    {
        try
        {
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
            if (createdNew)
            {
                return true;
            }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            return false;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "SingleInstance");
            return true;
        }
    }

    static async Task LoadAppSettings(Microsoft.Extensions.Configuration.ConfigurationManager config)
    {
        try
        {
            config.AddTomlStream(new MemoryStream(LowgiUI.Properties.Resources.defaultAppsettings));
            config.AddTomlFile("appsettings.toml", optional: true);
        }
        catch (Exception ex)
        {
            if (ex is InvalidDataException)
            {
                var msgBoxRet = MessageBox.Show(
                    "Failed to read settings, do you want reset to default?", 
                    "Lowgi - Settings Load Error", 
                    MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.No
                );

                if (msgBoxRet == MessageBoxResult.Yes)
                {
                    config.Sources.Clear();
                    config.AddTomlStream(new MemoryStream(LowgiUI.Properties.Resources.defaultAppsettings));
                }
            }
            else
            {
                throw;
            }
        }
    }

    private void CrashHandler(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
        {
            CrashLog.Write(exception, "UnhandledException");
        }
    }
}
