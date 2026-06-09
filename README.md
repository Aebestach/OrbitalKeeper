# Orbital Keeper - Station Keeping

<div align="center">

<img src="https://imgur.com/ZTXKrah.jpg" alt="Banner"/>

[![License](https://img.shields.io/github/license/Aebestach/OrbitalKeeper)](LICENSE)
[![Release](https://img.shields.io/github/v/release/Aebestach/OrbitalKeeper)](https://github.com/Aebestach/OrbitalKeeper/releases)

[English](README.md) | [中文](README_CN.md)

</div>

---

## Introduction

**Orbital Keeper** is a **Kerbal Space Program (KSP)** mod that keeps orbits stable by automatically performing orbit corrections to counter decay-driven orbital lowering.

Automatic station-keeping runs for **unloaded** vessels in the background. Loaded vessels can be corrected manually from the UI.

<div align="center">
    <img src="https://imgur.com/2gvRCTA.jpg" alt="UI Screenshot"/>
</div>

## Features

*   **Background station-keeping**
    *   Checks orbit drift at a configurable interval and applies corrections for unloaded vessels.
*   **Manual correction for loaded vessels**
    *   Trigger a correction with the UI’s Manual Correct action for direct control in-flight.
*   **Per-vessel configuration**
    *   Set target Ap/Pe/Inclination.
    *   Adjust tolerance, check interval, and engine selection mode.
*   **Resource-aware corrections**
    *   Consumes propellant and Electric Charge based on required delta-v.
    *   Warns when no eligible engine or insufficient resources are available.
    *   Unloaded vessels do not model resource connectivity; blockages are ignored.
*   **Station-keeping lifetime estimate**
    *   Shows estimated delta-v, propellant use, remaining correction count, maintain time, and nominal correction interval in Flight/Tracking Station.
    *   Uses SWAOD's public decay-estimation API; if SWAOD is not installed, this estimate is shown as unavailable.
    *   Per-correction delta-v is fixed from the target orbit tolerance band; correction interval and maintain time use target-orbit decay (not the current drifted orbit).
    *   Remaining correction count is based on propellant only (EC is not a lifetime limiter).
*   **VAB/SPH planning estimate**
    *   Editor window (default **Alt + O**, hidden by default) estimates craft mass, best engine Isp, fuel budget, and maintain time from target orbit settings before launch.
*   **RCS thruster support**
    *   Optional **Enable RCS Thrusters** toggle in Flight and VAB/SPH UI.
    *   RCS burns do not consume the mod's EC-per-delta-v cost; only propellant is charged.
    *   In `IgnitedOnly` mode, the stock RCS action group must be on for loaded vessels.
*   **Vessel overview**
    *   View on-orbit/sub-orbit vehicle status, target orbit and accumulated Δv consumption.
*   **Safety limits**
    *   Caps maximum correction delta-v and warns if target periapsis is too low.

## Compatibility

*   ❌ **Principia** : Not supported.
*   ✅ **Space Weather & Atmospheric Orbital Decay** : Recommended [SWAOD](https://forum.kerbalspaceprogram.com/topic/229637-112x-space-weather-atmospheric-orbital-decay-swaod/)

## Dependencies

*   **Click Through Blocker**

## Installation

1.  Copy the `GameData/OrbitalKeeper` folder into your KSP installation’s `GameData` directory.

## Usage Guide

### Open the UI

*   Use the GUI hotkey (default **Alt + O**) in **Flight** or **Tracking Station**.
*   If the toolbar button is enabled, click the Orbital Keeper icon in the stock AppLauncher.

### Configure a vessel

*   Select a vessel, then set target orbit parameters:
    *   Apoapsis (Ap), Periapsis (Pe), Inclination.
*   Set station-keeping options:
    *   Auto-keep toggle, tolerance, check interval, engine mode, RCS toggle, UI font size.
*   Click **Apply Settings** to save.

### VAB/SPH planning

*   Press **Alt + O** in the VAB or SPH to open the planning window.
*   Pick a body, set target Ap/Pe, tolerance, and check interval, then review craft-side estimates for the current build.
*   Toggle **Enable RCS Thrusters** if the craft relies on RCS for station-keeping.

### GUI settings

*   Adjust UI font size, hotkey key and modifiers, and toolbar button toggle.
*   Per-user GUI settings are saved to `GameData/OrbitalKeeper/PluginData/config.xml`.

### Actions

*   **Manual Correct** applies a correction immediately for the selected vessel.
*   **Vessel Overview** lists tracked vessels and their statuses.
*   **Remove Keeping** clears station-keeping data for the vessel.

## Configuration

### Difficulty Settings (per save)

When creating or editing a game, configure these under **Difficulty Settings → Orbital Keeper**. Choosing a global difficulty preset (Easy / Normal / Moderate / Hard) loads the values below; individual fields remain editable.

| Setting | Description | Range |
| :--- | :--- | :--- |
| EC per delta-v | Electric Charge per 1 m/s delta-v for main engines (RCS exempt) | 0-20 |
| Max correction delta-v | Maximum delta-v per correction (m/s) | 1-200 |

#### Difficulty presets

| | Easy | Normal | Moderate | Hard |
|---|:---:|:---:|:---:|:---:|
| EC per delta-v | 0 | 5 | 10 | 20 |
| Max correction delta-v (m/s) | 200 | 100 | 50 | 25 |

### Global config

Other defaults are stored in:
`GameData/OrbitalKeeper/OrbitalKeeper.cfg`

| Setting | Description | Default |
| :--- | :--- | :--- |
| `defaultTolerance` | Orbit tolerance percentage for vessels; Ap/Pe use ratios, Inc/Ecc use absolute values (with minimum thresholds), no correction within tolerance | `5.0` |
| `defaultCheckInterval` | Check interval in game seconds | `3600` |
| `defaultEngineMode` | Engine selection mode: `IgnitedOnly` uses ignited engines; `ActiveNotShutdown` uses activated engines not manually shut down | `IgnitedOnly` |
| `minSafeAltitudeMargin` | Minimum safe altitude above atmosphere (m) | `10000.0` |
| `showCorrectionMessages` | Show correction messages | `True` |
| `showResourceWarnings` | Show resource warnings | `True` |
| `messageDuration` | Message duration (s) | `5.0` |
| `enableToolbarButton` | Enable the stock AppLauncher toolbar button | `True` |

### Tolerance Notes

*   Ap/Pe use relative ratios, and a correction is needed only when outside `1 ± (tolerance% / 100)`.
*   When target Ap/Pe is very small (< 1 m), use absolute checks instead: `|current - target| > 1000m * (tolerance% / 100)`.
*   Inc/Ecc use absolute values with minimum thresholds (Ecc is derived from target Ap/Pe):
    *   Inc minimum threshold is `0.5°`.
    *   Ecc minimum threshold is `0.001`.
*   Corrections are executed only when the total computed delta-v is greater than `0.01 m/s`.

### Engine Mode Notes

*   `IgnitedOnly`: selects only engines currently ignited (`EngineIgnited = True`). For RCS, the stock RCS action group must be enabled on loaded vessels.
*   `ActiveNotShutdown`: selects engines activated and not manually shut down; unignited but staged and not shut down engines are also eligible. RCS modules only need to be enabled.

### Performance Note

*   When `enableToolbarButton` is enabled, stutters may occur once per second for up to 10 seconds when using JanitorsCloset. Behavior varies by device; if you need JanitorsCloset, you can disable this option.
