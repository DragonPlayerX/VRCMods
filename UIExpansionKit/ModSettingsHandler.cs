using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MelonLoader;
using UIExpansionKit.API;
using UIExpansionKit.Components;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UIExpansionKit
{
    public static class ModSettingsHandler
    {
        private static PreloadedBundleContents ourStuffBundle;
        private static readonly Dictionary<string, bool> ourCategoryExpanded = new Dictionary<string, bool>(); 

        public static void Initialize(PreloadedBundleContents stuffBundle)
        {
            ourStuffBundle = stuffBundle;
        }

        private static void AttachPinToggle(Transform controlRoot, string categoryId, string prefId, List<(string, string)> pinnedSettings)
        {
            var pinToggle = controlRoot.transform.Find("PinToggle").GetComponent<Toggle>();
            pinToggle.isOn = pinnedSettings.Contains((categoryId, prefId));
            pinToggle.onValueChanged.AddListener(new Action<bool>(isSet =>
            {
                if (isSet) 
                    ExpansionKitSettings.PinPref(categoryId, prefId);
                else
                    ExpansionKitSettings.UnpinPref(categoryId, prefId);
            }));
            ExpansionKitSettings.PinsEntry.OnValueChanged += (_, __) =>
            {
                pinToggle.isOn = ExpansionKitSettings.IsPinned(categoryId, prefId);
            };
        }

        private static void ResetValue(MelonPreferences_Entry entry)
        {
            var innerType = entry.GetReflectedType();
            typeof(ModSettingsHandler).GetMethod(nameof(ResetValueGeneric), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(innerType)
                .Invoke(null, new object[] { entry });
        }
        private static void ResetValueGeneric<T>(MelonPreferences_Entry<T> entry) => entry.Value = entry.DefaultValue;

        public static IEnumerator PopulateSettingsPanel(RectTransform settingsContentRoot)
        {
            yield return null;
            yield return null;
            yield return null;
            
            var categoryPrefab = ourStuffBundle.SettingsCategory;
            var boolPrefab = ourStuffBundle.SettingsBool;
            var textPrefab = ourStuffBundle.SettingsText;
            var comboBoxPrefab = ourStuffBundle.SettingsComboBox;

            settingsContentRoot.DestroyChildren();

            var pinnedSettings = ExpansionKitSettings.ListPinnedPrefs().ToList();
            
            foreach (var category in MelonPreferences.Categories.OrderBy(it => it.DisplayName ?? it.Identifier, StringComparer.InvariantCultureIgnoreCase))
            {
                var categoryId = category.Identifier;
                var prefDict = category.Entries;

                if (ExpansionKitApi.CustomCategoryUIs.TryGetValue(categoryId, out var specificPrefab))
                {
                    Object.Instantiate(specificPrefab, settingsContentRoot, false);
                    continue;
                }

                ExpansionKitApi.SettingPageExtensions.TryGetValue(categoryId, out var customEntries);

                var prefsToPopulate = prefDict.Where(it => !it.IsHidden).ToList();
                
                if (prefsToPopulate.Count == 0 && (customEntries?.RegisteredButtons.Count ?? 0) == 0)
                    continue;

                var categoryUi = Object.Instantiate(categoryPrefab, settingsContentRoot, false);
                categoryUi.GetComponentInChildren<Text>().text = category.DisplayName ?? categoryId;
                var categoryUiContent = categoryUi.transform.Find("CategoryEntries");
                var expandButtonTransform = categoryUi.transform.Find("ExpandButton");
                var expandButton = expandButtonTransform.GetComponent<Button>();
                var expandButtonText = expandButtonTransform.GetComponentInChildren<Text>();

                void SetExpanded(bool expanded)
                {
                    expandButtonText.text = expanded ? "^" : "V";
                    categoryUiContent.gameObject.SetActive(expanded);
                }
                
                expandButton.onClick.AddListener(new Action(() =>
                {
                    SetExpanded(ourCategoryExpanded[categoryId] = !ourCategoryExpanded[categoryId]);
                }));

                if (!ourCategoryExpanded.ContainsKey(categoryId))
                    ourCategoryExpanded[categoryId] = !ExpansionKitSettings.IsCategoriesStartCollapsed();
                
                SetExpanded(ourCategoryExpanded[categoryId]);

                var resetButton = categoryUi.transform.Find("ResetButton").GetComponent<Button>();
                resetButton.onClick.AddListener(new Action(() =>
                {
                    var clicksLeft = 3;
                    Text resetButtonText = null;
                    var menu = ExpansionKitApi.CreateCustomFullMenuPopup(LayoutDescription.WideSlimList);
                    var resetInvisible = false;

                    menu.AddLabel("Are you sure you want to reset all settings in the following category:");
                    menu.AddLabel(category.DisplayName ?? categoryId);
                    
                    menu.AddSimpleButton("Reset (3 clicks more...)", () =>
                    {
                        clicksLeft--;
                        switch (clicksLeft)
                        {
                            case 0:
                            {
                                foreach (var melonPreferencesEntry in category.Entries)
                                {
                                    if (melonPreferencesEntry.IsHidden && !resetInvisible) continue;
                                    
                                    ResetValue(melonPreferencesEntry);
                                }
                                
                                menu.Hide();
                                
                                MelonPreferences.Save();

                                break;
                            }
                            case 1:
                                resetButtonText!.text = "Reset (1 click left...)";
                                break;
                            case 2:
                                resetButtonText!.text = "Reset (2 clicks left...)";
                                break;
                        }
                    }, go => resetButtonText = go.GetComponentInChildren<Text>(true));
                    
                    menu.AddToggleButton("Also reset invisible settings", b => resetInvisible = b, () => resetInvisible);
                    
                    menu.AddSimpleButton("Cancel", menu.Hide);
                    
                    menu.Show(true);
                }));

                void CreateNumericSetting<T>(MelonPreferences_Entry<T> entry, Func<T, string> toString, Func<string, T?> fromString) where T:struct, IEquatable<T>
                {
                    var textSetting = Object.Instantiate(textPrefab, categoryUiContent, false);
                    textSetting.GetComponentInChildren<Text>().text = entry.DisplayName ?? entry.Identifier;
                    var textField = textSetting.GetComponentInChildren<InputField>();
                    textField.text = toString(entry.Value);
                    textField.contentType = typeof(T) == typeof(float) || typeof(T) == typeof(double)
                        ? InputField.ContentType.DecimalNumber
                        : InputField.ContentType.IntegerNumber;
                    textField.onValueChanged.AddListener(new Action<string>(value =>
                    {
                        var parsed = fromString(value);
                        if (parsed != null && !entry.Value.Equals(parsed.Value)) 
                            entry.Value = parsed.Value;
                    }));
                    textSetting.GetComponentInChildren<Button>().onClick.AddListener(new Action(() =>
                    {
                        BuiltinUiUtils.ShowInputPopup(entry.DisplayName ?? entry.Identifier, textField.text,
                            InputField.InputType.Standard, false, "Done", 
                            (result, _, __) =>
                            {
                                var parsed = fromString(result);
                                if (parsed != null)
                                {
                                    textField.text = result;
                                    if (!entry.Value.Equals(parsed.Value)) 
                                        entry.Value = parsed.Value;
                                }
                            });
                    }));
                    entry.OnValueChanged += (_, newValue) =>
                    {
                        UiExpansionKitMod.AreSettingsDirty = true;

                        textField.text = toString(newValue);
                    };
                }
                
                
                
                foreach (var pref in prefsToPopulate)
                {
                    var prefId = pref.Identifier;

                    switch (pref)
                    {
                        case MelonPreferences_Entry<string> stringPref:
                        {
                            if (ExpansionKitApi.EnumSettings.TryGetValue((categoryId, prefId), out var enumValues))
                            {
                                var comboSetting = Object.Instantiate(comboBoxPrefab, categoryUiContent, false);
                                comboSetting.GetComponentInChildren<Text>().text = pref.DisplayName ?? prefId;
                                var dropdown = comboSetting.GetComponentInChildren<Dropdown>();

                                void RefreshOptions()
                                {
                                    var options = new Il2CppSystem.Collections.Generic.List<Dropdown.OptionData>();
                                    var currentValue = stringPref.Value;
                                    var selectedIndex = enumValues.Count;
                                    for (var i = 0; i < enumValues.Count; i++)
                                    {
                                        var valueTuple = enumValues[i];
                                        options.Add(new Dropdown.OptionData(valueTuple.DisplayName));
                                        if (currentValue == valueTuple.SettingsValue)
                                            selectedIndex = i;
                                    }
                                    if (enumValues.All(it => it.SettingsValue != currentValue)) 
                                        options.Add(new Dropdown.OptionData(currentValue));
                                    dropdown.options = options;
                                    dropdown.value = selectedIndex;
                                }
                                dropdown.gameObject.GetOrAddComponent<EnableDisableListener>().OnEnabled += RefreshOptions;
                                RefreshOptions();
                                
                                dropdown.onValueChanged.AddListener(new Action<int>(value =>
                                {
                                    var currentValue = stringPref.Value;
                                    var newValue = value >= enumValues.Count
                                        ? currentValue
                                        : enumValues[value].SettingsValue;
                                    if (stringPref.Value != newValue)
                                        stringPref.Value = newValue;
                                }));
                                stringPref.OnValueChanged += (old, newValue) =>
                                {
                                    UiExpansionKitMod.AreSettingsDirty = true;
                                    
                                    int newIndex = -1;
                                    for (var i = 0; i < enumValues.Count; i++)
                                    {
                                        if (enumValues[i].SettingsValue == newValue)
                                        {
                                            newIndex = i;
                                            break;
                                        }
                                    }

                                    if (newIndex != -1)
                                        dropdown.value = newIndex;
                                };
                                
                                AttachPinToggle(comboSetting.transform, categoryId, prefId, pinnedSettings);
                            }
                            else
                            {
                                var textSetting = Object.Instantiate(textPrefab, categoryUiContent, false);
                                textSetting.GetComponentInChildren<Text>().text = pref.DisplayName ?? prefId;
                                var textField = textSetting.GetComponentInChildren<InputField>();
                                textField.text = stringPref.Value;
                                textField.onValueChanged.AddListener(new Action<string>(value =>
                                {
                                    if (stringPref.Value != value)
                                        stringPref.Value = value;
                                }));
                                textSetting.GetComponentInChildren<Button>().onClick.AddListener(new Action(() =>
                                {
                                    BuiltinUiUtils.ShowInputPopup(pref.DisplayName ?? prefId, textField.text,
                                        InputField.InputType.Standard, false, "Done",
                                        (result, _, __) =>
                                        {
                                            textField.text = result;
                                            if (stringPref.Value != result)
                                                stringPref.Value = result;
                                        });
                                }));
                                stringPref.OnValueChanged += (_, newValue) =>
                                {
                                    UiExpansionKitMod.AreSettingsDirty = true;
                                    textField.text = newValue;
                                };
                            }

                            break;
                        }
                        case MelonPreferences_Entry<bool> boolEntry:
                            var boolSetting = Object.Instantiate(boolPrefab, categoryUiContent, false);
                            boolSetting.GetComponentInChildren<Text>().text = pref.DisplayName ?? prefId;
                            var mainToggle = boolSetting.transform.Find("Toggle").GetComponent<Toggle>();
                            mainToggle.isOn = boolEntry.Value;
                            mainToggle.onValueChanged.AddListener(new Action<bool>(
                                isSet =>
                                {
                                    if (boolEntry.Value != isSet)
                                        boolEntry.Value = isSet;
                                }));
                            AttachPinToggle(boolSetting.transform, categoryId, prefId, pinnedSettings);
                            boolEntry.OnValueChanged += (old, newValue) =>
                            {
                                UiExpansionKitMod.AreSettingsDirty = true;
                                
                                mainToggle.isOn = newValue;
                            };
                            break;
                        case MelonPreferences_Entry<float> floatEntry:
                            CreateNumericSetting(floatEntry, f => f.ToString(CultureInfo.InvariantCulture),
                                s => float.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var f) ? f : null);
                            break;
                        case MelonPreferences_Entry<double> floatEntry:
                            CreateNumericSetting(floatEntry, f => f.ToString(CultureInfo.InvariantCulture),
                                s => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var f) ? f : null);
                            break;
                        case MelonPreferences_Entry<byte> floatEntry:
                            CreateNumericSetting(floatEntry, f => f.ToString(CultureInfo.InvariantCulture),
                                s => byte.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var f) ? f : null);
                            break;
                        case MelonPreferences_Entry<short> floatEntry:
                            CreateNumericSetting(floatEntry, f => f.ToString(CultureInfo.InvariantCulture),
                                s => short.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var f) ? f : null);
                            break;
                        case MelonPreferences_Entry<int> floatEntry:
                            CreateNumericSetting(floatEntry, f => f.ToString(CultureInfo.InvariantCulture),
                                s => int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var f) ? f : null);
                            break;
                        case MelonPreferences_Entry<long> floatEntry:
                            CreateNumericSetting(floatEntry, f => f.ToString(CultureInfo.InvariantCulture),
                                s => long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var f) ? f : null);
                            break;
                        default:
                            var entryType = pref.GetReflectedType();
                            if (entryType.IsEnum)
                            {
                                var settingTransform = (Transform) typeof(ModSettingsHandler)
                                    .GetMethod(nameof(CreateEnumSetting), BindingFlags.Static | BindingFlags.NonPublic)
                                    .MakeGenericMethod(entryType)
                                    .Invoke(null, new object[] { pref, comboBoxPrefab, categoryUiContent});
                                AttachPinToggle(settingTransform, categoryId, prefId, pinnedSettings);
                                break;
                            }
                            if (MelonDebug.IsEnabled())
                                MelonLogger.Msg($"Unknown mod pref type {pref.GetType()}");
                            break;
                    }
                }
                
                customEntries?.PopulateButtons(categoryUiContent, false, false);
            }

            UiExpansionKitMod.SetLayerRecursively(settingsContentRoot.gameObject, 12);
        }

        private static Transform CreateEnumSetting<T>(MelonPreferences_Entry<T> entry, GameObject comboBoxPrefab, Transform categoryUiContent) where T : Enum
        {
            var enumValues = EnumPrefUtil.GetEnumSettingOptions<T>();
            
            var comboSetting = Object.Instantiate(comboBoxPrefab, categoryUiContent, false);
            comboSetting.GetComponentInChildren<Text>().text = entry.DisplayName ?? entry.Identifier;
            var dropdown = comboSetting.GetComponentInChildren<Dropdown>();
            
            var options = new Il2CppSystem.Collections.Generic.List<Dropdown.OptionData>();
            var currentValue = entry.Value;
            var selectedIndex = enumValues.Count;
            for (var i = 0; i < enumValues.Count; i++)
            {
                var valueTuple = enumValues[i];
                options.Add(new Dropdown.OptionData(valueTuple.DisplayName));
                if (currentValue.CompareTo(valueTuple.SettingsValue) == 0)
                    selectedIndex = i;
            }
            
            dropdown.options = options;
            dropdown.value = selectedIndex;

            dropdown.onValueChanged.AddListener(new Action<int>(value =>
            {
                var currentValue = entry.Value;
                var newValue = value >= enumValues.Count
                    ? currentValue
                    : enumValues[value].SettingsValue;
                if (entry.Value.CompareTo(newValue) != 0)
                    entry.Value = newValue;
            }));
            entry.OnValueChanged += (_, newValue) =>
            {
                UiExpansionKitMod.AreSettingsDirty = true;

                int newIndex = -1;
                for (var i = 0; i < enumValues.Count; i++)
                {
                    if (enumValues[i].SettingsValue.CompareTo(newValue) == 0)
                    {
                        newIndex = i;
                        break;
                    }
                }

                if (newIndex != -1)
                    dropdown.value = newIndex;
            };

            return comboSetting.transform;
        }
    }
}