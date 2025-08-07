using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Interface;
using Dalamud.Bindings.ImGui;
using NoSoliciting.Ml;
using NoSoliciting.Resources;

namespace NoSoliciting.Interface {
    public class Settings : IDisposable {
        private Plugin Plugin { get; }
        private PluginUi Ui { get; }

        private bool _showSettings;

        private bool ShowSettings {
            get => this._showSettings;
            set => this._showSettings = value;
        }

        public Settings(Plugin plugin, PluginUi ui) {
            this.Plugin = plugin;
            this.Ui = ui;

            this.Plugin.Interface.UiBuilder.OpenConfigUi += this.Open;
        }

        public void Dispose() {
            this.Plugin.Interface.UiBuilder.OpenConfigUi -= this.Open;
        }

        private void Open() {
            this.ShowSettings = true;
        }

        public void Toggle() {
            this.ShowSettings = !this.ShowSettings;
        }

        public void Show() {
            this.ShowSettings = true;
        }

        public void Draw() {
            if (!this.ShowSettings) {
                return;
            }

            var windowTitle = string.Format(Language.Settings, Plugin.Name);
            if (!ImGui.Begin($"{windowTitle}###NoSoliciting settings", ref this._showSettings)) {
                return;
            }

            var advanced = this.Plugin.Config.AdvancedMode;
            if (ImGui.Checkbox(Language.AdvancedMode, ref advanced)) {
                this.Plugin.Config.AdvancedMode = advanced;
                this.Plugin.Config.Save();
            }

            ImGui.Spacing();

            if (!ImGui.BeginTabBar("##nosoliciting-tabs")) {
                return;
            }

            this.DrawMachineLearningConfig();

            this.DrawOtherFilters();

            this.DrawOtherTab();

            ImGui.EndTabBar();

            ImGui.Separator();

            if (ImGui.Button(Language.ShowReportingWindow)) {
                this.Ui.Report.Open();
            }

            ImGui.End();
        }

        #region ML config

        private void DrawMachineLearningConfig() {
            if (this.Plugin.Config.AdvancedMode) {
                this.DrawAdvancedMachineLearningConfig();
            } else {
                this.DrawBasicMachineLearningConfig();
            }

            if (!ImGui.BeginTabItem($"{Language.ModelTab}###model-tab")) {
                return;
            }

            ImGui.TextUnformatted(string.Format(Language.ModelTabVersion, this.Plugin.MlFilter?.Version));
            ImGui.TextUnformatted(string.Format(Language.ModelTabStatus, this.Plugin.MlStatus.Description()));
            var lastError = MlFilter.LastError;
            if (lastError != null) {
                ImGui.TextUnformatted(string.Format(Language.ModelTabError, lastError));
            }

            if (ImGui.Button(Language.UpdateModel)) {
                // prevent issues when people spam the button
                if (ImGui.GetIO().KeyCtrl || this.Plugin.MlStatus is MlFilterStatus.Uninitialised or MlFilterStatus.Initialised) {
                    this.Plugin.MlFilter?.Dispose();
                    this.Plugin.MlFilter = null;
                    this.Plugin.MlStatus = MlFilterStatus.Uninitialised;
                    this.Plugin.InitialiseMachineLearning(ImGui.GetIO().KeyAlt);
                }
            }

            ImGui.EndTabItem();
        }

