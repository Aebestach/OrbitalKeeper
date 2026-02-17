# Orbital Keeper

[English](README.md) | [中文](README_CN.md)

---

## 📖 Introduction

**Orbital Keeper** is a **Kerbal Space Program (KSP)** mod that keeps orbits stable by automatically performing orbit corrections to counter decay-driven orbital lowering.

Automatic station-keeping runs for **unloaded** vessels in the background. Loaded vessels can be corrected manually from the UI.

## ✨ Features

*   **🛰️ Background station-keeping**
    *   Checks orbit drift at a configurable interval and applies corrections for unloaded vessels.
*   **🧭 Manual correction for loaded vessels**
    *   Trigger a correction from the UI when you want direct control in-flight.
*   **⚙️ Per-vessel configuration**
    *   Set target Ap/Pe/Inclination/Eccentricity.
    *   Adjust tolerance, check interval, and engine selection mode.
*   **🔋 Resource-aware corrections**
    *   Consumes propellant and Electric Charge based on required delta-v.
    *   Warns when no eligible engine or insufficient resources are available.
*   **📋 Vessel overview**
    *   Track multiple vessels and see status, target orbit, and total delta-v spent.
*   **🛡️ Safety limits**
    *   Caps maximum correction delta-v and warns if target periapsis is too low.

## 🧩 Compatibility

*   ❌ **Principia** : Not supported.
*   ✅ **Space Weather & Atmospheric Orbital Decay** : Recommended [SWAOD](https://forum.kerbalspaceprogram.com/topic/229637-112x-space-weather-atmospheric-orbital-decay-swaod/)

## 📥 Installation

1.  Copy the `GameData/OrbitalKeeper` folder into your KSP installation’s `GameData` directory.

## 🎮 Usage Guide

### Open the UI

*   Click the Orbital Keeper icon in the stock AppLauncher while in **Flight** or **Tracking Station**.

### Configure a vessel

*   Select a vessel, then set target orbit parameters:
    *   Apoapsis (Ap), Periapsis (Pe), Inclination, Eccentricity.
*   Set station-keeping options:
    *   Auto-keep toggle, tolerance, check interval, engine mode, UI font size.
*   Click **Apply Settings** to save.

### Actions

*   **Manual Correct** applies a correction immediately for the selected vessel.
*   **Refresh Status** recalculates drift and resource availability.
*   **Vessel Overview** lists tracked vessels and their statuses.
*   **Remove Keeping** clears station-keeping data for the vessel.

## ⚙️ Configuration

Global defaults are stored in:
`GameData/OrbitalKeeper/OrbitalKeeper.cfg`

| Setting | Description | Default |
| :--- | :--- | :--- |
| `defaultTolerance` | Orbit tolerance percentage for vessels; Ap/Pe use ratios, Inc/Ecc use absolute values (with minimum thresholds), no correction within tolerance | `5.0` |
| `defaultCheckInterval` | Check interval in game seconds | `3600` |
| `defaultEngineMode` | Engine selection mode: `IgnitedOnly` uses ignited engines; `ActiveNotShutdown` uses activated engines not manually shut down | `IgnitedOnly` |
| `ecPerDeltaV` | Electric Charge per 1 m/s delta-v | `5.0` |
| `minSafeAltitudeMargin` | Minimum safe altitude above atmosphere (m) | `10000.0` |
| `maxCorrectionDeltaV` | Max delta-v per correction (m/s) | `500.0` |
| `showCorrectionMessages` | Show correction messages | `True` |
| `showResourceWarnings` | Show resource warnings | `True` |
| `messageDuration` | Message duration (s) | `5.0` |

### Tolerance Notes

*   Ap/Pe use relative ratios, and a correction is needed only when outside `1 ± (tolerance% / 100)`.
*   When target Ap/Pe is very small (< 1 m), use absolute checks instead: `|current - target| > 1000m * (tolerance% / 100)`.
*   Inc/Ecc use absolute values with minimum thresholds:
    *   Inc minimum threshold is `0.5°`.
    *   Ecc minimum threshold is `0.001`.
*   Corrections are executed only when the total computed delta-v is greater than `0.01 m/s`.

### Engine Mode Notes

*   `IgnitedOnly`: selects only engines currently ignited (`EngineIgnited = True`).
*   `ActiveNotShutdown`: selects engines activated and not manually shut down; unignited but staged and not shut down engines are also eligible.
