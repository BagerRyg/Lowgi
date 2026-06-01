using CommunityToolkit.Mvvm.ComponentModel;
using LowgiPrimitives;
using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;

namespace LowgiUI
{
    public partial class UserSettingsWrapper : ObservableObject
    {
        public UserSettingsWrapper()
        {
            Properties.Settings.Default.LightTheme = CheckTheme.GetSystemLightTheme();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "")]
        public StringCollection SelectedDevices => Properties.Settings.Default.SelectedDevices;
        public StringCollection CachedBatteryPercentages => Properties.Settings.Default.CachedBatteryPercentages;

        public bool NumericDisplay
        {
            get => Properties.Settings.Default.NumericDisplay;
            set
            {
                Properties.Settings.Default.NumericDisplay = value;
                SaveSettings();

                OnPropertyChanged();
            }
        }

        public bool LowBatteryWarning
        {
            get => Properties.Settings.Default.LowBatteryWarning;
            set
            {
                Properties.Settings.Default.LowBatteryWarning = value;
                SaveSettings();

                OnPropertyChanged();
            }
        }

        public int LowBatteryWarningThreshold
        {
            get => Properties.Settings.Default.LowBatteryWarningThreshold;
            set
            {
                Properties.Settings.Default.LowBatteryWarningThreshold = value;
                SaveSettings();

                OnPropertyChanged();
            }
        }

        public LogiLedMode LedMode
        {
            get
            {
                return Enum.TryParse(Properties.Settings.Default.LedMode, true, out LogiLedMode ledMode)
                    ? ledMode
                    : LogiLedMode.Dynamic;
            }
            set
            {
                Properties.Settings.Default.LedMode = value.ToString();
                SaveSettings();

                OnPropertyChanged();
            }
        }

        public bool EnableLogging
        {
            get => Properties.Settings.Default.EnableLogging;
            set
            {
                Properties.Settings.Default.EnableLogging = value;
                SaveSettings();
                CrashLog.SetEnabled(value, "Lowgi.exe");

                OnPropertyChanged();
            }
        }

        public int BatteryPollingIntervalMinutes
        {
            get => Properties.Settings.Default.BatteryPollingIntervalMinutes;
            set
            {
                Properties.Settings.Default.BatteryPollingIntervalMinutes = value;
                SaveSettings();
                RuntimeSettings.SetBatteryPollingIntervalMinutes(value);

                OnPropertyChanged();
            }
        }

        public bool LightTheme
        {
            get => Properties.Settings.Default.LightTheme;
            set
            {
                Properties.Settings.Default.LightTheme = value;
                SaveSettings();
                TrayThemeManager.Apply(value);

                OnPropertyChanged();
            }
        }

        public void AddDevice(string deviceId)
        {
            if (Properties.Settings.Default.SelectedDevices.Contains(deviceId))
            {
                return;
            }

            Properties.Settings.Default.SelectedDevices.Add(deviceId);
            SaveSettings();

            OnPropertyChanged(nameof(SelectedDevices));
        }

        public void RemoveDevice(string deviceId)
        {
            Properties.Settings.Default.SelectedDevices.Remove(deviceId);
            SaveSettings();

            OnPropertyChanged(nameof(SelectedDevices));
        }

        public bool TryGetCachedBatteryPercentage(string deviceId, out double batteryPercentage)
        {
            batteryPercentage = -1;
            string prefix = $"{deviceId}|";
            string? cachedValue = CachedBatteryPercentages
                .Cast<string>()
                .LastOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal));

            return cachedValue != null
                && double.TryParse(cachedValue[prefix.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out batteryPercentage);
        }

        public void SetCachedBatteryPercentage(string deviceId, double batteryPercentage)
        {
            string prefix = $"{deviceId}|";
            for (int i = CachedBatteryPercentages.Count - 1; i >= 0; i--)
            {
                if (CachedBatteryPercentages[i]?.StartsWith(prefix, StringComparison.Ordinal) == true)
                {
                    CachedBatteryPercentages.RemoveAt(i);
                }
            }

            CachedBatteryPercentages.Add($"{prefix}{batteryPercentage.ToString(CultureInfo.InvariantCulture)}");
            SaveSettings();
        }

        private static void SaveSettings()
        {
            try
            {
                Properties.Settings.Default.Save();
            }
            catch (Exception ex) when (ex is System.Configuration.ConfigurationErrorsException or UnauthorizedAccessException)
            {
            }
        }
    }
}
