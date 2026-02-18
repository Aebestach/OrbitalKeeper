using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.UI.Screens;

namespace OrbitalKeeper
{
    /// <summary>
    /// Main GUI window for Orbital Keeper.
    /// Provides UI for configuring station-keeping parameters, viewing status,
    /// and manually triggering corrections. Available in Flight and Tracking Station scenes.
    /// All user-facing strings are localized via the Loc helper class.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public class OrbitalKeepUI : MonoBehaviour
    {
        // --- GUI state ---
        private bool guiVisible;
        private bool showFleetView;
        private Rect windowRect = new Rect(300, 200, 420, 0);
        private Rect fleetWindowRect = new Rect(730, 200, 350, 0);
        private Vector2 fleetScrollPos;
        private DebrisVisibility debrisVisibility = DebrisVisibility.All;
        private DebrisVisibility lastDebrisVisibility = DebrisVisibility.All;
        private double lastFleetPopulateTime = -1;
        private List<FleetEntry> cachedFleetEntries = new List<FleetEntry>();
        private int cachedFilteredCount;

        private const int WINDOW_ID = 0x4F4B_0001; // "OK" prefix
        private const int FLEET_WINDOW_ID = 0x4F4B_0002;
        private const float BASE_FONT_SIZE = 12f;
        private const float BASE_MAIN_WIDTH = 420f;
        private const float BASE_FLEET_WIDTH = 350f;

        // --- AppLauncher ---
        private ApplicationLauncherButton appButton;

        // --- Target vessel (for Flight scene) ---
        private Vessel targetVessel;
        private VesselKeepData editData;

        // --- Input field strings ---
        private string inputAp = "0";
        private string inputPe = "0";
        private string inputInc = "0";
        private string inputEcc = "0";
        private string inputInterval = "3600";
        private float toleranceSlider = 5f;
        private float fontSizeSlider;
        private bool lastLowPeWarning;
        private bool needsLayoutRecalc;

        // --- Tracking station selection ---
        private Vessel trackingStationVessel;

        // --- Cached GUIStyles (rebuilt when font size changes) ---
        private static int _cachedFontSize;
        private static GUIStyle _labelStyle;
        private static GUIStyle _boldStyle;
        private static GUIStyle _richStyle;
        private static GUIStyle _buttonStyle;
        private static GUIStyle _toggleStyle;
        private static GUIStyle _textFieldStyle;
        private static GUIStyle _boxStyle;
        private static GUIStyle _fleetBoxStyle;
        private static GUIStyle _windowStyle;
        private static GUIStyle _hSliderStyle;
        private static GUIStyle _hSliderThumbStyle;

        private void Start()
        {
            // Only activate in Flight and TrackingStation scenes
            if (HighLogic.LoadedScene != GameScenes.FLIGHT &&
                HighLogic.LoadedScene != GameScenes.TRACKSTATION)
            {
                Destroy(this);
                return;
            }

            // Ensure localization strings are loaded
            Loc.Load();

            // Initialize font size slider from saved setting
            fontSizeSlider = OrbitalKeepSettings.FontSize;
            windowRect.width = GetMainMinWidth();
            fleetWindowRect.width = GetFleetMinWidth();

            // Register AppLauncher button
            GameEvents.onGUIApplicationLauncherReady.Add(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherUnreadifying.Add(OnAppLauncherUnready);

            // Track vessel selection in tracking station
            GameEvents.onPlanetariumTargetChanged.Add(OnMapTargetChanged);
        }

        private void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherUnreadifying.Remove(OnAppLauncherUnready);
            GameEvents.onPlanetariumTargetChanged.Remove(OnMapTargetChanged);

            if (appButton != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(appButton);
                appButton = null;
            }
        }

        // ======================================================================
        //  APP LAUNCHER
        // ======================================================================

        private void OnAppLauncherReady()
        {
            if (appButton != null)
                return;

            appButton = ApplicationLauncher.Instance.AddModApplication(
                OnToolbarOn, OnToolbarOff,
                null, null, null, null,
                ApplicationLauncher.AppScenes.FLIGHT |
                ApplicationLauncher.AppScenes.TRACKSTATION |
                ApplicationLauncher.AppScenes.MAPVIEW,
                GameDatabase.Instance.GetTexture("OrbitalKeeper/Textures/icon_toolbar", false)
            );
        }

        private void OnAppLauncherUnready(GameScenes scene)
        {
            if (appButton != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(appButton);
                appButton = null;
            }
        }

        private void OnToolbarOn()
        {
            guiVisible = true;
            RefreshVessel();
        }

        private void OnToolbarOff()
        {
            guiVisible = false;
        }

        private void OnMapTargetChanged(MapObject mapObject)
        {
            if (mapObject != null && mapObject.type == MapObject.ObjectType.Vessel)
            {
                trackingStationVessel = mapObject.vessel;
                RefreshVessel();
            }
        }

        // ======================================================================
        //  VESSEL SELECTION
        // ======================================================================

        private void RefreshVessel()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                targetVessel = FlightGlobals.ActiveVessel;
            }
            else if (HighLogic.LoadedScene == GameScenes.TRACKSTATION)
            {
                targetVessel = trackingStationVessel;
            }

