using System.IO;
using UnityEngine;

namespace OrbitalKeeper
{
    /// <summary>
    /// Global settings for the Orbital Keeper mod.
    /// Loaded from GameData/OrbitalKeeper/OrbitalKeeper.cfg via GameDatabase.
    /// </summary>
    public static class OrbitalKeepSettings
    {
        // --- Default values for new vessels ---

        /// <summary>Default tolerance percentage for new vessels.</summary>
        public static double DefaultTolerance { get; private set; } = 5.0;

        /// <summary>Default check interval in game seconds.</summary>
        public static double DefaultCheckInterval { get; private set; } = 3600.0;

        /// <summary>Default engine selection mode for new vessels.</summary>
        public static EngineSelectionMode DefaultEngineMode { get; private set; } = EngineSelectionMode.IgnitedOnly;

        // --- Resource consumption rates ---

        /// <summary>EC consumed per 1 m/s of delta-v spent on station-keeping.</summary>
        public static double ECPerDeltaV { get; private set; } = 10.0;

        // --- Safety limits ---

        /// <summary>Minimum altitude above atmosphere (meters) below which station-keeping warns.</summary>
        public static double MinSafeAltitudeMargin { get; private set; } = 10000.0;

        /// <summary>Maximum delta-v that can be applied in a single correction (m/s).
        /// Prevents catastrophic orbit changes from misconfiguration.</summary>
        public static double MaxCorrectionDeltaV { get; private set; } = 500.0;

        // --- Notification settings ---

        /// <summary>Whether to show on-screen messages for automatic corrections.</summary>
        public static bool ShowCorrectionMessages { get; private set; } = true;

        /// <summary>Whether to show warnings when resources are insufficient.</summary>
        public static bool ShowResourceWarnings { get; private set; } = true;

        /// <summary>Duration of on-screen messages in seconds.</summary>
        public static float MessageDuration { get; private set; } = 5.0f;

        // --- UI settings (saved per-user in PluginData/config.xml) ---

        /// <summary>UI font size. Range: 10 - 20. Default 12.</summary>
        public static int FontSize { get; set; } = 12;
        /// <summary>GUI toggle key. Default: O.</summary>
        public static KeyCode GuiToggleKey { get; set; } = KeyCode.O;
        /// <summary>Require Alt modifier for GUI toggle hotkey.</summary>
        public static bool GuiToggleAlt { get; set; } = true;
        /// <summary>Require Ctrl modifier for GUI toggle hotkey.</summary>
        public static bool GuiToggleCtrl { get; set; } = false;
        /// <summary>Require Shift modifier for GUI toggle hotkey.</summary>
        public static bool GuiToggleShift { get; set; } = false;
        /// <summary>Whether to register the stock toolbar button.</summary>
        public static bool EnableToolbarButton { get; set; } = false;

        private const int FONT_SIZE_MIN = 10;
        private const int FONT_SIZE_MAX = 20;
        private static bool _hasUserToolbarButtonOverride;
        private static bool _userToolbarButtonValue;

        /// <summary>
        /// Loads settings from GameDatabase. Called once on mod initialization.
        /// Searches for a config node named ORBITAL_KEEPER_SETTINGS.
        /// Also loads per-user UI settings from PluginData/config.xml.
        /// </summary>
        public static void LoadSettings()
        {
            // Load per-user settings (font size, window positions, etc.)
            LoadUserSettings();
            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("ORBITAL_KEEPER_SETTINGS");
            if (nodes == null || nodes.Length == 0)
            {
                Debug.Log("[OrbitalKeeper] No settings config found, using defaults.");
                return;
            }

            ConfigNode settings = nodes[0];

            DefaultTolerance = ParseDouble(settings, "defaultTolerance", DefaultTolerance);
            DefaultCheckInterval = ParseDouble(settings, "defaultCheckInterval", DefaultCheckInterval);
            ECPerDeltaV = ParseDouble(settings, "ecPerDeltaV", ECPerDeltaV);
            MinSafeAltitudeMargin = ParseDouble(settings, "minSafeAltitudeMargin", MinSafeAltitudeMargin);
            MaxCorrectionDeltaV = ParseDouble(settings, "maxCorrectionDeltaV", MaxCorrectionDeltaV);
            MessageDuration = (float)ParseDouble(settings, "messageDuration", MessageDuration);
            if (settings.HasValue("enableToolbarButton"))
            {
                bool.TryParse(settings.GetValue("enableToolbarButton"), out bool enableToolbarButton);
                EnableToolbarButton = enableToolbarButton;
            }
            if (_hasUserToolbarButtonOverride)
            {
                EnableToolbarButton = _userToolbarButtonValue;
            }

            if (settings.HasValue("defaultEngineMode"))
            {
                if (System.Enum.TryParse(settings.GetValue("defaultEngineMode"), out EngineSelectionMode mode))
                    DefaultEngineMode = mode;
            }

            if (settings.HasValue("showCorrectionMessages"))
            {
                bool.TryParse(settings.GetValue("showCorrectionMessages"), out bool showCorr);
                ShowCorrectionMessages = showCorr;
            }

            if (settings.HasValue("showResourceWarnings"))
            {
                bool.TryParse(settings.GetValue("showResourceWarnings"), out bool showRes);
                ShowResourceWarnings = showRes;
            }

            Debug.Log($"[OrbitalKeeper] Settings loaded: Tolerance={DefaultTolerance}%, CheckInterval={DefaultCheckInterval}s, EC/dV={ECPerDeltaV}, MaxDV={MaxCorrectionDeltaV}m/s");
        }

