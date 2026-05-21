using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LowgiCore;
using LowgiCore.Managers;
using LowgiPrimitives;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LowgiUI
{
    public partial class NotifyIconViewModel : ObservableObject, IHostedService
    {
        private readonly MainTaskbarIconWrapper _mainTaskbarIconWrapper;

        [ObservableProperty]
        private ObservableCollection<LogiDeviceViewModel> _logiDevices;

        public ObservableCollection<DeviceMenuEntry> DeviceMenuEntries { get; } = [];

        private readonly UserSettingsWrapper _userSettings;
        public bool NumericDisplay
        {
            get
            {
                return _userSettings.NumericDisplay;
            }

            set
            {
                _userSettings.NumericDisplay = value;
                OnPropertyChanged();
            }
        }

        public bool LowBatteryWarning
        {
            get => _userSettings.LowBatteryWarning;
            set
            {
                _userSettings.LowBatteryWarning = value;
                OnPropertyChanged();
            }
        }

        public bool LowBatteryWarningThreshold2
        {
            get => _userSettings.LowBatteryWarningThreshold == 2;
            set => SetLowBatteryWarningThreshold(value, 2);
        }

        public bool LowBatteryWarningThreshold5
        {
            get => _userSettings.LowBatteryWarningThreshold == 5;
            set => SetLowBatteryWarningThreshold(value, 5);
        }

        public bool LowBatteryWarningThreshold10
        {
            get => _userSettings.LowBatteryWarningThreshold == 10;
            set => SetLowBatteryWarningThreshold(value, 10);
        }

        public bool LowBatteryWarningThreshold15
        {
            get => _userSettings.LowBatteryWarningThreshold == 15;
            set => SetLowBatteryWarningThreshold(value, 15);
        }

        public bool LowBatteryWarningThreshold20
        {
            get => _userSettings.LowBatteryWarningThreshold == 20;
            set => SetLowBatteryWarningThreshold(value, 20);
        }

        private void SetLowBatteryWarningThreshold(bool isChecked, int threshold)
        {
            if (!isChecked)
            {
                return;
            }

            _userSettings.LowBatteryWarningThreshold = threshold;
            OnPropertyChanged(nameof(LowBatteryWarningThreshold2));
            OnPropertyChanged(nameof(LowBatteryWarningThreshold5));
            OnPropertyChanged(nameof(LowBatteryWarningThreshold10));
            OnPropertyChanged(nameof(LowBatteryWarningThreshold15));
            OnPropertyChanged(nameof(LowBatteryWarningThreshold20));
        }

        public bool LedModeWhite
        {
            get => _userSettings.LedMode == LogiLedMode.White;
            set => SetLedMode(value, LogiLedMode.White);
        }

        public bool LedModeGrey
        {
            get => _userSettings.LedMode == LogiLedMode.Grey;
            set => SetLedMode(value, LogiLedMode.Grey);
        }

        public bool LedModeLowBattery
        {
            get => _userSettings.LedMode == LogiLedMode.LowBattery;
            set => SetLedMode(value, LogiLedMode.LowBattery);
        }

        public bool LedModeOff
        {
            get => _userSettings.LedMode == LogiLedMode.Off;
            set => SetLedMode(value, LogiLedMode.Off);
        }

        private void SetLedMode(bool isChecked, LogiLedMode mode)
        {
            if (!isChecked)
            {
                return;
            }

            _userSettings.LedMode = mode;
            OnPropertyChanged(nameof(LedModeWhite));
            OnPropertyChanged(nameof(LedModeGrey));
            OnPropertyChanged(nameof(LedModeLowBattery));
            OnPropertyChanged(nameof(LedModeOff));
        }

        public static string AssemblyVersion
        {
            get
            {
                return "v" + Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "Missing";
            }
        }

        private const string AutoStartRegKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AutoStartRegKeyValue = "LowgiGUI";
        private bool? _autoStart = null;
        public bool AutoStart
        {
            get
            {
                if (_autoStart == null)
                {
                    try
                    {
                        using RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey(AutoStartRegKey, false);
                        _autoStart = registryKey?.GetValue(AutoStartRegKeyValue) != null;
                    }
                    catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
                    {
                        _autoStart = false;
                    }
                }

                return _autoStart ?? false;
            }
            set
            {
                try
                {
                    using RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey(AutoStartRegKey, true);

                    if (registryKey == null)
                    {
                        return;
                    }

                    if (value)
                    {
                        registryKey.SetValue(AutoStartRegKeyValue, Environment.ProcessPath!);
                    }
                    else
                    {
                        registryKey.DeleteValue(AutoStartRegKeyValue, false);
                    }

                    _autoStart = value;
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
                {
                    _autoStart = false;
                }
            }
        }

        [ObservableProperty]
        private bool _rediscoverDevicesEnabled = true;

        [ObservableProperty]
        private string _quitHeader = "Quit";

        private bool _quitConfirmationRequired;
        private int _quitConfirmationVersion;

        private readonly IEnumerable<IDeviceManager> _deviceManagers;

        public NotifyIconViewModel(
            MainTaskbarIconWrapper mainTaskbarIconWrapper,
            ILogiDeviceCollection logiDeviceCollection,
            UserSettingsWrapper userSettings,
            IEnumerable<IDeviceManager> deviceManagers
        )
        {
            _mainTaskbarIconWrapper = mainTaskbarIconWrapper;
            ((ContextMenu)Application.Current.FindResource("SysTrayMenu")).DataContext = this;

            _logiDevices = (logiDeviceCollection as LogiDeviceCollection)!.Devices;
            _userSettings = userSettings;
            _deviceManagers = deviceManagers;

            _logiDevices.CollectionChanged += LogiDevicesCollectionChanged;
            SyncDeviceMenuEntries();
        }

        private void LogiDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            SyncDeviceMenuEntries();
        }

        private void SyncDeviceMenuEntries()
        {
            DeviceMenuEntries.Clear();

            foreach (var device in LogiDevices)
            {
                DeviceMenuEntries.Add(DeviceMenuEntry.ForDevice(device));
            }

            if (DeviceMenuEntries.Any())
            {
                DeviceMenuEntries.Add(DeviceMenuEntry.Separator());
            }

            DeviceMenuEntries.Add(DeviceMenuEntry.Rediscover());
        }

        [RelayCommand]
        private void QuitApplication()
        {
            if (_quitConfirmationRequired)
            {
                Environment.Exit(0);
                return;
            }

            _quitConfirmationRequired = true;
            int version = ++_quitConfirmationVersion;
            QuitHeader = "Confirm to Quit";

            _ = ResetQuitConfirmationAsync(version);
        }

        private async Task ResetQuitConfirmationAsync(int version)
        {
            await Task.Delay(5_000);

            if (version != _quitConfirmationVersion || !_quitConfirmationRequired)
            {
                return;
            }

            _quitConfirmationRequired = false;
            await Application.Current.Dispatcher.InvokeAsync(() => QuitHeader = "Quit");
        }

        [RelayCommand]
        private void DeviceClicked(object? sender)
        {
            if (sender is not MenuItem menuItem)
            {
                return;
            }

            LogiDevice logiDevice = (LogiDevice)menuItem.DataContext;

            if (menuItem.IsChecked)
            {
                _userSettings.AddDevice(logiDevice.DeviceId);
            }
            else
            {
                _userSettings.RemoveDevice(logiDevice.DeviceId);
            }
        }

        [RelayCommand]
        private async Task DeviceMenuEntryClicked(DeviceMenuEntry entry)
        {
            if (entry.IsRediscover)
            {
                await RediscoverDevices();
                return;
            }

            if (entry.Device == null)
            {
                return;
            }

            if (entry.Device.IsChecked)
            {
                _userSettings.AddDevice(entry.Device.DeviceId);
            }
            else
            {
                _userSettings.RemoveDevice(entry.Device.DeviceId);
            }
        }

        [RelayCommand]
        private async Task RediscoverDevices()
        {
            Console.WriteLine("Rediscover");
            RediscoverDevicesEnabled = false;

            foreach (var manager in _deviceManagers)
            {
                manager.RediscoverDevices();
            }

            await Task.Delay(10_000);

            RediscoverDevicesEnabled = true;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _mainTaskbarIconWrapper.Dispose();
            return Task.CompletedTask;
        }
    }

    public class DeviceMenuEntry
    {
        public LogiDeviceViewModel? Device { get; private init; }
        public string Header { get; private init; } = string.Empty;
        public bool IsDevice => Device != null;
        public bool IsSeparator { get; private init; }
        public bool IsRediscover { get; private init; }
        public bool IsEnabled => !IsSeparator;

        public static DeviceMenuEntry ForDevice(LogiDeviceViewModel device) => new()
        {
            Device = device,
            Header = device.DeviceName
        };

        public static DeviceMenuEntry Separator() => new()
        {
            IsSeparator = true
        };

        public static DeviceMenuEntry Rediscover() => new()
        {
            Header = "Rediscover Devices",
            IsRediscover = true
        };
    }
}
