using Hardcodet.Wpf.TaskbarNotification;
using LowgiCore;
using LowgiPrimitives;
using System;
using System.Collections.Generic;
using System.Windows;

namespace LowgiUI;

public class LowBatteryNotificationService
{
    private readonly MainTaskbarIconWrapper _mainTaskbarIconWrapper;
    private readonly UserSettingsWrapper _userSettings;
    private readonly Dictionary<string, bool> _warningActive = [];

    public LowBatteryNotificationService(MainTaskbarIconWrapper mainTaskbarIconWrapper, UserSettingsWrapper userSettings)
    {
        _mainTaskbarIconWrapper = mainTaskbarIconWrapper;
        _userSettings = userSettings;
    }

    public void CheckDevice(LogiDevice device)
    {
        if (!_userSettings.LowBatteryWarning || device.BatteryPercentage < 0)
        {
            return;
        }

        bool isLow = device.HasBattery
            && device.BatteryPercentage <= _userSettings.LowBatteryWarningThreshold
            && device.PowerSupplyStatus != PowerSupplyStatus.POWER_SUPPLY_STATUS_CHARGING;

        if (!isLow)
        {
            _warningActive[device.DeviceId] = false;
            return;
        }

        if (_warningActive.TryGetValue(device.DeviceId, out bool alreadyWarned) && alreadyWarned)
        {
            return;
        }

        _warningActive[device.DeviceId] = true;
        string percentage = Math.Round(device.BatteryPercentage).ToString("0");
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _mainTaskbarIconWrapper.ShowWarning(
                "Low mouse battery",
                $"{device.DeviceName} battery is at {percentage}%."
            );
        });
    }
}
