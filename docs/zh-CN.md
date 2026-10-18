# zh-CN (简体中文) Localization Reference · 中文汉化参考

> **Documentation-only contribution — no source changes.**
> 仅文档贡献，不改动上游任何源码。

---

## English

### What this is
A community-maintained **Simplified Chinese (zh-CN)** localization of the ClawTweaks Xbox Game Bar widget.
It is kept as a fork, not as a patch to the upstream tree:

- **Fork:** https://github.com/Yg-yeung/ClawTweaks · branch `zh-CN`
- **This file** is a notice + translation reference for the maintainer.

### Coverage
| Corpus | Translated |
|---|---|
| XAML UI strings | **915 / 1058 (86.5%)** |
| C# runtime UI strings | **145 / 171 (84.8%)** |

The remaining strings are intentionally left untranslated: units (`1200 MHz`), button combos (`Menu + ←`), glyph entities (`&#xE70D;`), font names (`Bahnschrift…`) and brand/device names.

### Approach
A source-level **dictionary + regeneration pipeline** (in the fork's `tools/`). It never edits the upstream source tree — the dictionary is applied to a fresh upstream snapshot on every run, so it cannot cause merge conflicts. Terminology is fixed in `translations/GLOSSARY.md`.

### Tracking upstream
On every upstream change the strings are re-extracted, the dictionary is updated incrementally, and the translated tree is regenerated — so the localization stays in sync with new releases.

### Request to the maintainer
1. Would you be open to official multi-language support (zh-CN, possibly zh-TW)?
2. If so, I'm glad to contribute the translations in whatever format you prefer (`.resw` / `.po` / JSON).

No pressure — this can simply remain a standalone mirror. Thanks for ClawTweaks!

---

## 中文说明

### 这是什么
这是社区维护的 **ClawTweaks（Xbox Game Bar 组件）简体中文汉化**，以 fork 形式维护，而不是直接修改上游源码树：

- **Fork：** https://github.com/Yg-yeung/ClawTweaks · 分支 `zh-CN`
- **本文件** 是给作者的告知 + 翻译参考。

### 覆盖范围
| 语料 | 已翻译 |
|---|---|
| XAML 界面文案 | **915 / 1058（86.5%）** |
| C# 运行时文案 | **145 / 171（84.8%）** |

其余字符串有意保留不译：单位（`1200 MHz`）、按键组合（`Menu + ←`）、字形实体（`&#xE70D;`）、字体名（`Bahnschrift…`）以及品牌/设备名。

### 实现方式
源码级 **「词典 + 重生成」管线**（位于 fork 的 `tools/`）。它从不改动上游源码树——每次都用词典对一份全新的上游快照重新生成，因此不会产生合并冲突。术语固定在 `translations/GLOSSARY.md`。

### 跟随上游更新
上游每次变动后，重新抽取字符串、增量更新词典、重新生成译文树，使汉化始终与新版本保持同步。

### 想请作者考虑
1. 是否愿意官方支持多语言（zh-CN，或再加 zh-TW）？
2. 若愿意，我很乐意按你偏好的格式（`.resw` / `.po` / JSON）贡献译文。

不勉强——保持独立镜像也完全可以。感谢作者带来 ClawTweaks！

---

## Terminology glossary · 术语对照

| English | 中文 |
|---|---|
| controller | 手柄 |
| emulation / backend | 模拟 / 后端 |
| virtual | 虚拟 |
| firmware | 固件 |
| helper | 助手程序（Helper） |
| tile | 磁贴 |
| hotkey | 热键 |
| widget | 组件 |
| overlay | 覆盖层 |
| on-screen overlay / OSD | 覆盖显示（OSD） |
| per-game | 每游戏 |
| profile | 配置 |
| preset | 预设 |
| boost | 加速 |
| frame | 帧 |
| frame generation | 帧生成 |
| FPS limiter | FPS 限制器 |
| gyro | 陀螺仪 |
| yaw / roll / pitch | 偏航 / 翻滚 / 俯仰 |
| stick | 摇杆 |
| trigger | 扳机 |
| bumper | 肩键 |
| D-Pad | 方向键 |
| deadzone | 死区 |
| anti-deadzone | 反死区 |
| rumble / vibration | 振动 |
| haptic | 触觉 |
| touchpad | 触摸板 |
| fan curve | 风扇曲线 |
| RPM | 转速（RPM） |
| charge limit | 充电限制 |
| enable / disable | 启用 / 停用 |
| on / off | 开 / 关 |
| save / apply | 保存 / 应用 |
| reset / restore | 重置 / 恢复 |
| default / custom | 默认 / 自定义 |
| resolution | 分辨率 |
| refresh rate | 刷新率 |
| V-Sync | 垂直同步 |
| scaling | 缩放 |
| iGPU | 核显 |
| P-Core / E-Core | P 核 / E 核 |
| undervolt / overvolt | 降压 / 加压 |
| handheld | 掌机 |
| posture | 持机姿态 |

## Sample translations · 译文样例

| English | 中文 |
|---|---|
| TDP Mode | TDP 模式 |
| Fan Curve | 风扇曲线 |
| Charge Limit | 充电限制 |
| Deadzone | 死区 |
| Button Remapping | 按键重映射 |
| Performance Mode | 性能模式 |
| Virtual Controller | 虚拟手柄 |
| Factory Reset | 恢复出厂设置 |
| Disconnected | 未连接 |
| Coming soon | 即将推出 |
