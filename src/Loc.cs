using KSP.Localization;

namespace OrbitalKeeper
{
    /// <summary>
    /// Static localization helper. Caches all localized strings on first access.
    /// Uses KSP's Localizer system with keys defined in GameData/OrbitalKeeper/Localization/*.cfg.
    /// </summary>
    public static class Loc
    {
        private static bool loaded;

        // ==============================
        //  Window Titles
        // ==============================
        public static string WindowTitle = "Orbital Keeper - Station Keeping";
        public static string FleetWindowTitle = "Vessel Overview (Selectable)";

        // ==============================
        //  General Labels
        // ==============================
        public static string NoVesselSelected = "No vessel selected.";
        public static string VesselLabel = "Vessel: <<1>>";
        public static string NoVesselData = "Unable to retrieve vessel data.";
        public static string ScenarioNotLoaded = "ScenarioModule not loaded.";
        public static string TrackedVessels = "Tracked vessels: <<1>>";
        public static string UnknownVessel = "[Unknown] <<1>>";

        // ==============================
        //  Status
        // ==============================
        public static string StatusLabel = "Status:";
        public static string StatusDisabled = "Disabled";
        public static string StatusNominal = "Nominal";
        public static string StatusDrifting = "Orbit Drifting";
        public static string StatusCorrecting = "Correcting";
        public static string StatusInsufficientRes = "Insufficient Resources";
        public static string StatusNoEngine = "No Usable Engine";
        public static string StatusInvalidOrbit = "Invalid Orbit";
        public static string StatusUnknown = "Unknown";
        // Fleet short
        public static string StatusShortNominal = "OK";
        public static string StatusShortDrifting = "Drifting";
        public static string StatusShortInsufficientRes = "Low Res";
        public static string StatusShortNoEngine = "No Engine";
        public static string StatusShortInvalidOrbit = "Invalid";
        public static string StatusShortAuto = "Auto";
        public static string StatusShortDisabled = "Off";

        // ==============================
        //  Current Orbit Section
        // ==============================
        public static string SectionCurrentOrbit = "--- Current Orbit ---";
        public static string Apoapsis = "Apoapsis (Ap)";
        public static string Periapsis = "Periapsis (Pe)";
        public static string Inclination = "Inclination (Inc)";
        public static string Eccentricity = "Eccentricity (Ecc)";
        public static string OrbitalPeriod = "Orbital Period";

        // ==============================
        //  Target Parameters Section
        // ==============================
        public static string SectionTargetOrbit = "--- Target Orbital Parameters ---";
        public static string SetFromCurrent = "Set From Current Orbit";
        public static string TargetAp = "Target Apoapsis (km)";
        public static string TargetPe = "Target Periapsis (km)";
        public static string TargetInc = "Target Inclination (°)";
        public static string TargetEcc = "Target Eccentricity";

        // ==============================
        //  Configuration Section
        // ==============================
        public static string SectionConfig = "--- Configuration ---";
        public static string AutoKeepToggle = "Enable Auto Station-Keeping";
        public static string ToleranceLabel = "Tolerance: <<1>>%";
        public static string CheckInterval = "Check Interval (sec)";
        public static string EngineModeLabel = "Engine Mode:";
        public static string EngineModeIgnited = "Ignited Only";
        public static string EngineModeActive = "Active Not Shutdown";
        public static string ApplySettings = "Apply Settings";
        public static string FontSizeLabel = "Font Size: <<1>>";

        // ==============================
        //  Action Section
        // ==============================
        public static string SectionActions = "--- Actions ---";
        public static string ManualCorrect = "Manual Correction";
        public static string RefreshStatus = "Refresh Status";
        public static string WarningLowPe = "Warning: Target periapsis below safe altitude (<<1>>)";

        // ==============================
        //  Statistics Section
        // ==============================
        public static string SectionStats = "--- Statistics ---";
        public static string TotalDvSpent = "Total Δv Spent";
        public static string TotalECSpent = "Total EC Spent";

        // ==============================
        //  Footer
        // ==============================
        public static string FleetOverview = "Vessel Overview";
        public static string RemoveKeeping = "Remove Station-Keeping";