        private void DrawBasicMachineLearningConfig() {
            if (!ImGui.BeginTabItem($"{Language.FiltersTab}###filters-tab")) {
                return;
            }

            foreach (var category in MessageCategoryExt.UiOrder) {
                var check = this.Plugin.Config.BasicMlFilters.Contains(category);
                if (ImGui.Checkbox(category.Name(), ref check)) {
                    if (check) {
                        this.Plugin.Config.BasicMlFilters.Add(category);
                    } else {
                        this.Plugin.Config.BasicMlFilters.Remove(category);
                    }

                    this.Plugin.Config.Save();
                }

                if (!ImGui.IsItemHovered()) {
                    continue;
                }

                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(ImGui.GetFontSize() * 24);
                ImGui.TextUnformatted(category.Description());
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            ImGui.EndTabItem();
        }

        private void DrawAdvancedMachineLearningConfig() {
            if (!ImGui.BeginTabItem($"{Language.FiltersTab}###filters-tab")) {
                return;
            }

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(255f, 204f, 0f, 1f));
            ImGui.TextUnformatted(Language.AdvancedWarning1);
            ImGui.TextUnformatted(Language.AdvancedWarning2);
            ImGui.PopStyleColor();

            foreach (var category in MessageCategoryExt.UiOrder) {
                if (!ImGui.CollapsingHeader(category.Name())) {
                    continue;
                }

                if (!this.Plugin.Config.MlFilters.ContainsKey(category)) {
                    this.Plugin.Config.MlFilters[category] = new HashSet<ChatType>();
                }

                var types = this.Plugin.Config.MlFilters[category];

                void DrawTypes(ChatType type, string id) {
                    var name = type.Name(this.Plugin.DataManager);

                    var check = types.Contains(type);
                    if (!ImGui.Checkbox($"{name}##{id}", ref check)) {
                        return;
                    }

                    if (check) {
                        types.Add(type);
                    } else {
                        types.Remove(type);
                    }

                    this.Plugin.Config.Save();
                }

                DrawTypes(ChatType.None, category.ToString());

                foreach (var type in Filter.FilteredChatTypes) {
                    DrawTypes(type, category.ToString());
                }
            }

            ImGui.EndTabItem();
        }

        #endregion

        #region Other config

        internal bool ShowOtherFilters;

        private static unsafe bool BeginTabItem(string label, ImGuiTabItemFlags flags) {
            var unterminatedLabelBytes = Encoding.UTF8.GetBytes(label);
            var labelBytes = stackalloc byte[unterminatedLabelBytes.Length + 1];
            fixed (byte* unterminatedPtr = unterminatedLabelBytes) {
                Buffer.MemoryCopy(unterminatedPtr, labelBytes, unterminatedLabelBytes.Length + 1, unterminatedLabelBytes.Length);
            }

            labelBytes[unterminatedLabelBytes.Length] = 0;

            var num2 = (int) ImGuiNative.BeginTabItem(labelBytes, null, flags);
            return (uint) num2 > 0U;
        }

        private void DrawOtherFilters() {
            var flags = this.ShowOtherFilters ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            this.ShowOtherFilters = false;

            if (!BeginTabItem($"{Language.OtherFiltersTab}###other-filters-tab", flags)) {
                return;
            }

            if (ImGui.CollapsingHeader(Language.ChatFilters)) {
                var customChat = this.Plugin.Config.CustomChatFilter;
                if (ImGui.Checkbox(Language.EnableCustomChatFilters, ref customChat)) {
                    this.Plugin.Config.CustomChatFilter = customChat;
                    this.Plugin.Config.Save();
                }

                if (this.Plugin.Config.CustomChatFilter) {
                    var substrings = this.Plugin.Config.ChatSubstrings;
                    var regexes = this.Plugin.Config.ChatRegexes;
                    this.DrawCustom("chat", ref substrings, ref regexes);
                }
            }

            if (ImGui.CollapsingHeader(Language.PartyFinderFilters)) {
                var filterHugeItemLevelPFs = this.Plugin.Config.FilterHugeItemLevelPFs;
                // ReSharper disable once InvertIf
                if (ImGui.Checkbox(Language.FilterIlvlPfs, ref filterHugeItemLevelPFs)) {
                    this.Plugin.Config.FilterHugeItemLevelPFs = filterHugeItemLevelPFs;
                    this.Plugin.Config.Save();
                }

                var considerPrivate = this.Plugin.Config.ConsiderPrivatePfs;
                if (ImGui.Checkbox(Language.FilterPrivatePfs, ref considerPrivate)) {
                    this.Plugin.Config.ConsiderPrivatePfs = considerPrivate;
                    this.Plugin.Config.Save();
                }

                var customPf = this.Plugin.Config.CustomPFFilter;
                if (ImGui.Checkbox(Language.EnableCustomPartyFinderFilters, ref customPf)) {
                    this.Plugin.Config.CustomPFFilter = customPf;
                    this.Plugin.Config.Save();
                }

                if (this.Plugin.Config.CustomPFFilter) {
                    var substrings = this.Plugin.Config.PFSubstrings;
                    var regexes = this.Plugin.Config.PFRegexes;
                    this.DrawCustom("pf", ref substrings, ref regexes);
                }
            }

            ImGui.EndTabItem();
        }

