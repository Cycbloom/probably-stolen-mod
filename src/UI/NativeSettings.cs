using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DumpingGroundMap
{
    /// <summary>
    /// All loot tuning lives in the game's own mod-config window instead of the map.
    /// A postfix on ModConfigWindow.BuildEntry hooks the native sliders, and a patch
    /// on CustomUIElement.SetFixedWidth appends a value label to the same row, right
    /// of the slider, so the number sits where the thumb is instead of on its own
    /// line. Below the last entry the reputation value block and the preset buttons
    /// are appended. Dragging only changes Tuning's in-memory values and the labels;
    /// the config file is written on PointerUp or when Apply is pressed.
    /// </summary>
    internal static class NativeSettings
    {
        private const string KeyJunk = "junk_percent";
        private const string KeyHigh = "high_percent";
        private const string KeyCrate = "crate_percent";
        private const string KeyCard = "keycard_percent";
        private const string KeyMinor = "minor_percent";
        private const string KeyMinorValue = "minor_value";
        private const string KeyGroundWidth = Tuning.GroundWidthKey;
        private const string KeyGroundHeight = Tuning.GroundHeightKey;
        private const string KeyGroundAlign = Tuning.GroundAlignKey;
        private const string LogTag = "[DGM-SLIDER] ";

        private static CustomUIWindow window;
        private static CustomUIWindow builtFor;
        private static CustomUIElement junkValue, highValue;
        private static CustomUIElement crateValue, cardValue;
        private static CustomUIElement minorValue;
        private static CustomUIElement minorCrateValue;
        private static CustomUIElement stashCapValue, stashCapSlider;
        private static CustomUIElement foreignValue;
        private static CustomUIElement debugValue;
        private static CustomUIElement groundWidthValue, groundHeightValue;
        private static CustomUIElement specialtyDropdown;
        private static CustomUIElement groundAlignDropdown;
        private static CustomUIElement[] evLabels;
        private static readonly ReputationExpectationPreview reputationPreview = new ReputationExpectationPreview();
        private static CustomUIElement junkSlider, highSlider;
        private static CustomUIElement crateSlider, cardSlider;
        private static CustomUIElement minorSlider;
        private static CustomUIElement minorCrateSlider;
        private static CustomUIElement foreignSlider;
        private static CustomUIElement debugSlider;
        private static CustomUIElement groundWidthSlider, groundHeightSlider;
        private static bool dragging;
        private static int dragKind;
        private static float lastSlideAt;
        private static int slideCount;
        private static float lastLogAt;

        /// <summary>Builder and entry key of the row currently being built.</summary>
        private static CustomUIBuilder building;
        private static string buildingKey;

        internal static void BeginEntry(CustomUIBuilder b, ModConfig config, ConfigEntry entry)
        {
            building = null;
            buildingKey = null;
            try
            {
                if (b == null || config == null || entry == null) return;
                if (config.ModId != Core.Id) return;
                string key = entry.Key;
                if (!IsOurKey(key)) return;

                CustomUIWindow target = b.Handle;
                if (target == null) return;
                if (!ReferenceEquals(target, builtFor))
                {
                    Reset();
                    builtFor = target;
                    window = target;
                }
                building = b;
                buildingKey = key;
            }
            catch (Exception ex)
            {
                Debug.LogError(LogTag + "begin entry failed: " + ex);
            }
        }

        private static bool IsOurKey(string key)
        {
            return key == KeyJunk || key == KeyHigh
                || key == KeyCrate || key == KeyCard || key == KeyMinor
                || key == KeyMinorValue || key == Tuning.StashCapKey
                || key == Tuning.ForeignPercentKey
                || key == KeyGroundWidth || key == KeyGroundHeight
                || key == KeyGroundAlign
                || key == Tuning.SpecialtyKey
                || key == Tuning.DebugEnabledKey || key == Tuning.DebugPointsKey;
        }

        /// <summary>
        /// The native row calls SetFixedWidth on the slider right after adding it.
        /// Our postfix appends a value label to that same open row, so the number
        /// sits to the right of the slider instead of taking a line of its own.
        /// </summary>
        internal static void AttachValueLabel(CustomUIElement element)
        {
            try
            {
                if (element == null || building == null || buildingKey == null) return;
                if (buildingKey != KeyJunk && buildingKey != KeyHigh
                    && buildingKey != KeyCrate && buildingKey != KeyCard
                    && buildingKey != KeyMinor && buildingKey != KeyMinorValue
                    && buildingKey != Tuning.StashCapKey
                    && buildingKey != Tuning.ForeignPercentKey
                    && buildingKey != KeyGroundWidth && buildingKey != KeyGroundHeight
                    && buildingKey != Tuning.DebugPointsKey) return;
                if (!ReferenceEquals(element, building.Last)) return;
                if (element.slider == null) return;

                string key = buildingKey;
                // Cleared while we build the label so our own SetFixedWidth call below
                // cannot re-enter this postfix.
                buildingKey = null;
                building.AddLabel("", "dgm_value_" + key);
                CustomUIElement label = building.Last;
                // Fixed width with flexibleWidth 0 so the row keeps the slider where
                // the native layout put it and the number just sits to its right.
                // The debug row only prints "19.5", so a narrower label is enough
                // and leaves room next to the 224px slider.
                label.SetFixedWidth(key == Tuning.DebugPointsKey
                    || key == KeyGroundWidth || key == KeyGroundHeight ? 56f : 72f);
                if (label.text != null)
                {
                    label.text.alignment = TextAlignmentOptions.MidlineLeft;
                    label.text.color = Picker.Ink;
                }
                if (key == KeyJunk) junkValue = label;
                else if (key == KeyHigh) highValue = label;
                else if (key == KeyCrate) crateValue = label;
                else if (key == KeyCard) cardValue = label;
                else if (key == KeyMinor) minorValue = label;
                else if (key == KeyMinorValue) minorCrateValue = label;
                else if (key == Tuning.StashCapKey) stashCapValue = label;
                else if (key == Tuning.ForeignPercentKey) foreignValue = label;
                else if (key == KeyGroundWidth) groundWidthValue = label;
                else if (key == KeyGroundHeight) groundHeightValue = label;
                else debugValue = label;
            }
            catch (Exception ex)
            {
                Debug.LogError(LogTag + "value label failed: " + ex);
            }
        }

        /// <summary>Called from the BuildEntry postfix, after the native row is closed.</summary>
        internal static void Inject(CustomUIBuilder b, ModConfig config, ConfigEntry entry)
        {
            try
            {
                if (b == null || config == null || entry == null) return;
                if (config.ModId != Core.Id) return;
                string key = entry.Key;
                if (!IsOurKey(key)) return;
                if (window == null) window = b.Handle;
                if (window == null) return;

                if (key == KeyJunk)
                {
                    junkSlider = HookSlider(window, key, 1);
                    junkValue = EnsureValueLine(b, key, junkValue);
                }
                else if (key == KeyHigh)
                {
                    highSlider = HookSlider(window, key, 2);
                    highValue = EnsureValueLine(b, key, highValue);
                }
                else if (key == KeyCrate)
                {
                    crateSlider = HookSlider(window, key, 3);
                    crateValue = EnsureValueLine(b, key, crateValue);
                }
                else if (key == KeyCard)
                {
                    cardSlider = HookSlider(window, key, 4);
                    cardValue = EnsureValueLine(b, key, cardValue);
                }
                else if (key == KeyMinor)
                {
                    minorSlider = HookSlider(window, key, 5);
                    minorValue = EnsureValueLine(b, key, minorValue);
                }
                else if (key == KeyMinorValue)
                {
                    minorCrateSlider = HookSlider(window, key, 6);
                    minorCrateValue = EnsureValueLine(b, key, minorCrateValue);
                }
                else if (key == Tuning.StashCapKey)
                {
                    stashCapSlider = window.Get(key);
                    stashCapValue = EnsureValueLine(b, key, stashCapValue);
                    if (stashCapSlider != null && stashCapSlider.slider != null)
                    {
                        stashCapSlider.OnSlider(delegate(float value)
                        {
                            SyncSlider(stashCapSlider, Tuning.ClampStashCap(value));
                            Refresh();
                        });
                    }
                }
                else if (key == Tuning.ForeignPercentKey)
                {
                    foreignSlider = HookSlider(window, key, 9);
                    foreignValue = EnsureValueLine(b, key, foreignValue);
                }
                else if (key == KeyGroundAlign)
                {
                    groundAlignDropdown = b.Last;
                    HookGroundAlignDropdown(groundAlignDropdown, key);
                }
                else if (key == KeyGroundWidth)
                {
                    groundWidthSlider = HookSlider(window, key, 7);
                    groundWidthValue = EnsureValueLine(b, key, groundWidthValue);
                }
                else if (key == KeyGroundHeight)
                {
                    groundHeightSlider = HookSlider(window, key, 8);
                    groundHeightValue = EnsureValueLine(b, key, groundHeightValue);
                    AddGroundPresetBlock(b);
                }
                else if (key == Tuning.SpecialtyKey)
                {
                    specialtyDropdown = b.Last;
                    HookSpecialtyDropdown(specialtyDropdown, key);
                    AddEvBlock(b);
                }
                else if (key == Tuning.DebugEnabledKey)
                {
                    HookDebugToggle(window, key);
                }
                else if (key == Tuning.DebugPointsKey)
                {
                    debugSlider = HookDebugSlider(window, key);
                    debugValue = EnsureValueLine(b, key, debugValue);
                    if (debugValue != null)
                        debugValue.SetText(Tuning.DebugPoints.ToString("0.0", CultureInfo.InvariantCulture));
                }
                Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogError(LogTag + "inject failed: " + ex);
            }
            finally
            {
                building = null;
                buildingKey = null;
            }
        }

        /// <summary>
        /// Fallback only: if the row patch did not add a value label, keep the old
        /// separate line so the number is never missing.
        /// </summary>
        private static CustomUIElement EnsureValueLine(CustomUIBuilder b, string key, CustomUIElement current)
        {
            if (current != null) return current;
            b.AddLabel("", "dgm_value_" + key);
            CustomUIElement label = b.Last;
            Picker.Size(label, -1, 22);
            return label;
        }

        private static CustomUIElement HookSlider(CustomUIWindow target, string key, int kind)
        {
            CustomUIElement element = target.Get(key);
            if (element == null || element.slider == null)
            {
                Debug.LogWarning(LogTag + key + ": slider widget missing, live value disabled");
                return null;
            }
            if (key == KeyJunk)
            {
                element.slider.minValue = Tuning.JunkMin;
                element.slider.maxValue = Tuning.JunkMax;
                element.slider.wholeNumbers = true;
            }
            element.slider.onValueChanged.AddListener(delegate(float value) { OnSlide(kind, value); });
            HookRelease(element, key);
            Debug.Log(LogTag + key + ": live listener bound (min="
                + element.slider.minValue.ToString("0.###", CultureInfo.InvariantCulture)
                + " max=" + element.slider.maxValue.ToString("0.###", CultureInfo.InvariantCulture)
                + " whole=" + element.slider.wholeNumbers + ")");
            return element;
        }

        /// <summary>
        /// PointerUp trigger on the GameObject that owns the Slider component. Putting
        /// it on child graphics would make EventTrigger the press/drag target and the
        /// Slider would never move (regression fixed in 0.3.3).
        /// </summary>
        private static void HookRelease(CustomUIElement element, string key)
        {
            GameObject go = element.slider.gameObject;
            UnityEngine.EventSystems.EventTrigger trigger =
                go.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            if (trigger == null) trigger = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            if (trigger.triggers == null)
                trigger.triggers = new List<UnityEngine.EventSystems.EventTrigger.Entry>();
            UnityEngine.EventSystems.EventTrigger.Entry entry =
                new UnityEngine.EventSystems.EventTrigger.Entry();
            entry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp;
            entry.callback.AddListener(delegate(UnityEngine.EventSystems.BaseEventData data) { EndDrag(); });
            trigger.triggers.Add(entry);
            Debug.Log(LogTag + key + ": release hook on " + go.name);
        }

        private static void AddEvBlock(CustomUIBuilder b)
        {
            b.AddSpace(6);
            b.AddLabel(Copy.Get("nativesettings_main_drop_value_by_reputation_credits_item"), "dgm_ev_header");
            CustomUIElement header = b.Last;
            if (header.text != null) header.text.color = Picker.Accent;
            Picker.Size(header, -1, 26);

            // Fixed yardstick above the reputation rows: the untouched vanilla tables.
            b.AddLabel("", "dgm_ev_reference");
            CustomUIElement reference = b.Last;
            Picker.Size(reference, -1, 24);
            reference.SetText(ReferenceLine());
            if (reference.text != null) reference.text.color = Picker.Ink;

            b.AddLabel(Copy.Get("nativesettings_average_of_areas_open_at_each_tier"), "dgm_ev_basis");
            Picker.Size(b.Last, -1, 24);

            evLabels = new CustomUIElement[ReputationExpectationPreview.Milestones.Length];
            for (int i = 0; i < evLabels.Length; ++i)
            {
                b.AddLabel("", "dgm_ev_" + i);
                evLabels[i] = b.Last;
                Picker.Size(evLabels[i], -1, 48);
                // 标签预制的宽度只有约 400px，第二行的三项分解会被省略号截断。
                // 让它在纵向布局里按内容区的可用宽度
                // 伸展（flexibleWidth），允许收缩（minWidth 0），并关掉省略号，
                // 整条等式完整显示。
                LayoutElement layout = evLabels[i].root.GetComponent<LayoutElement>();
                if (layout == null) layout = evLabels[i].root.AddComponent<LayoutElement>();
                layout.minWidth = 0f;
                layout.flexibleWidth = 1f;
                if (evLabels[i].text != null)
                {
                    evLabels[i].text.richText = true;
                    evLabels[i].text.fontSize = 18;
                    evLabels[i].text.enableWordWrapping = false;
                    evLabels[i].text.overflowMode = TextOverflowModes.Overflow;
                    evLabels[i].text.alignment = TextAlignmentOptions.MidlineLeft;
                }
            }

            b.AddSpace(4);
            b.BeginRow(8f, TextAnchor.MiddleLeft);
            PresetButton(b, 0, Copy.Get("nativesettings_default"));
            PresetButton(b, 1, Copy.Get("nativesettings_enhanced"));
            b.End();
            b.AddSpace(4);
        }

        private static void PresetButton(CustomUIBuilder b, int preset, string text)
        {
            b.AddButton(text, delegate { ApplyPreset(preset); });
            Picker.Size(b.Last, 140, 34);
            LootPreset profile = LootPreset.For(preset);
            b.Last.SetTooltip(string.Format(CultureInfo.InvariantCulture,
                Copy.Get("nativesettings_junk_0_specialty_1_valuables_2_crates"),
                profile.Junk, profile.SpecialtyPercent, profile.High, profile.Crate,
                profile.Minor, profile.MinorValue, profile.StashCap));
        }

        /// <summary>「拾荒地面」分组末尾的三个常用尺寸，只写宽高两项。</summary>
        private static void AddGroundPresetBlock(CustomUIBuilder b)
        {
            b.AddSpace(6);
            b.AddLabel(Copy.Get("nativesettings_common_sizes_sets_width_and_height_together"), "dgm_ground_presets");
            CustomUIElement header = b.Last;
            if (header.text != null) header.text.color = Picker.Accent;
            Picker.Size(header, -1, 26);

            b.BeginRow(8f, TextAnchor.MiddleLeft);
            GroundPresetButton(b, 16, 13);
            GroundPresetButton(b, 19, 21);
            GroundPresetButton(b, 19, 31);
            b.End();
            b.AddSpace(4);
        }

        private static void GroundPresetButton(CustomUIBuilder b, int width, int height)
        {
            b.AddButton(width + " × " + height, delegate { ApplyGroundPreset(width, height); });
            Picker.Size(b.Last, 120, 34);
        }

        private static void ApplyGroundPreset(int width, int height)
        {
            EndDrag();
            Tuning.ApplyGroundPreset(width, height);
            SyncSlider(groundWidthSlider, Tuning.GroundWidth);
            SyncSlider(groundHeightSlider, Tuning.GroundHeight);
            Refresh();
        }

        internal static void RefreshIfOpen()
        {
            if (window == null || !window.IsAlive) return;
            Refresh();
        }

        /// <summary>
        /// 调试开关与滑条都常显，不按条件隐藏。原生 BuildEntry 给开关/滑条传的
        /// onChanged 是 null，勾选只改控件、要按「应用」才写回配置；这里自己绑
        /// 事件，让开关即时生效、滑条松手即写入。
        /// </summary>
        private static void HookDebugToggle(CustomUIWindow target, string key)
        {
            CustomUIElement element = target.Get(key);
            if (element == null || element.toggle == null)
            {
                Debug.LogWarning(LogTag + key + ": toggle widget missing");
                return;
            }
            element.OnToggle(delegate(bool value) { Tuning.SetDebugEnabledLive(value); });
            Debug.Log(LogTag + key + ": live listener bound");
        }

        /// <summary>
        /// 原生下拉框的 onChanged 也是 null，换档只改控件、按「应用」才写回配置。
        /// 这里自己绑 OnDropdown，换档立即写配置项并走同一条刷新链路
        /// （Tuning.Changed → ModLifecycle.RefreshViews → NativeSettings.RefreshIfOpen → Refresh），
        /// 与滑条拖动、预设按钮完全一致。
        /// </summary>
        private static void HookSpecialtyDropdown(CustomUIElement element, string key)
        {
            if (element == null || element.dropdown == null)
            {
                Debug.LogWarning(LogTag + key + ": dropdown widget missing, live EV refresh disabled");
                return;
            }
            LocalizeDropdown(element, Tuning.SpecialtyDisplayLabels);
            element.OnDropdown(delegate(int index)
            {
                Tuning.SetSpecialtyTierLive(index);
                Debug.Log(LogTag + "ev refresh tier=" + Tuning.SpecialtyTier
                    + " percent=" + Tuning.SpecialtyPercent.ToString("0.#", CultureInfo.InvariantCulture)
                    + " source=dropdown");
            });
            Debug.Log(LogTag + key + ": dropdown bound (options="
                + element.dropdown.options.Count + ")");
        }

        private static void HookGroundAlignDropdown(CustomUIElement element, string key)
        {
            if (element == null || element.dropdown == null)
            {
                Debug.LogWarning(LogTag + key + ": dropdown widget missing, alignment fixed at default");
                return;
            }
            LocalizeDropdown(element, Tuning.GroundAlignLabels);
            element.SetIndex(Tuning.GroundAlign, false);
            element.OnDropdown(delegate(int index)
            {
                Tuning.SetGroundAlignLive(index);
                Debug.Log(LogTag + "ground align=" + Tuning.GroundAlign + " source=dropdown");
            });
            Debug.Log(LogTag + key + ": dropdown bound (options="
                + element.dropdown.options.Count + ", current=" + Tuning.GroundAlign + ")");
        }

        private static void LocalizeDropdown(CustomUIElement element, string[] labels)
        {
            if (element == null || element.dropdown == null) return;
            // Keep the native choices and their indices for Apply/Reset/persistence.
            // Only replace what the player sees in TMP's option data.
            for (int i = 0; i < labels.Length && i < element.dropdown.options.Count; ++i)
                element.dropdown.options[i].text = labels[i];
            element.dropdown.RefreshShownValue();
        }

        private static CustomUIElement HookDebugSlider(CustomUIWindow target, string key)
        {
            CustomUIElement element = target.Get(key);
            if (element == null || element.slider == null)
            {
                Debug.LogWarning(LogTag + key + ": slider widget missing");
                return null;
            }
            element.OnSlider(delegate(float value)
            {
                float snapped = Mathf.Round(value * 2f) * 0.5f;
                if (Mathf.Abs(snapped - value) > 0.001f) element.SetFloat(snapped, false);
                if (debugValue != null)
                    debugValue.SetText(snapped.ToString("0.0", CultureInfo.InvariantCulture));
            });
            GameObject go = element.slider.gameObject;
            UnityEngine.EventSystems.EventTrigger trigger =
                go.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            if (trigger == null) trigger = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            if (trigger.triggers == null)
                trigger.triggers = new List<UnityEngine.EventSystems.EventTrigger.Entry>();
            UnityEngine.EventSystems.EventTrigger.Entry entry =
                new UnityEngine.EventSystems.EventTrigger.Entry();
            entry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp;
            entry.callback.AddListener(delegate(UnityEngine.EventSystems.BaseEventData data)
            {
                CommitDebugSlider();
            });
            trigger.triggers.Add(entry);
            Debug.Log(LogTag + key + ": slider bound (min="
                + element.slider.minValue.ToString("0.###", CultureInfo.InvariantCulture)
                + " max=" + element.slider.maxValue.ToString("0.###", CultureInfo.InvariantCulture)
                + " whole=" + element.slider.wholeNumbers + ")");
            return element;
        }

        /// <summary>松手写入：按 0.5 点吸附，由 Tuning 决定是否因开关关闭而忽略。</summary>
        private static void CommitDebugSlider()
        {
            if (debugSlider == null || debugSlider.slider == null) return;
            float value = debugSlider.slider.value;
            Tuning.SetDebugPointsFromSlider(value);
            // 松手后以真正写入的值为准：开关关闭时会被忽略，标签要退回原值。
            if (debugValue != null)
                debugValue.SetText(Tuning.DebugPoints.ToString("0.0", CultureInfo.InvariantCulture));
            Debug.Log(LogTag + "debug slider release raw="
                + value.ToString("0.###", CultureInfo.InvariantCulture)
                + " entry=" + Tuning.DebugPoints.ToString("0.##", CultureInfo.InvariantCulture)
                + " stored=" + Reputation.ExactPoints.ToString("0.##", CultureInfo.InvariantCulture)
                + " hasStore=" + Reputation.HasStore);
        }

        /// <summary>
        /// Writes any value still held in memory. Called before the native Apply runs
        /// and when the window is rebuilt or the mod is disabled, so a lost PointerUp
        /// can never leave Tuning stuck on an unsaved preview value.
        /// </summary>
        internal static void CommitDrag()
        {
            if (dragging)
            {
                EndDrag();
                return;
            }
            Tuning.CommitLive();
        }

        private static void Refresh()
        {
            if (window == null || !window.IsAlive) return;

            // Native ResetWidgets writes without notifications and leaves config
            // entries unchanged until Apply. Labels and EV must show the widgets.
            if (junkValue != null)
                junkValue.SetText(Mathf.RoundToInt(SliderValue(junkSlider, Tuning.JunkPercent)) + "%");
            if (highValue != null)
                highValue.SetText(SliderValue(highSlider, Tuning.HighPercent).ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (crateValue != null)
                crateValue.SetText(SliderValue(crateSlider, Tuning.CratePercent).ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (cardValue != null)
                cardValue.SetText(SliderValue(cardSlider, Tuning.KeycardPercent).ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (minorValue != null)
                minorValue.SetText(SliderValue(minorSlider, Tuning.MinorPercent).ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (minorCrateValue != null)
                minorCrateValue.SetText(SliderValue(minorCrateSlider, Tuning.MinorValue).ToString("0", CultureInfo.InvariantCulture));
            if (stashCapValue != null)
                stashCapValue.SetText(SliderValue(stashCapSlider, Tuning.StashCap).ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (foreignValue != null)
                foreignValue.SetText(SliderValue(foreignSlider, Tuning.ForeignPercent).ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (debugValue != null)
                debugValue.SetText(SliderValue(debugSlider, Tuning.DebugPoints).ToString("0.0", CultureInfo.InvariantCulture));
            if (groundWidthValue != null)
                groundWidthValue.SetText(SliderValue(groundWidthSlider, Tuning.GroundWidth).ToString("0", CultureInfo.InvariantCulture));
            if (groundHeightValue != null)
                groundHeightValue.SetText(SliderValue(groundHeightSlider, Tuning.GroundHeight).ToString("0", CultureInfo.InvariantCulture));
            if (evLabels == null) return;

            int tier = specialtyDropdown != null && specialtyDropdown.dropdown != null
                ? specialtyDropdown.dropdown.value : Tuning.SpecialtyTier;
            LootSettings preview = new LootSettings(
                SliderValue(junkSlider, Tuning.JunkPercent),
                SliderValue(highSlider, Tuning.HighPercent),
                (Mathf.Clamp(tier, 0, 3) + 1) * 25f,
                SliderValue(crateSlider, Tuning.CratePercent),
                SliderValue(cardSlider, Tuning.KeycardPercent),
                Tuning.ForeignEnabled ? SliderValue(foreignSlider, Tuning.ForeignPercent) : 0f);
            ForeignLootTable foreign = preview.ForeignPercent > 0f
                ? ForeignLoot.Snapshot(Tuning.ForeignVanilla) : ForeignLootTable.Empty;
            int currentTier = 0;
            for (int i = 1; i < ReputationExpectationPreview.Milestones.Length; ++i)
                if (Reputation.Points >= ReputationExpectationPreview.Milestones[i]) currentTier = i;
            for (int i = 0; i < evLabels.Length; ++i)
            {
                int points = ReputationExpectationPreview.Milestones[i];
                string name = string.Format(CultureInfo.InvariantCulture,
                    Copy.Get("nativesettings_reputation_0_1_area_average"),
                    points, ReputationExpectationPreview.OpenCount(points));
                if (i == currentTier) name += Copy.Get("nativesettings_current_tier");
                SetEvLine(evLabels[i], name, reputationPreview.Get(i, preview, foreign));
                evLabels[i].SetTextColor(i == currentTier ? Picker.Accent : Picker.Ink);
            }

        }

        private static float SliderValue(CustomUIElement element, float fallback)
        {
            return element != null && element.slider != null ? element.slider.value : fallback;
        }

        internal static void BeforeNativeReset(CustomUIWindow target, ModConfig config)
        {
            if (config == null || config.ModId != Core.Id || !ReferenceEquals(target, window)) return;
            CommitDrag();
        }

        internal static void AfterNativeReset(CustomUIWindow target, ModConfig config)
        {
            if (config == null || config.ModId != Core.Id || !ReferenceEquals(target, window)) return;
            LocalizeDropdown(specialtyDropdown, Tuning.SpecialtyDisplayLabels);
            LocalizeDropdown(groundAlignDropdown, Tuning.GroundAlignLabels);
            Refresh();
        }

        private static string ReferenceLine()
        {
            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = 0; i < Region.All.Length; ++i)
            {
                float value = Tuning.BaseEv(Region.All[i]);
                if (value < min) min = value;
                if (value > max) max = value;
            }
            // Every area is reverse-solved to the same target, so one compact figure
            // is enough; fall back to a range if rounding ever splits them.
            string valueText = (max - min) <= 0.011f
                ? Copy.Get("nativesettings_every_area") + min.ToString("0.00", CultureInfo.InvariantCulture)
                : min.ToString("0.00", CultureInfo.InvariantCulture) + " – "
                    + max.ToString("0.00", CultureInfo.InvariantCulture);
            return Copy.Get("nativesettings_vanilla_reference_100_junk_no_specialty") + valueText;
        }

        private static void SetEvLine(CustomUIElement label, string name, MainExpectation parts)
        {
            string total = parts.Total.ToString("0.00", CultureInfo.InvariantCulture);
            string components = Copy.Get("lootnoticeview_base") + parts.Base.ToString("0.00", CultureInfo.InvariantCulture)
                + Copy.Get("nativesettings_rare_uplift") + parts.Valuable.ToString("0.00", CultureInfo.InvariantCulture)
                + Copy.Get("lootnoticeview_crates_cards") + parts.Supply.ToString("0.00", CultureInfo.InvariantCulture);
            label.SetText(name + Copy.Get("nativesettings_total") + total
                + "\n<size=15>" + components + "</size>");
            label.SetTooltip(name + "\n" + components + " ≈ " + total
                + Copy.Get("nativesettings_credits_item_current_settings_equal_weight_average"));
        }

        private static void OnSlide(int kind, float value)
        {
            if (kind == 1)
            {
                BeginDrag(1, Tuning.JunkPercent);
                float snapped = Mathf.Round(value);
                SyncSlider(junkSlider, snapped);
                Tuning.SetJunkPercentLive(snapped);
                LogSlide(1, value, Tuning.JunkPercent);
            }
            else if (kind == 2)
            {
                BeginDrag(2, Tuning.HighPercent);
                float snapped = Mathf.Round(value * 2f) / 2f;
                SyncSlider(highSlider, snapped);
                Tuning.SetHighPercentLive(snapped);
                LogSlide(2, value, Tuning.HighPercent);
            }
            else if (kind == 3)
            {
                BeginDrag(3, Tuning.CratePercent);
                float snapped = Mathf.Round(value * 10f) / 10f;
                SyncSlider(crateSlider, snapped);
                Tuning.SetCratePercentLive(snapped);
                LogSlide(3, value, Tuning.CratePercent);
            }
            else if (kind == 4)
            {
                BeginDrag(4, Tuning.KeycardPercent);
                float snapped = Mathf.Round(value * 10f) / 10f;
                SyncSlider(cardSlider, snapped);
                Tuning.SetKeycardPercentLive(snapped);
                LogSlide(4, value, Tuning.KeycardPercent);
            }
            else if (kind == 5)
            {
                BeginDrag(5, Tuning.MinorPercent);
                float snapped = Mathf.Round(value * 10f) / 10f;
                SyncSlider(minorSlider, snapped);
                Tuning.SetMinorPercentLive(snapped);
                LogSlide(5, value, Tuning.MinorPercent);
            }
            else if (kind == 6)
            {
                BeginDrag(6, Tuning.MinorValue);
                float snapped = Mathf.Round(value / 10f) * 10f;
                SyncSlider(minorCrateSlider, snapped);
                Tuning.SetMinorValueLive(snapped);
                LogSlide(6, value, Tuning.MinorValue);
            }
            else if (kind == 7)
            {
                BeginDrag(7, Tuning.GroundWidth);
                float snapped = Mathf.Round(value);
                SyncSlider(groundWidthSlider, snapped);
                Tuning.SetGroundWidthLive((int)snapped);
                LogSlide(7, value, Tuning.GroundWidth);
            }
            else if (kind == 8)
            {
                BeginDrag(8, Tuning.GroundHeight);
                float snapped = Mathf.Round(value);
                SyncSlider(groundHeightSlider, snapped);
                Tuning.SetGroundHeightLive((int)snapped);
                LogSlide(8, value, Tuning.GroundHeight);
            }
            else if (kind == 9)
            {
                BeginDrag(9, Tuning.ForeignPercent);
                float snapped = Mathf.Round(value * 10f) / 10f;
                SyncSlider(foreignSlider, snapped);
                Tuning.SetForeignPercentLive(snapped);
                LogSlide(9, value, Tuning.ForeignPercent);
            }
        }

        private static void ApplyPreset(int preset)
        {
            EndDrag();
            Tuning.ApplyPreset(preset);
            // The native dropdown owns its own selection; writing the config entry
            // does not move it, so drive the widget explicitly.
            if (specialtyDropdown != null)
                specialtyDropdown.SetIndex(Tuning.SpecialtyTier, false);
            CustomUIElement cap = window == null ? null : window.Get(Tuning.StashCapKey);
            if (cap != null && cap.slider != null) cap.SetFloat(Tuning.StashCap, false);
            CustomUIElement adaptive = window == null ? null : window.Get(Tuning.StashWindowKey);
            if (adaptive != null && adaptive.toggle != null) adaptive.toggle.SetIsOnWithoutNotify(Tuning.StashWindowEnabled);
            CustomUIElement minorAdaptive = window == null ? null : window.Get(Tuning.MinorWindowKey);
            if (minorAdaptive != null && minorAdaptive.toggle != null) minorAdaptive.toggle.SetIsOnWithoutNotify(Tuning.MinorWindowEnabled);
            SyncWidgets();
            Refresh();
        }

        private static void SyncSlider(CustomUIElement element, float value)
        {
            if (element == null || element.slider == null) return;
            if (Mathf.Abs(element.slider.value - value) > 0.001f) element.SetFloat(value, false);
        }

        private static void SyncWidgets()
        {
            if (dragging) return;
            SyncSlider(junkSlider, Tuning.JunkPercent);
            SyncSlider(highSlider, Tuning.HighPercent);
            SyncSlider(crateSlider, Tuning.CratePercent);
            SyncSlider(cardSlider, Tuning.KeycardPercent);
            SyncSlider(minorSlider, Tuning.MinorPercent);
            SyncSlider(minorCrateSlider, Tuning.MinorValue);
            SyncSlider(groundWidthSlider, Tuning.GroundWidth);
            SyncSlider(groundHeightSlider, Tuning.GroundHeight);
            if (groundAlignDropdown != null)
                groundAlignDropdown.SetIndex(Tuning.GroundAlign, false);
        }

        private static void BeginDrag(int kind, float from)
        {
            float now = Time.realtimeSinceStartup;
            if (!dragging || kind != dragKind || now - lastSlideAt > 0.4f)
            {
                if (dragging) Tuning.CommitLive();
                dragging = true;
                dragKind = kind;
                slideCount = 0;
                lastLogAt = 0f;
                Debug.Log(LogTag + "drag begin kind=" + kind + " from="
                    + from.ToString("0.###", CultureInfo.InvariantCulture));
            }
            lastSlideAt = now;
        }

        private static void EndDrag()
        {
            if (!dragging) return;
            dragging = false;
            lastSlideAt = 0f;
            Tuning.CommitLive();
            Debug.Log(LogTag + "drag end kind=" + dragKind + " callbacks=" + slideCount
                + " saved=" + Num(Tuning.JunkPercent) + "/" + Num(Tuning.HighPercent));
            Refresh();
        }

        /// <summary>Throttled trail: one line every 0.25 s plus a callback counter.</summary>
        private static void LogSlide(int kind, float raw, float applied)
        {
            ++slideCount;
            float now = Time.realtimeSinceStartup;
            if (now - lastLogAt < 0.25f) return;
            lastLogAt = now;
            Debug.Log(LogTag + "kind=" + kind
                + " raw=" + raw.ToString("0.###", CultureInfo.InvariantCulture)
                + " applied=" + applied.ToString("0.###", CultureInfo.InvariantCulture)
                + " live=" + (Tuning.HasLive ? 1 : 0)
                + " callbacks=" + slideCount
                + " ev=" + Tuning.TableFor(Region.All[0]).Ev.ToString("0.####", CultureInfo.InvariantCulture));
        }

        private static string Num(float value)
        {
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        internal static void Reset()
        {
            Tuning.CommitLive();
            if (dragging)
            {
                dragging = false;
                lastSlideAt = 0f;
            }
            window = null;
            builtFor = null;
            junkValue = null;
            highValue = null;
            crateValue = null;
            cardValue = null;
            minorValue = null;
            minorCrateValue = null;
            stashCapValue = null;
            stashCapSlider = null;
            foreignValue = null;
            foreignSlider = null;
            debugValue = null;
            groundWidthValue = null;
            groundHeightValue = null;
            specialtyDropdown = null;
            groundAlignDropdown = null;
            evLabels = null;
            junkSlider = null;
            highSlider = null;
            crateSlider = null;
            cardSlider = null;
            minorSlider = null;
            minorCrateSlider = null;
            debugSlider = null;
            groundWidthSlider = null;
            groundHeightSlider = null;
            building = null;
            buildingKey = null;
            dragging = false;
            dragKind = 0;
            lastSlideAt = 0f;
            slideCount = 0;
            lastLogAt = 0f;
        }
    }
}
