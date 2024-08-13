﻿using AutoDuty.Helpers;
using AutoDuty.IPC;
using AutoDuty.Managers;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.ImGuiMethods;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static AutoDuty.AutoDuty;

namespace AutoDuty.Windows
{
    using Dalamud.Interface.Components;
    using ECommons.ExcelServices;
    using ECommons.GameFunctions;

    internal static class MainTab
    {
        private static int _currentStepIndex = -1;
        private static ContentPathsManager.ContentPathContainer? _dutySelected;
        private static readonly string _pathsURL = "https://github.com/ffxivcode/DalamudPlugins/tree/main/AutoDuty/Paths";

        internal static void Draw()
        {
            if (MainWindow.CurrentTabName != "Main")
                MainWindow.CurrentTabName = "Main";
            var _support = Plugin.Configuration.Support;
            var _trust = Plugin.Configuration.Trust;
            var _squadron = Plugin.Configuration.Squadron;
            var _regular = Plugin.Configuration.Regular;
            var _trial = Plugin.Configuration.Trial;
            var _raid = Plugin.Configuration.Raid;
            var _variant = Plugin.Configuration.Variant;

            void DrawPathSelection()
            {
                if (Plugin.CurrentTerritoryContent == null || !ObjectHelper.IsReady)
                    return;

                using var d = ImRaii.Disabled(Plugin is { InDungeon: true, Stage: > 0 });

                if (ContentPathsManager.DictionaryPaths.TryGetValue(Plugin.CurrentTerritoryContent?.TerritoryType ?? 0, out var container))
                {
                    List<ContentPathsManager.DutyPath> curPaths = container.Paths;
                    if (curPaths.Count > 1)
                    {
                        int curPath = Math.Clamp(Plugin.CurrentPath, 0, curPaths.Count - 1);
                        ImGui.PushItemWidth(240 * ImGuiHelpers.GlobalScale);
                        if (ImGui.Combo("##SelectedPath", ref curPath, [.. curPaths.Select(dp => dp.Name)], curPaths.Count))
                        {
                            if (!Plugin.Configuration.PathSelections.ContainsKey(Plugin.CurrentTerritoryContent.TerritoryType))
                                Plugin.Configuration.PathSelections.Add(Plugin.CurrentTerritoryContent.TerritoryType, []);

                            Plugin.Configuration.PathSelections[Plugin.CurrentTerritoryContent.TerritoryType][Svc.ClientState.LocalPlayer.GetJob()] = curPath;
                            Plugin.Configuration.Save();
                            Plugin.CurrentPath = curPath;
                            Plugin.LoadPath();
                        }
                        ImGui.PopItemWidth();
                        ImGui.SameLine();

                        using var d2 = ImRaii.Disabled(!Plugin.Configuration.PathSelections.ContainsKey(Plugin.CurrentTerritoryContent.TerritoryType) ||
                                                       !Plugin.Configuration.PathSelections[Plugin.CurrentTerritoryContent.TerritoryType].ContainsKey(Svc.ClientState.LocalPlayer.GetJob()));
                        if (ImGui.Button("Clear Saved Path"))
                        {
                            Plugin.Configuration.PathSelections[Plugin.CurrentTerritoryContent.TerritoryType].Remove(Svc.ClientState.LocalPlayer.GetJob());
                            Plugin.Configuration.Save();
                            if (!Plugin.InDungeon)
                                container.SelectPath(out Plugin.CurrentPath);
                        }
                    }
                }
            }

            if (Plugin.InDungeon && Plugin.CurrentTerritoryContent != null)
            {
                var progress = VNavmesh_IPCSubscriber.IsEnabled ? VNavmesh_IPCSubscriber.Nav_BuildProgress() : 0;
                if (progress >= 0)
                {
                    ImGui.Text($"{Plugin.CurrentTerritoryContent.DisplayName} Mesh: Loading: ");
                    ImGui.ProgressBar(progress, new(200, 0));
                }
                else
                    ImGui.Text($"{Plugin.CurrentTerritoryContent.DisplayName} Mesh: Loaded Path: {(ContentPathsManager.DictionaryPaths.ContainsKey(Plugin.CurrentTerritoryContent.TerritoryType) ? "Loaded" : "None")}");

                ImGui.Separator();
                ImGui.Spacing();

                DrawPathSelection();
                if (!Plugin.Running && !Plugin.Overlay.IsOpen)
                    MainWindow.GotoAndActions();
                using (var d = ImRaii.Disabled(!VNavmesh_IPCSubscriber.IsEnabled || !Plugin.InDungeon || !VNavmesh_IPCSubscriber.Nav_IsReady() || !BossMod_IPCSubscriber.IsEnabled))
                {
                    using (var d1 = ImRaii.Disabled(!Plugin.InDungeon || !ContentPathsManager.DictionaryPaths.ContainsKey(Plugin.CurrentTerritoryContent.TerritoryType) || Plugin.Stage > 0))
                    {
                        if (ImGui.Button("Start"))
                        {
                            Plugin.LoadPath();
                            _currentStepIndex = -1;
                            if (Plugin.MainListClicked)
                                Plugin.StartNavigation(!Plugin.MainListClicked);
                            else
                                Plugin.Run(Svc.ClientState.TerritoryType);
                        }
                        ImGui.SameLine(0, 15);
                    }
                    ImGui.PushItemWidth(150 * ImGuiHelpers.GlobalScale);
                    if (Plugin.Configuration.UseSliderInputs)
                    {
                        if (ImGui.SliderInt("Times", ref Plugin.Configuration.LoopTimes, 0, 100))
                            Plugin.Configuration.Save();
                    }
                    else
                    {
                        if (ImGui.InputInt("Times", ref Plugin.Configuration.LoopTimes))
                            Plugin.Configuration.Save();
                    }
                    ImGui.PopItemWidth();
                    ImGui.SameLine(0, 5);
                    using (var d2 = ImRaii.Disabled(!Plugin.InDungeon || Plugin.Stage == 0))
                    {
                        MainWindow.StopResumePause();
                        if (Plugin.Started)
                        {
                            //ImGui.SameLine(0, 5);
                            ImGui.TextColored(new Vector4(0, 255f, 0, 1), $"{Plugin.Action}");
                        }
                    }
                    if (!ImGui.BeginListBox("##MainList", new Vector2(355 * ImGuiHelpers.GlobalScale, 425 * ImGuiHelpers.GlobalScale))) return;

                    if ((VNavmesh_IPCSubscriber.IsEnabled || Plugin.Configuration.UsingAlternativeMovementPlugin) && (BossMod_IPCSubscriber.IsEnabled || Plugin.Configuration.UsingAlternativeBossPlugin) && (ReflectionHelper.RotationSolver_Reflection.RotationSolverEnabled || Plugin.Configuration.UsingAlternativeRotationPlugin))
                    {
                        foreach (var item in Plugin.ListBoxPOSText.Select((name, index) => (name, index)))
                        {
                            Vector4 v4 = new();
                            if (item.index == Plugin.Indexer)
                                v4 = new Vector4(0, 255, 0, 1);
                            else
                                v4 = new Vector4(255, 255, 255, 1);
                            ImGui.TextColored(v4, item.name);
                            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && Plugin.Stage == 0)
                            {
                                if (item.index == Plugin.Indexer)
                                {
                                    Plugin.Indexer = -1;
                                    Plugin.MainListClicked = false;
                                }
                                else
                                {
                                    Plugin.Indexer = item.index;
                                    Plugin.MainListClicked = true;
                                }
                            }
                        }
                        if (_currentStepIndex != Plugin.Indexer && _currentStepIndex > -1 && Plugin.Stage > 0)
                        {
                            var lineHeight = ImGui.GetTextLineHeightWithSpacing();
                            _currentStepIndex = Plugin.Indexer;
                            if (_currentStepIndex > 1)
                                ImGui.SetScrollY((_currentStepIndex - 1) * lineHeight);
                        }
                        else if (_currentStepIndex == -1 && Plugin.Stage > 0)
                        {
                            _currentStepIndex = 0;
                            ImGui.SetScrollY(_currentStepIndex);
                        }
                        if (Plugin.InDungeon && Plugin.ListBoxPOSText.Count < 1 && !ContentPathsManager.DictionaryPaths.ContainsKey(Plugin.CurrentTerritoryContent.TerritoryType))
                            ImGui.TextColored(new Vector4(0, 255, 0, 1), $"No Path file was found for:\n{TerritoryName.GetTerritoryName(Plugin.CurrentTerritoryContent.TerritoryType).Split('|')[1].Trim()}\n({Plugin.CurrentTerritoryContent.TerritoryType}.json)\nin the Paths Folder:\n{Plugin.PathsDirectory.FullName.Replace('\\', '/')}\nPlease download from:\n{_pathsURL}\nor Create in the Build Tab");
                    }
                    else
                    {
                        if (!VNavmesh_IPCSubscriber.IsEnabled && !Plugin.Configuration.UsingAlternativeMovementPlugin)
                            ImGui.TextColored(new Vector4(255, 0, 0, 1), "AutoDuty Requires VNavmesh plugin to be Installed and Loaded\nPlease add 3rd party repo:\nhttps://puni.sh/api/repository/veyn");
                        if (!BossMod_IPCSubscriber.IsEnabled && !Plugin.Configuration.UsingAlternativeBossPlugin)
                            ImGui.TextColored(new Vector4(255, 0, 0, 1), "AutoDuty Requires BossMod plugin to be Installed and Loaded\nPlease add 3rd party repo:\nhttps://puni.sh/api/repository/veyn");
                        if (!ReflectionHelper.RotationSolver_Reflection.RotationSolverEnabled && !Plugin.Configuration.UsingAlternativeRotationPlugin)
                            ImGui.TextColored(new Vector4(255, 0, 0, 1), "AutoDuty Requires Rotation Solver plugin to be Installed and Loaded\nPlease add 3rd party repo:\nhttps://raw.githubusercontent.com/FFXIV-CombatReborn/CombatRebornRepo/main/pluginmaster.json");
                    }
                    ImGui.EndListBox();
                }
            }
            else
            {
                if (!Plugin.Running && !Plugin.Overlay.IsOpen)
                    MainWindow.GotoAndActions();

                using (var d2 = ImRaii.Disabled(Plugin.CurrentTerritoryContent == null || (Plugin.Configuration.Trust && Plugin.Configuration.SelectedTrustMembers.Count(x => x is null) > 0)))
                {
                    if (!Plugin.Running)
                    {
                        if (ImGui.Button("Run"))
                        {
                            if (!Plugin.Configuration.Support && !Plugin.Configuration.Trust && !Plugin.Configuration.Squadron && !Plugin.Configuration.Regular && !Plugin.Configuration.Trial && !Plugin.Configuration.Raid && !Plugin.Configuration.Variant)
                                MainWindow.ShowPopup("Error", "You must select a version\nof the dungeon to run");
                            else if (Svc.Party.PartyId > 0 && (Plugin.Configuration.Support || Plugin.Configuration.Squadron || Plugin.Configuration.Trust))
                                MainWindow.ShowPopup("Error", "You must not be in a party to run Support, Squadron or Trust");
                            else if (Svc.Party.PartyId == 0 && Plugin.Configuration.Regular && !Plugin.Configuration.Unsynced)
                                MainWindow.ShowPopup("Error", "You must be in a group of 4 to run Regular Duties");
                            else if (Plugin.Configuration.Regular && !Plugin.Configuration.Unsynced && !ObjectHelper.PartyValidation())
                                MainWindow.ShowPopup("Error", "You must have the correcty party makeup to run Regular Duties");
                            else if (ContentPathsManager.DictionaryPaths.ContainsKey(Plugin.CurrentTerritoryContent?.TerritoryType ?? 0))
                                Plugin.Run();
                            else
                                MainWindow.ShowPopup("Error", "No path was found");
                        }
                    }
                    else
                        MainWindow.StopResumePause();
                }
                using (var d1 = ImRaii.Disabled(Plugin.Running))
                {
                    using (var d2 = ImRaii.Disabled(Plugin.CurrentTerritoryContent == null))
                    {
                        ImGui.SameLine(0, 15);
                        ImGui.PushItemWidth(200 * ImGuiHelpers.GlobalScale);
                        if (Plugin.Configuration.UseSliderInputs)
                        {
                            if (ImGui.SliderInt("Times", ref Plugin.Configuration.LoopTimes, 0, 100))
                                Plugin.Configuration.Save();
                        }
                        else
                        {
                            if (ImGui.InputInt("Times", ref Plugin.Configuration.LoopTimes))
                                Plugin.Configuration.Save();
                        }
                        ImGui.PopItemWidth();
                    }

                    if (ImGui.Checkbox("Support", ref Plugin.Configuration.support))
                    {
                        if (Plugin.Configuration.support)
                        {
                            Plugin.Configuration.Support = Plugin.Configuration.support;
                            _dutySelected = null;
                            Plugin.Configuration.Save();
                        }
                    }
                    ImGui.SameLine(0, 5);
                    if (ImGui.Checkbox("Trust", ref Plugin.Configuration.trust))
                    {
                        if (Plugin.Configuration.trust)
                        {
                            Plugin.Configuration.Trust = Plugin.Configuration.trust;
                            _dutySelected = null;
                            Plugin.Configuration.Save();
                        }
                    }
                    ImGui.SameLine(0, 5);
                    if (ImGui.Checkbox("Squadron", ref Plugin.Configuration.squadron))
                    {
                        if (Plugin.Configuration.squadron)
                        {
                            Plugin.Configuration.Squadron = Plugin.Configuration.squadron;
                            _dutySelected = null;
                            Plugin.Configuration.Save();
                        }
                    }
                    ImGui.SameLine(0, 5);
                    if (ImGui.Checkbox("Regular", ref Plugin.Configuration.regular))
                    {
                        if (Plugin.Configuration.regular)
                        {
                            Plugin.Configuration.Regular = Plugin.Configuration.regular;
                            _dutySelected = null;
                            Plugin.Configuration.Save();
                        }
                    }
                    ImGui.SameLine(0, 5);
                    if (ImGui.Checkbox("Trial", ref Plugin.Configuration.trial))
                    {
                        if (Plugin.Configuration.trial)
                        {
                            Plugin.Configuration.Trial = Plugin.Configuration.trial;
                            _dutySelected = null;
                            Plugin.Configuration.Save();
                        }
                    }
                    //ImGui.SameLine(0, 5);
                    if (ImGui.Checkbox("Raid", ref Plugin.Configuration.raid))
                    {
                        if (Plugin.Configuration.raid)
                        {
                            Plugin.Configuration.Raid = Plugin.Configuration.raid;
                            _dutySelected = null;
                            Plugin.Configuration.Save();
                        }
                    }
                    ImGui.SameLine(0, 5);
                    if (ImGui.Checkbox("Variant", ref Plugin.Configuration.variant))
                    {
                        Plugin.Configuration.Variant = Plugin.Configuration.variant;
                        if (Plugin.Configuration.variant)
                        {
                            _dutySelected = null;
                            Plugin.Configuration.Save();
                        }
                    }

                    if (Plugin.Configuration.Support || Plugin.Configuration.Trust || Plugin.Configuration.Squadron || Plugin.Configuration.Regular || Plugin.Configuration.Trial || Plugin.Configuration.Raid || Plugin.Configuration.Variant)
                    {
                        //ImGui.SameLine(0, 15);
                        ImGui.Separator();
                        if (ImGui.Checkbox("Hide Unavailable Duties", ref Plugin.Configuration.HideUnavailableDuties))
                            Plugin.Configuration.Save();

                        if (Plugin.Configuration.Support || Plugin.Configuration.Trust)
                        {
                            ImGui.SameLine();
                            bool leveling = _support ? Plugin.SupportLeveling :
                                            _trust   ? Plugin.TrustLeveling : false;
                            bool equip = Plugin.Configuration.AutoEquipRecommendedGear;
                            if (ImGui.Checkbox("Leveling", ref leveling))
                            {
                                if (leveling)
                                {
                                    if (equip)
                                        AutoEquipHelper.Invoke();

                                    ContentHelper.Content? duty = LevelingHelper.SelectHighestLevelingRelevantDuty(Plugin.Configuration.Trust);
                                    if (duty != null)
                                    {
                                        _dutySelected                  = ContentPathsManager.DictionaryPaths[duty.TerritoryType];
                                        Plugin.CurrentTerritoryContent = duty;

                                        _dutySelected.SelectPath(out Plugin.CurrentPath);

                                        if (Plugin.Configuration.Support)
                                            Plugin.SupportLeveling            = leveling;
                                        else if (Plugin.Configuration.Trust) 
                                            Plugin.TrustLeveling = leveling;
                                    }
                                }
                                else
                                {
                                    if (Plugin.Configuration.Support)
                                        Plugin.SupportLeveling = leveling;
                                    else if (Plugin.Configuration.Trust)
                                        Plugin.TrustLeveling = leveling;
                                }
                            }
                            ImGuiComponents.HelpMarker("Leveling Mode will queue you for the most CONSISTENT dungeon considering your lvl + Ilvl. \nIt will NOT always queue you for the highest level dungeon, it follows this list instead:\nL16-L23 (i0): TamTara \nL24-31 (i0): Totorak\nL32-40 (i0): Brayflox\nL41-52 (i0): Stone Vigil\nL53-60 (i105): Sohm Al\nL61-66 (i240): Sirensong Sea\nL67-70 (i255): Doma Castle\nL71-74 (i370): Holminster\nL75-80 (i380): Qitana\nL81-86 (i500): Tower of Zot\nL87-90 (i515): Ktisis\nL91-100 (i630): Highest Level DT Dungeons");
                        }

                        if (Plugin.Configuration.Trust)
                        {
                            ImGui.Separator();
                            if (_dutySelected != null && _dutySelected.Content.TrustMembers.Count > 0)
                            {
                                ImGuiEx.LineCentered(() => ImGuiEx.TextUnderlined("Select your Trust Party"));
                                ImGui.Columns(3, null, false);

                                TrustManager.ResetTrustIfInvalid();
                                for (int i = 0; i < Plugin.Configuration.SelectedTrustMembers.Length; i++)
                                {
                                    TrustMemberName? member = Plugin.Configuration.SelectedTrustMembers[i];

                                    if (member is null)
                                        continue;

                                    if (_dutySelected.Content.TrustMembers.All(x => x.MemberName != member))
                                    {
                                        Svc.Log.Debug($"Killing {member}");
                                        Plugin.Configuration.SelectedTrustMembers[i] = null;
                                    }
                                }

                                using (ImRaii.Disabled(Plugin.TrustLevelingEnabled))
                                {
                                    foreach (TrustMember member in _dutySelected.Content.TrustMembers)
                                    {
                                        bool       enabled        = Plugin.Configuration.SelectedTrustMembers.Where(x => x != null).Any(x => x == member.MemberName);
                                        CombatRole playerRole     = Player.Job.GetRole();
                                        int        numberSelected = Plugin.Configuration.SelectedTrustMembers.Count(x => x != null);

                                        TrustMember?[] members = Plugin.Configuration.SelectedTrustMembers.Select(tmn => tmn != null ? TrustManager.members[(TrustMemberName)tmn] : null).ToArray();

                                        bool canSelect = members.CanSelectMember(member, playerRole) && member.Level >= _dutySelected.Content.ClassJobLevelRequired;

                                        using (ImRaii.Disabled(!enabled && (numberSelected == 3 || !canSelect)))
                                        {
                                            if (ImGui.Checkbox($"###{member.Index}{_dutySelected.id}", ref enabled))
                                            {
                                                if (enabled)
                                                {
                                                    for (int i = 0; i < 3; i++)
                                                    {
                                                        if (Plugin.Configuration.SelectedTrustMembers[i] is null)
                                                        {
                                                            Plugin.Configuration.SelectedTrustMembers[i] = member.MemberName;
                                                            break;
                                                        }
                                                    }
                                                }
                                                else
                                                {
                                                    if (Plugin.Configuration.SelectedTrustMembers.Where(x => x != null).Any(x => x == member.MemberName))
                                                    {
                                                        int idx = Plugin.Configuration.SelectedTrustMembers.IndexOf(x => x != null && x == member.MemberName);
                                                        Plugin.Configuration.SelectedTrustMembers[idx] = null;
                                                    }
                                                }

                                                Plugin.Configuration.Save();
                                            }
                                        }

                                        ImGui.SameLine(0, 2);
                                        ImGui.SetItemAllowOverlap();
                                        ImGui.TextColored(member.Role switch
                                        {
                                            TrustRole.DPS => ImGuiHelper.RoleDPSColor,
                                            TrustRole.Healer => ImGuiHelper.RoleHealerColor,
                                            TrustRole.Tank => ImGuiHelper.RoleTankColor,
                                            TrustRole.AllRounder => ImGuiHelper.RoleAllRounderColor,
                                            _ => Vector4.One
                                        }, member.Name);
                                        if (member.Level > 0)
                                        {
                                            ImGui.SameLine(0, 2);
                                            ImGui.Text(member.Level.ToString());
                                        }

                                        ImGui.NextColumn();
                                    }
                                }

                                if(ImGui.Button("Refresh"))
                                    TrustManager.ClearCachedLevels();
                                ImGui.NextColumn();
                                ImGui.Columns(1, null, true);
                            } else if (ImGui.Button("Refresh trust member levels"))
                            {
                                TrustManager.ClearCachedLevels();
                            }
                        }

                        DrawPathSelection();
                    }
                    if (Plugin.Configuration.Regular || Plugin.Configuration.Trial || Plugin.Configuration.Raid)
                    {
                        ImGui.SameLine(0, 5);
                        if (ImGui.Checkbox("Unsynced", ref Plugin.Configuration.Unsynced))
                            Plugin.Configuration.Save();
                    }
                    using var d3 = ImRaii.Disabled(Plugin.LevelingEnabled);
                    if (Plugin.LevelingEnabled)
                        ImGui.TextWrapped("AutoDuty will automatically select the best dungeon");

                    if (!ImGui.BeginListBox("##DutyList", new Vector2(355 * ImGuiHelpers.GlobalScale, 425 * ImGuiHelpers.GlobalScale))) return;
                    
                    if (Player.Available)
                    if (VNavmesh_IPCSubscriber.IsEnabled && BossMod_IPCSubscriber.IsEnabled)
                    {
                        if ((Player.Job.GetRole() != CombatRole.NonCombat && Player.Job != Job.BLU) || (Player.Job == Job.BLU && (Plugin.Configuration.Regular || Plugin.Configuration.Trial || Plugin.Configuration.Raid)))
                        {
                            Dictionary<uint, ContentHelper.Content> dictionary = [];
                            if (Plugin.Configuration.Support)
                                dictionary = ContentHelper.DictionaryContent.Where(x => x.Value.DawnContent).ToDictionary();
                            else if (Plugin.Configuration.Trust)
                                dictionary = ContentHelper.DictionaryContent.Where(x => x.Value.TrustContent).ToDictionary();
                            else if (Plugin.Configuration.Squadron)
                                dictionary = ContentHelper.DictionaryContent.Where(x => x.Value.GCArmyContent).ToDictionary();
                            else if (Plugin.Configuration.Regular)
                                dictionary = ContentHelper.DictionaryContent.Where(x => x.Value.ContentType == 2).ToDictionary();
                            else if (Plugin.Configuration.Trial)
                                dictionary = ContentHelper.DictionaryContent.Where(x => x.Value.ContentType == 4).ToDictionary();
                            else if (Plugin.Configuration.Raid)
                                dictionary = ContentHelper.DictionaryContent.Where(x => x.Value.ContentType == 5).ToDictionary();
                            else if (Plugin.Configuration.Variant)
                                dictionary = ContentHelper.DictionaryContent.Where(x => x.Value.VariantContent).ToDictionary();
    
                            if (dictionary.Count > 0 && ObjectHelper.IsReady)
                            {
                                short level = PlayerHelper.GetCurrentLevelFromSheet();
                                short ilvl = PlayerHelper.GetCurrentItemLevelFromGearSet(updateGearsetBeforeCheck: false);
    
                                foreach ((uint _, ContentHelper.Content? content) in dictionary)
                                {
                                    bool canRun = content.CanRun(level, ilvl) && (!_trust || content.CanTrustRun());
                                    using (var d2 = ImRaii.Disabled(!canRun))
                                    {
                                        if (Plugin.Configuration.HideUnavailableDuties && !canRun)
                                            continue;
                                        if (ImGui.Selectable($"({content.TerritoryType}) {content.DisplayName}", _dutySelected?.id == content.TerritoryType))
                                        {
                                            _dutySelected = ContentPathsManager.DictionaryPaths[content.TerritoryType];
                                            Plugin.CurrentTerritoryContent = content;
                                            _dutySelected.SelectPath(out Plugin.CurrentPath);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                ImGui.TextColored(new Vector4(0, 1, 0, 1), "Please select one of Support, Trust, Squadron or Regular\nto Populate the Duty List");
                            }
                        }
                        else
                        {
                            if (Player.Job.GetRole() == CombatRole.NonCombat || Player.Job == Job.BLU)
                                ImGui.TextColored(new Vector4(255, 1, 0, 1), "Friendly reminder that AutoDuty sadly does NOT work \nwhen playing as a DoH or DoL!!!");
                            if (Player.Job == Job.BLU)
                                ImGui.TextColored(new Vector4(0, 1, 1, 1), "OR BLUE MAGE... REALLY!?");
                        }
                    }
                    else
                    {
                        if (!VNavmesh_IPCSubscriber.IsEnabled)
                            ImGui.TextColored(new Vector4(255, 0, 0, 1), "AutoDuty Requires VNavmesh plugin to be Installed and Loaded\nFor proper navigation and movement\nPlease add 3rd party repo:\nhttps://puni.sh/api/repository/veyn");
                        if (!BossMod_IPCSubscriber.IsEnabled)
                            ImGui.TextColored(new Vector4(255, 0, 0, 1), "AutoDuty Requires BossMod plugin to be Installed and Loaded\nFor proper named mechanic handling\nPlease add 3rd party repo:\nhttps://puni.sh/api/repository/veyn");
                    }
                    ImGui.EndListBox();
                }
            }
        }

        internal static void PathsUpdated()
        {
            _dutySelected = null;
        }
    }
}