        private void DrawCustom(string name, ref List<string> substrings, ref List<string> regexes) {
            ImGui.Columns(2);

            ImGui.TextUnformatted(Language.SubstringsToFilter);
            if (ImGui.BeginChild($"##{name}-substrings", new Vector2(0, 175))) {
                for (var i = 0; i < substrings.Count; i++) {
                    var input = substrings[i];
                    if (ImGui.InputText($"##{name}-substring-{i}", ref input, 1_000)) {
                        substrings[i] = input;
                    }

                    ImGui.SameLine();
                    ImGui.PushFont(UiBuilder.IconFont);
                    if (ImGui.Button($"{FontAwesomeIcon.Trash.ToIconString()}##{name}-substring-{i}-remove")) {
                        substrings.RemoveAt(i);
                    }

                    ImGui.PopFont();
                }

                ImGui.PushFont(UiBuilder.IconFont);
                if (ImGui.Button($"{FontAwesomeIcon.Plus.ToIconString()}##{name}-substring-add")) {
                    substrings.Add("");
                }

                ImGui.PopFont();

                ImGui.EndChild();
            }

            ImGui.NextColumn();

            ImGui.TextUnformatted(Language.RegularExpressionsToFilter);
            if (ImGui.BeginChild($"##{name}-regexes", new Vector2(0, 175))) {
                for (var i = 0; i < regexes.Count; i++) {
                    var input = regexes[i];
                    if (ImGui.InputText($"##{name}-regex-{i}", ref input, 1_000)) {
                        try {
                            _ = new Regex(input);
                            // update if valid
                            regexes[i] = input;
                        } catch (ArgumentException) {
                            // ignore
                        }
                    }

                    ImGui.SameLine();
                    ImGui.PushFont(UiBuilder.IconFont);
                    if (ImGui.Button($"{FontAwesomeIcon.Trash.ToIconString()}##{name}-regex-{i}-remove")) {
                        regexes.RemoveAt(i);
                    }

                    ImGui.PopFont();
                }

                ImGui.PushFont(UiBuilder.IconFont);
                if (ImGui.Button($"{FontAwesomeIcon.Plus.ToIconString()}##{name}-regex-add")) {
                    regexes.Add("");
                }

                ImGui.PopFont();

                ImGui.EndChild();
            }

            ImGui.Columns(1);

            // ReSharper disable once InvertIf
            var saveLoc = Language.SaveFilters;
            if (ImGui.Button($"{saveLoc}##{name}-save")) {
                this.Plugin.Config.Save();
                this.Plugin.Config.CompileRegexes();
            }
        }

        #endregion

        private void DrawOtherTab() {
            if (!ImGui.BeginTabItem($"{Language.OtherTab}###other-tab")) {
                return;
            }

            var useGameLanguage = this.Plugin.Config.FollowGameLanguage;
            if (ImGui.Checkbox(Language.OtherGameLanguage, ref useGameLanguage)) {
                this.Plugin.Config.FollowGameLanguage = useGameLanguage;
                this.Plugin.Config.Save();
                this.Plugin.ConfigureLanguage();
            }

            var logFilteredPfs = this.Plugin.Config.LogFilteredPfs;
            if (ImGui.Checkbox(Language.LogFilteredPfs, ref logFilteredPfs)) {
                this.Plugin.Config.LogFilteredPfs = logFilteredPfs;
                this.Plugin.Config.Save();
            }

            var logFilteredMessages = this.Plugin.Config.LogFilteredChat;
            if (ImGui.Checkbox(Language.LogFilteredMessages, ref logFilteredMessages)) {
                this.Plugin.Config.LogFilteredChat = logFilteredMessages;
                this.Plugin.Config.Save();
            }

            ImGui.EndTabItem();
        }
    }
}
