using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.UI.Screens;
using KSP.Localization;
using ClickThroughFix;

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
        private List<CelestialBody> cachedBodyFilterBodies = new List<CelestialBody>();
        private string[] cachedBodyFilterOptions = Array.Empty<string>();
        private bool bodyFilterOptionsInitialized;
        private int bodyFilterIndex;
        private int lastBodyFilterIndex = -1;
        private bool showBodyPickerPopup;
        private Vector2 bodyPickerScrollPos;
        private Rect bodyPickerRect;

        private const int WINDOW_ID = 0x4F4B_0001; // "OK" prefix
        private const int FLEET_WINDOW_ID = 0x4F4B_0002;
        private const int BODY_PICKER_WINDOW_ID = 0x4F4B_0003;
        private const float BASE_FONT_SIZE = 18f;
        private const float BASE_MAIN_COLUMN_WIDTH = 410f;
        private const float BASE_MAIN_COLUMN_GAP = 8f;
        private const float BASE_MAIN_WIDTH = BASE_MAIN_COLUMN_WIDTH * 2f + BASE_MAIN_COLUMN_GAP;
        private const float BASE_FLEET_WIDTH = 350f;
        private const float FLEET_SCROLL_HEIGHT = 320f;
        private const float FLEET_ENTRY_SPACING = 4f;
        private const float FLEET_LEVEL2_INSET = 10f;
        private const float FLEET_LEVEL1_INSET = 3f;
        // Body picker popup dimensions (aligned with SpaceWeatherAndAtmosphericOrbitalDecay)
        private const float BODY_POPUP_MAX_WIDTH = 280f;
        private const float BODY_POPUP_DEFAULT_WIDTH = 180f;
        private const float BODY_POPUP_LIST_HEIGHT = 340f;
        private const float BODY_BUTTON_PADDING = 16f;
        private const float LIFETIME_ESTIMATE_CACHE_INTERVAL = 1.0f;

        // --- AppLauncher ---
        private ApplicationLauncherButton appButton;
        private bool appLauncherReady;
        private bool appLauncherEventsRegistered;
        private float nextAppLauncherRegisterAttemptRealtime;

        // --- Target vessel (for Flight scene) ---
        private Vessel targetVessel;
        private VesselKeepData editData;

        // --- Input field strings ---
        private string inputAp = "0";
        private string inputPe = "0";
        private string inputInc = "0";
        private string inputInterval = "3600";
        private string inputTolerance = "5.0";
        private bool inputAutoKeepEnabled;
        private bool inputAllowRcs;
        private bool autoKeepConfigExpanded = true;
        private bool lastLowPeWarning;
        private bool needsLayoutRecalc;
        private float _lastUiScaleFactor = -1f;
        private StationKeepEstimator.EstimateResult cachedLifetimeEstimate;
        private Guid cachedLifetimeVesselId = Guid.Empty;
        private int cachedLifetimeSignature;
        private float lastLifetimeEstimateTime = -1f;

        // --- Tracking station selection ---
        private Vessel trackingStationVessel;

        // --- Cached GUIStyles (rebuilt when font size changes) ---
        private static int _cachedFontSize;
        private static GUIStyle _labelStyle;
        private static GUIStyle _rowLabelStyle;
        private static GUIStyle _boldStyle;
        private static GUIStyle _richStyle;
        private static GUIStyle _statusRichStyle;
        private static GUIStyle _buttonStyle;
        private static GUIStyle _toggleStyle;
        private static GUIStyle _textFieldStyle;
        private static GUIStyle _boxStyle;
        private static GUIStyle _fleetBoxStyle;
        private static GUIStyle _windowStyle;

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

            OrbitalKeepParameters.Instance?.ApplyAutoUiScale();
            windowRect.width = GetMainMinWidth();
            fleetWindowRect.width = GetFleetMinWidth();

            var parameters = OrbitalKeepParameters.Instance;
            SetToolbarButtonEnabled(parameters != null && parameters.enableToolbarButton);

            // Track vessel selection in tracking station
            GameEvents.onPlanetariumTargetChanged.Add(OnMapTargetChanged);

            if (parameters != null && parameters.enableToolbarButton && ApplicationLauncher.Instance != null)
                OnAppLauncherReady();
        }

        private void OnDestroy()
        {
            UnregisterAppLauncherEvents();
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

        private void Update()
        {
            HandleGuiHotkey();
            TryRegisterAppLauncherButton();
        }

        private void OnAppLauncherReady()
        {
            if (OrbitalKeepParameters.Instance == null || !OrbitalKeepParameters.Instance.enableToolbarButton)
                return;
            appLauncherReady = true;
            float now = Time.realtimeSinceStartup;
            nextAppLauncherRegisterAttemptRealtime = now;
        }

        private void OnAppLauncherUnready(GameScenes scene)
        {
            appLauncherReady = false;
            nextAppLauncherRegisterAttemptRealtime = 0f;
            if (appButton != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(appButton);
                appButton = null;
            }
        }

        private void SetToolbarButtonEnabled(bool enabled)
        {
            if (enabled)
            {
                RegisterAppLauncherEvents();
                if (ApplicationLauncher.Instance != null)
                    OnAppLauncherReady();
                return;
            }

            OnAppLauncherUnready(HighLogic.LoadedScene);
            UnregisterAppLauncherEvents();
        }

        private void RegisterAppLauncherEvents()
        {
            if (appLauncherEventsRegistered)
                return;
            GameEvents.onGUIApplicationLauncherReady.Add(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherUnreadifying.Add(OnAppLauncherUnready);
            appLauncherEventsRegistered = true;
        }

        private void UnregisterAppLauncherEvents()
        {
            if (!appLauncherEventsRegistered)
                return;
            GameEvents.onGUIApplicationLauncherReady.Remove(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherUnreadifying.Remove(OnAppLauncherUnready);
            appLauncherEventsRegistered = false;
        }

        private void TryRegisterAppLauncherButton()
        {
            if (OrbitalKeepParameters.Instance == null || !OrbitalKeepParameters.Instance.enableToolbarButton)
                return;
            if (appButton != null || !appLauncherReady)
                return;
            if (ApplicationLauncher.Instance == null)
                return;

            float now = Time.realtimeSinceStartup;
            if (now < nextAppLauncherRegisterAttemptRealtime)
                return;

            appButton = ApplicationLauncher.Instance.AddModApplication(
                OnToolbarOn, OnToolbarOff,
                null, null, null, null,
                ApplicationLauncher.AppScenes.FLIGHT |
                ApplicationLauncher.AppScenes.TRACKSTATION |
                ApplicationLauncher.AppScenes.MAPVIEW,
                GameDatabase.Instance.GetTexture("OrbitalKeeper/Textures/icon_toolbar", false)
            );

            if (appButton == null)
            {
                // Retry later to avoid hammering registration during unstable UI periods.
                nextAppLauncherRegisterAttemptRealtime = now + 1f;
            }
        }

        private void HandleGuiHotkey()
        {
            var parameters = OrbitalKeepParameters.Instance;
            if (parameters == null || parameters.ResolveHotkeyKey() == KeyCode.None)
                return;
            if (!parameters.IsHotkeyPressed())
                return;

            guiVisible = !guiVisible;
            if (guiVisible)
                RefreshVessel();
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
                RefreshTargetStatus();
                InvalidateLifetimeEstimate();
            }
        }

        private void SyncInputFields()
        {
            if (editData == null) return;
            inputAp = (editData.TargetApoapsis / 1000.0).ToString("F3"); // Display in km
            inputPe = (editData.TargetPeriapsis / 1000.0).ToString("F3");
            inputInc = editData.TargetInclination.ToString("F2");
            inputInterval = editData.CheckInterval.ToString("F0");
            inputTolerance = editData.Tolerance.ToString("F1");
            inputAutoKeepEnabled = editData.AutoKeepEnabled;
            inputAllowRcs = editData.AllowRcsEngines;
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

            float uiScale = UIScale.Factor;
            if (!Mathf.Approximately(uiScale, _lastUiScaleFactor))
            {
                if (_lastUiScaleFactor > 0f)
                    ApplyUiScaleChange(_lastUiScaleFactor, uiScale);
                _lastUiScaleFactor = uiScale;
                needsLayoutRecalc = true;
            }

            if (needsLayoutRecalc)
            {
                windowRect.height = 0;
                needsLayoutRecalc = false;
            }
            UpdateFleetWindowWidth();

            UIScale.BeginGUI();
            try
            {
                windowRect = ClickThruBlocker.GUILayoutWindow(WINDOW_ID, windowRect, DrawMainWindow,
                    Loc.WindowTitle, _windowStyle, GUILayout.MinWidth(GetMainMinWidth()));
                windowRect = UIScale.ClampToGuiScreen(windowRect);

                if (showFleetView)
                {
                    fleetWindowRect = ClickThruBlocker.GUILayoutWindow(FLEET_WINDOW_ID, fleetWindowRect, DrawFleetWindow,
                        Loc.FleetWindowTitle, _windowStyle, GUILayout.MinWidth(GetFleetMinWidth()));
                    fleetWindowRect = UIScale.ClampToGuiScreen(fleetWindowRect);
                }

                if (showBodyPickerPopup)
                {
                    if (!showFleetView)
                    {
                        showBodyPickerPopup = false;
                        bodyPickerRect = new Rect(0, 0, 0, 0);
                    }
                    else
                    {
                        Vector2 screen = UIScale.GuiScreenSize();
                        float scale = 1f;
                        float maxWidth = Mathf.Round(BODY_POPUP_MAX_WIDTH * scale);
                        float defaultWidth = Mathf.Round(BODY_POPUP_DEFAULT_WIDTH * scale);
                        float listHeight = Mathf.Round(BODY_POPUP_LIST_HEIGHT * scale);
                        float closeBtnHeight = (_buttonStyle ?? GUI.skin.button).CalcSize(new GUIContent(Loc.FleetBodyPickerClose)).y + 8f;
                        float pickerHeight = 24f + 6f + listHeight + 8f + closeBtnHeight;
                        float contentWidth = 0f;
                        GUIStyle measureStyle = _buttonStyle ?? GUI.skin.button;
                        float maxItemWidth = (BODY_POPUP_MAX_WIDTH - 24f) * scale;
                        foreach (string opt in cachedBodyFilterOptions)
                        {
                            float w = measureStyle.CalcSize(new GUIContent(opt)).x;
                            if (w > contentWidth) contentWidth = Mathf.Min(w, maxItemWidth);
                        }
                        float scrollWidth = Mathf.Max(contentWidth + 10f, defaultWidth);
                        scrollWidth = Mathf.Min(scrollWidth, maxWidth);
                        float pickerWidth = scrollWidth;

                        if (bodyPickerRect.width <= 0 || bodyPickerRect.height <= 0)
                        {
                            bodyPickerRect = new Rect(
                                (screen.x - pickerWidth) * 0.5f,
                                (screen.y - pickerHeight) * 0.5f,
                                pickerWidth,
                                pickerHeight);
                        }
                        bodyPickerRect = ClickThruBlocker.GUILayoutWindow(BODY_PICKER_WINDOW_ID, bodyPickerRect, DrawBodyPickerPopup,
                            Loc.FleetBodyPickerTitle, _windowStyle, GUILayout.Width(pickerWidth), GUILayout.Height(pickerHeight));
                        bodyPickerRect = UIScale.ClampToGuiScreen(bodyPickerRect);
                        if (!showBodyPickerPopup)
                            bodyPickerRect = new Rect(0, 0, 0, 0);
                    }
                }
            }
            finally
            {
                UIScale.EndGUI();
            }
        }

        private void ApplyUiScaleChange(float oldScale, float newScale)
        {
            if (oldScale <= 0f || newScale <= 0f)
                return;

            windowRect = ScaleWindowPosition(windowRect, oldScale, newScale);
            fleetWindowRect = ScaleWindowPosition(fleetWindowRect, oldScale, newScale);
            bodyPickerRect = new Rect(0, 0, 0, 0);
        }

        private static Rect ScaleWindowPosition(Rect rect, float oldScale, float newScale)
        {
            if (rect.width <= 0f && rect.height <= 0f)
                return rect;

            float ratio = oldScale / newScale;
            rect.x *= ratio;
            rect.y *= ratio;
            return UIScale.ClampToGuiScreen(rect);
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

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(GetMainColumnWidth()));
            // --- Current orbit display ---
            DrawCurrentOrbitSection();
            GUILayout.Space(8);

            // --- Target parameters input ---
            DrawTargetParametersSection();
            GUILayout.Space(8);

            // --- Action buttons ---
            DrawActionButtons();
            GUILayout.EndVertical();

            GUILayout.Space(GetMainColumnGap());

            GUILayout.BeginVertical(GUILayout.Width(GetMainColumnWidth()));
            // --- Statistics ---
            DrawStatisticsSection();
            GUILayout.Space(8);

            // --- Configuration ---
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.Space(8);

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
            float rowH = ButtonHeight;
            GUILayout.BeginHorizontal(_boxStyle, GUILayout.Height(rowH + 12f));
            GUILayout.Label(Loc.StatusLabel, _rowLabelStyle, GUILayout.Width(GetStatusLabelWidth()), GUILayout.Height(rowH));

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

            GUILayout.Label(statusText, _statusRichStyle, GUILayout.ExpandWidth(true), GUILayout.Height(rowH));
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
            if (GUILayout.Button(Loc.SetFromCurrent, _buttonStyle, GUILayout.Height(ButtonHeight)))
            {
                SetTargetFromCurrentOrbit();
            }
            GUILayout.Space(4);

            inputAp = DrawInputRow(Loc.TargetAp, inputAp);
            inputPe = DrawInputRow(Loc.TargetPe, inputPe);
            inputInc = DrawInputRow(Loc.TargetInc, inputInc);

            GUILayout.Space(6);
            bool prevAutoKeepConfigExpanded = autoKeepConfigExpanded;
            autoKeepConfigExpanded = DrawFoldoutHeader(Loc.ConfigAutoKeepSettings, autoKeepConfigExpanded);
            if (autoKeepConfigExpanded)
            {
                inputAutoKeepEnabled = DrawLabeledToggle(inputAutoKeepEnabled, Loc.AutoKeepToggle, ButtonHeight);
                inputTolerance = DrawInputRow(
                    $"{Loc.Format(Loc.ToleranceLabel, inputTolerance)} [1-20]",
                    inputTolerance);
                inputInterval = DrawInputRow(Loc.CheckInterval, inputInterval);

                GUILayout.BeginHorizontal(GUILayout.Height(ButtonHeight));
                GUILayout.Label(Loc.EngineModeLabel, _rowLabelStyle, GUILayout.Width(GetLabelWidth()), GUILayout.Height(ButtonHeight));
                if (DrawCompactLabeledToggle(editData.EngineMode == EngineSelectionMode.IgnitedOnly,
                    Loc.EngineModeIgnited, ButtonHeight))
                {
                    editData.EngineMode = EngineSelectionMode.IgnitedOnly;
                }
                if (DrawCompactLabeledToggle(editData.EngineMode == EngineSelectionMode.ActiveNotShutdown,
                    Loc.EngineModeActive, ButtonHeight))
                {
                    editData.EngineMode = EngineSelectionMode.ActiveNotShutdown;
                }
                GUILayout.EndHorizontal();
                inputAllowRcs = DrawLabeledToggle(inputAllowRcs, Loc.AllowRcsEnginesToggle, ButtonHeight);
                editData.AllowRcsEngines = inputAllowRcs;
            }
            if (GUILayout.Button(Loc.ApplySettings, _buttonStyle, GUILayout.Height(ButtonHeight)))
            {
                ApplyOrbitKeepSettings();
            }
            if (prevAutoKeepConfigExpanded != autoKeepConfigExpanded)
            {
                needsLayoutRecalc = true;
            }

            GUILayout.EndVertical();
        }

        private void DrawActionButtons()
        {
            GUILayout.Label(Loc.SectionActions, _boldStyle);
            GUILayout.BeginHorizontal();

            if (GUILayout.Button(Loc.ManualCorrect, _buttonStyle, GUILayout.Height(ButtonHeight)))
            {
                if (targetVessel != null)
                {
                    ApplyOrbitKeepSettings(false); // Save orbit/keep settings first
                    VesselKeepModule module = targetVessel.GetComponent<VesselKeepModule>();
                    if (module != null)
                    {
                        module.ManualCorrection();
                        RefreshVessel();
                    }
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
            DrawSeparator();

            StationKeepEstimator.EstimateResult estimate = GetLifetimeEstimate();
            if (estimate.Available)
            {
                DrawParamRow(Loc.EstimateDvPerCorrection, $"{estimate.DeltaVPerCorrection:F3} m/s");
                DrawParamRow(Loc.EstimateFuelPerCorrection, $"{estimate.Budget.RequiredFuelMass:F5} t");
                DrawParamRow(Loc.EstimateRemainingCorrections, FormatCorrectionCount(estimate.AvailableCorrections));
                DrawParamRow(Loc.EstimateMaintainTime, FormatLongTime(estimate.EstimatedLifetimeSeconds));
                DrawParamRow(Loc.EstimateNextCorrection, FormatLongTime(estimate.SecondsPerCorrection));
                DrawEstimateNotes();
            }
            else
            {
                DrawParamRow(Loc.EstimateMaintainTime, estimate.UnavailableReason);
            }

            GUILayout.EndVertical();
        }

        private void DrawFooterButtons()
        {
            GUILayout.BeginHorizontal();
            showFleetView = GUILayout.Toggle(showFleetView, Loc.FleetOverview, _buttonStyle, GUILayout.ExpandWidth(true), GUILayout.Height(ButtonHeight));

            if (GUILayout.Button(Loc.RemoveKeeping, _buttonStyle, GUILayout.ExpandWidth(true), GUILayout.Height(ButtonHeight)))
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

            DrawBodyFilter();
            GUILayout.Space(2);

            GUILayout.BeginHorizontal(GUILayout.Height(ButtonHeight));
            GUILayout.Space(8f);
            debrisVisibility = DrawDebrisVisibilityToggle(debrisVisibility);
            GUILayout.Space(8f);
            GUILayout.FlexibleSpace();
            RefreshFleetEntriesIfNeeded(debrisVisibility);
            GUILayout.Label(
                Loc.Format(Loc.TrackedVessels, cachedFilteredCount.ToString()),
                _boldStyle,
                GUILayout.Height(ButtonHeight));
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            GUIStyle scrollStyle = new GUIStyle(GUI.skin.scrollView);
            scrollStyle.padding.left = 0;
            scrollStyle.padding.right = 0;

            // Level-2 panel (scroll area) centered within level-3 window
            GUILayout.BeginHorizontal();
            GUILayout.Space(FLEET_LEVEL2_INSET);
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            fleetScrollPos = GUILayout.BeginScrollView(fleetScrollPos, false, false, GUI.skin.horizontalScrollbar, GUI.skin.verticalScrollbar, scrollStyle, GUILayout.Height(FLEET_SCROLL_HEIGHT), GUILayout.ExpandWidth(true));

            for (int i = 0; i < cachedFleetEntries.Count; i++)
            {
                FleetEntry entry = cachedFleetEntries[i];
                VesselKeepData data = entry.Data;
                string vesselName = entry.Name;

                // Level-1 entry box centered within level-2 scroll panel
                GUILayout.BeginHorizontal();
                GUILayout.Space(FLEET_LEVEL1_INSET);
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
                    data.TotalDeltaVSpent.ToString("F2")), _labelStyle);

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
                GUILayout.Space(FLEET_LEVEL1_INSET);
                GUILayout.EndHorizontal();

                if (i < cachedFleetEntries.Count - 1)
                    GUILayout.Space(FLEET_ENTRY_SPACING);
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.Space(FLEET_LEVEL2_INSET);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
            GUILayout.Label(Loc.FleetSelectionHint, _labelStyle);
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
                lastDebrisVisibility != visibility ||
                lastBodyFilterIndex != bodyFilterIndex)
            {
                EnsureFleetDataPopulated(visibility);
                cachedFleetEntries = BuildFleetEntries(StationKeepScenario.Instance.GetAllVesselData(), visibility, GetSelectedBody());
                cachedFleetEntries.Sort(CompareFleetEntries);
                cachedFilteredCount = cachedFleetEntries.Count;
                lastFleetPopulateTime = now;
                lastDebrisVisibility = visibility;
                lastBodyFilterIndex = bodyFilterIndex;
            }
        }

        private List<FleetEntry> BuildFleetEntries(IEnumerable<VesselKeepData> allData, DebrisVisibility visibility, CelestialBody bodyFilter)
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
                if (bodyFilter != null)
                {
                    if (v == null || v.orbit == null || v.orbit.referenceBody != bodyFilter)
                        continue;
                }

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

        private void DrawBodyFilter()
        {
            RefreshBodyFilterOptionsIfNeeded();
            string buttonText = GetCurrentBodyFilterLabel();
            float textMaxWidth = GetBodyFilterTextMaxWidth();
            string displayText = TruncateWithEllipsis(buttonText, textMaxWidth, _labelStyle ?? GUI.skin.label);
            float buttonWidth = GetBodyFilterButtonWidth();

            float lineHeight = ButtonHeight;
            GUILayout.BeginHorizontal(GUILayout.Height(lineHeight));
            GUILayout.Label(Loc.FleetBodyFilter, _labelStyle, GUILayout.ExpandWidth(false), GUILayout.Height(lineHeight));
            if (GUILayout.Button(displayText, _buttonStyle, GUILayout.Width(buttonWidth), GUILayout.Height(lineHeight)))
            {
                showBodyPickerPopup = true;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawBodyPickerPopup(int id)
        {
            GUILayout.BeginVertical();
            float scale = 1f;
            float maxItemWidth = (BODY_POPUP_MAX_WIDTH - 24f) * scale;
            float contentWidth = 0f;
            GUIStyle btnStyle = _buttonStyle ?? GUI.skin.button;
            for (int i = 0; i < cachedBodyFilterOptions.Length; i++)
            {
                float w = btnStyle.CalcSize(new GUIContent(cachedBodyFilterOptions[i])).x;
                if (w > contentWidth) contentWidth = Mathf.Min(w, maxItemWidth);
            }
            float scrollWidth = Mathf.Max(contentWidth + 10f, BODY_POPUP_DEFAULT_WIDTH * scale);
            scrollWidth = Mathf.Min(scrollWidth, BODY_POPUP_MAX_WIDTH * scale);
            float listHeight = BODY_POPUP_LIST_HEIGHT * scale;

            GUILayout.Space(6);
            bodyPickerScrollPos = GUILayout.BeginScrollView(bodyPickerScrollPos, false, true, GUILayout.Width(scrollWidth), GUILayout.Height(listHeight));
            for (int i = 0; i < cachedBodyFilterOptions.Length; i++)
            {
                string name = cachedBodyFilterOptions[i];
                string displayName = TruncateWithEllipsis(name, maxItemWidth, btnStyle);

                if (GUILayout.Button(displayName, btnStyle))
                {
                    bodyFilterIndex = i;
                    showBodyPickerPopup = false;
                }
            }
            GUILayout.EndScrollView();
            GUILayout.Space(8);
            if (GUILayout.Button(Loc.FleetBodyPickerClose, btnStyle))
            {
                showBodyPickerPopup = false;
            }
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 28));
        }

        private void RefreshBodyFilterOptionsIfNeeded()
        {
            if (bodyFilterOptionsInitialized)
                return;

            if (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0)
            {
                cachedBodyFilterBodies.Clear();
                cachedBodyFilterOptions = new[] { Loc.FleetBodyAll };
                bodyFilterIndex = Mathf.Clamp(bodyFilterIndex, 0, cachedBodyFilterOptions.Length - 1);
                return;
            }

            cachedBodyFilterBodies = new List<CelestialBody>(FlightGlobals.Bodies);
            List<string> options = new List<string>(cachedBodyFilterBodies.Count + 1);
            options.Add(Loc.FleetBodyAll);
            foreach (CelestialBody body in cachedBodyFilterBodies)
            {
                string name = (body.bodyName ?? string.Empty).Replace("^N", string.Empty).Trim();
                options.Add(string.IsNullOrEmpty(name) ? "?" : name);
            }
            cachedBodyFilterOptions = options.ToArray();
            bodyFilterIndex = Mathf.Clamp(bodyFilterIndex, 0, cachedBodyFilterOptions.Length - 1);
            bodyFilterOptionsInitialized = true;
        }

        private CelestialBody GetSelectedBody()
        {
            if (bodyFilterIndex <= 0)
                return null;
            int index = bodyFilterIndex - 1;
            if (index < 0 || index >= cachedBodyFilterBodies.Count)
                return null;
            return cachedBodyFilterBodies[index];
        }

        private string GetCurrentBodyFilterLabel()
        {
            if (cachedBodyFilterOptions == null || cachedBodyFilterOptions.Length == 0)
                return Loc.Unit_NA;
            int index = Mathf.Clamp(bodyFilterIndex, 0, cachedBodyFilterOptions.Length - 1);
            return cachedBodyFilterOptions[index];
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

        private static readonly DebrisVisibility[] DebrisVisibilityOptions =
            { DebrisVisibility.All, DebrisVisibility.Only, DebrisVisibility.Hide };

        private DebrisVisibility DrawDebrisVisibilityToggle(DebrisVisibility current)
        {
            string[] labels = { Loc.DebrisAll, Loc.DebrisOnly, Loc.DebrisHide };
            DebrisVisibility selected = current;
            for (int i = 0; i < labels.Length; i++)
            {
                bool isSelected = DebrisVisibilityOptions[i] == current;
                float width = ButtonWidth(labels[i], 72f);
                if (GUILayout.Toggle(isSelected, labels[i], _buttonStyle, GUILayout.Width(width), GUILayout.Height(ButtonHeight)) && !isSelected)
                    selected = DebrisVisibilityOptions[i];
            }
            return selected;
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

        private void ApplyOrbitKeepSettings(bool refreshStatus = true)
        {
            if (editData != null && targetVessel != null)
            {
                // Parse target parameters (input is in km, convert to meters)
                if (double.TryParse(inputAp, out double ap))
                    editData.TargetApoapsis = ap * 1000.0;
                if (double.TryParse(inputPe, out double pe))
                    editData.TargetPeriapsis = pe * 1000.0;
                if (double.TryParse(inputInc, out double inc))
                    editData.TargetInclination = inc;
                if (double.TryParse(inputInterval, out double interval))
                    editData.CheckInterval = Math.Max(60.0, interval); // Min 60s

                if (double.TryParse(inputTolerance, out double tolerance))
                {
                    editData.Tolerance = Math.Max(1.0, Math.Min(20.0, tolerance));
                    inputTolerance = editData.Tolerance.ToString("F1");
                }

                editData.AutoKeepEnabled = inputAutoKeepEnabled;
                editData.AllowRcsEngines = inputAllowRcs;
            }

            // Save to scenario
            if (editData != null)
                StationKeepScenario.Instance?.SetVesselData(editData);

            ScreenMessages.PostScreenMessage(Loc.SettingsSaved,
                OrbitalKeepSettings.MessageDuration, ScreenMessageStyle.UPPER_CENTER);

            if (refreshStatus && targetVessel != null)
            {
                RefreshTargetStatus();
                RefreshVessel();
            }
        }

        private void RefreshTargetStatus()
        {
            if (targetVessel == null || editData == null)
                return;

            VesselKeepModule module = targetVessel.GetComponent<VesselKeepModule>();
            if (module != null)
            {
                module.RefreshStatus();
                return;
            }

            if (!VesselKeepModule.IsValidOrbitForKeeping(targetVessel))
            {
                editData.Status = KeepStatus.InvalidOrbit;
                return;
            }

            var correction = DeltaVCalculator.CalculateCorrection(targetVessel, editData);
            if (!correction.NeedsCorrection)
            {
                editData.Status = editData.AutoKeepEnabled ? KeepStatus.Nominal : KeepStatus.Disabled;
                return;
            }

            ResourceManager.EngineInfo engineInfo = targetVessel.loaded
                ? ResourceManager.FindBestEngine(
                    targetVessel,
                    editData.EngineMode,
                    editData.AllowRcsEngines)
                : ResourceManager.FindBestEngineUnloaded(
                    targetVessel.protoVessel,
                    editData.EngineMode,
                    editData.AllowRcsEngines);

            if (!engineInfo.Found)
            {
                editData.Status = KeepStatus.NoEngine;
                return;
            }

            var resourceCheck = ResourceManager.CheckResources(targetVessel, correction.TotalDeltaV, engineInfo);
            editData.Status = resourceCheck.Sufficient ? KeepStatus.Drifting : KeepStatus.InsufficientResources;
        }

        private void SetTargetFromCurrentOrbit()
        {
            if (targetVessel == null || targetVessel.orbit == null || editData == null)
                return;

            editData.TargetApoapsis = targetVessel.orbit.ApA;
            editData.TargetPeriapsis = targetVessel.orbit.PeA;
            editData.TargetInclination = targetVessel.orbit.inclination;
            SyncInputFields();

            StationKeepScenario.Instance?.SetVesselData(editData);
            InvalidateLifetimeEstimate();
        }

        private void InvalidateLifetimeEstimate()
        {
            cachedLifetimeVesselId = Guid.Empty;
            cachedLifetimeSignature = 0;
            lastLifetimeEstimateTime = -1f;
        }

        private StationKeepEstimator.EstimateResult GetLifetimeEstimate()
        {
            if (targetVessel == null || editData == null)
            {
                return new StationKeepEstimator.EstimateResult
                {
                    Available = false,
                    UnavailableReason = Loc.Unit_NA
                };
            }

            float now = Time.realtimeSinceStartup;
            int signature = BuildLifetimeEstimateSignature();
            if (cachedLifetimeVesselId == targetVessel.id &&
                cachedLifetimeSignature == signature &&
                now - lastLifetimeEstimateTime < LIFETIME_ESTIMATE_CACHE_INTERVAL)
            {
                return cachedLifetimeEstimate;
            }

            cachedLifetimeEstimate = StationKeepEstimator.Estimate(targetVessel, editData);
            cachedLifetimeVesselId = targetVessel.id;
            cachedLifetimeSignature = signature;
            lastLifetimeEstimateTime = now;
            return cachedLifetimeEstimate;
        }

        private int BuildLifetimeEstimateSignature()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (targetVessel != null ? targetVessel.id.GetHashCode() : 0);
                hash = hash * 31 + (targetVessel != null && targetVessel.loaded ? 1 : 0);
                hash = hash * 31 + ResourceManager.GetVesselMass(targetVessel).GetHashCode();
                hash = hash * 31 + editData.TargetApoapsis.GetHashCode();
                hash = hash * 31 + editData.TargetPeriapsis.GetHashCode();
                hash = hash * 31 + editData.TargetInclination.GetHashCode();
                hash = hash * 31 + editData.Tolerance.GetHashCode();
                hash = hash * 31 + editData.CheckInterval.GetHashCode();
                hash = hash * 31 + editData.EngineMode.GetHashCode();
                hash = hash * 31 + (editData.AllowRcsEngines ? 1 : 0);
                return hash;
            }
        }

        // ======================================================================
        //  GUI HELPERS
        // ======================================================================

        private static void DrawParamRow(string label, string value)
        {
            float rowH = ButtonHeight;
            GUILayout.BeginHorizontal(GUILayout.Height(rowH));
            GUILayout.Label(label, _rowLabelStyle, GUILayout.Width(GetLabelWidth()), GUILayout.Height(rowH));
            GUILayout.Label(value, _rowLabelStyle, GUILayout.ExpandWidth(true), GUILayout.Height(rowH));
            GUILayout.EndHorizontal();
        }

        private static void DrawSeparator()
        {
            GUILayout.Space(4);
            GUILayout.Box(GUIContent.none, GUILayout.ExpandWidth(true), GUILayout.Height(1));
            GUILayout.Space(4);
        }

        private static float GetNoteTextWidth()
        {
            const float boxHorizontalPadding = 16f;
            return Mathf.Max(200f, GetMainColumnWidth() - boxHorizontalPadding);
        }

        private static void DrawEstimateNotes()
        {
            float noteWidth = GetNoteTextWidth();
            GUILayout.Space(4);
            GUILayout.Label(Loc.EstimateIntervalNote, _richStyle, GUILayout.Width(noteWidth));
            GUILayout.Label(Loc.EstimateEcNote, _richStyle, GUILayout.Width(noteWidth));
        }

        private static string DrawInputRow(string label, string currentValue)
        {
            float rowH = ButtonHeight;
            GUILayout.BeginHorizontal(GUILayout.Height(rowH));
            GUILayout.Label(label, _rowLabelStyle, GUILayout.Width(GetLabelWidth()), GUILayout.Height(rowH));
            string newValue = GUILayout.TextField(currentValue, _textFieldStyle, GUILayout.Width(GetInputWidth()), GUILayout.Height(rowH));
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);
            return newValue;
        }

        private static bool DrawLabeledToggle(bool selected, string label, float rowHeight)
        {
            const float toggleSize = 22f;
            const float rowLeftNudge = -4f;

            GUILayout.BeginHorizontal(GUILayout.Height(rowHeight));
            if (rowLeftNudge != 0f)
                GUILayout.Space(rowLeftNudge);
            Rect slot = GUILayoutUtility.GetRect(toggleSize + 6f, rowHeight, GUILayout.Width(toggleSize + 6f));
            Rect toggleRect = new Rect(slot.x, slot.y + (slot.height - toggleSize) * 0.5f, toggleSize, toggleSize);
            bool newSelected = GUI.Toggle(toggleRect, selected, GUIContent.none, _toggleStyle);
            GUILayout.Label(label, _rowLabelStyle, GUILayout.ExpandWidth(true), GUILayout.Height(rowHeight));
            GUILayout.EndHorizontal();
            return newSelected;
        }

        private static bool DrawCompactLabeledToggle(bool selected, string label, float rowHeight)
        {
            const float toggleSize = 22f;
            float labelWidth = _rowLabelStyle.CalcSize(new GUIContent(label ?? string.Empty)).x;
            float totalWidth = toggleSize + 8f + labelWidth;

            GUILayout.BeginHorizontal(GUILayout.Width(totalWidth), GUILayout.Height(rowHeight));
            Rect slot = GUILayoutUtility.GetRect(toggleSize + 4f, rowHeight, GUILayout.Width(toggleSize + 4f));
            Rect toggleRect = new Rect(slot.x, slot.y + (slot.height - toggleSize) * 0.5f, toggleSize, toggleSize);
            bool newSelected = GUI.Toggle(toggleRect, selected, GUIContent.none, _toggleStyle);
            GUILayout.Label(label, _rowLabelStyle, GUILayout.Width(labelWidth), GUILayout.Height(rowHeight));
            GUILayout.EndHorizontal();
            return newSelected;
        }

        private static bool DrawFoldoutHeader(string title, bool expanded)
        {
            string marker = expanded ? "▼" : "▶";
            return GUILayout.Toggle(expanded, $"{marker} {title}", _buttonStyle, GUILayout.Height(ButtonHeight));
        }

        private static string TruncateWithEllipsis(string text, float maxWidth, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text)) return text;
            GUIContent content = new GUIContent(text);
            if (style.CalcSize(content).x <= maxWidth) return text;
            const string ellipsis = "...";
            float ellipsisWidth = style.CalcSize(new GUIContent(ellipsis)).x;
            float available = maxWidth - ellipsisWidth;
            for (int i = text.Length - 1; i > 0; i--)
            {
                string truncated = text.Substring(0, i);
                if (style.CalcSize(new GUIContent(truncated)).x <= available)
                    return truncated + ellipsis;
            }
            return ellipsis;
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
            return GetMainColumnWidth() * 2f + BASE_MAIN_COLUMN_GAP;
        }

        private static float GetMainColumnWidth()
        {
            float engineModeWidth = GetLabelWidth() + GetEngineModeOptionWidth() * 2f + 32f;
            float footerWidth = ButtonWidth(Loc.FleetOverview, 160f) + ButtonWidth(Loc.RemoveKeeping, 160f) + 24f;
            return Mathf.Max(BASE_MAIN_COLUMN_WIDTH, Mathf.Max(engineModeWidth, footerWidth));
        }

        private static float GetMainColumnGap()
        {
            return BASE_MAIN_COLUMN_GAP;
        }

        private static float GetFleetMinWidth()
        {
            float baseWidth = BASE_FLEET_WIDTH;
            if (_labelStyle == null || _windowStyle == null)
                return baseWidth;
            float hintWidth = _labelStyle.CalcSize(new GUIContent(Loc.FleetSelectionHint)).x;
            float debrisWidth =
                ButtonWidth(Loc.DebrisOnly, 72f) +
                ButtonWidth(Loc.DebrisAll, 72f) +
                ButtonWidth(Loc.DebrisHide, 72f) +
                24f;
            float padding = 20f + _windowStyle.padding.left + _windowStyle.padding.right + _labelStyle.margin.left + _labelStyle.margin.right;
            return Mathf.Max(baseWidth, Mathf.Max(hintWidth + padding, debrisWidth + padding));
        }

        private void UpdateFleetWindowWidth()
        {
            if (!showFleetView)
                return;
            float minWidth = GetFleetMinWidth();
            if (minWidth <= fleetWindowRect.width)
                return;
            fleetWindowRect.width = minWidth;
            fleetWindowRect.height = 0;
        }

        private static float GetLabelWidth()
        {
            return 175f;
        }

        private static float GetInputWidth()
        {
            return 150f;
        }

        private static float GetStatusLabelWidth()
        {
            return 50f;
        }

        private static float GetEngineModeLabelWidth()
        {
            return 100f;
        }

        private static float GetEngineModeOptionWidth()
        {
            return Mathf.Max(
                CompactToggleWidth(Loc.EngineModeIgnited),
                CompactToggleWidth(Loc.EngineModeActive));
        }

        private static float CompactToggleWidth(string label)
        {
            const float toggleSize = 22f;
            float labelWidth = _rowLabelStyle != null
                ? _rowLabelStyle.CalcSize(new GUIContent(label ?? string.Empty)).x
                : (label ?? string.Empty).Length * BASE_FONT_SIZE * 0.55f;
            return toggleSize + 8f + labelWidth;
        }

        private static float ButtonWidth(string label, float minWidth)
        {
            float width = _buttonStyle != null
                ? _buttonStyle.CalcSize(new GUIContent(label ?? string.Empty)).x + 28f
                : (label ?? string.Empty).Length * BASE_FONT_SIZE * 0.75f + 28f;
            return Mathf.Ceil(Mathf.Max(minWidth, width));
        }

        private static float GetFleetNameWidth()
        {
            return 200f;
        }

        private static float GetFleetStatusWidth()
        {
            return 80f;
        }

        /// <summary>
        /// Text width for truncation; based on "All Bodies" at current font size (no padding).
        /// Longer names are truncated with "..".
        /// </summary>
        private static float GetBodyFilterTextMaxWidth()
        {
            GUIStyle style = _labelStyle ?? GUI.skin.label;
            return Mathf.Round(style.CalcSize(new GUIContent(Loc.FleetBodyAll)).x);
        }

        /// <summary>
        /// Total width for body filter button: base text width + internal left/right padding.
        /// Button is fixed at this width, right-aligned.
        /// </summary>
        private static float GetBodyFilterButtonWidth()
        {
            float textWidth = GetBodyFilterTextMaxWidth();
            return Mathf.Round(textWidth + 16f); // 8px padding on each side inside button
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

        private static string FormatCorrectionCount(double count)
        {
            if (double.IsNaN(count) || count <= 0.0)
                return "0";
            if (double.IsPositiveInfinity(count) || count > 9999.0)
                return ">9999";
            if (count >= 100.0)
                return count.ToString("F0");
            if (count >= 10.0)
                return count.ToString("F1");
            return count.ToString("F2");
        }

        private static string FormatLongTime(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0.0)
                return Loc.Unit_NA;
            if (double.IsPositiveInfinity(seconds))
                return Loc.EstimateTimeGT100Years;

            double dayLength = GameSettings.KERBIN_TIME ? 21600.0 : 86400.0;
            double yearLength = dayLength * (GameSettings.KERBIN_TIME ? 426.0 : 365.0);

            if (seconds > yearLength * 100.0)
                return Loc.EstimateTimeGT100Years;

            if (seconds >= yearLength)
            {
                int years = (int)(seconds / yearLength);
                int days = (int)((seconds % yearLength) / dayLength);
                return Loc.Format(Loc.EstimateTimeYearsDays, years.ToString(), days.ToString());
            }

            if (seconds >= dayLength)
            {
                int days = (int)(seconds / dayLength);
                int hours = (int)((seconds % dayLength) / 3600.0);
                return Loc.Format(Loc.EstimateTimeDaysHours, days.ToString(), hours.ToString());
            }

            return FormatTime(seconds);
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
            int size = (int)BASE_FONT_SIZE;
            if (size == _cachedFontSize && _labelStyle != null)
                return;

            _cachedFontSize = size;

            _labelStyle = CreateSingleLineStyle(GUI.skin.label, size);
            _rowLabelStyle = CreateSingleLineStyle(GUI.skin.label, size, TextAnchor.MiddleLeft);
            _boldStyle = CreateSingleLineStyle(GUI.skin.label, size, TextAnchor.MiddleLeft, FontStyle.Bold);
            _richStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                richText = true,
                wordWrap = true,
                clipping = TextClipping.Overflow,
                alignment = TextAnchor.UpperLeft
            };
            _statusRichStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                richText = true,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                alignment = TextAnchor.MiddleLeft
            };
            _buttonStyle = CreateSingleLineStyle(GUI.skin.button, size, TextAnchor.MiddleCenter, FontStyle.Bold);
            _buttonStyle.padding = new RectOffset(GUI.skin.button.padding.left, GUI.skin.button.padding.right, 6, 6);
            _toggleStyle = CreateSingleLineStyle(GUI.skin.toggle, size, TextAnchor.MiddleLeft, FontStyle.Bold);
            _toggleStyle.padding = new RectOffset(0, 0, 0, 0);
            _toggleStyle.margin = new RectOffset(0, 0, 0, 0);
            _textFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = size,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(GUI.skin.textField.padding.left, GUI.skin.textField.padding.right, 4, 4)
            };
            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = size,
                padding = new RectOffset(8, 8, 6, 6),
                stretchWidth = true
            };
            _fleetBoxStyle = new GUIStyle(_boxStyle);
            _windowStyle = new GUIStyle(GUI.skin.window) { fontSize = size + 1 };
        }

        private static GUIStyle CreateSingleLineStyle(GUIStyle template, int fontSize, TextAnchor alignment = TextAnchor.MiddleLeft, FontStyle fontStyle = FontStyle.Normal)
        {
            return new GUIStyle(template)
            {
                fontSize = fontSize,
                fontStyle = fontStyle,
                alignment = alignment,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 3, 3)
            };
        }

        private static float ButtonHeight => BASE_FONT_SIZE + 16f;
    }
}
