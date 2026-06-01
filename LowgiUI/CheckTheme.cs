using Microsoft.Win32;
using System.ComponentModel;

namespace LowgiUI
{
    public static class CheckTheme
    {
        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string RegistryValueName = "SystemUsesLightTheme";

        private static bool _lightTheme = true;
        public static bool LightTheme => _lightTheme;

        public static string ThemeSuffix
        {
            get
            {
                return LightTheme ? "" : "_dark";
            }
        }

        public static event PropertyChangedEventHandler? StaticPropertyChanged;

        public static void SetLightTheme(bool lightTheme)
        {
            _lightTheme = lightTheme;
            StaticPropertyChanged?.Invoke(typeof(CheckTheme), new(nameof(LightTheme)));
        }

        public static bool GetSystemLightTheme()
        {
            try
            {
                using var regPath = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
                return regPath?.GetValue(RegistryValueName, 0) is int regFlag && regFlag != 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
