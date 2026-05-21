using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;

namespace LowgiUI
{
    public partial class UserSettingsWrapper : ObservableObject
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "")]
        public StringCollection SelectedDevices => Properties.Settings.Default.SelectedDevices;
        public StringCollection CachedBatteryPercentages => Properties.Settings.Default.CachedBatteryPercentages;

        public bool NumericDisplay
        {
            get => Properties.Settings.Default.NumericDisplay;
            set
            {
                Properties.Settings.Default.NumericDisplay = value;
                Properties.Settings.Default.Save();

                OnPropertyChanged();
            }
        }

        public bool LowBatteryWarning
        {
            get => Properties.Settings.Default.LowBatteryWarning;
            set
            {
                Properties.Settings.Default.LowBatteryWarning = value;
                Properties.Settings.Default.Save();

                OnPropertyChanged();
            }
        }

        public int LowBatteryWarningThreshold
        {
            get => Properties.Settings.Default.LowBatteryWarningThreshold;
            set
            {
                Properties.Settings.Default.LowBatteryWarningThreshold = value;
                Properties.Settings.Default.Save();

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
            Properties.Settings.Default.Save();

            OnPropertyChanged(nameof(SelectedDevices));
        }

        public void RemoveDevice(string deviceId)
        {
            Properties.Settings.Default.SelectedDevices.Remove(deviceId);
            Properties.Settings.Default.Save();

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
            Properties.Settings.Default.Save();
        }
    }
}
