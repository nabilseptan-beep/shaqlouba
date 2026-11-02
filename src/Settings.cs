using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Shaqlouba
{
    internal struct Hotkey
    {
        public uint Modifiers;
        public Keys Key;

        public Hotkey(uint modifiers, Keys key)
        {
            Modifiers = modifiers;
            Key = key;
        }

        public override string ToString()
        {
            string text = "";
            if ((Modifiers & Native.MOD_WIN) != 0) text += "Win+";
            if ((Modifiers & Native.MOD_CONTROL) != 0) text += "Ctrl+";
            if ((Modifiers & Native.MOD_ALT) != 0) text += "Alt+";
            if ((Modifiers & Native.MOD_SHIFT) != 0) text += "Shift+";
            return text + KeyName(Key);
        }

        static string KeyName(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9) return ((char)key).ToString();
            return key.ToString();
        }
    }

    // Stored in %APPDATA%\Shaqlouba\settings.ini as key=value lines.
    internal sealed class Settings
    {
        static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shaqlouba");
        static readonly string FilePath = Path.Combine(Folder, "settings.ini");

        public Hotkey Hotkey = new Hotkey(Native.MOD_CONTROL | Native.MOD_SHIFT, Keys.Q);
        public bool SwitchLayout = true;
        public bool Welcomed;

        public static Settings Load()
        {
            var settings = new Settings();
            if (!File.Exists(FilePath)) return settings;

            var values = new Dictionary<string, string>();
            foreach (string line in File.ReadAllLines(FilePath))
            {
                int equals = line.IndexOf('=');
                if (equals > 0) values[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
            }

            uint modifiers;
            int key;
            string modifiersText, keyText;
            if (values.TryGetValue("hotkey_modifiers", out modifiersText) && uint.TryParse(modifiersText, out modifiers)
                && values.TryGetValue("hotkey_key", out keyText) && int.TryParse(keyText, out key))
            {
                settings.Hotkey = new Hotkey(modifiers, (Keys)key);
            }

            string flag;
            if (values.TryGetValue("switch_layout", out flag)) settings.SwitchLayout = flag != "0";
            if (values.TryGetValue("welcomed", out flag)) settings.Welcomed = flag == "1";
            return settings;
        }

        public void Save()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllLines(FilePath, new[]
            {
                "hotkey_modifiers=" + Hotkey.Modifiers,
                "hotkey_key=" + (int)Hotkey.Key,
                "switch_layout=" + (SwitchLayout ? "1" : "0"),
                "welcomed=" + (Welcomed ? "1" : "0"),
            });
        }
    }

    internal static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "Shaqlouba";

        public static bool IsEnabled
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return key != null && key.GetValue(ValueName) != null;
            }
        }

        public static void Set(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                else key.DeleteValue(ValueName, false);
            }
        }
    }
}
