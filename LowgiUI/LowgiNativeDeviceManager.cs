using LowgiCore.Managers;
using LowgiHID;
using LowgiPrimitives;
using LowgiPrimitives.MessageStructs;
using MessagePipe;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LowgiUI;

public class LowgiNativeDeviceManager : IDeviceManager, IHostedService
{
    private readonly IPublisher<IPCMessage> _deviceEventBus;
    private readonly AppSettings _appSettings;
    private readonly CancellationTokenSource _cts = new();
    private bool _started;

    public LowgiNativeDeviceManager(IPublisher<IPCMessage> deviceEventBus, IOptions<AppSettings> appSettings)
    {
        _deviceEventBus = deviceEventBus;
        _appSettings = appSettings.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        GlobalSettings.settings = _appSettings.Native;

        try
        {
            HidppManagerContext.Instance.HidppDeviceEvent += OnHidppDeviceEvent;
            HidppManagerContext.Instance.Start(_cts.Token);
            _started = true;
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
        }

        _cts.Cancel();

        return Task.CompletedTask;
    }

    public async void RediscoverDevices()
    {
        if (_started)
        {
            await HidppManagerContext.Instance.ForceBatteryUpdates();
        }
    }

    private void OnHidppDeviceEvent(IPCMessageType messageType, IPCMessage message)
    {
        _deviceEventBus.Publish(message);
    }
}