        // ======================================================================
        //  PER-USER SETTINGS (saved locally, not in save file)
        // ======================================================================

        /// <summary>Loads per-user UI settings from PluginData/config.xml.</summary>
        private static void LoadUserSettings()
        {
            _hasUserToolbarButtonOverride = false;
            string path = GetUserSettingsPath();
            if (File.Exists(path))
            {
                ConfigNode node = ConfigNode.Load(path);
                if (node != null && node.HasValue("FontSize"))
                {
                    int.TryParse(node.GetValue("FontSize"), out int size);
                    FontSize = size;
                }
                if (node != null && node.HasValue("GuiToggleKey"))
                {
                    if (System.Enum.TryParse(node.GetValue("GuiToggleKey"), true, out KeyCode key))
                        GuiToggleKey = key;
                }
                if (node != null && node.HasValue("GuiToggleAlt"))
                {
                    bool.TryParse(node.GetValue("GuiToggleAlt"), out bool alt);
                    GuiToggleAlt = alt;
                }
                if (node != null && node.HasValue("GuiToggleCtrl"))
                {
                    bool.TryParse(node.GetValue("GuiToggleCtrl"), out bool ctrl);
                    GuiToggleCtrl = ctrl;
                }
                if (node != null && node.HasValue("GuiToggleShift"))
                {
                    bool.TryParse(node.GetValue("GuiToggleShift"), out bool shift);
                    GuiToggleShift = shift;
                }
                if (node != null && node.HasValue("EnableToolbarButton"))
                {
                    bool.TryParse(node.GetValue("EnableToolbarButton"), out bool enableToolbarButton);
                    _hasUserToolbarButtonOverride = true;
                    _userToolbarButtonValue = enableToolbarButton;
                    EnableToolbarButton = enableToolbarButton;
                }
            }
            FontSize = System.Math.Max(FONT_SIZE_MIN, System.Math.Min(FONT_SIZE_MAX, FontSize));
            if (GuiToggleKey == KeyCode.None)
                GuiToggleKey = KeyCode.O;
        }

        /// <summary>Saves per-user UI settings to PluginData/config.xml.</summary>
        public static void SaveUserSettings()
        {
            string path = GetUserSettingsPath();
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var node = new ConfigNode("ORBITAL_KEEPER_USER_SETTINGS");
            node.AddValue("FontSize", FontSize);
            node.AddValue("GuiToggleKey", GuiToggleKey.ToString());
            node.AddValue("GuiToggleAlt", GuiToggleAlt);
            node.AddValue("GuiToggleCtrl", GuiToggleCtrl);
            node.AddValue("GuiToggleShift", GuiToggleShift);
            node.AddValue("EnableToolbarButton", EnableToolbarButton);
            node.Save(path);
        }

        private static string GetUserSettingsPath()
        {
            return Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "OrbitalKeeper", "PluginData", "config.xml");
        }

        private static double ParseDouble(ConfigNode node, string key, double defaultValue)
        {
            string val = node.GetValue(key);
            if (val != null && double.TryParse(val, out double result))
                return result;
            return defaultValue;
        }
    }
}
