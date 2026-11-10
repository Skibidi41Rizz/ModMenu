#nullable disable
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;

// Жесткая привязка типов к UnityEngine для защиты от конфликтов имен в Visual Studio
using Vector2 = UnityEngine.Vector2;
using Color = UnityEngine.Color;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;
using Button = UnityEngine.UI.Button;
using Selectable = UnityEngine.UI.Selectable;

[assembly: MelonInfo(typeof(BrutalistickModMenu.ModMenuMod), "ModMenu", "1.1.0", "misha")]
[assembly: MelonGame("Kubunautilus", "BrutalistickVR")]
[assembly: MelonPriority(100)]

namespace BrutalistickModMenu
{
    public class ModMenuMod : MelonMod
    {
        public const string Tag = "[ModMenu]";
        private static float _nextEnsure;

        // Обычные кнопки (по клику)
        private static readonly Dictionary<int, System.Action> ButtonActions = new Dictionary<int, System.Action>();

        // Кнопки с поддержкой зажатия ([-] и [+])
        private class HoldStepInfo
        {
            public GameObject ButtonObj;
            public MelonPreferences_Entry Entry;
            public int Direction; // -1 или +1
            public Text ValueLabel;
        }

        private static readonly Dictionary<int, HoldStepInfo> HoldButtons = new Dictionary<int, HoldStepInfo>();
        private static HoldStepInfo _activeHold;
        private static float _holdStartTime;
        private static float _nextHoldTick;

        internal static HarmonyLib.Harmony SharedHarmony;

        public override void OnInitializeMelon()
        {
            SharedHarmony = HarmonyInstance;

            ModToggleManager.Init();
            PatchTabs();
            PatchClicksAndHolds();

            var mods = ModConfigManager.BuildScriptModList();
            MelonLogger.Msg($"{Tag} Loaded! Active mods/configs found: {mods.Count}");
            foreach (var m in mods)
            {
                ModToggleManager.EnsureModPatched(m.Melon);
                MelonLogger.Msg($"{Tag}   -> {m.Name} ({m.Version}) | CFG Categories: {m.Categories.Count}");
            }
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            MelonCoroutines.Start(ProbeLater(2.5f));
        }

        private static IEnumerator ProbeLater(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            ModMenuPanel.Ensure();
        }

        public override void OnUpdate()
        {
            if (Time.unscaledTime >= _nextEnsure)
            {
                _nextEnsure = Time.unscaledTime + 1.5f;
                ModMenuPanel.Ensure();
            }

            UpdateHoldStep();
        }

        // --- Логика экспоненциального изменения при зажатии ---
        private static void UpdateHoldStep()
        {
            if (_activeHold == null) return;

            if (_activeHold.ButtonObj == null || !_activeHold.ButtonObj.activeInHierarchy)
            {
                StopHold();
                return;
            }

            if (Time.unscaledTime < _nextHoldTick) return;
            _nextHoldTick = Time.unscaledTime + 0.08f; // ~12.5 шагов в секунду при удержании

            float heldDuration = Time.unscaledTime - _holdStartTime;
            float step = CalculateDynamicStep(_activeHold.Entry, heldDuration);

            ModConfigManager.StepNumericEntry(_activeHold.Entry, step * _activeHold.Direction, saveImmediately: false);

            if (_activeHold.ValueLabel != null)
                _activeHold.ValueLabel.text = ModConfigManager.FormatValue(_activeHold.Entry);
        }

        private static float CalculateDynamicStep(MelonPreferences_Entry entry, float heldSeconds)
        {
            bool isInt = entry.GetReflectedType() == typeof(int);

            // Разгон шага по времени удержания: 0.1 -> 1 -> 5 -> 10 -> 50 -> 100 -> 500
            if (heldSeconds < 1.2f) return isInt ? 1f : 0.1f;
            if (heldSeconds < 2.2f) return 1f;
            if (heldSeconds < 3.2f) return 5f;
            if (heldSeconds < 4.2f) return 10f;
            if (heldSeconds < 5.5f) return 50f;
            if (heldSeconds < 7.0f) return 100f;
            return 500f;
        }

        private static void StopHold()
        {
            if (_activeHold != null)
            {
                MelonPreferences.Save();
                _activeHold = null;
            }
        }

