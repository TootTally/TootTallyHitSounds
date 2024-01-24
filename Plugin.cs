﻿using BaboonAPI.Hooks.Initializer;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TootTallyCore.Utils.Helpers;
using TootTallyCore.Utils.TootTallyModules;
using TootTallySettings;
using TootTallySettings.TootTallySettingsObjects;
using UnityEngine;
using UnityEngine.Networking;

namespace TootTallyHitSounds
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency("TootTallyCore", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("TootTallySettings", BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin, ITootTallyModule
    {
        public static Plugin Instance;

        private const string CONFIG_NAME = "TootTallyHitSounds.cfg";
        private Harmony _harmony;
        public ConfigEntry<bool> ModuleConfigEnabled { get; set; }
        public bool IsConfigInitialized { get; set; }

        //Change this name to whatever you want
        public string Name { get => PluginInfo.PLUGIN_NAME; set => Name = value; }

        public static TootTallySettingPage settingPage;

        public static void LogInfo(string msg) => Instance.Logger.LogInfo(msg);
        public static void LogError(string msg) => Instance.Logger.LogError(msg);

        private void Awake()
        {
            if (Instance != null) return;
            Instance = this;
            _harmony = new Harmony(Info.Metadata.GUID);

            GameInitializationEvent.Register(Info, TryInitialize);
        }

        private void TryInitialize()
        {
            // Bind to the TTModules Config for TootTally
            ModuleConfigEnabled = TootTallyCore.Plugin.Instance.Config.Bind("Modules", "HitSounds", true, "Add HitSounds when notes become active.");
            TootTallyModuleManager.AddModule(this);
            TootTallySettings.Plugin.Instance.AddModuleToSettingPage(this);
        }

        public void LoadModule()
        {
            string configPath = Path.Combine(Paths.BepInExRootPath, "config/");
            ConfigFile config = new ConfigFile(configPath + CONFIG_NAME, true) { SaveOnConfigSet = true };
            Volume = config.Bind("General", nameof(Volume), 1f, "Volume of the hitsounds.");
            HitSoundName = config.Bind("General", nameof(HitSoundName), "None", "Name of the hitsound wav file.");

            string sourceFolderPath = Path.Combine(Path.GetDirectoryName(Plugin.Instance.Info.Location), "HitSounds");
            string targetFolderPath = Path.Combine(Paths.BepInExRootPath, "HitSounds");
            FileHelper.TryMigrateFolder(sourceFolderPath, targetFolderPath, false);

            settingPage = TootTallySettingsManager.AddNewPage("HitSounds", "HitSounds", 40f, new Color(0, 0, 0, 0));
            TootTallySettings.Plugin.TryAddThunderstoreIconToPageButton(Instance.Info.Location, Name, settingPage);
            settingPage.AddSlider("Volume", 0, 1, Volume, false);

            

            _harmony.PatchAll(typeof(ModuleTemplatePatches));
            LogInfo($"Module loaded!");
        }

        public void UnloadModule()
        {
            _harmony.UnpatchSelf();
            settingPage.Remove();
            LogInfo($"Module unloaded!");
        }

        private TootTallySettingDropdown CreateDropdownFromFolder(string folderName, ConfigEntry<string> config, string defaultValue)
        {
            var folderNames = new List<string> { defaultValue };
            var folderPath = Path.Combine(Paths.BepInExRootPath, folderName);
            if (Directory.Exists(folderPath))
            {
                var directories = Directory.GetDirectories(folderPath).ToList();
                directories.ForEach(d =>
                {
                    if (Path.GetExtension(d).ToLower().Contains("wav"))
                        folderNames.Add(Path.GetFileNameWithoutExtension(d));
                });
            }
            settingPage.AddLabel(folderName, folderName, 24, TMPro.FontStyles.Normal, TMPro.TextAlignmentOptions.BottomLeft);
            return settingPage.AddDropdown($"{folderName}Dropdown", config, folderNames.ToArray());
        }

        public static class ModuleTemplatePatches
        {
            private static bool _lastIsActive;
            private static AudioSource _hitsound;
            private static float _volume;
            private static bool _isClipLoaded;

            [HarmonyPatch(typeof(GameController), nameof(GameController.Start))]
            [HarmonyPostfix]
            public static void LoadHitSound(GameController __instance)
            {
                _isClipLoaded = false;

                if (Plugin.Instance.HitSoundName.Value == "None") return;

                _lastIsActive = false;
                _isSlider = false;
                _hitsound.volume = _volume = Plugin.Instance.Volume.Value;
                _hitsound = __instance.gameObject.AddComponent<AudioSource>();
                Plugin.Instance.StartCoroutine(TryLoadingAudioClipLocal($"{Plugin.Instance.HitSoundName.Value}.wav", clip =>
                {
                    _hitsound.clip = clip;
                    _isClipLoaded = true;
                }));
            }

            private static bool _isSlider;
            private static int _lastIndex;

            [HarmonyPatch(typeof(GameController), nameof(GameController.grabNoteRefs))]
            [HarmonyPrefix]
            public static void GetIsSlider(GameController __instance)
            {
                if (__instance.currentnoteindex + 1 >= __instance.allnotevals.Count) return;

                _isSlider = Mathf.Abs(__instance.leveldata[__instance.currentnoteindex + 1][0] - (__instance.leveldata[__instance.currentnoteindex][0] + __instance.leveldata[__instance.currentnoteindex][1])) < 0.05f;
            }

            [HarmonyPatch(typeof(GameController), nameof(GameController.Update))]
            [HarmonyPostfix]
            public static void PlaySoundOnNewNoteActive(GameController __instance)
            {
                if (_hitsound == null || !_isClipLoaded) return;

                var fuck = b2s(__instance.leveldata[__instance.currentnoteindex][0], __instance.tempo);
                if (__instance.musictrack.time > fuck
                    && __instance.currentnoteindex != _lastIndex
                    && !_isSlider)
                {
                    _lastIndex = __instance.currentnoteindex;
                    _volume = Plugin.Instance.Volume.Value;
                    _hitsound.Play();
                }

                if (_volume < 0)
                {
                    _volume = 0;
                    _hitsound.Stop();
                }
                else if (_volume > 0)
                {
                    _hitsound.volume = Mathf.Clamp(_volume, 0, 1);
                    _volume -= Time.unscaledDeltaTime;
                }

                _lastIsActive = __instance.noteactive;
            }

            public static float b2s(float time, float bpm) => time / bpm * 60f;

            public static IEnumerator<UnityWebRequestAsyncOperation> TryLoadingAudioClipLocal(string fileName, Action<AudioClip> callback)
            {
                string assetDir = Path.Combine(Paths.BepInExRootPath, "HitSounds");
                assetDir = Path.Combine(assetDir, fileName);
                UnityWebRequest webRequest = UnityWebRequestMultimedia.GetAudioClip("file://" + assetDir, AudioType.MPEG);
                yield return webRequest.SendWebRequest();
                if (!webRequest.isHttpError && !webRequest.isNetworkError)
                    callback(DownloadHandlerAudioClip.GetContent(webRequest));
            }
        }

        public ConfigEntry<float> Volume { get; set; }
        public ConfigEntry<string> HitSoundName { get; set; }
    }
}