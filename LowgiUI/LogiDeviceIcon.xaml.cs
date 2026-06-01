using Hardcodet.Wpf.TaskbarNotification;
using LowgiCore;
using LowgiPrimitives;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;

namespace LowgiUI
{
    public class LogiDeviceIconFactory
    {
        private readonly AppSettings _appSettings;
        private readonly UserSettingsWrapper _userSettings;

        public LogiDeviceIconFactory(IOptions<AppSettings> appSettings, UserSettingsWrapper userSettings)
        {
            _appSettings = appSettings.Value;
            _userSettings = userSettings;
        }

        public LogiDeviceIcon CreateDeviceIcon(LogiDevice device, Action<LogiDeviceIcon>? config = null)
        {
            LogiDeviceIcon output = new(device, _appSettings, _userSettings);
            config?.Invoke(output);

            return output;
        }
    }

    public partial class LogiDeviceIcon : UserControl, IDisposable
    {
        #region IDisposable
        private bool disposedValue;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _device.PropertyChanged -= LogiDevicePropertyChanged;
                    _userSettings.PropertyChanged -= NotifyIconViewModelPropertyChanged;
                    CheckTheme.StaticPropertyChanged -= CheckThemePropertyChanged;
                    _activeDeviceIcons.Remove(this);
                    SubRef();
                    taskbarIcon.ContextMenu.Opened -= MainTaskBarIcon.PositionContextMenu;
                }

                disposedValue = true;
                taskbarIcon.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion

        private static int _refCount = 0;
        private static readonly List<LogiDeviceIcon> _activeDeviceIcons = [];
        public static int RefCount => _refCount;

        public static void AddRef()
        {
            _refCount++;
            RefCountChanged?.Invoke(RefCount);
        }

        public static void SubRef()
        {
            _refCount--;
            RefCountChanged?.Invoke(RefCount);
        }

        public static event Action<int>? RefCountChanged;

        public static bool TryShowWarning(LogiDevice device, string title, string message)
        {
            LogiDeviceIcon? icon = _activeDeviceIcons.FirstOrDefault(x =>
                x.DataContext is LogiDevice activeDevice
                && activeDevice.DeviceId == device.DeviceId);
            if (icon == null)
            {
                return false;
            }

            icon.DrawBatteryIconNow();
            icon.taskbarIcon.ShowBalloonTip(title, message, BalloonIcon.Warning);
            return true;
        }

        private Action<TaskbarIcon, LogiDevice> _drawBatteryIcon;
        private readonly LogiDevice _device;
        private readonly UserSettingsWrapper _userSettings;

        public LogiDeviceIcon(LogiDevice device, AppSettings appSettings, UserSettingsWrapper userSettings)
        {
            InitializeComponent();

            if (!appSettings.UI.EnableRichToolTips)
                taskbarIcon.TrayToolTip = null;

            taskbarIcon.ContextMenu.Opened += MainTaskBarIcon.PositionContextMenu;
            AddRef();
            _activeDeviceIcons.Add(this);

            DataContext = device;
            _device = device;
            _userSettings = userSettings;

            device.PropertyChanged += LogiDevicePropertyChanged;
            userSettings.PropertyChanged += NotifyIconViewModelPropertyChanged;
            CheckTheme.StaticPropertyChanged += CheckThemePropertyChanged;
            _drawBatteryIcon = userSettings.NumericDisplay ? DrawNumericIcon : BatteryIconDrawing.DrawIcon;
            DrawBatteryIcon();
        }

        private void CheckThemePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            DrawBatteryIcon();
        }

        private void NotifyIconViewModelPropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            if (s is not UserSettingsWrapper userSettings)
            {
                return;
            }

            if (e.PropertyName == nameof(UserSettingsWrapper.NumericDisplay))
            {
                _drawBatteryIcon = userSettings.NumericDisplay ? DrawNumericIcon : BatteryIconDrawing.DrawIcon;
                DrawBatteryIcon();
            }
            else if (e.PropertyName == nameof(UserSettingsWrapper.LowBatteryWarningThreshold))
            {
                DrawBatteryIcon();
            }
        }

        private void LogiDevicePropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            if (s is not LogiDevice)
            {
                return;
            }
            else if (e.PropertyName is nameof(LogiDevice.BatteryPercentage) or nameof(LogiDevice.PowerSupplyStatus))
            {
                DrawBatteryIcon();
            }
        }

        private void DrawBatteryIcon()
        {
            _ = Dispatcher.BeginInvoke(DrawBatteryIconNow);
        }

        private void DrawBatteryIconNow()
        {
            _drawBatteryIcon(taskbarIcon, (LogiDevice)DataContext);
        }

        private void DrawNumericIcon(TaskbarIcon taskbarIcon, LogiDevice device)
        {
            BatteryIconDrawing.DrawNumeric(taskbarIcon, device, _userSettings.LowBatteryWarningThreshold);
        }
    }
}