        // ==============================
        //  Fleet View
        // ==============================
        public static string FleetInfoLine = "Ap: <<1>> | Pe: <<2>> | Δv: <<3>>m/s";
        public static string FleetBodyFilter = "Body:";
        public static string FleetBodyAll = "All Bodies";
        public static string DebrisOnly = "Debris Only";
        public static string DebrisAll = "All Vessels";
        public static string DebrisHide = "No Debris";

        // ==============================
        //  Screen Messages
        // ==============================
        public static string SettingsSaved = "[OrbitalKeeper] Settings saved.";
        public static string MsgNoEngine = "[OrbitalKeeper] <<1>>: No usable engine found.";
        public static string MsgInsufficientRes = "[OrbitalKeeper] <<1>>: Insufficient resources - <<2>>";
        public static string MsgCorrectionDone = "[OrbitalKeeper] <<1>>: Orbit correction complete (Δv=<<2>>m/s, EC=<<3>>, <<4>>)";
        public static string MsgInvalidOrbit = "[OrbitalKeeper] <<1>>: Vessel is not in a valid orbit.";
        public static string MsgNoCorrection = "[OrbitalKeeper] <<1>>: Orbit within tolerance, no correction needed.";

        // ==============================
        //  Correction Descriptions
        // ==============================
        public static string DescApDrift = "Ap drift(<<1>>m-><<2>>m)";
        public static string DescPeDrift = "Pe drift(<<1>>m-><<2>>m)";
        public static string DescEccDrift = "Ecc drift(<<1>>-><<2>>)";
        public static string DescIncDrift = "Inc drift(<<1>>°-><<2>>°)";

        // ==============================
        //  Resource Shortage
        // ==============================
        public static string ShortageEC = "EC insufficient(need <<1>>, have <<2>>)";
        public static string ShortagePropellant = "<<1>> insufficient(need <<2>>, have <<3>>)";

        // ==============================
        //  Units
        // ==============================
        public static string Unit_m = "m";
        public static string Unit_km = "km";
        public static string Unit_Mm = "Mm";
        public static string Unit_Gm = "Gm";
        public static string Unit_NA = "N/A";
        public static string TimeFormat_hms = "<<1>>h <<2>>m <<3>>s";
        public static string TimeFormat_ms = "<<1>>m <<2>>s";
        public static string TimeFormat_s = "<<1>>s";

