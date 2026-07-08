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
        private const float BASE_FONT_SIZE = 18f;
        private const float BASE_WINDOW_WIDTH = 520f;

        private bool guiVisible;
        private Rect windowRect = new Rect(280, 160, BASE_WINDOW_WIDTH, 0);
        private List<CelestialBody> bodies = new List<CelestialBody>();
        private int bodyIndex;

        private string inputAp = "100.000";
        private string inputPe = "100.000";
        private string inputTolerance = "5.0";
        private string inputInterval = "3600";
        private bool inputAllowRcs;
        private float _lastUiScaleFactor = -1f;

        private static int cachedFontSize;
        private static GUIStyle labelStyle;
        private static GUIStyle rowLabelStyle;
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
            OrbitalKeepSettings.SyncFromParameters();
            inputTolerance = OrbitalKeepSettings.DefaultTolerance.ToString("F1");
            inputInterval = OrbitalKeepSettings.DefaultCheckInterval.ToString("F0");
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

            float uiScale = UIScale.Factor;
            if (!Mathf.Approximately(uiScale, _lastUiScaleFactor))
            {
                if (_lastUiScaleFactor > 0f)
                {
                    windowRect.x *= _lastUiScaleFactor / uiScale;
                    windowRect.y *= _lastUiScaleFactor / uiScale;
                }
                _lastUiScaleFactor = uiScale;
                windowRect.height = 0;
                windowRect = UIScale.ClampToGuiScreen(windowRect);
            }

            UIScale.BeginGUI();
            try
            {
                windowRect = ClickThruBlocker.GUILayoutWindow(
                    WINDOW_ID,
                    windowRect,
                    DrawWindow,
                    Loc.EditorWindowTitle,
                    windowStyle,
                    GUILayout.MinWidth(GetWindowWidth()));
                windowRect = UIScale.ClampToGuiScreen(windowRect);
            }
            finally
            {
                UIScale.EndGUI();
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
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.Space(6);
            DrawOrbitInputs();
            GUILayout.Space(8);
            DrawCraftEstimate();
            GUILayout.Space(6);

            if (GUILayout.Button(Loc.Close, buttonStyle, GUILayout.Height(ButtonHeight)))
                guiVisible = false;

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void DrawOrbitInputs()
        {
            GUILayout.Label(Loc.EditorSectionOrbit, boldStyle);
            GUILayout.BeginVertical(boxStyle);

            float lineHeight = ButtonHeight;
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

            if (GUILayout.Button(Loc.EditorUseSuggestedOrbit, buttonStyle, GUILayout.Height(ButtonHeight)))
                SetSuggestedOrbitForSelectedBody();

            inputAp = DrawInputRow(Loc.TargetAp, inputAp);
            inputPe = DrawInputRow(Loc.TargetPe, inputPe);
            inputTolerance = DrawInputRow($"{Loc.Format(Loc.ToleranceLabel, inputTolerance)} [1-20]", inputTolerance);
            inputInterval = DrawInputRow(Loc.CheckInterval, inputInterval);
            inputAllowRcs = GUILayout.Toggle(inputAllowRcs, Loc.AllowRcsEnginesToggle, buttonStyle, GUILayout.ExpandWidth(true), GUILayout.Height(ButtonHeight));

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
            float rowH = ButtonHeight;
            GUILayout.BeginHorizontal(GUILayout.Height(rowH));
            GUILayout.Label(label, rowLabelStyle ?? labelStyle, GUILayout.Width(GetLabelWidth()), GUILayout.Height(rowH));
            GUILayout.Label(value, rowLabelStyle ?? labelStyle, GUILayout.ExpandWidth(true), GUILayout.Height(rowH));
            GUILayout.EndHorizontal();
        }

        private static string DrawInputRow(string label, string currentValue)
        {
            float rowH = ButtonHeight;
            GUILayout.BeginHorizontal(GUILayout.Height(rowH));
            GUILayout.Label(label, rowLabelStyle ?? labelStyle, GUILayout.Width(GetLabelWidth()), GUILayout.Height(rowH));
            string newValue = GUILayout.TextField(currentValue, textFieldStyle, GUILayout.Width(GetInputWidth()), GUILayout.Height(rowH));
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);
            return newValue;
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
            const float windowHorizontalPadding = 24f;
            return Mathf.Max(200f, GetWindowWidth() - boxHorizontalPadding - windowHorizontalPadding);
        }

        private static void DrawEstimateNotes()
        {
            float noteWidth = GetNoteTextWidth();
            GUILayout.Space(4);
            GUILayout.Label(Loc.EstimateIntervalNote, richStyle, GUILayout.Width(noteWidth));
            GUILayout.Label(Loc.EstimateEcNote, richStyle, GUILayout.Width(noteWidth));
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

        private static void RebuildStylesIfNeeded()
        {
            int size = (int)BASE_FONT_SIZE;
            if (size == cachedFontSize && labelStyle != null)
                return;

            cachedFontSize = size;
            labelStyle = CreateSingleLineStyle(GUI.skin.label, size);
            rowLabelStyle = CreateSingleLineStyle(GUI.skin.label, size, TextAnchor.MiddleLeft);
            boldStyle = CreateSingleLineStyle(GUI.skin.label, size, TextAnchor.MiddleLeft, FontStyle.Bold);
            richStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                richText = true,
                wordWrap = true,
                clipping = TextClipping.Overflow,
                alignment = TextAnchor.UpperLeft
            };
            buttonStyle = CreateSingleLineStyle(GUI.skin.button, size, TextAnchor.MiddleCenter, FontStyle.Bold);
            buttonStyle.padding = new RectOffset(GUI.skin.button.padding.left, GUI.skin.button.padding.right, 6, 6);
            textFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = size,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(GUI.skin.textField.padding.left, GUI.skin.textField.padding.right, 4, 4)
            };
            boxStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = size,
                padding = new RectOffset(8, 8, 6, 6),
                stretchWidth = true
            };
            windowStyle = new GUIStyle(GUI.skin.window) { fontSize = size + 1 };
            centeredLabelStyle = CreateSingleLineStyle(labelStyle, size, TextAnchor.MiddleCenter);
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

        private static float GetWindowWidth()
        {
            float bodyRowWidth = GetLabelWidth() + GetSmallButtonWidth() * 2f + GetBodyNameWidth() + 24f;
            float inputRowWidth = GetLabelWidth() + GetInputWidth() + 24f;
            float buttonWidth = Mathf.Max(
                ButtonWidth(Loc.EditorUseSuggestedOrbit, BASE_WINDOW_WIDTH),
                ButtonWidth(Loc.AllowRcsEnginesToggle, BASE_WINDOW_WIDTH));
            buttonWidth = Mathf.Max(buttonWidth, ButtonWidth(Loc.Close, BASE_WINDOW_WIDTH));
            float noteWidth = EstimateNoteTextWidth(Loc.EstimateIntervalNote, Loc.EstimateEcNote);
            return Mathf.Max(BASE_WINDOW_WIDTH, Mathf.Max(buttonWidth, Mathf.Max(bodyRowWidth, inputRowWidth)), noteWidth);
        }

        private static float EstimateNoteTextWidth(params string[] notes)
        {
            const float boxHorizontalPadding = 16f;
            const float windowHorizontalPadding = 24f;
            float maxText = 0f;
            foreach (string note in notes)
            {
                if (string.IsNullOrEmpty(note))
                    continue;
                float width = richStyle != null
                    ? richStyle.CalcSize(new GUIContent(note)).x
                    : note.Length * BASE_FONT_SIZE * 0.75f;
                maxText = Mathf.Max(maxText, width);
            }
            return maxText + boxHorizontalPadding + windowHorizontalPadding + 8f;
        }

        private static float ButtonWidth(string label, float minWidth)
        {
            float width = buttonStyle != null
                ? buttonStyle.CalcSize(new GUIContent(label ?? string.Empty)).x + 28f
                : (label ?? string.Empty).Length * BASE_FONT_SIZE * 0.75f + 28f;
            return Mathf.Ceil(Mathf.Max(minWidth, width));
        }

        private static float ButtonHeight => BASE_FONT_SIZE + 16f;

        private static float GetLabelWidth() => 175f;

        private static float GetInputWidth() => 150f;

        private static float GetSmallButtonWidth() => 55f;

        private static float GetBodyNameWidth() => 150f;
    }
}
