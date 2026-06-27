using LowgiCore.Managers;
using LowgiHID;
using LowgiPrimitives;
using LowgiPrimitives.MessageStructs;
using MessagePipe;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LowgiUI;

public class LowgiNativeDeviceManager : IDeviceManager, IHostedService
{
    private static readonly TimeSpan LedModeReapplyInterval = TimeSpan.FromSeconds(15);

    private readonly IPublisher<IPCMessage> _deviceEventBus;
    private readonly AppSettings _appSettings;
    private readonly UserSettingsWrapper _userSettings;
    private readonly CancellationTokenSource _cts = new();
    private bool _started;

    public LowgiNativeDeviceManager(IPublisher<IPCMessage> deviceEventBus, IOptions<AppSettings> appSettings, UserSettingsWrapper userSettings)
    {
        _deviceEventBus = deviceEventBus;
        _appSettings = appSettings.Value;
        _userSettings = userSettings;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        GlobalSettings.settings = _appSettings.Native;
        GlobalSettings.settings.PollPeriod = RuntimeSettings.BatteryPollingIntervalSeconds;

        try
        {
            HidppManagerContext.Instance.HidppDeviceEvent += OnHidppDeviceEvent;
            HidppManagerContext.Instance.Start(_cts.Token);
            _userSettings.PropertyChanged += UserSettingsPropertyChanged;
            _started = true;
            _ = ReapplyLedModeLoop(_cts.Token);
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException)
        {
            _started = false;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            HidppManagerContext.Instance.HidppDeviceEvent -= OnHidppDeviceEvent;
            _userSettings.PropertyChanged -= UserSettingsPropertyChanged;
        }

        _cts.Cancel();

        return Task.CompletedTask;
    }

    public async void RediscoverDevices()
    {
        if (_started)
        {
            await HidppManagerContext.Instance.ForceBatteryUpdates();
            await ApplyLedModeAsync(refreshBattery: false, refreshLed: true);
        }
    }

    private void OnHidppDeviceEvent(IPCMessageType messageType, IPCMessage message)
    {
        _deviceEventBus.Publish(message);

        if (message is InitMessage)
        {
            _ = RefreshBatteryAndLedMode();
        }
        else if (message is UpdateMessage)
        {
            _ = ApplyLedModeAsync(refreshBattery: false);
        }
    }

    private void UserSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UserSettingsWrapper.LedMode))
        {
            _ = ApplyLedModeAsync(refreshLed: true);
        }
        else if (e.PropertyName is nameof(UserSettingsWrapper.LowBatteryWarningThreshold)
            or nameof(UserSettingsWrapper.SelectedDevices))
        {
            _ = ApplyLedModeAsync();
        }
        else if (e.PropertyName is nameof(UserSettingsWrapper.BatteryPollingIntervalMinutes))
        {
            GlobalSettings.settings.PollPeriod = RuntimeSettings.BatteryPollingIntervalSeconds;
        }
    }

    private Task ApplyLedModeAsync(bool refreshBattery = true, bool refreshLed = false)
    {
        if (!_started)
        {
            return Task.CompletedTask;
        }

        return HidppManagerContext.Instance.ApplyLedMode(
            _userSettings.LedMode,
            _userSettings.LowBatteryWarningThreshold,
            _userSettings.SelectedDevices.Cast<string>().Where(x => !string.IsNullOrEmpty(x)),
            refreshBattery,
            refreshLed);
    }

    private async Task RefreshBatteryAndLedMode()
    {
        if (!_started)
        {
            return;
        }

        await HidppManagerContext.Instance.ForceBatteryUpdates();
        await ApplyLedModeAsync(refreshBattery: false);
    }

    private async Task ReapplyLedModeLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(LedModeReapplyInterval, cancellationToken);
                await ApplyLedModeAsync(refreshBattery: false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex, "LED reapply loop");
            }
        }
    }
}