        /// <summary>
        /// Loads all localized strings from KSP's Localizer system.
        /// Should be called once after the game loads localization configs.
        /// Safe to call multiple times; subsequent calls are no-ops.
        /// </summary>
        public static void Load()
        {
            if (loaded) return;
            loaded = true;

            // Window Titles
            WindowTitle = Get("#LOC_OrbKeep_WindowTitle", WindowTitle);
            FleetWindowTitle = Get("#LOC_OrbKeep_FleetWindowTitle", FleetWindowTitle);

            // General
            NoVesselSelected = Get("#LOC_OrbKeep_NoVesselSelected", NoVesselSelected);
            VesselLabel = Get("#LOC_OrbKeep_VesselLabel", VesselLabel);
            NoVesselData = Get("#LOC_OrbKeep_NoVesselData", NoVesselData);
            ScenarioNotLoaded = Get("#LOC_OrbKeep_ScenarioNotLoaded", ScenarioNotLoaded);
            TrackedVessels = Get("#LOC_OrbKeep_TrackedVessels", TrackedVessels);
            UnknownVessel = Get("#LOC_OrbKeep_UnknownVessel", UnknownVessel);

            // Status
            StatusLabel = Get("#LOC_OrbKeep_StatusLabel", StatusLabel);
            StatusDisabled = Get("#LOC_OrbKeep_StatusDisabled", StatusDisabled);
            StatusNominal = Get("#LOC_OrbKeep_StatusNominal", StatusNominal);
            StatusDrifting = Get("#LOC_OrbKeep_StatusDrifting", StatusDrifting);
            StatusCorrecting = Get("#LOC_OrbKeep_StatusCorrecting", StatusCorrecting);
            StatusInsufficientRes = Get("#LOC_OrbKeep_StatusInsufficientRes", StatusInsufficientRes);
            StatusNoEngine = Get("#LOC_OrbKeep_StatusNoEngine", StatusNoEngine);
            StatusInvalidOrbit = Get("#LOC_OrbKeep_StatusInvalidOrbit", StatusInvalidOrbit);
            StatusUnknown = Get("#LOC_OrbKeep_StatusUnknown", StatusUnknown);
            StatusShortNominal = Get("#LOC_OrbKeep_StatusShortNominal", StatusShortNominal);
            StatusShortDrifting = Get("#LOC_OrbKeep_StatusShortDrifting", StatusShortDrifting);
            StatusShortInsufficientRes = Get("#LOC_OrbKeep_StatusShortInsufficientRes", StatusShortInsufficientRes);
            StatusShortNoEngine = Get("#LOC_OrbKeep_StatusShortNoEngine", StatusShortNoEngine);
            StatusShortInvalidOrbit = Get("#LOC_OrbKeep_StatusShortInvalidOrbit", StatusShortInvalidOrbit);
            StatusShortAuto = Get("#LOC_OrbKeep_StatusShortAuto", StatusShortAuto);
            StatusShortDisabled = Get("#LOC_OrbKeep_StatusShortDisabled", StatusShortDisabled);

            // Current Orbit
            SectionCurrentOrbit = Get("#LOC_OrbKeep_SectionCurrentOrbit", SectionCurrentOrbit);
            Apoapsis = Get("#LOC_OrbKeep_Apoapsis", Apoapsis);
            Periapsis = Get("#LOC_OrbKeep_Periapsis", Periapsis);
            Inclination = Get("#LOC_OrbKeep_Inclination", Inclination);
            Eccentricity = Get("#LOC_OrbKeep_Eccentricity", Eccentricity);
            OrbitalPeriod = Get("#LOC_OrbKeep_OrbitalPeriod", OrbitalPeriod);

            // Target Parameters
            SectionTargetOrbit = Get("#LOC_OrbKeep_SectionTargetOrbit", SectionTargetOrbit);
            SetFromCurrent = Get("#LOC_OrbKeep_SetFromCurrent", SetFromCurrent);
            TargetAp = Get("#LOC_OrbKeep_TargetAp", TargetAp);
            TargetPe = Get("#LOC_OrbKeep_TargetPe", TargetPe);
            TargetInc = Get("#LOC_OrbKeep_TargetInc", TargetInc);
            TargetEcc = Get("#LOC_OrbKeep_TargetEcc", TargetEcc);

            // Configuration
            SectionConfig = Get("#LOC_OrbKeep_SectionConfig", SectionConfig);
            AutoKeepToggle = Get("#LOC_OrbKeep_AutoKeepToggle", AutoKeepToggle);
            ToleranceLabel = Get("#LOC_OrbKeep_ToleranceLabel", ToleranceLabel);
            CheckInterval = Get("#LOC_OrbKeep_CheckInterval", CheckInterval);
            EngineModeLabel = Get("#LOC_OrbKeep_EngineModeLabel", EngineModeLabel);
            EngineModeIgnited = Get("#LOC_OrbKeep_EngineModeIgnited", EngineModeIgnited);
            EngineModeActive = Get("#LOC_OrbKeep_EngineModeActive", EngineModeActive);
            ApplySettings = Get("#LOC_OrbKeep_ApplySettings", ApplySettings);
            FontSizeLabel = Get("#LOC_OrbKeep_FontSizeLabel", FontSizeLabel);

            // Actions
            SectionActions = Get("#LOC_OrbKeep_SectionActions", SectionActions);
            ManualCorrect = Get("#LOC_OrbKeep_ManualCorrect", ManualCorrect);
            RefreshStatus = Get("#LOC_OrbKeep_RefreshStatus", RefreshStatus);
            WarningLowPe = Get("#LOC_OrbKeep_WarningLowPe", WarningLowPe);

            // Statistics
            SectionStats = Get("#LOC_OrbKeep_SectionStats", SectionStats);
            TotalDvSpent = Get("#LOC_OrbKeep_TotalDvSpent", TotalDvSpent);
            TotalECSpent = Get("#LOC_OrbKeep_TotalECSpent", TotalECSpent);

            // Footer
            FleetOverview = Get("#LOC_OrbKeep_FleetOverview", FleetOverview);
            RemoveKeeping = Get("#LOC_OrbKeep_RemoveKeeping", RemoveKeeping);

            // Fleet
            FleetInfoLine = Get("#LOC_OrbKeep_FleetInfoLine", FleetInfoLine);
            FleetBodyFilter = Get("#LOC_OrbKeep_FleetBodyFilter", FleetBodyFilter);
            FleetBodyAll = Get("#LOC_OrbKeep_FleetBodyAll", FleetBodyAll);
            DebrisOnly = Get("#LOC_OrbKeep_DebrisOnly", DebrisOnly);
            DebrisAll = Get("#LOC_OrbKeep_DebrisAll", DebrisAll);
            DebrisHide = Get("#LOC_OrbKeep_DebrisHide", DebrisHide);

            // Screen Messages
            SettingsSaved = Get("#LOC_OrbKeep_SettingsSaved", SettingsSaved);
            MsgNoEngine = Get("#LOC_OrbKeep_MsgNoEngine", MsgNoEngine);
            MsgInsufficientRes = Get("#LOC_OrbKeep_MsgInsufficientRes", MsgInsufficientRes);
            MsgCorrectionDone = Get("#LOC_OrbKeep_MsgCorrectionDone", MsgCorrectionDone);
            MsgInvalidOrbit = Get("#LOC_OrbKeep_MsgInvalidOrbit", MsgInvalidOrbit);
            MsgNoCorrection = Get("#LOC_OrbKeep_MsgNoCorrection", MsgNoCorrection);

            // Correction Descriptions
            DescApDrift = Get("#LOC_OrbKeep_DescApDrift", DescApDrift);
            DescPeDrift = Get("#LOC_OrbKeep_DescPeDrift", DescPeDrift);
            DescEccDrift = Get("#LOC_OrbKeep_DescEccDrift", DescEccDrift);
            DescIncDrift = Get("#LOC_OrbKeep_DescIncDrift", DescIncDrift);

            // Resource Shortage
            ShortageEC = Get("#LOC_OrbKeep_ShortageEC", ShortageEC);
            ShortagePropellant = Get("#LOC_OrbKeep_ShortagePropellant", ShortagePropellant);

            // Units
            Unit_m = Get("#LOC_OrbKeep_Unit_m", Unit_m);
            Unit_km = Get("#LOC_OrbKeep_Unit_km", Unit_km);
            Unit_Mm = Get("#LOC_OrbKeep_Unit_Mm", Unit_Mm);
            Unit_Gm = Get("#LOC_OrbKeep_Unit_Gm", Unit_Gm);
            Unit_NA = Get("#LOC_OrbKeep_Unit_NA", Unit_NA);
            TimeFormat_hms = Get("#LOC_OrbKeep_TimeFormat_hms", TimeFormat_hms);
            TimeFormat_ms = Get("#LOC_OrbKeep_TimeFormat_ms", TimeFormat_ms);
            TimeFormat_s = Get("#LOC_OrbKeep_TimeFormat_s", TimeFormat_s);
        }

        /// <summary>
        /// Helper: tries Localizer.GetStringByTag; falls back to defaultValue.
        /// </summary>
        private static string Get(string tag, string defaultValue)
        {
            if (Localizer.TryGetStringByTag(tag, out string result))
                return result;
            return defaultValue;
        }

        /// <summary>
        /// Convenience wrapper for Localizer.Format with arguments.
        /// Replaces &lt;&lt;1&gt;&gt;, &lt;&lt;2&gt;&gt;, ... placeholders.
        /// </summary>
        public static string Format(string template, params object[] args)
        {
            return Localizer.Format(template, args);
        }
    }
}
