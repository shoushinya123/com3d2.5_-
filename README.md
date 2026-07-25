# COM3D2.5 修改器

> COM3D2.5 运行时修改器 · BepInEx 插件 · 单文件 26KB · 跨版本可用

进游戏后按 **F9** 弹出面板，实时修改女仆能力值、契约、关系、性经验、好感度、销售额、生命/精神/理性等。不修改游戏文件（除拷贝一个 DLL），改前请自行存档。

---

## ✨ 功能

| 分类       | 能力                                                                                          |
| -------- | ------------------------------------------------------------------------------------------- |
| 快捷按钮     | 全能力拉满 · 回满 生命/精神/理性 · 好感拉满 · 销售额 +100万                                                      |
| 能力值(19项) | 爱情/奉*/*欲/魅力/气品/*客/护理/料理/歌唱/舞蹈/卖点/M值/变态/教育率/好感度/兴奋度/官能度/夜伽回数/销售额 —— 每项可 `设置` / `加100` / `清零` |
| 契约       | 专属 / 自由 / 挖角 / 育成中 / 新人 / 租赁 / 出租中                                                          |
| 关系       | 接触 / 信赖 / 恋人 + 附加关系(无/恋人/朋友/奴隶/妻子)                                                     |
| *经验      | *女 / 非*女① / 非*女② / 经验②                                                                      |
| 女仆切换     | `<` `>` 切换，显示 姓 名 [昵称] (称呼)                                                                 |
| 中文化      | 面板全中文；运行时从系统加载微软雅黑，不依赖任何翻译插件                                                                |

---
<img width="1601" height="870" alt="ScreenShot_2026-07-26_022834_779" src="https://github.com/user-attachments/assets/bc6a1e99-d0f5-4778-8298-14a907290afc" />

## 📋 环境要求

- **COM3D2 x64（Mono）**，版本不低于本插件开发版本（Unity 2022.3.62f2，COM3D2.5 系列）。更高版本一般直接可用。
- **BepInEx x64**（doorstop 加载）：游戏目录下有 `winhttp.dll` + `doorstop_config.ini`，且 `BepInEx\core\BepInEx.dll` 存在。
  - 未安装：到 <https://github.com/BepInEx/BepInEx> 装 BepInEx x64 (Unity Mono)。
- Harmony 由 BepInEx 自带（`BepInEx\core\0Harmony.dll`），无需单独安装。

---

## 📦 安装

### 方式一：一键安装（推荐）

1. 解压安装包。
2. 双击 `安装.bat`。
   - 脚本自动找游戏目录（脚本所在目录/上级 → 注册表 → 常见路径 → 手动输入）。
   - 自动检查 BepInEx、检测游戏是否运行、备份旧版、拷贝 DLL。
3. 安装前**先关闭游戏**（运行中会锁住 DLL）。

### 方式二：手动

把 `COM3D2InGameTrainer.dll` 拷到 `<游戏根目录>\BepInEx\plugins\` 即可。

### 卸载

双击 `卸载.bat`，或直接删除 `BepInEx\plugins\COM3D2InGameTrainer.dll`。

---

## 🎮 使用

1. 启动游戏，读档进入**俱乐部**（出现女仆的地方）。
2. 按 **F9** 开关面板。
3. `<` `>` 切换女仆，点按钮或输入数值改能力（即时生效）。

调试日志：`BepInEx\trainer_debug.log`（排查问题时看这个）。

---

## 🔧 版本兼容与移植

**为什么能跨版本：**

- 反射访问稳定字段（`contract`/`seikeiken`/`relation`/`baseLovely`…`sales`），命名跨版本稳定。
- Harmony 钩 `GameMain.Update`（游戏主循环），各版本都在。
- 不依赖固定偏移/二进制布局；中文字体走系统 API。

**移植到其他版本：**

1. 先直接试（拷 DLL → F9）。面板出现且功能正常即兼容。
2. 不兼容时看 `trainer_debug.log`：
   - 无日志 → 插件没加载，查 BepInEx 是否就绪、DLL 是否在 plugins。
   - 有 Awake 但无 `GameMain.Update patch FIRST CALL` → 没进主循环，进俱乐部再看。
   - 某字段改了没反应 → 该版本字段改名，用探针 dump 新字段名后更新代码。
3. 旧引擎（Unity 2018）：csproj 改 `netstandard2.0` 重编。

---

## 🛠️ 从源码构建

依赖 .NET SDK（能跑 `dotnet build`）。

```bash
dotnet build COM3D2InGameTrainer.csproj /p:GameDir=<游戏根目录> /p:Configuration=Release
```

- `GameDir` 指向目标游戏根目录，csproj 从 `COM3D2x64_Data\Managed\` 引用该版本的 `Assembly-CSharp.dll` 和 Unity 模块。
- 输出直达 `<GameDir>\BepInEx\plugins\COM3D2InGameTrainer.dll`。

**编译要点（已踩过的坑）：**

- `netstandard2.1`（Unity 2021+ 的 CoreModule 要求）；旧引擎改回 `netstandard2.0`。
- `Font` 类在 `UnityEngine.TextRenderingModule.dll`，csproj 需显式引用。
- `Input` 类在 `UnityEngine.InputLegacyModule.dll`，需显式引用。
- `<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>`，否则输出到子目录 BepInEx 扫不到。

---

## 🏗️ 工作原理

COM3D2 里 BepInEx 的 `BaseUnityPlugin` 的 `Update`/`OnGUI` **不会被 Unity 调用**（游戏对插件 GameObject 有特殊调度）。所以本插件用 **Harmony 给 `GameMain.Update` 打 Postfix**，借游戏自己的主循环把 `TrainerGUI`（MonoBehaviour）挂到 `GameMain` 的 GameObject 上——这样 `OnGUI` 才会每帧跑、面板才能刷新。

数据访问全部走**反射**（`MaidStatus.Status` 的属性/字段、`contract`/`seikeiken` 枚举等），不写死偏移，所以跨版本友好。

---

## ⚠️ 已知限制

- **资金（俱乐部金币）改不了**：玩家资金没有 C# 字段，仅 `GameMain.TJSFuncGetPlayerMoney/SetPlayerMoney` 两个 TJS 绑定方法可读写，资金存在 TJS 虚拟机里。需走 `ScriptManager.EvalScript` 执行 TJS 代码（未实现）。
- **改性经验/处女不重触发初次剧情**：剧情由事件标志驱动，改 `seikeiken` 只改状态显示。
- **夜间技能/称号满级**：未实现（`yotogiSkill.skillDatas_` 结构已探明；称号系统未在 Status/GameMain 找到字段）。

---

## 📁 项目结构

```
InGameTrainer/
├─ COM3D2InGameTrainer.cs        # 源码（插件 + Harmony 补丁 + GUI）
├─ COM3D2InGameTrainer.csproj    # 工程文件
├─ README.md                     # 本文件
├─ COM3D2修改器开发记录.md        # 开发过程记录
├─ dist/                         # 一键安装包内容
│  ├─ COM3D2InGameTrainer.dll
│  ├─ install.ps1 / 安装.bat
│  ├─ uninstall.ps1 / 卸载.bat
│  ├─ 使用说明.txt
│  └─ 版本兼容与移植说明.md
└─ COM3D2修改器_一键安装包_v1.0.zip  # 分发包
```

---

## ⚖️ 免责

本插件仅供学习交流。修改存档/内存有风险，使用前请备份存档，后果自负。与游戏官方无关。

