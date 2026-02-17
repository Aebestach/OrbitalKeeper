# Orbital Keeper

[English](README.md) | [中文](README_CN.md)

---

## 📖 简介

**Orbital Keeper** 是一款用于 **Kerbal Space Program (KSP)** 的轨道维持模组，通过自动执行轨道修正，抵消轨道衰减带来的轨道降低。

自动轨道维持仅对**未加载**的载具生效；已加载载具可通过 UI 手动修正。

## ✨ 功能特性

*   **🛰️ 后台轨道维持**
    *   按可配置的时间间隔检查轨道，并对未加载载具执行修正。
*   **🧭 已加载载具手动修正**
    *   在飞行中通过 UI 触发修正，保持可控性。
*   **⚙️ 单载具配置**
    *   设置目标远地点/近地点/倾角/偏心率。
    *   调整容差、检查间隔、引擎选择模式。
*   **🔋 资源感知修正**
    *   按所需 Δv 消耗推进剂与电量。
    *   引擎不可用或资源不足时给出提示。
    *   未加载载具的资源统计不区分连通性，阻隔不会生效。
*   **📋 载具总览**
    *   查看已跟踪载具的状态、目标轨道与累计 Δv 消耗。
*   **🛡️ 安全限制**
    *   限制单次修正 Δv 上限，并在目标近地点过低时显示警告。

## 🧩 兼容性

*   ❌ **Principia** : 不支持.
*   ✅ **Space Weather & Atmospheric Orbital Decay** : 推荐[SWAOD](https://forum.kerbalspaceprogram.com/topic/229637-112x-space-weather-atmospheric-orbital-decay-swaod/)

## 📥 安装说明

1.  将 `GameData/OrbitalKeeper` 文件夹复制到 KSP 安装目录的 `GameData` 中。

## 🎮 使用指南

### 打开 UI

*   在**飞行场景**或**追踪站**中点击 Orbital Keeper 图标。

### 配置载具

*   选择载具后设置目标轨道参数：
    *   远地点（Ap）、近地点（Pe）、倾角、偏心率。
*   设置轨道维持选项：
    *   自动维持开关、容差、检查间隔、引擎模式、UI 字体大小。
*   点击 **应用设置** 保存设置。

### 操作

*   **Manual Correct** 立即对当前载具执行修正。
*   **Refresh Status** 刷新计算轨道状态与资源可用性。
*   **Vessel Overview** 显示已跟踪载具与状态。
*   **Remove Keeping** 清除该载具的轨道维持数据。

## ⚙️ 配置

全局默认值位于：
`GameData/OrbitalKeeper/OrbitalKeeper.cfg`

| 配置项 | 描述 | 默认值 |
| :--- | :--- | :--- |
| `defaultTolerance` | 载具的轨道容差百分比；Ap/Pe 按比例，Inc/Ecc 按绝对值（含最小阈值），容差内不修正 | `5.0` |
| `defaultCheckInterval` | 检查间隔（游戏秒） | `3600` |
| `defaultEngineMode` | 引擎选择模式：`IgnitedOnly` 仅使用已点火引擎；`ActiveNotShutdown` 使用已激活且未手动关闭的引擎 | `IgnitedOnly` |
| `ecPerDeltaV` | 每 1 m/s Δv 消耗的电量 | `5.0` |
| `minSafeAltitudeMargin` | 大气层以上的最小安全高度（m） | `10000.0` |
| `maxCorrectionDeltaV` | 单次修正的最大 Δv（m/s） | `500.0` |
| `showCorrectionMessages` | 是否显示修正提示 | `True` |
| `showResourceWarnings` | 是否显示资源不足警告 | `True` |
| `messageDuration` | 提示信息持续时间（s） | `5.0` |

### 容差说明

*   Ap/Pe 采用相对比例判断，超出 `1 ± (容差百分比/100)` 才会判定需要维持。
*   若目标 Ap/Pe 非常小（< 1m），改为绝对值判断：`|current - target| > 1000m * (容差百分比/100)`。
*   Inc/Ecc 使用绝对值判断，且带最小阈值：
    *   Inc 最小阈值为 `0.5°`。
    *   Ecc 最小阈值为 `0.001`。
*   只有当计算出的总修正 Δv 大于 `0.01 m/s` 时才会执行修正。

### 引擎模式说明

*   `IgnitedOnly`：只选择当前已点火的引擎（`EngineIgnited = True`）。
*   `ActiveNotShutdown`：选择已激活且未手动关闭的引擎；未点火但已分级且未关闭的引擎也可被认为可用。