        public static void RegisterButton(Button btn, System.Action onClick)
        {
            if (btn == null || onClick == null) return;
            ButtonActions[btn.GetInstanceID()] = onClick;
        }

        public static void RegisterHoldStepButton(Button btn, MelonPreferences_Entry entry, int direction, Text valueLabel)
        {
            if (btn == null || entry == null) return;
            HoldButtons[btn.GetInstanceID()] = new HoldStepInfo
            {
                ButtonObj = btn.gameObject,
                Entry = entry,
                Direction = direction,
                ValueLabel = valueLabel
            };
        }

        public static void ClearRegisteredButtons()
        {
            StopHold();
            HoldButtons.Clear();
        }

        private void PatchTabs()
        {
            try
            {
                var mi = typeof(MenuUIManager).GetMethod("ChangeActiveTab",
                    BindingFlags.Public | BindingFlags.Instance);
                if (mi != null)
                {
                    HarmonyInstance.Patch(mi, postfix: new HarmonyMethod(
                        typeof(ModMenuMod).GetMethod(nameof(ChangeActiveTabPostfix),
                            BindingFlags.Static | BindingFlags.NonPublic)));
                }
            }
            catch (System.Exception e)
            {
                MelonLogger.Warning($"{Tag} ChangeActiveTab patch error: {e.Message}");
            }
        }

        private static void ChangeActiveTabPostfix(int tabIndex)
        {
            ModMenuPanel.Hide();
        }

        private void PatchClicksAndHolds()
        {
            try
            {
                var clickMi = typeof(Button).GetMethod("OnPointerClick",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(PointerEventData) }, null);
                if (clickMi != null)
                {
                    HarmonyInstance.Patch(clickMi, postfix: new HarmonyMethod(
                        typeof(ModMenuMod).GetMethod(nameof(OnPointerClickPostfix),
                            BindingFlags.Static | BindingFlags.NonPublic)));
                }

                var downMi = typeof(Selectable).GetMethod("OnPointerDown",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(PointerEventData) }, null);
                if (downMi != null)
                {
                    HarmonyInstance.Patch(downMi, postfix: new HarmonyMethod(
                        typeof(ModMenuMod).GetMethod(nameof(OnPointerDownPostfix),
                            BindingFlags.Static | BindingFlags.NonPublic)));
                }

                var upMi = typeof(Selectable).GetMethod("OnPointerUp",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(PointerEventData) }, null);
                if (upMi != null)
                {
                    HarmonyInstance.Patch(upMi, postfix: new HarmonyMethod(
                        typeof(ModMenuMod).GetMethod(nameof(OnPointerUpPostfix),
                            BindingFlags.Static | BindingFlags.NonPublic)));
                }

