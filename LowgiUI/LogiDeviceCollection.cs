using LowgiCore;
using LowgiPrimitives.MessageStructs;
using MessagePipe;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows;

namespace LowgiUI
{
    public class LogiDeviceCollection : ILogiDeviceCollection
    {
        private readonly UserSettingsWrapper _userSettings;
        private readonly LogiDeviceViewModelFactory _logiDeviceViewModelFactory;
        private readonly LowBatteryNotificationService _lowBatteryNotificationService;
        private readonly ISubscriber<IPCMessage> _subscriber;

        public ObservableCollection<LogiDeviceViewModel> Devices { get; } = [];
        public IEnumerable<LogiDevice> GetDevices() => Devices;

        public LogiDeviceCollection(
            UserSettingsWrapper userSettings,
            LogiDeviceViewModelFactory logiDeviceViewModelFactory,
            LowBatteryNotificationService lowBatteryNotificationService,
            ISubscriber<IPCMessage> subscriber
        )
        {
            _userSettings = userSettings;
            _logiDeviceViewModelFactory = logiDeviceViewModelFactory;
            _lowBatteryNotificationService = lowBatteryNotificationService;
            _subscriber = subscriber;

            _subscriber.Subscribe(x =>
            {
                if (x is InitMessage initMessage)
                {
                    OnInitMessage(initMessage);
                }
                else if (x is UpdateMessage updateMessage)
                {
                    OnUpdateMessage(updateMessage);
                }
            });

            LoadPreviouslySelectedDevices();
        }

        private void LoadPreviouslySelectedDevices()
        {
            foreach (var deviceId in _userSettings.SelectedDevices)
            {
                if (string.IsNullOrEmpty(deviceId))
                {
                    continue;
                }

                Devices.Add(
                    _logiDeviceViewModelFactory.CreateViewModel((x) =>
                    {
                        x.DeviceId = deviceId!;
                        x.DeviceName = "Not Initialised";
                        if (_userSettings.TryGetCachedBatteryPercentage(deviceId!, out double batteryPercentage))
                        {
                            x.BatteryPercentage = batteryPercentage;
                        }
                        x.IsChecked = true;
                    })
                );
            }
        }

        public bool TryGetDevice(string deviceId, [NotNullWhen(true)] out LogiDevice? device)
        {
            device = Devices.SingleOrDefault(x => x.DeviceId == deviceId);

            return device != null;
        }

        public void OnInitMessage(InitMessage initMessage)
        {
            LogiDeviceViewModel? dev = Devices.SingleOrDefault(x => x.DeviceId == initMessage.deviceId);
            if (dev != null)
            {
                Application.Current.Dispatcher.BeginInvoke(() => dev.UpdateState(initMessage));

                return;
            }

            dev = _logiDeviceViewModelFactory.CreateViewModel((x) => x.UpdateState(initMessage));

            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                Devices.Add(dev);
                AutoSelectOnlyDevice();
            });
        }

        public void OnUpdateMessage(UpdateMessage updateMessage)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var device = Devices.FirstOrDefault(dev => dev.DeviceId == updateMessage.deviceId);
                if (device == null) { return; }

                device.UpdateState(updateMessage);
                _userSettings.SetCachedBatteryPercentage(device.DeviceId, device.BatteryPercentage);
                _lowBatteryNotificationService.CheckDevice(device);
            });
        }

        private void AutoSelectOnlyDevice()
        {
            if (Devices.Count != 1 || _userSettings.SelectedDevices.Cast<string>().Any(x => !string.IsNullOrEmpty(x)))
            {
                return;
            }

            var device = Devices[0];
            device.IsChecked = true;
            _userSettings.AddDevice(device.DeviceId);
        }
    }
}
