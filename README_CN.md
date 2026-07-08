# Orbital Keeper - Station Keeping
# 轨道守护者

<div align="center">

<img src="https://imgur.com/ZTXKrah.jpg" alt="Banner"/>

[![License](https://img.shields.io/github/license/Aebestach/OrbitalKeeper)](LICENSE)
[![Release](https://img.shields.io/github/v/release/Aebestach/OrbitalKeeper)](https://github.com/Aebestach/OrbitalKeeper/releases)

[English](README.md) | [中文](README_CN.md)

</div>

---

## 简介

**轨道守护者** 是一款用于 **Kerbal Space Program (KSP)** 的轨道维持模组，通过自动执行轨道修正，抵消由轨道衰减带来的影响。

自动轨道维持对**已加载与未加载**载具均生效；正在飞行的载具也可通过 UI 手动触发即时修正。

<div align="center">
    <img src="https://imgur.com/2gvRCTA.jpg" alt="UI Screenshot"/>
</div>

## 功能特性

*   **自动轨道维持（已加载 / 未加载）**
    *   按可配置的游戏时间间隔检查轨道，并对已跟踪载具自动执行修正。
    *   调度器基于**游戏时间**驱动，高倍速下持续按间隔检查。
    *   当近地点衰减接近危险高度时，会触发**紧急检查**优先处理。
*   **已加载载具安全修正**
    *   对已加载载具采用沿当前速度方向的速度脉冲修正，避免直接改写轨道根数导致的不稳定。
    *   可通过 UI 的 **Manual Correct** 立即手动修正。
*   **单载具配置**
    *   设置目标远地点/近地点/倾角。
    *   调整容差、检查间隔、引擎选择模式。
*   **资源感知修正**
    *   按所需 Δv 消耗推进剂与电量。
    *   引擎不可用或资源不足时给出提示。
    *   未加载载具的资源统计不区分连通性，不受“允许相互供应”影响。
*   **轨道维持寿命估算**
    *   在飞行/追踪站中显示预计单次 Δv、推进剂、剩余修正次数、预计维持时间与名义修正周期。
    *   通过 SWAOD 公共 API（`TryEstimateStationKeepingCadence` / `TryEstimateCurrentDecayRates`）获取衰减率；未安装 SWAOD 时该估算显示为不可用。
    *   紧急检查优先使用 SWAOD 的近地点衰减速率（`PeriapsisDaDt`）；风暴增强估算仅在 Kerbalism 报告太阳风暴时参与。
    *   预计单次 Δv 按目标轨道容差带宽固定；修正周期与维持时间按目标轨道衰减估算（不随容差内漂移变化）。
    *   剩余修正次数仅按推进剂计算（寿命估算不把 EC 当作上限）。
*   **VAB/SPH 规划估算**
    *   装配场景窗口（默认 **Alt + O**，初始隐藏）可在发射前按目标轨道估算整船质量、最佳引擎比冲、燃料预算与维持时间。
*   **RCS 推进器支持**
    *   飞行与 VAB/SPH 界面均可勾选 **启用 RCS 推进器**。
    *   RCS 修正不消耗 mod 的「每 delta-v 电量」；仅扣除推进剂。
    *   `仅已点火` 模式下，已加载载具需开启游戏自带 RCS 动作组。
*   **载具总览**
    *   查看在轨/次轨载具的状态、目标轨道与累计 Δv 消耗。
*   **安全限制**
    *   限制单次修正 Δv 上限，并在目标近地点过低时显示警告。

## 兼容性

*   ❌ **Principia** : 不支持.
*   ✅ **Space Weather & Atmospheric Orbital Decay** : 推荐搭配 [SWAOD](https://forum.kerbalspaceprogram.com/topic/229637-112x-space-weather-atmospheric-orbital-decay-swaod/) 以获得衰减与寿命估算。

## 依赖

*   **Click Through Blocker**

## 安装说明

1.  将 `GameData/OrbitalKeeper` 文件夹复制到 KSP 安装目录的 `GameData` 中。

## 使用指南

### 打开 UI

*   在**飞行场景**或**追踪站**中使用界面快捷键（默认 **Alt + O**）。
*   启用工具栏按钮后，可点击 AppLauncher 的 Orbital Keeper 图标。

### 配置载具

*   选择载具后设置目标轨道参数：
    *   远地点（Ap）、近地点（Pe）、倾角。
*   设置轨道维持选项：
    *   自动维持开关、容差、检查间隔、引擎模式、RCS 开关、UI 字体大小。
*   点击 **应用设置** 保存设置。

### VAB/SPH 规划

*   在 VAB 或 SPH 中按 **Alt + O** 打开规划窗口。
*   选择天体并设置目标远/近地点、容差与检测间隔，查看当前装配的估算结果。
*   若载具依赖 RCS 维持轨道，请勾选 **启用 RCS 推进器**。

### 界面设置

*   调整 UI 字体大小、快捷键按键与修饰键，以及工具栏按钮开关。
*   每用户界面设置保存在 `GameData/OrbitalKeeper/PluginData/config.xml`。

### 操作

*   **Manual Correct** 立即对当前载具执行修正。
*   **Vessel Overview** 显示已跟踪载具与状态。
*   **Remove Keeping** 清除该载具的轨道维持数据。

## 配置

按存档的选项位于 **困难设置 → 轨道守护者**（创建或编辑存档时）。鼠标悬停各项可查看说明。

### 容差说明

*   Ap/Pe 采用相对比例判断，超出 `1 ± (容差百分比/100)` 才会判定需要维持。
*   若目标 Ap/Pe 非常小（< 1m），改为绝对值判断：`|current - target| > 1000m * (容差百分比/100)`。
*   Inc/Ecc 使用绝对值判断，且带最小阈值（Ecc 由目标 Ap/Pe 推导）：
    *   Inc 最小阈值为 `0.5°`。
    *   Ecc 最小阈值为 `0.001`。
*   只有当计算出的总修正 Δv 大于 `0.01 m/s` 时才会执行修正。

### 引擎模式说明

*   `IgnitedOnly`：只选择当前已点火的引擎（`EngineIgnited = True`）。RCS 在已加载载具上还需开启游戏自带 RCS 动作组。
*   `ActiveNotShutdown`：选择已激活且未手动关闭的引擎；未点火但已分级且未关闭的引擎也可被认为可用。RCS 只需模块处于启用状态。

### 性能提示

*   自动维持调度按游戏时间推进，追踪站中高倍速（如 10000x）下也能持续检查；紧急检查会在近地点快速下降时提前介入。
*   启用 `enableToolbarButton` 后，在使用 JanitorsCloset 的情况下可能出现 10 秒以内、每秒 1 次的卡顿。（具体卡顿情况以设备为主，若需使用 JanitorsCloset，可以选择关闭）