                var exitMi = typeof(Selectable).GetMethod("OnPointerExit",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(PointerEventData) }, null);
                if (exitMi != null)
                {
                    HarmonyInstance.Patch(exitMi, postfix: new HarmonyMethod(
                        typeof(ModMenuMod).GetMethod(nameof(OnPointerUpPostfix),
                            BindingFlags.Static | BindingFlags.NonPublic)));
                }
            }
            catch (System.Exception e)
            {
                MelonLogger.Warning($"{Tag} Pointer patch error: {e.Message}");
            }
        }

        private static void OnPointerClickPostfix(Button __instance)
        {
            if (__instance == null) return;
            if (ButtonActions.TryGetValue(__instance.GetInstanceID(), out var action))
            {
                try
                {
                    action.Invoke();
                }
                catch (System.Exception e)
                {
                    MelonLogger.Warning($"{Tag} Click error: {e.Message}");
                }
            }
        }

        private static void OnPointerDownPostfix(Selectable __instance)
        {
            if (__instance == null) return;
            if (HoldButtons.TryGetValue(__instance.GetInstanceID(), out var holdInfo))
            {
                _activeHold = holdInfo;
                _holdStartTime = Time.unscaledTime;
                _nextHoldTick = Time.unscaledTime + 0.35f; // Пауза перед началом авто-повтора

                // Первый одиночный шаг сразу при нажатии
                float initialStep = (holdInfo.Entry.GetReflectedType() == typeof(int)) ? 1f : 0.1f;
                ModConfigManager.StepNumericEntry(holdInfo.Entry, initialStep * holdInfo.Direction, saveImmediately: true);

                if (holdInfo.ValueLabel != null)
                    holdInfo.ValueLabel.text = ModConfigManager.FormatValue(holdInfo.Entry);
            }
        }

        private static void OnPointerUpPostfix(Selectable __instance)
        {
            if (__instance == null || _activeHold == null) return;
            if (HoldButtons.TryGetValue(__instance.GetInstanceID(), out var holdInfo) && holdInfo == _activeHold)
            {
                StopHold();
            }
        }
    }

    // =========================================================================
    // СИСТЕМА ОТКЛЮЧЕНИЯ / ВКЛЮЧЕНИЯ МОДОВ НАЛЕТУ (ДАЖЕ БЕЗ КОНФИГА)
    // =========================================================================
    public static class ModToggleManager
    {
        private static MelonPreferences_Category _stateCat;
        private static readonly HashSet<Assembly> DisabledAssemblies = new HashSet<Assembly>();
        private static readonly HashSet<Assembly> PatchedAssemblies = new HashSet<Assembly>();

        public static void Init()
        {
            _stateCat = MelonPreferences.CreateCategory("ModMenu_States", "ModMenu States");
        }

        public static bool IsModEnabled(ModMenuItem item)
        {
            if (item == null) return true;

            // Если у мода есть собственная настройка Enabled в конфиге — читаем её
            var nativeEnabled = FindNativeEnabledEntry(item);
            if (nativeEnabled != null && nativeEnabled.BoxedValue is bool b)
                return b;

            if (item.Melon?.MelonAssembly?.Assembly != null)
                return !DisabledAssemblies.Contains(item.Melon.MelonAssembly.Assembly);

            return true;
        }

        public static void ToggleMod(ModMenuItem item)
        {
            if (item == null) return;
            bool nextState = !IsModEnabled(item);

            // 1. Переключаем штатный параметр Enabled в конфиге мода (если он есть)
            var nativeEnabled = FindNativeEnabledEntry(item);
            if (nativeEnabled != null)
            {
                nativeEnabled.BoxedValue = nextState;
            }

            // 2. Блокируем/разблокируем выполнение кода сборки мода
            if (item.Melon?.MelonAssembly?.Assembly != null)
            {
                EnsureModPatched(item.Melon);
                var asm = item.Melon.MelonAssembly.Assembly;

                if (nextState)
                    DisabledAssemblies.Remove(asm);
                else
                    DisabledAssemblies.Add(asm);

                SaveModState(item.Name, nextState);
            }

            MelonPreferences.Save();
            MelonLogger.Msg($"{ModMenuMod.Tag} Mod '{item.Name}' set to {(nextState ? "ENABLED" : "DISABLED")}");
        }

        private static void SaveModState(string modName, bool enabled)
        {
            if (_stateCat == null || string.IsNullOrEmpty(modName)) return;
            string key = "Enabled_" + modName.Replace(" ", "_");
            var entry = _stateCat.GetEntry<bool>(key) ?? _stateCat.CreateEntry(key, true);
            entry.Value = enabled;
        }

        public static void EnsureModPatched(MelonBase melon)
        {
            if (melon?.MelonAssembly?.Assembly == null || ModMenuMod.SharedHarmony == null) return;
            var asm = melon.MelonAssembly.Assembly;
            if (asm == typeof(ModMenuMod).Assembly) return;
            if (PatchedAssemblies.Contains(asm)) return;

            PatchedAssemblies.Add(asm);

            // Проверяем сохраненное состояние из прошлого запуска
            string key = "Enabled_" + (melon.Info?.Name ?? "Mod").Replace(" ", "_");
            var savedEntry = _stateCat.GetEntry<bool>(key) ?? _stateCat.CreateEntry(key, true);
            if (!savedEntry.Value)
                DisabledAssemblies.Add(asm);

            var prefixMethod = typeof(ModToggleManager).GetMethod(nameof(ExecutionGuardPrefix),
                BindingFlags.Static | BindingFlags.NonPublic);

            // Патчим методы жизненного цикла MelonMod и MonoBehaviour внутри этого мода
            string[] targetMethods =
            {
                "OnUpdate", "OnFixedUpdate", "OnLateUpdate", "OnGUI",
                "OnSceneWasLoaded", "OnSceneWasInitialized",
                "Update", "FixedUpdate", "LateUpdate"
            };

            try
            {
                foreach (var type in asm.GetTypes())
                {
                    foreach (string mName in targetMethods)
                    {
                        var mi = type.GetMethod(mName,
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                        if (mi != null && mi.ReturnType == typeof(void))
                        {
                            ModMenuMod.SharedHarmony.Patch(mi, prefix: new HarmonyMethod(prefixMethod));
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                MelonLogger.Warning($"{ModMenuMod.Tag} Could not hook lifecycle of {melon.Info?.Name}: {e.Message}");
            }
        }

        private static bool ExecutionGuardPrefix(MethodBase __originalMethod)
        {
            var asm = __originalMethod?.DeclaringType?.Assembly;
            if (asm != null && DisabledAssemblies.Contains(asm))
                return false; // Пропускаем выполнение метода, если мод отключен
            return true;
        }

        private static MelonPreferences_Entry FindNativeEnabledEntry(ModMenuItem item)
        {
            foreach (var cat in item.Categories)
            {
                if (cat?.Entries == null) continue;
                foreach (var e in cat.Entries)
                {
                    if (e != null && e.GetReflectedType() == typeof(bool) &&
                        (string.Equals(e.Identifier, "Enabled", System.StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(e.Identifier, "Enable", System.StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(e.Identifier, "ModEnabled", System.StringComparison.OrdinalIgnoreCase)))
                    {
                        return e;
                    }
                }
            }
            return null;
        }
    }

    // =========================================================================
    // ПОСТРОИТЕЛЬ UI В МЕНЮ НАСТРОЕК ИГРЫ (STRICTLY ENGLISH)
    // =========================================================================
    public static class ModMenuPanel
    {
        private static GameObject _panelRoot;
        private static GameObject _tabButtonObj;
        private static Transform _leftListRoot;
        private static Transform _rightConfigRoot;
        private static Font _cachedFont;

        private static List<ModMenuItem> _mods = new List<ModMenuItem>();
        private static int _selectedModIndex = 0;
        private static int _configPage = 0;
        private const int EntriesPerPage = 5;

        public static void Ensure()
        {
            if (_panelRoot != null) return;

            var menuMgr = UnityEngine.Object.FindObjectOfType<MenuUIManager>();
            if (menuMgr == null) return;

            try
            {
                BuildUI(menuMgr);
            }
            catch (System.Exception e)
            {
                MelonLogger.Warning($"{ModMenuMod.Tag} Failed to build UI: {e.Message}");
            }
        }

        public static void Hide()
        {
            ModMenuMod.ClearRegisteredButtons();
            if (_panelRoot != null && _panelRoot.activeSelf)
                _panelRoot.SetActive(false);
        }

        public static void TogglePanel(MenuUIManager menuMgr)
        {
            if (_panelRoot == null) return;
            bool nextState = !_panelRoot.activeSelf;

            if (nextState)
            {
                _mods = ModConfigManager.BuildScriptModList();
                if (_selectedModIndex >= _mods.Count) _selectedModIndex = 0;
                _panelRoot.SetActive(true);
                _panelRoot.transform.SetAsLastSibling();
                RebuildLists();
            }
            else
            {
                Hide();
            }
        }

        private static void BuildUI(MenuUIManager menuMgr)
        {
            _cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            var existingText = menuMgr.GetComponentInChildren<Text>(true);
            if (existingText != null && existingText.font != null)
                _cachedFont = existingText.font;

            Button templateBtn = menuMgr.GetComponentInChildren<Button>(true);
            Transform canvasParent = menuMgr.transform;
            if (templateBtn != null && templateBtn.transform.parent != null)
                canvasParent = templateBtn.transform.parent.parent ?? templateBtn.transform.parent;

            // 1. Кнопка "MODS [CFG]" поднята НАД панелью настроек (Y: 1.02 .. 1.09), чтобы ничего не перекрывать
            _tabButtonObj = CreateStyledButton(canvasParent, "ModMenu_TabButton", "MODS [CFG]",
                new Vector2(0.82f, 1.02f), new Vector2(0.98f, 1.09f), new Color(0.18f, 0.45f, 0.22f, 0.95f));

            var tabBtn = _tabButtonObj.GetComponent<Button>();
            ModMenuMod.RegisterButton(tabBtn, () => TogglePanel(menuMgr));

            // 2. Основная панель Мод-Меню
            _panelRoot = new GameObject("ModMenu_MainPanel");
            _panelRoot.transform.SetParent(canvasParent, false);

            var rootRt = _panelRoot.AddComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.03f, 0.05f);
            rootRt.anchorMax = new Vector2(0.97f, 0.90f);
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            var bg = _panelRoot.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.1f, 0.96f);

            // Заголовок
            CreateLabel(_panelRoot.transform, "Header", "ACTIVE MODS & CONFIG EDITOR",
                new Vector2(0.02f, 0.90f), new Vector2(0.85f, 0.98f), 18, TextAnchor.MiddleLeft, Color.white);

            // Кнопка закрытия [X]
            var closeBtnObj = CreateStyledButton(_panelRoot.transform, "CloseBtn", "X",
                new Vector2(0.92f, 0.91f), new Vector2(0.98f, 0.98f), new Color(0.6f, 0.15f, 0.15f, 1f));
            ModMenuMod.RegisterButton(closeBtnObj.GetComponent<Button>(), Hide);

            // Левая колонка (Список модов)
            var leftObj = new GameObject("LeftModsList");
            leftObj.transform.SetParent(_panelRoot.transform, false);
            var leftRt = leftObj.AddComponent<RectTransform>();
            leftRt.anchorMin = new Vector2(0.02f, 0.04f);
            leftRt.anchorMax = new Vector2(0.34f, 0.88f);
            leftRt.offsetMin = Vector2.zero;
            leftRt.offsetMax = Vector2.zero;
            _leftListRoot = leftObj.transform;

            // Правая колонка (Статус мода + Настройки CFG)
            var rightObj = new GameObject("RightConfigList");
            rightObj.transform.SetParent(_panelRoot.transform, false);
            var rightRt = rightObj.AddComponent<RectTransform>();
            rightRt.anchorMin = new Vector2(0.36f, 0.04f);
            rightRt.anchorMax = new Vector2(0.98f, 0.88f);
            rightRt.offsetMin = Vector2.zero;
            rightRt.offsetMax = Vector2.zero;
            _rightConfigRoot = rightObj.transform;

            _panelRoot.SetActive(false);
        }

        private static void RebuildLists()
        {
            ModMenuMod.ClearRegisteredButtons();
            ClearChildren(_leftListRoot);
            ClearChildren(_rightConfigRoot);

            if (_mods.Count == 0)
            {
                CreateLabel(_leftListRoot, "Empty", "No active mods found",
                    new Vector2(0f, 0.8f), new Vector2(1f, 0.95f), 14, TextAnchor.MiddleCenter, Color.gray);
                return;
            }

            // 1. Список модов слева
            int maxMods = System.Math.Min(_mods.Count, 10);
            for (int i = 0; i < maxMods; i++)
            {
                int capturedIdx = i;
                var mod = _mods[i];
                bool isSelected = (i == _selectedModIndex);
                bool isEnabled = ModToggleManager.IsModEnabled(mod);

                float top = 1f - (i * 0.095f);
                float bottom = top - 0.085f;

                Color btnColor = isSelected
                    ? new Color(0.22f, 0.48f, 0.28f, 1f)
                    : new Color(0.18f, 0.18f, 0.22f, 1f);

                if (!isEnabled)
                    btnColor = isSelected ? new Color(0.5f, 0.2f, 0.2f, 1f) : new Color(0.25f, 0.14f, 0.14f, 1f);

                string statusPrefix = isEnabled ? "[ON] " : "[OFF] ";
                string label = $"{statusPrefix}{mod.Name}";
                var btnObj = CreateStyledButton(_leftListRoot, $"ModBtn_{i}", label,
                    new Vector2(0f, bottom), new Vector2(1f, top), btnColor);

                ModMenuMod.RegisterButton(btnObj.GetComponent<Button>(), () =>
                {
                    _selectedModIndex = capturedIdx;
                    _configPage = 0;
                    RebuildLists();
                });
            }

            // 2. Шапка выбранного мода справа: Кнопка ENABLE/DISABLE + Кнопка RESET DEFAULTS
            var selectedMod = _mods[_selectedModIndex];
            bool modEnabled = ModToggleManager.IsModEnabled(selectedMod);

            Color toggleModColor = modEnabled
                ? new Color(0.16f, 0.48f, 0.22f, 1f)
                : new Color(0.55f, 0.16f, 0.16f, 1f);

            string toggleModText = modEnabled ? "MOD STATUS: ENABLED" : "MOD STATUS: DISABLED";
            var modToggleBtn = CreateStyledButton(_rightConfigRoot, "ToggleModStateBtn", toggleModText,
                new Vector2(0.02f, 0.89f), new Vector2(0.55f, 0.99f), toggleModColor);

            ModMenuMod.RegisterButton(modToggleBtn.GetComponent<Button>(), () =>
            {
                ModToggleManager.ToggleMod(selectedMod);
                RebuildLists();
            });

            var allEntries = new List<MelonPreferences_Entry>();
            foreach (var cat in selectedMod.Categories)
            {
                if (cat?.Entries != null)
                    allEntries.AddRange(cat.Entries);
            }

            // Кнопка сброса всех настроек мода по умолчанию (RESET DEFAULTS)
            if (allEntries.Count > 0)
            {
                var resetAllBtn = CreateStyledButton(_rightConfigRoot, "ResetDefaultsBtn", "RESET DEFAULTS",
                    new Vector2(0.58f, 0.89f), new Vector2(0.98f, 0.99f), new Color(0.45f, 0.30f, 0.12f, 1f));

                ModMenuMod.RegisterButton(resetAllBtn.GetComponent<Button>(), () =>
                {
                    ModConfigManager.ResetAllToDefault(allEntries);
                    RebuildLists();
                });
            }

            if (allEntries.Count == 0)
            {
                CreateLabel(_rightConfigRoot, "NoCfg",
                    $"Mod '{selectedMod.Name}' ({selectedMod.Version})\nNo config entries in MelonPreferences.cfg.\nYou can toggle this mod ON/OFF using the button above.",
                    new Vector2(0.05f, 0.35f), new Vector2(0.95f, 0.78f), 14, TextAnchor.MiddleCenter, new Color(0.75f, 0.75f, 0.75f, 1f));
                return;
            }

            // 3. Постраничный список настроек
            int totalPages = (allEntries.Count + EntriesPerPage - 1) / EntriesPerPage;
            if (_configPage >= totalPages) _configPage = 0;

            int start = _configPage * EntriesPerPage;
            int count = System.Math.Min(EntriesPerPage, allEntries.Count - start);

            for (int row = 0; row < count; row++)
            {
                var entry = allEntries[start + row];
                float top = 0.84f - (row * 0.14f);
                float bottom = top - 0.12f;

                BuildEntryRow(_rightConfigRoot, entry, bottom, top);
            }

            if (totalPages > 1)
            {
                var prevBtn = CreateStyledButton(_rightConfigRoot, "PrevPage", "< PREV",
                    new Vector2(0.08f, 0.01f), new Vector2(0.34f, 0.11f), new Color(0.25f, 0.25f, 0.3f, 1f));
                ModMenuMod.RegisterButton(prevBtn.GetComponent<Button>(), () =>
                {
                    _configPage = (_configPage - 1 + totalPages) % totalPages;
                    RebuildLists();
                });

                CreateLabel(_rightConfigRoot, "PageInfo", $"Page {_configPage + 1} / {totalPages}",
                    new Vector2(0.35f, 0.01f), new Vector2(0.65f, 0.11f), 14, TextAnchor.MiddleCenter, Color.white);

                var nextBtn = CreateStyledButton(_rightConfigRoot, "NextPage", "NEXT >",
                    new Vector2(0.66f, 0.01f), new Vector2(0.92f, 0.11f), new Color(0.25f, 0.25f, 0.3f, 1f));
                ModMenuMod.RegisterButton(nextBtn.GetComponent<Button>(), () =>
                {
                    _configPage = (_configPage + 1) % totalPages;
                    RebuildLists();
                });
            }
        }

        private static void BuildEntryRow(Transform parent, MelonPreferences_Entry entry, float bottom, float top)
        {
            // Берем строго entry.Identifier, чтобы имена были на английском как в MelonPreferences.cfg
            string entryTitle = entry.Identifier;
            CreateLabel(parent, $"Lbl_{entry.Identifier}", entryTitle,
                new Vector2(0.02f, bottom), new Vector2(0.48f, top), 14, TextAnchor.MiddleLeft, Color.white);

            System.Type t = entry.GetReflectedType();

            if (t == typeof(bool) || t.IsEnum)
            {
                string valText = ModConfigManager.FormatValue(entry);
                var toggleBtn = CreateStyledButton(parent, $"Toggle_{entry.Identifier}", valText,
                    new Vector2(0.50f, bottom), new Vector2(0.88f, top), new Color(0.2f, 0.25f, 0.32f, 1f));

                ModMenuMod.RegisterButton(toggleBtn.GetComponent<Button>(), () =>
                {
                    ModConfigManager.ToggleOrCycleEntry(entry);
                    RebuildLists();
                });
            }
            else if (t == typeof(int) || t == typeof(float))
            {
                var valLabel = CreateLabel(parent, $"Val_{entry.Identifier}", ModConfigManager.FormatValue(entry),
                    new Vector2(0.61f, bottom), new Vector2(0.77f, top), 14, TextAnchor.MiddleCenter, Color.yellow);

                var minusBtnObj = CreateStyledButton(parent, $"Minus_{entry.Identifier}", "-",
                    new Vector2(0.50f, bottom), new Vector2(0.60f, top), new Color(0.35f, 0.2f, 0.2f, 1f));
                ModMenuMod.RegisterHoldStepButton(minusBtnObj.GetComponent<Button>(), entry, -1, valLabel);

                var plusBtnObj = CreateStyledButton(parent, $"Plus_{entry.Identifier}", "+",
                    new Vector2(0.78f, bottom), new Vector2(0.88f, top), new Color(0.2f, 0.35f, 0.2f, 1f));
                ModMenuMod.RegisterHoldStepButton(plusBtnObj.GetComponent<Button>(), entry, +1, valLabel);
            }
            else
            {
                CreateLabel(parent, $"Val_{entry.Identifier}", ModConfigManager.FormatValue(entry),
                    new Vector2(0.50f, bottom), new Vector2(0.88f, top), 13, TextAnchor.MiddleRight, Color.gray);
            }

            // Кнопка [R] — быстрый сброс конкретного параметра по умолчанию
            var resetOneBtn = CreateStyledButton(parent, $"Reset_{entry.Identifier}", "R",
                new Vector2(0.90f, bottom), new Vector2(0.98f, top), new Color(0.35f, 0.25f, 0.15f, 1f));
            ModMenuMod.RegisterButton(resetOneBtn.GetComponent<Button>(), () =>
            {
                entry.ResetToDefault();
                MelonPreferences.Save();
                RebuildLists();
            });
        }

        private static GameObject CreateStyledButton(Transform parent, string name, string text,
            Vector2 anchorMin, Vector2 anchorMax, Color bgColor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            CreateLabel(go.transform, "Text", text, Vector2.zero, Vector2.one, 13, TextAnchor.MiddleCenter, Color.white);
            return go;
        }

        private static Text CreateLabel(Transform parent, string name, string content,
            Vector2 anchorMin, Vector2 anchorMax, int fontSize, TextAnchor align, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var txt = go.AddComponent<Text>();
            txt.font = _cachedFont;
            txt.text = content;
            txt.fontSize = fontSize;
            txt.alignment = align;
            txt.color = color;
            txt.raycastTarget = false;
            return txt;
        }

        private static void ClearChildren(Transform root)
        {
            if (root == null) return;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(root.GetChild(i).gameObject);
            }
        }
    }

    // =========================================================================
    // СКАНЕР СКРИПТОВЫХ МОДОВ И КОНФИГОВ (MELONPREFERENCES)
    // =========================================================================
    public class ModMenuItem
    {
        public string Name = string.Empty;
        public string Version = string.Empty;
        public string Author = string.Empty;
        public MelonBase Melon;
        public bool IsUnlinkedCategory;
        public List<MelonPreferences_Category> Categories = new List<MelonPreferences_Category>();
    }

    public static class ModConfigManager
    {
        public static List<ModMenuItem> BuildScriptModList()
        {
            var result = new List<ModMenuItem>();
            var claimedCategories = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var melon in MelonBase.RegisteredMelons)
            {
                if (melon?.Info == null) continue;
                if (string.Equals(melon.Info.Name, "ModMenu", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                var item = new ModMenuItem
                {
                    Name = melon.Info.Name ?? "Unknown Mod",
                    Version = melon.Info.Version ?? "1.0.0",
                    Author = melon.Info.Author ?? "Unknown",
                    Melon = melon
                };

                string cleanModName = Normalize(item.Name);
                string asmName = Normalize(melon.MelonAssembly?.Assembly?.GetName().Name);

                foreach (var cat in MelonPreferences.Categories)
                {
                    if (cat == null) continue;
                    if (string.Equals(cat.Identifier, "ModMenu_States", System.StringComparison.OrdinalIgnoreCase))
                        continue;

                    string cleanCatId = Normalize(cat.Identifier);
                    string cleanCatTitle = Normalize(cat.DisplayName);

                    bool matchByModName = !string.IsNullOrEmpty(cleanModName) &&
                                          (cleanCatId.Contains(cleanModName) || cleanCatTitle.Contains(cleanModName));

                    bool matchByAsmName = !string.IsNullOrEmpty(asmName) &&
                                          (cleanCatId.Contains(asmName) || cleanCatTitle.Contains(asmName));

                    if (matchByModName || matchByAsmName)
                    {
                        item.Categories.Add(cat);
                        if (!string.IsNullOrEmpty(cat.Identifier))
                            claimedCategories.Add(cat.Identifier);
                    }
                }

                result.Add(item);
            }

            foreach (var cat in MelonPreferences.Categories)
            {
                if (cat == null || string.IsNullOrEmpty(cat.Identifier)) continue;
                if (claimedCategories.Contains(cat.Identifier)) continue;
                if (cat.Entries == null || cat.Entries.Count == 0) continue;
                if (cat.Identifier.StartsWith("MelonLoader", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(cat.Identifier, "ModMenu_States", System.StringComparison.OrdinalIgnoreCase)) continue;

                // Используем строго английский Identifier категории
                result.Add(new ModMenuItem
                {
                    Name = cat.Identifier,
                    Version = "CFG",
                    Author = "MelonPreferences",
                    IsUnlinkedCategory = true,
                    Categories = new List<MelonPreferences_Category> { cat }
                });
            }

            return result;
        }

        public static void ResetAllToDefault(List<MelonPreferences_Entry> entries)
        {
            if (entries == null) return;
            foreach (var entry in entries)
            {
                entry?.ResetToDefault();
            }
            MelonPreferences.Save();
        }

        public static void ToggleOrCycleEntry(MelonPreferences_Entry entry)
        {
            if (entry == null) return;

            System.Type t = entry.GetReflectedType();
            if (t == typeof(bool) && entry.BoxedValue is bool currentBool)
            {
                entry.BoxedValue = !currentBool;
                MelonPreferences.Save();
            }
            else if (t.IsEnum && entry.BoxedValue != null)
            {
                System.Array values = System.Enum.GetValues(t);
                if (values.Length > 0)
                {
                    int currentIndex = System.Array.IndexOf(values, entry.BoxedValue);
                    int nextIndex = (currentIndex + 1) % values.Length;
                    object nextValue = values.GetValue(nextIndex);
                    if (nextValue != null)
                    {
                        entry.BoxedValue = nextValue;
                        MelonPreferences.Save();
                    }
                }
            }
        }

        public static void StepNumericEntry(MelonPreferences_Entry entry, float delta, bool saveImmediately = true)
        {
            if (entry == null) return;

            System.Type t = entry.GetReflectedType();
            if (t == typeof(int) && entry.BoxedValue is int currentInt)
            {
                entry.BoxedValue = currentInt + (int)System.Math.Round(delta);
                if (saveImmediately) MelonPreferences.Save();
            }
            else if (t == typeof(float) && entry.BoxedValue is float currentFloat)
            {
                entry.BoxedValue = (float)System.Math.Round(currentFloat + delta, 2);
                if (saveImmediately) MelonPreferences.Save();
            }
        }

        public static string FormatValue(MelonPreferences_Entry entry)
        {
            if (entry?.BoxedValue == null) return "null";

            if (entry.BoxedValue is bool b)
                return b ? "ON" : "OFF";

            if (entry.BoxedValue is float f)
                return f.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

            return entry.BoxedValue.ToString() ?? "null";
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
        }
    }
}

namespace System.Runtime.CompilerServices
{
    internal sealed class NullableAttribute : System.Attribute
    {
        public NullableAttribute(byte b) { }
        public NullableAttribute(byte[] b) { }
    }

    internal sealed class NullableContextAttribute : System.Attribute
    {
        public NullableContextAttribute(byte b) { }
    }
}