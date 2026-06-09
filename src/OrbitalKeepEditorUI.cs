using System;
using System.Collections.Generic;
using ClickThroughFix;
using UnityEngine;

namespace OrbitalKeeper
{
    /// <summary>
    /// Editor-side estimator for planning station-keeping resource lifetime before launch.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class OrbitalKeepEditorUI : MonoBehaviour
    {
        private const int WINDOW_ID = 0x4F4B_0101;
        private const float BASE_FONT_SIZE = 12f;
        private const float BASE_WINDOW_WIDTH = 470f;

        private bool guiVisible;
        private bool guiConfigExpanded = true;
        private Rect windowRect = new Rect(280, 160, BASE_WINDOW_WIDTH, 0);
        private List<CelestialBody> bodies = new List<CelestialBody>();
        private int bodyIndex;

        private string inputAp = "100.000";
        private string inputPe = "100.000";
        private string inputTolerance = "5.0";
        private string inputInterval = "3600";
        private bool inputAllowRcs;
        private string inputFontSize = "12";

        private static int cachedFontSize;
        private static GUIStyle labelStyle;
        private static GUIStyle boldStyle;
        private static GUIStyle richStyle;
        private static GUIStyle buttonStyle;
        private static GUIStyle textFieldStyle;
        private static GUIStyle boxStyle;
        private static GUIStyle windowStyle;
        private static GUIStyle centeredLabelStyle;

        private void Start()
        {
            if (HighLogic.LoadedScene != GameScenes.EDITOR)
            {
                Destroy(this);
                return;
            }

            Loc.Load();
            OrbitalKeepSettings.LoadSettings();
            inputTolerance = OrbitalKeepSettings.DefaultTolerance.ToString("F1");
            inputInterval = OrbitalKeepSettings.DefaultCheckInterval.ToString("F0");
            inputFontSize = OrbitalKeepSettings.FontSize.ToString();
            RefreshBodies();
            SetSuggestedOrbitForSelectedBody();
            windowRect.width = GetWindowWidth();
        }

        private void Update()
        {
            HandleGuiHotkey();
        }

        private void OnGUI()
        {
            if (!guiVisible)
                return;

            GUI.skin = HighLogic.Skin;
            RebuildStylesIfNeeded();
            windowRect = ClickThruBlocker.GUILayoutWindow(
                WINDOW_ID,
                windowRect,
                DrawWindow,
                Loc.EditorWindowTitle,
                windowStyle,
                GUILayout.MinWidth(GetWindowWidth()));
        }

        private void HandleGuiHotkey()
        {
            if (OrbitalKeepSettings.GuiToggleKey == KeyCode.None)
                return;
            if (!Input.GetKeyDown(OrbitalKeepSettings.GuiToggleKey))
                return;
            if (!AreHotkeyModifiersSatisfied(
                OrbitalKeepSettings.GuiToggleAlt,
                OrbitalKeepSettings.GuiToggleCtrl,
                OrbitalKeepSettings.GuiToggleShift))
            {
                return;
            }

            guiVisible = !guiVisible;
        }

        private static bool AreHotkeyModifiersSatisfied(bool requireAlt, bool requireCtrl, bool requireShift)
        {
            bool altPressed = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool ctrlPressed = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (requireAlt && !altPressed)
                return false;
            if (requireCtrl && !ctrlPressed)
                return false;
            if (requireShift && !shiftPressed)
                return false;
            return true;
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.Space(6);
            DrawOrbitInputs();
            GUILayout.Space(8);
            DrawCraftEstimate();
            GUILayout.Space(8);
            DrawGuiConfigSection();
            GUILayout.Space(6);

            if (GUILayout.Button(Loc.Close, buttonStyle))
                guiVisible = false;

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void DrawOrbitInputs()
        {
            GUILayout.Label(Loc.EditorSectionOrbit, boldStyle);
            GUILayout.BeginVertical(boxStyle);

            float lineHeight = OrbitalKeepSettings.FontSize + 8f;
            GUILayout.BeginHorizontal(GUILayout.Height(lineHeight));
            GUILayout.Label(Loc.EditorBody, labelStyle, GUILayout.Width(GetLabelWidth()), GUILayout.Height(lineHeight));
            if (GUILayout.Button("<", buttonStyle, GUILayout.Width(GetSmallButtonWidth()), GUILayout.Height(lineHeight)))
                ChangeBody(-1);
            GUILayout.Label(
                GetSelectedBodyName(),
                centeredLabelStyle ?? labelStyle,
                GUILayout.Width(GetBodyNameWidth()),
                GUILayout.Height(lineHeight));
            if (GUILayout.Button(">", buttonStyle, GUILayout.Width(GetSmallButtonWidth()), GUILayout.Height(lineHeight)))
                ChangeBody(1);
            GUILayout.EndHorizontal();

            if (GUILayout.Button(Loc.EditorUseSuggestedOrbit, buttonStyle))
                SetSuggestedOrbitForSelectedBody();

            inputAp = DrawInputRow(Loc.TargetAp, inputAp);
            inputPe = DrawInputRow(Loc.TargetPe, inputPe);
            inputTolerance = DrawInputRow($"{Loc.Format(Loc.ToleranceLabel, inputTolerance)} [1-20]", inputTolerance);
            inputInterval = DrawInputRow(Loc.CheckInterval, inputInterval);
            inputAllowRcs = GUILayout.Toggle(inputAllowRcs, Loc.AllowRcsEnginesToggle, buttonStyle);

            GUILayout.EndVertical();
        }

        private void DrawCraftEstimate()
        {
            GUILayout.Label(Loc.EditorSectionCraft, boldStyle);
            GUILayout.BeginVertical(boxStyle);

            ShipConstruct ship = EditorLogic.fetch?.ship;
            double mass = ResourceManager.GetEditorShipMass(ship);
            DrawParamRow(Loc.EditorCraftMass, $"{mass:F3} t");

            StationKeepEstimator.EditorEstimateResult estimate = BuildEstimate(ship);
            if (estimate.Available)
            {
                DrawParamRow(Loc.EditorBestEngine, $"{estimate.EngineInfo.Isp:F0} s");
                DrawSeparator();
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

        private void DrawGuiConfigSection()
        {
            GUILayout.Label(Loc.SectionConfig, boldStyle);
            GUILayout.BeginVertical(boxStyle);

            bool prevExpanded = guiConfigExpanded;
            guiConfigExpanded = DrawFoldoutHeader(Loc.ConfigGuiSettings, guiConfigExpanded);
            if (guiConfigExpanded)
            {
                inputFontSize = DrawInputRow(
                    $"{Loc.Format(Loc.FontSizeLabel, inputFontSize)} [10-20]",
                    inputFontSize);
                GUILayout.Label(
                    Loc.Format(Loc.CurrentHotkey, FormatHotkeyDisplay(
                        OrbitalKeepSettings.GuiToggleKey.ToString(),
                        OrbitalKeepSettings.GuiToggleAlt,
                        OrbitalKeepSettings.GuiToggleCtrl,
                        OrbitalKeepSettings.GuiToggleShift)),
                    labelStyle);

                if (GUILayout.Button(Loc.ApplySettings, buttonStyle))
                    ApplyGuiSettings();
            }

            if (prevExpanded != guiConfigExpanded)
                windowRect.height = 0;

            GUILayout.EndVertical();
        }

        private void ApplyGuiSettings()
        {
            bool changed = false;

            if (int.TryParse(inputFontSize, out int parsedSize))
            {
                int newFontSize = Math.Max(10, Math.Min(20, parsedSize));
                inputFontSize = newFontSize.ToString();
                if (newFontSize != OrbitalKeepSettings.FontSize)
                {
                    OrbitalKeepSettings.FontSize = newFontSize;
                    cachedFontSize = 0;
                    centeredLabelStyle = null;
                    windowRect.width = GetWindowWidth();
                    windowRect.height = 0;
                    changed = true;
                }
            }

            if (changed)
            {
                OrbitalKeepSettings.SaveUserSettings();
                ScreenMessages.PostScreenMessage(
                    Loc.SettingsSaved,
                    OrbitalKeepSettings.MessageDuration,
                    ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private static bool DrawFoldoutHeader(string title, bool expanded)
        {
            string marker = expanded ? "▼" : "▶";
            return GUILayout.Toggle(expanded, $"{marker} {title}", buttonStyle);
        }

        private StationKeepEstimator.EditorEstimateResult BuildEstimate(ShipConstruct ship)
        {
            if (!TryParseInputs(out double targetAp, out double targetPe, out double tolerance, out double interval))
            {
                return new StationKeepEstimator.EditorEstimateResult
                {
                    Available = false,
                    UnavailableReason = Loc.EstimateUnavailableInvalidOrbit
                };
            }

            return StationKeepEstimator.EstimateEditor(
                ship,
                GetSelectedBody(),
                targetAp,
                targetPe,
                tolerance,
                interval,
                inputAllowRcs);
        }

        private bool TryParseInputs(
            out double targetApoapsis,
            out double targetPeriapsis,
            out double tolerance,
            out double interval)
        {
            targetApoapsis = 0.0;
            targetPeriapsis = 0.0;
            tolerance = OrbitalKeepSettings.DefaultTolerance;
            interval = OrbitalKeepSettings.DefaultCheckInterval;

            if (!double.TryParse(inputAp, out double apKm))
                return false;
            if (!double.TryParse(inputPe, out double peKm))
                return false;

            if (double.TryParse(inputTolerance, out double parsedTolerance))
                tolerance = Math.Max(1.0, Math.Min(20.0, parsedTolerance));
            if (double.TryParse(inputInterval, out double parsedInterval))
                interval = Math.Max(60.0, parsedInterval);

            targetApoapsis = apKm * 1000.0;
            targetPeriapsis = peKm * 1000.0;
            return targetApoapsis >= targetPeriapsis && targetPeriapsis > 0.0;
        }

        private void RefreshBodies()
        {
            bodies.Clear();
            if (FlightGlobals.Bodies != null)
                bodies.AddRange(FlightGlobals.Bodies);

            if (bodies.Count == 0)
                return;

            int homeIndex = bodies.FindIndex(body => body != null && body.isHomeWorld);
            if (homeIndex >= 0)
                bodyIndex = homeIndex;
            else
                bodyIndex = Math.Max(0, bodies.FindIndex(body => body != null && body.atmosphere));
        }

        private void ChangeBody(int direction)
        {
            if (bodies.Count == 0)
                return;

            bodyIndex = (bodyIndex + direction + bodies.Count) % bodies.Count;
            SetSuggestedOrbitForSelectedBody();
        }

        private CelestialBody GetSelectedBody()
        {
            if (bodies.Count == 0)
                return null;
            bodyIndex = Math.Max(0, Math.Min(bodies.Count - 1, bodyIndex));
            return bodies[bodyIndex];
        }

        private string GetSelectedBodyName()
        {
            CelestialBody body = GetSelectedBody();
            if (body == null)
                return Loc.Unit_NA;
            return (body.bodyName ?? Loc.Unit_NA).Replace("^N", string.Empty).Trim();
        }

        private void SetSuggestedOrbitForSelectedBody()
        {
            CelestialBody body = GetSelectedBody();
            if (body == null)
                return;

            double altitude = body.atmosphere
                ? Math.Max(body.atmosphereDepth + 30000.0, 100000.0)
                : 100000.0;
            inputAp = (altitude / 1000.0).ToString("F3");
            inputPe = (altitude / 1000.0).ToString("F3");
        }

        private static void DrawParamRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(GetLabelWidth()));
            GUILayout.Label(value, labelStyle);
            GUILayout.EndHorizontal();
        }

        private static string DrawInputRow(string label, string currentValue)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(GetLabelWidth()));
            string newValue = GUILayout.TextField(currentValue, textFieldStyle, GUILayout.Width(GetInputWidth()));
            GUILayout.EndHorizontal();
            return newValue;
        }

        private static void DrawSeparator()
        {
            GUILayout.Space(4);
            GUILayout.Box(GUIContent.none, GUILayout.ExpandWidth(true), GUILayout.Height(1));
            GUILayout.Space(4);
        }

        private static void DrawEstimateNotes()
        {
            GUILayout.Space(4);
            GUILayout.Label(Loc.EstimateIntervalNote, richStyle);
            GUILayout.Label(Loc.EstimateEcNote, richStyle);
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

        private static string FormatTime(double seconds)
        {
            if (seconds < 0)
                return Loc.Unit_NA;

            int totalSeconds = (int)seconds;
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int secs = totalSeconds % 60;

            if (hours > 0)
                return Loc.Format(Loc.TimeFormat_hms, hours.ToString(), minutes.ToString(), secs.ToString());
            if (minutes > 0)
                return Loc.Format(Loc.TimeFormat_ms, minutes.ToString(), secs.ToString());
            return Loc.Format(Loc.TimeFormat_s, secs.ToString());
        }

        private static string FormatHotkeyDisplay(string keyInput, bool alt, bool ctrl, bool shift)
        {
            string key = string.IsNullOrEmpty(keyInput) ? Loc.Unit_NA : keyInput.ToUpperInvariant();
            string prefix = string.Empty;
            if (ctrl) prefix += "Ctrl+";
            if (alt) prefix += "Alt+";
            if (shift) prefix += "Shift+";
            return prefix + key;
        }

        private static void RebuildStylesIfNeeded()
        {
            int size = OrbitalKeepSettings.FontSize;
            if (size == cachedFontSize && labelStyle != null)
                return;

            cachedFontSize = size;
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = size, wordWrap = true };
            boldStyle = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold };
            richStyle = new GUIStyle(GUI.skin.label) { fontSize = size, richText = true, wordWrap = true };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = size };
            textFieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = size };
            boxStyle = new GUIStyle(GUI.skin.box) { fontSize = size };
            windowStyle = new GUIStyle(GUI.skin.window) { fontSize = size };
            centeredLabelStyle = new GUIStyle(labelStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false
            };
        }

        private static float GetWindowWidth()
        {
            return Mathf.Round(BASE_WINDOW_WIDTH * OrbitalKeepSettings.FontSize / BASE_FONT_SIZE);
        }

        private static float GetLabelWidth()
        {
            return Mathf.Round(175f * OrbitalKeepSettings.FontSize / BASE_FONT_SIZE);
        }

        private static float GetInputWidth()
        {
            return Mathf.Round(150f * OrbitalKeepSettings.FontSize / BASE_FONT_SIZE);
        }

        private static float GetSmallButtonWidth()
        {
            return Mathf.Round(55f * OrbitalKeepSettings.FontSize / BASE_FONT_SIZE);
        }

        private static float GetBodyNameWidth()
        {
            return Mathf.Round(150f * OrbitalKeepSettings.FontSize / BASE_FONT_SIZE);
        }
    }
}
