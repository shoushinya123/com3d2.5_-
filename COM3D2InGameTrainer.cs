using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace COM3D2.InGameTrainer
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class TrainerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.com3d2.ingametrainer";
        public const string PluginName = "COM3D2 In-Game Trainer";
        public const string PluginVersion = "1.0.0";

        public static ConfigEntry<KeyboardShortcut> ToggleKey;
        public static ConfigEntry<int> MaxStatValue;
        public static bool UiVisible = true;
        private static readonly string DebugFile = Path.Combine(Paths.PluginPath, "..", "trainer_debug.log");

        public static void DebugLog(string msg)
        {
            try { File.AppendAllText(DebugFile, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); } catch { }
        }

        void Awake()
        {
            ToggleKey = Config.Bind("General", "ToggleKey", new KeyboardShortcut(KeyCode.F9), "Toggle key");
            MaxStatValue = Config.Bind("General", "MaxStatValue", 9999, "Max stat value");
            Config.Bind("General", "ShowOnStart", true, "Show on start");

            try { File.Delete(DebugFile); } catch { }
            DebugLog("Plugin Awake - applying Harmony patches");

            var harmony = new Harmony("com.com3d2.ingametrainer");
            harmony.PatchAll();
            DebugLog("Harmony patches applied");

            Logger.LogInfo("[COM3D2 Trainer] Loaded. Press F9 to toggle.");
        }
    }

    [HarmonyPatch(typeof(GameMain), "Update")]
    public static class GameMainUpdatePatch
    {
        private static bool _initialized = false;
        private static TrainerGUI _gui;

        [HarmonyPostfix]
        public static void Postfix(GameMain __instance)
        {
            if (!_initialized)
            {
                _initialized = true;
                TrainerPlugin.DebugLog("GameMain.Update patch FIRST CALL - adding TrainerGUI component");

                // Attach TrainerGUI to GameMain's own GameObject
                _gui = __instance.gameObject.AddComponent<TrainerGUI>();
                TrainerPlugin.DebugLog("TrainerGUI component added to GameMain GameObject");
            }
        }
    }

    public class TrainerGUI : MonoBehaviour
    {
        private Vector2 _scroll;
        private int _selectedMaid = 0;
        private MaidStatus.Status _currentStatus;
        private readonly Dictionary<string, string> _buf = new Dictionary<string, string>();
        private int _lastSel = -1;
        private int _lastCount = -1;
        private int _frameCount = 0;
        private bool _guiLogged = false;
        private Font _font;

        private static readonly (string prop, string label)[] Stats = new[]
        {
            ("baseLovely",      "爱情"),
            ("baseHousi",       "奉仕"),
            ("baseInyoku",      "淫欲"),
            ("baseCharm",       "魅力"),
            ("baseElegance",    "气品"),
            ("baseReception",   "接客"),
            ("baseCare",        "护理"),
            ("baseCooking",     "料理"),
            ("baseVocal",       "歌唱"),
            ("baseDance",       "舞蹈"),
            ("baseAppealPoint", "卖点"),
            ("baseMvalue",      "M值"),
            ("baseHentai",      "变态"),
            ("baseTeachRate",   "教育率"),
            ("likability",      "好感度"),
            ("currentExcite",   "兴奋度"),
            ("currentSensual",  "官能度"),
            ("playCountYotogi", "夜伽回数"),
            ("sales",           "销售额"),
        };

        private static readonly (string cur, string max)[] Vitals = new[]
        {
            ("currentHp",   "maxHp"),
            ("currentMind", "maxMind"),
            ("currentReason","maxReason"),
        };

        void Awake()
        {
            TrainerPlugin.DebugLog("TrainerGUI Awake");
            _font = LoadChineseFont();
            if (_font != null) TrainerPlugin.DebugLog("Chinese font loaded: " + _font.name);
            else TrainerPlugin.DebugLog("WARN: Chinese font not loaded - Chinese may show as boxes");
        }

        void Start()
        {
            TrainerPlugin.DebugLog("TrainerGUI Start");
        }

        private Font LoadChineseFont()
        {
            try
            {
                var m = typeof(Font).GetMethod("CreateDynamicFontFromOSFont",
                    new[] { typeof(string), typeof(int) });
                if (m != null)
                {
                    foreach (var name in new[] { "Microsoft YaHei", "微软雅黑", "SimHei", "黑体", "SimSun", "宋体" })
                    {
                        var f = m.Invoke(null, new object[] { name, 14 }) as Font;
                        if (f != null) return f;
                    }
                }
            }
            catch (Exception e) { TrainerPlugin.DebugLog("LoadChineseFont error: " + e.Message); }
            return null;
        }

        private const System.Reflection.BindingFlags AllInst =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

        private bool _probed = false;

        // 首次拿到女仆时，把夜伽技能系统与称号系统的真实成员结构 dump 到日志，
        // 以便据此实现"夜间技能全满级 / 称号全满级"（API 探针未导出这些类）。
        private void ProbeOnce()
        {
            if (_probed || _currentStatus == null) return;
            _probed = true;
            try
            {
                TrainerPlugin.DebugLog("===== PROBE: yotogi & titles =====");

                // ---- 夜伽技能 yotogiSkill ----
                var ysProp = typeof(MaidStatus.Status).GetProperty("yotogiSkill", AllInst);
                object ysk = ysProp != null ? ysProp.GetValue(_currentStatus, null) : null;
                TrainerPlugin.DebugLog("PROBE yotogiSkill = " + (ysk == null ? "null" : ysk.GetType().FullName));
                if (ysk != null)
                {
                    DumpMembers(ysk, ysk.GetType(), "yotogiSkill");
                    foreach (var f in ysk.GetType().GetFields(AllInst))
                        try { DumpCollection(f.GetValue(ysk), "yotogiSkill." + f.Name); } catch { }
                    foreach (var p in ysk.GetType().GetProperties(AllInst))
                        try { if (p.GetIndexParameters().Length == 0) DumpCollection(p.GetValue(ysk, null), "yotogiSkill." + p.Name); } catch { }
                }

                // ---- jobClass / yotogiClass（可能也含技能等级）----
                foreach (var propName in new[] { "jobClass", "yotogiClass" })
                {
                    var pr = typeof(MaidStatus.Status).GetProperty(propName, AllInst);
                    object v = pr != null ? pr.GetValue(_currentStatus, null) : null;
                    TrainerPlugin.DebugLog("PROBE " + propName + " = " + (v == null ? "null" : v.GetType().FullName));
                    if (v != null) DumpMembers(v, v.GetType(), propName);
                }

                // ---- 称号 title 扫描 ----
                TrainerPlugin.DebugLog("PROBE --- title scan ---");
                ScanTitleOn(_currentStatus, typeof(MaidStatus.Status), "Status");
                var gm = GameMain.Instance;
                if (gm != null)
                {
                    ScanTitleOn(gm, gm.GetType(), "GameMain");
                    foreach (var p in typeof(GameMain).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        if (p.PropertyType.IsPrimitive || p.PropertyType == typeof(string)) continue;
                        try
                        {
                            var v = p.GetValue(gm, null);
                            if (v != null && HasTitle(p.Name, p.PropertyType))
                            {
                                TrainerPlugin.DebugLog("PROBE GameMain." + p.Name + " = " + v.GetType().FullName);
                                DumpMembers(v, v.GetType(), "GameMain." + p.Name);
                                ScanTitleOn(v, v.GetType(), "GameMain." + p.Name);
                            }
                        }
                        catch { }
                    }
                }
                TrainerPlugin.DebugLog("===== PROBE END =====");
            }
            catch (Exception e) { TrainerPlugin.DebugLog("PROBE error: " + e.Message); }
        }

        private static bool HasTitle(string name, System.Type t)
        {
            return name.IndexOf("title", System.StringComparison.OrdinalIgnoreCase) >= 0
                || t.Name.IndexOf("title", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ScanTitleOn(object obj, System.Type t, string path)
        {
            foreach (var f in t.GetFields(AllInst))
                if (HasTitle(f.Name, f.FieldType))
                {
                    string val = "?";
                    try { val = "" + f.GetValue(obj); } catch { }
                    TrainerPlugin.DebugLog("PROBE " + path + ".F " + f.Name + " : " + f.FieldType.FullName + " = " + val);
                }
            foreach (var p in t.GetProperties(AllInst))
                try
                {
                    if (p.GetIndexParameters().Length > 0) continue;
                    if (HasTitle(p.Name, p.PropertyType))
                    {
                        string val = "?";
                        try { val = "" + p.GetValue(obj, null); } catch { }
                        TrainerPlugin.DebugLog("PROBE " + path + ".P " + p.Name + " : " + p.PropertyType.FullName + " = " + val);
                    }
                }
                catch { }
        }

        private void DumpMembers(object obj, System.Type t, string prefix)
        {
            foreach (var f in t.GetFields(AllInst))
            {
                string val = "?";
                try { val = "" + f.GetValue(obj); } catch { }
                TrainerPlugin.DebugLog("PROBE " + prefix + ".F " + f.Name + " : " + f.FieldType.Name + " = " + val);
            }
            foreach (var p in t.GetProperties(AllInst))
                try
                {
                    if (p.GetIndexParameters().Length > 0) { TrainerPlugin.DebugLog("PROBE " + prefix + ".P[] " + p.Name + " : " + p.PropertyType.Name); continue; }
                    string val = "?";
                    try { val = "" + p.GetValue(obj, null); } catch { }
                    TrainerPlugin.DebugLog("PROBE " + prefix + ".P " + p.Name + " : " + p.PropertyType.Name + " = " + val);
                }
                catch { }
        }

        private void DumpCollection(object col, string label)
        {
            if (col == null || col is string) return;
            if (!(col is System.Collections.IEnumerable)) return;
            int count = -1;
            try { var c = col.GetType().GetProperty("Count"); if (c != null) count = (int)c.GetValue(col, null); } catch { }
            TrainerPlugin.DebugLog("PROBE COLL " + label + " : " + col.GetType().Name + " count=" + count);
            int i = 0;
            foreach (var item in (System.Collections.IEnumerable)col)
            {
                if (i >= 3) break;
                TrainerPlugin.DebugLog("PROBE COLL[" + i + "] " + label + " = " + (item == null ? "null" : item.GetType().FullName));
                if (item != null) DumpMembers(item, item.GetType(), label + "[" + i + "]");
                i++;
            }
        }

        void Update()
        {
            _frameCount++;
            if (_frameCount == 1)
                TrainerPlugin.DebugLog("TrainerGUI Update FIRST FRAME!");

            if (Input.GetKeyDown(TrainerPlugin.ToggleKey.Value.MainKey))
            {
                TrainerPlugin.UiVisible = !TrainerPlugin.UiVisible;
                TrainerPlugin.DebugLog($"Key pressed! UiVisible={TrainerPlugin.UiVisible}");
            }
        }

        void OnGUI()
        {
            if (!_guiLogged)
            {
                _guiLogged = true;
                TrainerPlugin.DebugLog($"TrainerGUI OnGUI FIRST CALL! screen={Screen.width}x{Screen.height}");
            }
            if (!TrainerPlugin.UiVisible) return;

            // 应用中文字体，确保按钮/标签中文正常显示（不依赖 JAT 字体替换）
            if (_font != null)
            {
                GUI.skin.font = _font;
                GUI.skin.window.font = _font;
                GUI.skin.button.font = _font;
                GUI.skin.label.font = _font;
                GUI.skin.textField.font = _font;
                GUI.skin.box.font = _font;
                GUI.skin.toggle.font = _font;
                if (GUI.skin.customStyles != null)
                    foreach (var s in GUI.skin.customStyles) if (s != null) s.font = _font;
            }

            float scale = Mathf.Max(Screen.height / 1080f, 0.5f);
            Rect r = new Rect(20, 20, 420 * scale, 600 * scale);
            GUILayout.Window(0x4F32, r, DrawWindow, "COM3D2 修改器 (F9)");
        }

        private void DrawWindow(int id)
        {
            List<Maid> maids = GetMaids();
            int count = maids?.Count ?? 0;

            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(32))) { _selectedMaid--; }
            string title = "(no maids)";
            if (count > 0)
            {
                _selectedMaid = Mathf.Clamp(_selectedMaid, 0, count - 1);
                title = GetMaidName(maids[_selectedMaid]) + " (" + (_selectedMaid + 1) + "/" + count + ")";
            }
            GUILayout.Label(title, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", GUILayout.Width(32))) { _selectedMaid++; }
            GUILayout.EndHorizontal();

            if (count == 0)
            {
                GUILayout.Label("等待游戏加载中...");
                GUILayout.EndVertical();
                GUI.DragWindow();
                return;
            }

            _selectedMaid = Mathf.Clamp(_selectedMaid, 0, count - 1);
            _currentStatus = maids[_selectedMaid].status;

            // 首次有女仆时，探测夜伽技能/称号系统的真实结构（API 探针未导出）
            ProbeOnce();

            if (_selectedMaid != _lastSel || count != _lastCount)
            {
                SyncBuffers(_currentStatus);
                _lastSel = _selectedMaid;
                _lastCount = count;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("全能力 拉满")) MaxAll();
            if (GUILayout.Button("回满 生命/精神/理性")) RefillVitals();
            if (GUILayout.Button("好感 拉满")) SetStat("likability", TrainerPlugin.MaxStatValue.Value);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("销售额 +100万")) AddStat("sales", 1000000L);
            if (GUILayout.Button("资金 +100万"))
            {
                if (PlayerMoney.Available) PlayerMoney.Set(PlayerMoney.Get() + 1000000L);
            }
            GUILayout.EndHorizontal();

            // 契约状态
            DrawContractRow();

            // 关系
            DrawRelationRow();

            // 性经验/处女
            DrawSeikeikenRow();

            // 资金（俱乐部全局数值，与女仆的销售额是两码事）
            DrawMoneyRow();

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (var (prop, label) in Stats)
                DrawStatRow(prop, label);
            GUILayout.EndScrollView();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void DrawRelationRow()
        {
            if (_currentStatus == null) return;

            // relation (Relation 枚举: Contact=接触, Trust=信赖, Lover=恋人)
            var relProp = typeof(MaidStatus.Status).GetProperty("relation",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (relProp != null && relProp.CanWrite)
            {
                object current = relProp.GetValue(_currentStatus, null);
                Type relType = relProp.PropertyType;

                GUILayout.BeginHorizontal();
                GUILayout.Label("关系", GUILayout.Width(72));
                GUILayout.Label(current?.ToString() ?? "?", GUILayout.Width(80));

                if (relType.IsEnum)
                {
                    var names = Enum.GetNames(relType);
                    var values = Enum.GetValues(relType);
                    for (int i = 0; i < values.Length; i++)
                    {
                        string name = names[i];
                        string label;
                        switch (name)
                        {
                            case "Contact": label = "接触"; break;
                            case "Trust": label = "信赖"; break;
                            case "Lover": label = "恋人"; break;
                            default: label = name; break;
                        }
                        if (GUILayout.Button(label, GUILayout.Width(56)))
                        {
                            relProp.SetValue(_currentStatus, values.GetValue(i), null);
                            TrainerPlugin.DebugLog("Relation changed to " + name);
                        }
                    }
                }
                GUILayout.EndHorizontal();
            }

            // additionalRelation (附加关系)
            var addRelProp = typeof(MaidStatus.Status).GetProperty("additionalRelation",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (addRelProp != null && addRelProp.CanWrite)
            {
                object current = addRelProp.GetValue(_currentStatus, null);
                Type addRelType = addRelProp.PropertyType;

                GUILayout.BeginHorizontal();
                GUILayout.Label("附加关系", GUILayout.Width(72));
                GUILayout.Label(current?.ToString() ?? "?", GUILayout.Width(80));

                if (addRelType.IsEnum)
                {
                    var names = Enum.GetNames(addRelType);
                    var values = Enum.GetValues(addRelType);
                    for (int i = 0; i < values.Length; i++)
                    {
                        string name = names[i];
                        string label;
                        switch (name)
                        {
                            case "None": label = "无"; break;
                            case "Lover": label = "恋人"; break;
                            case "Friend": label = "朋友"; break;
                            case "Slave": label = "奴隶"; break;
                            case "God": label = "神"; break;
                            case "Wife": label = "妻子"; break;
                            case "Mistress": label = "情妇"; break;
                            default: label = name; break;
                        }
                        if (GUILayout.Button(label, GUILayout.Width(56)))
                        {
                            addRelProp.SetValue(_currentStatus, values.GetValue(i), null);
                            TrainerPlugin.DebugLog("AdditionalRelation changed to " + name);
                        }
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        // 性経験/处女：Seikeiken 枚举 No_No(处女)/Yes_No/No_Yes/Yes_Yes
        // 注意：游戏里"初次"事件可能由事件标志驱动，改 seikeiken 字段会改状态显示，
        // 但不一定会重新触发"初次"剧情；要还原初次事件需配合事件标志操作。
        private void DrawSeikeikenRow()
        {
            if (_currentStatus == null) return;
            // seikeiken 是 public 字段；先试字段再试属性
            var f = typeof(MaidStatus.Status).GetField("seikeiken", AllInst);
            System.Reflection.PropertyInfo p = null;
            if (f == null) p = typeof(MaidStatus.Status).GetProperty("seikeiken", AllInst);
            if (f == null && p == null) return;

            object current = f != null ? f.GetValue(_currentStatus) : p.GetValue(_currentStatus, null);
            string curStr = current?.ToString() ?? "?";
            Type t = f != null ? f.FieldType : p.PropertyType;

            GUILayout.BeginHorizontal();
            GUILayout.Label("性経験", GUILayout.Width(72));
            GUILayout.Label(curStr, GUILayout.Width(84));

            if (t.IsEnum)
            {
                var names = Enum.GetNames(t);
                var values = Enum.GetValues(t);
                for (int i = 0; i < values.Length; i++)
                {
                    string name = names[i];
                    string label;
                    switch (name)
                    {
                        case "No_No":  label = "处女"; break;
                        case "Yes_No": label = "非处女①"; break;
                        case "No_Yes": label = "非处女②"; break;
                        case "Yes_Yes": label = "经验②"; break;
                        default: label = name; break;
                    }
                    if (GUILayout.Button(label, GUILayout.Width(64)))
                    {
                        if (f != null) f.SetValue(_currentStatus, values.GetValue(i));
                        else if (p != null && p.CanWrite) p.SetValue(_currentStatus, values.GetValue(i), null);
                        TrainerPlugin.DebugLog("Seikeiken changed to " + name);
                    }
                }
            }
            GUILayout.EndHorizontal();
        }

        private void DrawStatRow(string prop, string label)
        {
            var p = typeof(MaidStatus.Status).GetProperty(prop,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (p == null) return;
            object cur = p.GetValue(_currentStatus, null);

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(72));
            GUILayout.Label(cur == null ? "-" : cur.ToString(), GUILayout.Width(64));

            if (!_buf.ContainsKey(prop)) _buf[prop] = cur?.ToString() ?? "0";
            _buf[prop] = GUILayout.TextField(_buf[prop], GUILayout.Width(72));

            if (GUILayout.Button("设置", GUILayout.Width(40))) SetStat(prop, _buf[prop]);
            if (GUILayout.Button("加100", GUILayout.Width(48))) AddStat(prop, 100L);
            if (GUILayout.Button("清零", GUILayout.Width(44))) SetStat(prop, "0");
            GUILayout.EndHorizontal();
        }

        private void DrawContractRow()
        {
            if (_currentStatus == null) return;
            var contractField = typeof(MaidStatus.Status).GetField("contract",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (contractField == null) return;

            object current = contractField.GetValue(_currentStatus);
            string curStr = current?.ToString() ?? "?";
            Type contractType = contractField.FieldType;

            GUILayout.BeginHorizontal();
            GUILayout.Label("契约", GUILayout.Width(72));
            GUILayout.Label(curStr, GUILayout.Width(80));

            // 如果是枚举，列出所有可选值
            if (contractType.IsEnum)
            {
                var names = Enum.GetNames(contractType);
                var values = Enum.GetValues(contractType);
                for (int i = 0; i < values.Length; i++)
                {
                    string name = names[i];
                    // 翻译枚举名为中文
                    string label;
                    switch (name)
                    {
                        case "Exclusive": label = "专属"; break;
                        case "Free": label = "自由"; break;
                        case "Scout": label = "挖角"; break;
                        case "Trainee": label = "育成中"; break;
                        case "New": label = "新人"; break;
                        case "Rental": label = "租赁"; break;
                        case "Leased": label = "出租中"; break;
                        default: label = name; break;
                    }

                    if (GUILayout.Button(label, GUILayout.Width(56)))
                    {
                        contractField.SetValue(_currentStatus, values.GetValue(i));
                        TrainerPlugin.DebugLog($"Contract changed to {name}");
                    }
                }
            }
            GUILayout.EndHorizontal();
        }

        private void SetStat(string prop, string text)
        {
            if (_currentStatus == null) return;
            var p = typeof(MaidStatus.Status).GetProperty(prop,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (p == null || !p.CanWrite) return;
            Type t = p.PropertyType;
            try
            {
                if (t == typeof(int)) { int v = int.Parse(text); p.SetValue(_currentStatus, v, null); _buf[prop] = v.ToString(); }
                else if (t == typeof(long)) { long v = long.Parse(text); p.SetValue(_currentStatus, v, null); _buf[prop] = v.ToString(); }
                else if (t == typeof(short)) { short v = short.Parse(text); p.SetValue(_currentStatus, v, null); _buf[prop] = v.ToString(); }
            }
            catch { }
        }

        private void SetStat(string prop, int value)
        {
            if (_currentStatus == null) return;
            var p = typeof(MaidStatus.Status).GetProperty(prop,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (p == null || !p.CanWrite) return;
            Type t = p.PropertyType;
            if (t == typeof(int)) p.SetValue(_currentStatus, value, null);
            else if (t == typeof(long)) p.SetValue(_currentStatus, (long)value, null);
            else if (t == typeof(short)) p.SetValue(_currentStatus, (short)value, null);
            _buf[prop] = value.ToString();
        }

        private void AddStat(string prop, long delta)
        {
            if (_currentStatus == null) return;
            var p = typeof(MaidStatus.Status).GetProperty(prop,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (p == null || !p.CanWrite) return;
            Type t = p.PropertyType;
            object cur = p.GetValue(_currentStatus, null);
            if (t == typeof(int)) { int v = (int)cur + (int)delta; p.SetValue(_currentStatus, v, null); _buf[prop] = v.ToString(); }
            else if (t == typeof(long)) { long v = (long)cur + delta; p.SetValue(_currentStatus, v, null); _buf[prop] = v.ToString(); }
            else if (t == typeof(short)) { short v = (short)((short)cur + delta); p.SetValue(_currentStatus, v, null); _buf[prop] = v.ToString(); }
        }

        private void MaxAll()
        {
            foreach (var (prop, _) in Stats) SetStat(prop, TrainerPlugin.MaxStatValue.Value);
        }

        private void RefillVitals()
        {
            if (_currentStatus == null) return;
            foreach (var (cur, max) in Vitals)
            {
                var maxP = typeof(MaidStatus.Status).GetProperty(max,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var curP = typeof(MaidStatus.Status).GetProperty(cur,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (maxP != null && curP != null && curP.CanWrite)
                    curP.SetValue(_currentStatus, maxP.GetValue(_currentStatus, null), null);
            }
        }

        private void SyncBuffers(MaidStatus.Status status)
        {
            if (status == null) return;
            foreach (var (prop, _) in Stats)
            {
                var p = typeof(MaidStatus.Status).GetProperty(prop,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                _buf[prop] = p?.GetValue(status, null)?.ToString() ?? "0";
            }
        }

        private List<Maid> GetMaids()
        {
            try
            {
                return GameMain.Instance.CharacterMgr.GetStockMaidList();
            }
            catch (Exception e)
            {
                TrainerPlugin.DebugLog("GetMaids error: " + e.Message);
                return null;
            }
        }

        private string GetMaidName(Maid maid)
        {
            if (maid == null) return "Maid";
            try
            {
                var st = maid.status;
                string ln = st?.lastName ?? "";
                string fn = st?.firstName ?? "";
                string nick = st?.nickName ?? "";
                string call = st?.callName ?? "";

                // 拼出 "姓 名 (昵称)" 的完整名字
                string fullName = (ln + " " + fn).Trim();
                if (!string.IsNullOrEmpty(nick))
                    fullName += "  [" + nick + "]";
                if (!string.IsNullOrEmpty(call) && call != nick)
                    fullName += "  (" + call + ")";

                if (!string.IsNullOrEmpty(fullName)) return fullName;
                // 最后兜底
                return maid.NickName ?? ("Maid#" + (_selectedMaid + 1));
            }
            catch
            {
                return "Maid#" + (_selectedMaid + 1);
            }
        }

        private void DrawMoneyRow()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("资金", GUILayout.Width(72));
            if (PlayerMoney.Available)
            {
                long cur = PlayerMoney.Get();
                GUILayout.Label(cur < 0 ? "?" : cur.ToString(), GUILayout.Width(110));

                if (!_buf.ContainsKey("_money")) _buf["_money"] = cur.ToString();
                _buf["_money"] = GUILayout.TextField(_buf["_money"], GUILayout.Width(110));

                if (GUILayout.Button("设置", GUILayout.Width(48)))
                {
                    if (long.TryParse(_buf["_money"], out long v)) PlayerMoney.Set(v);
                }
                if (GUILayout.Button("加100万", GUILayout.Width(72)))
                {
                    long now = PlayerMoney.Get();
                    if (now >= 0) PlayerMoney.Set(now + 1000000L);
                }
                if (GUILayout.Button("清零", GUILayout.Width(44)))
                {
                    PlayerMoney.Set(0);
                }
            }
            else
            {
                GUILayout.Label("未找到资金字段（见日志）", GUILayout.Width(200));
            }
            GUILayout.EndHorizontal();
        }

        // 俱乐部/玩家资金：API 探针未导出该字段，运行时用反射扫描 GameMain 及 CharacterMgr
        // 上名字含 money/coin/zeni 等的数值字段。资金与女仆的销售额(sales)完全无关。
        private static class PlayerMoney
        {
            private static object _target;
            private static System.Reflection.FieldInfo _field;
            private static System.Reflection.PropertyInfo _prop;
            private static int _frame;

            private static readonly string[] Keywords = new[]
            {
                // 注意：不要放 "kin" —— 会误命中所有 <X>k__Backing 字段里的 "backing"
                "money", "coin", "zeni", "kane", "yen", "fund", "cash", "wallet", "asset"
            };

            private static bool IsNumeric(Type t)
            {
                return t == typeof(int) || t == typeof(long) || t == typeof(uint) || t == typeof(ulong)
                    || t == typeof(short) || t == typeof(ushort) || t == typeof(byte);
            }

            private static bool Matches(string name)
            {
                string lower = name.ToLower();
                foreach (var kw in Keywords)
                    if (lower.Contains(kw)) return true;
                return false;
            }

            private static bool Resolve()
            {
                if (_field != null || _prop != null) return true;
                if ((_frame++ % 30) != 0) return false; // 未找到前每 30 帧重试一次，避免每帧全量扫描

                try
                {
                    var gm = GameMain.Instance;
                    var targets = new List<object>();
                    if (gm != null)
                    {
                        targets.Add(gm);
                        if (gm.CharacterMgr != null) targets.Add(gm.CharacterMgr);
                        // 把 GameMain 上所有对象类型的公共属性也纳入扫描（设施/经营/存档等管理器）
                        foreach (var p in typeof(GameMain).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                        {
                            if (p.PropertyType.IsPrimitive || p.PropertyType == typeof(string)) continue;
                            try { var v = p.GetValue(gm, null); if (v != null) targets.Add(v); } catch { }
                        }
                    }

                    foreach (var obj in targets)
                    {
                        var t = obj.GetType();
                        var seen = new HashSet<Type>();
                        while (t != null && t != typeof(object) && !seen.Contains(t))
                        {
                            seen.Add(t);
                            foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                            {
                                if (!Matches(f.Name)) continue;
                                if (IsNumeric(f.FieldType))
                                {
                                    _target = obj; _field = f;
                                    TrainerPlugin.DebugLog($"PlayerMoney resolved: {f.DeclaringType.Name}.{f.Name} ({f.FieldType.Name})");
                                    return true;
                                }
                            }
                            foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                            {
                                if (!Matches(p.Name)) continue;
                                if (IsNumeric(p.PropertyType) && p.CanRead)
                                {
                                    _target = obj; _prop = p;
                                    TrainerPlugin.DebugLog($"PlayerMoney resolved: {p.DeclaringType.Name}.{p.Name} ({p.PropertyType.Name})");
                                    return true;
                                }
                            }
                            t = t.BaseType;
                        }
                    }

                    // 关键词没命中 -> 一次性把 GameMain 及其管理器的所有数值字段名 dump 出来，便于定位资金字段
                    if (!_dumped)
                    {
                        _dumped = true;
                        DiagnosticDump(gm);
                    }
                    TrainerPlugin.DebugLog("PlayerMoney: no matching field/property found yet");
                }
                catch (Exception e)
                {
                    TrainerPlugin.DebugLog("PlayerMoney resolve error: " + e.Message);
                }
                return false;
            }

            private static bool _dumped = false;
            private static void DiagnosticDump(GameMain gm)
            {
                try
                {
                    var objs = new List<object>();
                    if (gm != null)
                    {
                        objs.Add(gm);
                        foreach (var p in typeof(GameMain).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                        {
                            if (p.PropertyType.IsPrimitive || p.PropertyType == typeof(string)) continue;
                            try { var v = p.GetValue(gm, null); if (v != null) objs.Add(v); } catch { }
                        }
                    }
                    int n = 0;
                    foreach (var obj in objs)
                    {
                        var t = obj.GetType();
                        var seen = new HashSet<Type>();
                        while (t != null && t != typeof(object) && !seen.Contains(t))
                        {
                            seen.Add(t);
                            foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                            {
                                if (IsNumeric(f.FieldType))
                                {
                                    string val = "?";
                                    try { val = Convert.ToInt64(f.GetValue(obj)).ToString(); } catch { }
                                    TrainerPlugin.DebugLog($"DIAG NUM FIELD: {f.DeclaringType.Name}.{f.Name} ({f.FieldType.Name}) = {val}");
                                    if (++n >= 250) return;
                                }
                            }
                            t = t.BaseType;
                        }
                    }
                    TrainerPlugin.DebugLog($"DIAG done, {n} numeric fields listed");
                }
                catch (Exception e) { TrainerPlugin.DebugLog("DIAG error: " + e.Message); }
            }

            public static bool Available => Resolve();

            public static long Get()
            {
                if (!Resolve()) return -1;
                try
                {
                    if (_field != null) return Convert.ToInt64(_field.GetValue(_target));
                    if (_prop != null) return Convert.ToInt64(_prop.GetValue(_target, null));
                }
                catch { }
                return -1;
            }

            public static void Set(long value)
            {
                if (!Resolve()) return;
                try
                {
                    if (_field != null)
                    {
                        if (_field.FieldType == typeof(long) || _field.FieldType == typeof(ulong))
                            _field.SetValue(_target, value);
                        else
                            _field.SetValue(_target, (int)Math.Min(value, int.MaxValue));
                    }
                    else if (_prop != null && _prop.CanWrite)
                    {
                        if (_prop.PropertyType == typeof(long) || _prop.PropertyType == typeof(ulong))
                            _prop.SetValue(_target, value, null);
                        else
                            _prop.SetValue(_target, (int)Math.Min(value, int.MaxValue), null);
                    }
                    TrainerPlugin.DebugLog("PlayerMoney set -> " + value);
                }
                catch (Exception e)
                {
                    TrainerPlugin.DebugLog("PlayerMoney set error: " + e.Message);
                }
            }
        }
    }
}
