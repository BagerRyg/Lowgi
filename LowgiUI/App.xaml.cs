using LowgiCore;
using LowgiCore.Managers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using System;
using LowgiPrimitives.IPC;
using System.Globalization;
using System.IO;
using System.Threading;
using LowgiPrimitives;
using Tommy.Extensions.Configuration;

using static LowgiUI.AppExtensions;
using System.Threading.Tasks;

namespace LowgiUI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CrashHandler);

        EnableEfficiencyMode();

        var builder = Host.CreateEmptyApplicationBuilder(null);
        await LoadAppSettings(builder.Configuration);

        builder.Services.Configure<AppSettings>(builder.Configuration);
        builder.Services.AddLowgiMessagePipe(true);
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
        Dispatcher.InvokeShutdown();
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
        Exception e = (Exception)args.ExceptionObject;
        long unixTime = DateTimeOffset.Now.ToUnixTimeSeconds();

        using StreamWriter writer = new($"./crashlog_{unixTime}.log", false);
        writer.WriteLine(e.ToString());
    }
}