            if (targetVessel != null && StationKeepScenario.Instance != null)
            {
                editData = StationKeepScenario.Instance.GetOrCreateVesselData(targetVessel);
                SyncInputFields();
            }
        }

        private void SyncInputFields()
        {
            if (editData == null) return;
            inputAp = (editData.TargetApoapsis / 1000.0).ToString("F3"); // Display in km
            inputPe = (editData.TargetPeriapsis / 1000.0).ToString("F3");
            inputInc = editData.TargetInclination.ToString("F2");
            inputEcc = editData.TargetEccentricity.ToString("F6");
            inputInterval = editData.CheckInterval.ToString("F0");
            toleranceSlider = (float)editData.Tolerance;
        }

        // ======================================================================
        //  GUI RENDERING
        // ======================================================================

        private void OnGUI()
        {
            if (!guiVisible)
                return;

            GUI.skin = HighLogic.Skin;
            RebuildStylesIfNeeded();

            if (needsLayoutRecalc)
            {
                windowRect.height = 0;
                needsLayoutRecalc = false;
            }

            windowRect = GUILayout.Window(WINDOW_ID, windowRect, DrawMainWindow,
                Loc.WindowTitle, _windowStyle, GUILayout.MinWidth(GetMainMinWidth()));

            if (showFleetView)
            {
                fleetWindowRect = GUILayout.Window(FLEET_WINDOW_ID, fleetWindowRect, DrawFleetWindow,
                    Loc.FleetWindowTitle, _windowStyle, GUILayout.MinWidth(GetFleetMinWidth()));
            }
        }

        private void DrawMainWindow(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.Space(6);

            // --- Vessel selection info ---
            if (targetVessel == null)
            {
                GUILayout.Label(Loc.NoVesselSelected, _labelStyle);
                DrawFooterButtons();
                GUILayout.EndVertical();
                GUI.DragWindow();
                return;
            }

            GUILayout.Label(Loc.Format(Loc.VesselLabel, targetVessel.vesselName), _boldStyle);
            GUILayout.Space(4);

            if (editData == null)
            {
                GUILayout.Label(Loc.NoVesselData, _labelStyle);
                DrawFooterButtons();
                GUILayout.EndVertical();
                GUI.DragWindow();
                return;
            }

            // --- Status indicator ---
            DrawStatusSection();
            GUILayout.Space(8);

            // --- Current orbit display ---
            DrawCurrentOrbitSection();
            GUILayout.Space(8);

            // --- Target parameters input ---
            DrawTargetParametersSection();
            GUILayout.Space(8);

            // --- Configuration ---
            DrawConfigSection();
            GUILayout.Space(8);

            // --- Action buttons ---
            DrawActionButtons();
            GUILayout.Space(8);

            // --- Statistics ---
            DrawStatisticsSection();
            GUILayout.Space(4);

            // --- Footer ---
            DrawFooterButtons();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        // --------------------------------------------------
        //  SECTIONS
        // --------------------------------------------------

        private void DrawStatusSection()
        {
            GUILayout.BeginHorizontal(_boxStyle);
            GUILayout.Label(Loc.StatusLabel, _labelStyle, GUILayout.Width(GetStatusLabelWidth()));

            string statusText;
            switch (editData.Status)
            {
                case KeepStatus.Disabled:
                    statusText = $"<color=gray>{Loc.StatusDisabled}</color>";
                    break;
                case KeepStatus.Nominal:
                    statusText = $"<color=green>{Loc.StatusNominal}</color>";
                    break;
                case KeepStatus.Drifting:
                    statusText = $"<color=yellow>{Loc.StatusDrifting}</color>";
                    break;
                case KeepStatus.Correcting:
                    statusText = $"<color=cyan>{Loc.StatusCorrecting}</color>";
                    break;
                case KeepStatus.InsufficientResources:
                    statusText = $"<color=red>{Loc.StatusInsufficientRes}</color>";
                    break;
                case KeepStatus.NoEngine:
                    statusText = $"<color=red>{Loc.StatusNoEngine}</color>";
                    break;
                case KeepStatus.InvalidOrbit:
                    statusText = $"<color=orange>{Loc.StatusInvalidOrbit}</color>";
                    break;
                default:
                    statusText = Loc.StatusUnknown;
                    break;
            }

            GUILayout.Label(statusText, _richStyle);
            GUILayout.EndHorizontal();
        }

        private void DrawCurrentOrbitSection()
        {
            GUILayout.Label(Loc.SectionCurrentOrbit, _boldStyle);
            if (targetVessel.orbit != null)
            {
                Orbit o = targetVessel.orbit;
                GUILayout.BeginVertical(_boxStyle);
                DrawParamRow(Loc.Apoapsis, FormatAltitude(o.ApA));
                DrawParamRow(Loc.Periapsis, FormatAltitude(o.PeA));
                DrawParamRow(Loc.Inclination, $"{o.inclination:F2}°");
                DrawParamRow(Loc.Eccentricity, $"{o.eccentricity:F6}");
                DrawParamRow(Loc.OrbitalPeriod, FormatTime(o.period));
                GUILayout.EndVertical();
            }
        }

        private void DrawTargetParametersSection()
        {
            GUILayout.Label(Loc.SectionTargetOrbit, _boldStyle);
            GUILayout.BeginVertical(_boxStyle);

            // Set-from-current button
            if (GUILayout.Button(Loc.SetFromCurrent, _buttonStyle))
            {
                SetTargetFromCurrentOrbit();
            }
            GUILayout.Space(4);

            inputAp = DrawInputRow(Loc.TargetAp, inputAp);
            inputPe = DrawInputRow(Loc.TargetPe, inputPe);
            inputInc = DrawInputRow(Loc.TargetInc, inputInc);
            inputEcc = DrawInputRow(Loc.TargetEcc, inputEcc);

            GUILayout.EndVertical();
        }

        private void DrawConfigSection()
        {
            GUILayout.Label(Loc.SectionConfig, _boldStyle);
            GUILayout.BeginVertical(_boxStyle);

            // Auto-keep toggle
            editData.AutoKeepEnabled = GUILayout.Toggle(editData.AutoKeepEnabled, Loc.AutoKeepToggle, _toggleStyle);

            // Tolerance slider
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.Format(Loc.ToleranceLabel, toleranceSlider.ToString("F1")),
                _labelStyle, GUILayout.Width(GetLabelWidth()));
            toleranceSlider = GUILayout.HorizontalSlider(toleranceSlider, 1f, 20f,
                _hSliderStyle, _hSliderThumbStyle);
            GUILayout.EndHorizontal();

            // Check interval
            inputInterval = DrawInputRow(Loc.CheckInterval, inputInterval);

            // Engine selection mode
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.EngineModeLabel, _labelStyle, GUILayout.Width(GetEngineModeLabelWidth()));
            if (GUILayout.Toggle(editData.EngineMode == EngineSelectionMode.IgnitedOnly,
                Loc.EngineModeIgnited, _toggleStyle, GUILayout.Width(GetEngineModeOptionWidth())))
            {
                editData.EngineMode = EngineSelectionMode.IgnitedOnly;
            }
            if (GUILayout.Toggle(editData.EngineMode == EngineSelectionMode.ActiveNotShutdown,
                Loc.EngineModeActive, _toggleStyle, GUILayout.Width(GetEngineModeOptionWidth())))
            {
                editData.EngineMode = EngineSelectionMode.ActiveNotShutdown;
            }
            GUILayout.EndHorizontal();

            // Font size slider
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.Format(Loc.FontSizeLabel, ((int)fontSizeSlider).ToString()),
                _labelStyle, GUILayout.Width(GetLabelWidth()));
            fontSizeSlider = GUILayout.HorizontalSlider(fontSizeSlider, 10f, 22f,
                _hSliderStyle, _hSliderThumbStyle);
            GUILayout.EndHorizontal();

            // Apply config button
            if (GUILayout.Button(Loc.ApplySettings, _buttonStyle))
            {
                ApplySettings();
            }

            GUILayout.EndVertical();
        }

        private void DrawActionButtons()
        {
            GUILayout.Label(Loc.SectionActions, _boldStyle);
            GUILayout.BeginHorizontal();

            if (GUILayout.Button(Loc.ManualCorrect, _buttonStyle, GUILayout.Height(30)))
            {
                if (targetVessel != null)
                {
                    ApplySettings(); // Save settings first
                    VesselKeepModule module = targetVessel.GetComponent<VesselKeepModule>();
                    if (module != null)
                    {
                        module.ManualCorrection();
                        RefreshVessel();
                    }
                }
            }

            if (GUILayout.Button(Loc.RefreshStatus, _buttonStyle, GUILayout.Height(30)))
            {
                if (targetVessel != null)
                {
                    VesselKeepModule module = targetVessel.GetComponent<VesselKeepModule>();
                    if (module != null)
                    {
                        module.RefreshStatus();
                    }
                    RefreshVessel();
                }
            }

            GUILayout.EndHorizontal();

            // Safety warning if target periapsis is below atmosphere
            bool showLowPeWarning = false;
            if (targetVessel != null && targetVessel.orbit.referenceBody.atmosphere)
            {
                double atmDepth = targetVessel.orbit.referenceBody.atmosphereDepth;
                if (editData.TargetPeriapsis < atmDepth + OrbitalKeepSettings.MinSafeAltitudeMargin)
                {
                    showLowPeWarning = true;
                    string safeAlt = FormatAltitude(atmDepth + OrbitalKeepSettings.MinSafeAltitudeMargin);
                    GUILayout.Label(
                        $"<color=red>{Loc.Format(Loc.WarningLowPe, safeAlt)}</color>",
                        _richStyle);
                }
            }
            if (showLowPeWarning != lastLowPeWarning)
            {
                needsLayoutRecalc = true;
                lastLowPeWarning = showLowPeWarning;
            }
        }

        private void DrawStatisticsSection()
        {
            GUILayout.Label(Loc.SectionStats, _boldStyle);
            GUILayout.BeginVertical(_boxStyle);
            DrawParamRow(Loc.TotalDvSpent, $"{editData.TotalDeltaVSpent:F2} m/s");
            DrawParamRow(Loc.TotalECSpent, $"{editData.TotalECSpent:F1}");
            GUILayout.EndVertical();
        }

        private void DrawFooterButtons()
        {
            GUILayout.BeginHorizontal();
            showFleetView = GUILayout.Toggle(showFleetView, Loc.FleetOverview, _buttonStyle);

            if (GUILayout.Button(Loc.RemoveKeeping, _buttonStyle))
            {
                if (targetVessel != null && StationKeepScenario.Instance != null)
                {
                    StationKeepScenario.Instance.RemoveVesselData(targetVessel.id);
                    editData = null;
                    RefreshVessel();
                }
            }
            GUILayout.EndHorizontal();
        }

        // --------------------------------------------------
        //  FLEET OVERVIEW WINDOW
        // --------------------------------------------------

        private void DrawFleetWindow(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.Space(6);

            if (StationKeepScenario.Instance == null)
            {
                GUILayout.Label(Loc.ScenarioNotLoaded, _labelStyle);
                GUILayout.EndVertical();
                GUI.DragWindow();
                return;
            }

            GUILayout.BeginHorizontal();
            debrisVisibility = DrawDebrisVisibilityToggle(debrisVisibility);
            GUILayout.FlexibleSpace();
            RefreshFleetEntriesIfNeeded(debrisVisibility);
            GUILayout.Label(
                Loc.Format(Loc.TrackedVessels, cachedFilteredCount.ToString()),
                _boldStyle);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            GUIStyle scrollStyle = new GUIStyle(GUI.skin.scrollView);
            scrollStyle.padding.left = 0;
            scrollStyle.padding.right = 0;
            fleetScrollPos = GUILayout.BeginScrollView(fleetScrollPos, false, false, GUI.skin.horizontalScrollbar, GUI.skin.verticalScrollbar, scrollStyle, GUILayout.Height(320));

            foreach (FleetEntry entry in cachedFleetEntries)
            {
                VesselKeepData data = entry.Data;
                string vesselName = entry.Name;

                GUILayout.BeginVertical(_fleetBoxStyle, GUILayout.ExpandWidth(true));
                GUILayout.BeginHorizontal();
                GUILayout.Label(vesselName, _boldStyle, GUILayout.Width(GetFleetNameWidth()));

                string status;
                switch (data.Status)
                {
                    case KeepStatus.Nominal:
                        status = $"<color=green>{Loc.StatusShortNominal}</color>"; break;
                    case KeepStatus.Drifting:
                        status = $"<color=yellow>{Loc.StatusShortDrifting}</color>"; break;
                    case KeepStatus.InsufficientResources:
                        status = $"<color=red>{Loc.StatusShortInsufficientRes}</color>"; break;
                    case KeepStatus.NoEngine:
                        status = $"<color=red>{Loc.StatusShortNoEngine}</color>"; break;
                    case KeepStatus.InvalidOrbit:
                        status = $"<color=orange>{Loc.StatusShortInvalidOrbit}</color>"; break;
                    default:
                        status = data.AutoKeepEnabled ? Loc.StatusShortAuto : Loc.StatusShortDisabled; break;
                }

                GUILayout.Label(status, _richStyle, GUILayout.Width(GetFleetStatusWidth()));
                GUILayout.EndHorizontal();

                GUILayout.Label(Loc.Format(Loc.FleetInfoLine,
                    FormatAltitude(data.TargetApoapsis),
                    FormatAltitude(data.TargetPeriapsis),
                    data.TotalDeltaVSpent.ToString("F1")), _labelStyle);

                GUILayout.EndVertical();
                Rect entryRect = GUILayoutUtility.GetLastRect();
                if (GUI.Button(entryRect, GUIContent.none, GUIStyle.none))
                {
                    if (HighLogic.LoadedScene == GameScenes.TRACKSTATION && entry.Vessel != null)
                    {
                        trackingStationVessel = entry.Vessel;
                        if (PlanetariumCamera.fetch != null && entry.Vessel.mapObject != null)
                        {
                            PlanetariumCamera.fetch.SetTarget(entry.Vessel.mapObject);
                        }
                        RefreshVessel();
                    }
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void RefreshFleetEntriesIfNeeded(DebrisVisibility visibility)
        {
            if (StationKeepScenario.Instance == null)
                return;

            double now = Planetarium.GetUniversalTime();
            if (lastFleetPopulateTime < 0 ||
                now - lastFleetPopulateTime > 2.0 ||
                lastDebrisVisibility != visibility)
            {
                EnsureFleetDataPopulated(visibility);
                cachedFleetEntries = BuildFleetEntries(StationKeepScenario.Instance.GetAllVesselData(), visibility);
                cachedFleetEntries.Sort(CompareFleetEntries);
                cachedFilteredCount = cachedFleetEntries.Count;
                lastFleetPopulateTime = now;
                lastDebrisVisibility = visibility;
            }
        }

        private List<FleetEntry> BuildFleetEntries(IEnumerable<VesselKeepData> allData, DebrisVisibility visibility)
        {
            List<FleetEntry> entries = new List<FleetEntry>();
            Vessel activeVessel = FlightGlobals.ActiveVessel;
            Dictionary<Guid, Vessel> vesselIndex = BuildVesselIndex();
            foreach (VesselKeepData data in allData)
            {
                vesselIndex.TryGetValue(data.VesselId, out Vessel v);
                if (!IsFleetVesselEligible(v))
                    continue;
                bool isActive = v != null && activeVessel != null && v == activeVessel;
                if (!isActive && !IsOrbitOrSuborbit(v))
                    continue;
                if (visibility == DebrisVisibility.Hide && v != null && v.vesselType == VesselType.Debris)
                    continue;
                if (visibility == DebrisVisibility.Only && (v == null || v.vesselType != VesselType.Debris))
                    continue;

                string vesselName = v != null
                    ? v.vesselName
                    : Loc.Format(Loc.UnknownVessel, data.VesselId.ToString());
                entries.Add(new FleetEntry
                {
                    Data = data,
                    Name = vesselName,
                    IsActive = isActive,
                    Vessel = v
                });
            }
            return entries;
        }

        private Dictionary<Guid, Vessel> BuildVesselIndex()
        {
            Dictionary<Guid, Vessel> index = new Dictionary<Guid, Vessel>();
            if (FlightGlobals.Vessels == null)
                return index;
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                if (vessel == null)
                    continue;
                index[vessel.id] = vessel;
            }
            return index;
        }

        private DebrisVisibility DrawDebrisVisibilityToggle(DebrisVisibility current)
        {
            string[] options = { Loc.DebrisOnly, Loc.DebrisAll, Loc.DebrisHide };
            int selected = GUILayout.Toolbar((int)current, options, _buttonStyle);
            return (DebrisVisibility)selected;
        }

        private static bool IsFleetVesselEligible(Vessel vessel)
        {
            if (vessel == null)
                return false;
            if (vessel.vesselType == VesselType.Flag)
                return false;
            if (vessel.vesselType == VesselType.Unknown)
                return false;
            if (vessel.vesselType == VesselType.SpaceObject)
                return false;
            return true;
        }

        private static bool IsOrbitOrSuborbit(Vessel vessel)
        {
            if (vessel == null)
                return false;
            return vessel.situation == Vessel.Situations.ORBITING ||
                   vessel.situation == Vessel.Situations.SUB_ORBITAL;
        }

        private void EnsureFleetDataPopulated(DebrisVisibility visibility)
        {
            if (StationKeepScenario.Instance == null || FlightGlobals.Vessels == null)
                return;

            Vessel activeVessel = FlightGlobals.ActiveVessel;
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                if (vessel == null || vessel.orbit == null)
                    continue;
                if (!IsFleetVesselEligible(vessel))
                    continue;
                bool isActive = activeVessel != null && vessel == activeVessel;
                if (!isActive && !IsOrbitOrSuborbit(vessel))
                    continue;
                if (visibility == DebrisVisibility.Hide && vessel.vesselType == VesselType.Debris)
                    continue;
                if (visibility == DebrisVisibility.Only && vessel.vesselType != VesselType.Debris)
                    continue;

                StationKeepScenario.Instance.GetOrCreateVesselData(vessel);
            }
        }

        private static int CompareFleetEntries(FleetEntry a, FleetEntry b)
        {
            if (a.IsActive != b.IsActive)
                return a.IsActive ? -1 : 1;
            int nameCompare = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            if (nameCompare != 0)
                return nameCompare;
            return string.CompareOrdinal(a.Data.VesselId.ToString(), b.Data.VesselId.ToString());
        }

        private class FleetEntry
        {
            public VesselKeepData Data;
            public string Name;
            public bool IsActive;
            public Vessel Vessel;
        }

        private enum DebrisVisibility
        {
            Only,
            All,
            Hide
        }

        // ======================================================================
        //  SETTINGS APPLICATION
        // ======================================================================

        private void ApplySettings()
        {
            if (editData == null || targetVessel == null)
                return;

            // Parse target parameters (input is in km, convert to meters)
            if (double.TryParse(inputAp, out double ap))
                editData.TargetApoapsis = ap * 1000.0;
            if (double.TryParse(inputPe, out double pe))
                editData.TargetPeriapsis = pe * 1000.0;
            if (double.TryParse(inputInc, out double inc))
                editData.TargetInclination = inc;
            if (double.TryParse(inputEcc, out double ecc))
                editData.TargetEccentricity = ecc;
            if (double.TryParse(inputInterval, out double interval))
                editData.CheckInterval = Math.Max(60.0, interval); // Min 60s

            editData.Tolerance = toleranceSlider;

            // Save font size
            int newFontSize = (int)fontSizeSlider;
            if (newFontSize != OrbitalKeepSettings.FontSize)
            {
                OrbitalKeepSettings.FontSize = newFontSize;
                windowRect.width = GetMainMinWidth();
                fleetWindowRect.width = GetFleetMinWidth();
                windowRect.height = 0;
                fleetWindowRect.height = 0;
                OrbitalKeepSettings.SaveUserSettings();
                _cachedFontSize = 0; // Force style rebuild on next frame
            }

            // Save to scenario
            StationKeepScenario.Instance?.SetVesselData(editData);

            ScreenMessages.PostScreenMessage(Loc.SettingsSaved,
                OrbitalKeepSettings.MessageDuration, ScreenMessageStyle.UPPER_CENTER);
        }

        private void SetTargetFromCurrentOrbit()
        {
            if (targetVessel == null || targetVessel.orbit == null || editData == null)
                return;

            editData.TargetApoapsis = targetVessel.orbit.ApA;
            editData.TargetPeriapsis = targetVessel.orbit.PeA;
            editData.TargetInclination = targetVessel.orbit.inclination;
            editData.TargetEccentricity = targetVessel.orbit.eccentricity;

            SyncInputFields();

            StationKeepScenario.Instance?.SetVesselData(editData);
        }

        // ======================================================================
        //  GUI HELPERS
        // ======================================================================

        private static void DrawParamRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _labelStyle, GUILayout.Width(GetLabelWidth()));
            GUILayout.Label(value, _labelStyle);
            GUILayout.EndHorizontal();
        }

        private static string DrawInputRow(string label, string currentValue)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _labelStyle, GUILayout.Width(GetLabelWidth()));
            string newValue = GUILayout.TextField(currentValue, _textFieldStyle, GUILayout.Width(GetInputWidth()));
            GUILayout.EndHorizontal();
            return newValue;
        }

        private static string FormatAltitude(double altitudeMeters)
        {
            if (Math.Abs(altitudeMeters) >= 1e9)
                return $"{altitudeMeters / 1e9:F3} {Loc.Unit_Gm}";
            if (Math.Abs(altitudeMeters) >= 1e6)
                return $"{altitudeMeters / 1e6:F3} {Loc.Unit_Mm}";
            if (Math.Abs(altitudeMeters) >= 1e3)
                return $"{altitudeMeters / 1e3:F3} {Loc.Unit_km}";
            return $"{altitudeMeters:F1} {Loc.Unit_m}";
        }

        private static float GetMainMinWidth()
        {
            return Mathf.Round(BASE_MAIN_WIDTH * OrbitalKeepSettings.FontSize / BASE_FONT_SIZE);
        }

        private static float GetFleetMinWidth()
        {
            return Mathf.Round(BASE_FLEET_WIDTH * OrbitalKeepSettings.FontSize / BASE_FONT_SIZE);
        }

        private static float GetLabelWidth()
        {
            return Mathf.Round(140f * OrbitalKeepSettings.FontSize / BASE_FONT_SIZE);
        }

        private static float GetInputWidth()
        {
            return Mathf.Round(150f * OrbitalKeepSettings.FontSize / BASE_FONT_SIZE);
        }

        private static float GetStatusLabelWidth()
        {
            return Mathf.Round(50f * OrbitalKeepSettings.FontSize / BASE_FONT_SIZE);
        }

        private static float GetEngineModeLabelWidth()
        {
            return Mathf.Round(100f * OrbitalKeepSettings.FontSize / BASE_FONT_SIZE);
        }

        private static float GetEngineModeOptionWidth()
        {
            return Mathf.Round(120f * OrbitalKeepSettings.FontSize / BASE_FONT_SIZE);
        }

        private static float GetFleetNameWidth()
        {
            return Mathf.Round(200f * OrbitalKeepSettings.FontSize / BASE_FONT_SIZE);
        }

        private static float GetFleetStatusWidth()
        {
            return Mathf.Round(80f * OrbitalKeepSettings.FontSize / BASE_FONT_SIZE);
        }

        private static string FormatTime(double seconds)
        {
            if (seconds < 0) return Loc.Unit_NA;

            int totalSeconds = (int)seconds;
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int secs = totalSeconds % 60;

            if (hours > 0)
                return Loc.Format(Loc.TimeFormat_hms,
                    hours.ToString(), minutes.ToString(), secs.ToString());
            if (minutes > 0)
                return Loc.Format(Loc.TimeFormat_ms,
                    minutes.ToString(), secs.ToString());
            return Loc.Format(Loc.TimeFormat_s, secs.ToString());
        }

        // ======================================================================
        //  GUI STYLE MANAGEMENT
        // ======================================================================

        /// <summary>
        /// Rebuilds all cached GUIStyles if the font size setting has changed.
        /// Called at the start of each OnGUI frame.
        /// </summary>
        private static void RebuildStylesIfNeeded()
        {
            int size = OrbitalKeepSettings.FontSize;
            if (size == _cachedFontSize && _labelStyle != null)
                return;

            _cachedFontSize = size;

            _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = size };
            _boldStyle = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold };
            _richStyle = new GUIStyle(GUI.skin.label) { fontSize = size, richText = true };
            _buttonStyle = new GUIStyle(GUI.skin.button);
            _toggleStyle = new GUIStyle(GUI.skin.toggle) { fontSize = size };
            _textFieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = size };
            _boxStyle = new GUIStyle(GUI.skin.box) { fontSize = size };
            _fleetBoxStyle = new GUIStyle(_boxStyle);
            _fleetBoxStyle.padding = new RectOffset(0, _boxStyle.padding.right, _boxStyle.padding.top, _boxStyle.padding.bottom);
            _fleetBoxStyle.margin = new RectOffset(0, 0, _boxStyle.margin.top, _boxStyle.margin.bottom);
            _windowStyle = new GUIStyle(GUI.skin.window) { fontSize = size };
            _hSliderStyle = new GUIStyle(GUI.skin.horizontalSlider);
            _hSliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb);
        }
    }
}
