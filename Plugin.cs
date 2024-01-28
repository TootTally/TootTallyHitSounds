using BaboonAPI.Hooks.Initializer;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TootTallyCore.Utils;
using TootTallyCore.Utils.Helpers;
using TootTallyCore.Utils.TootTallyGlobals;
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
        public const string DEFAULT_HITSOUND = "None";
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
            HitSoundName = config.Bind("General", nameof(HitSoundName), DEFAULT_HITSOUND, "Name of the hitsound wav file.");
            SyncWithNotes = config.Bind("General", nameof(SyncWithNotes), false, "Useful for charters to make sure your map is on time. Use with 0ms audio latency for best results.");
            SyncWithSong = config.Bind("General", nameof(SyncWithSong), true, "Better for players since the clicking sound will (most of the time) match the music's timing.");

            string sourceFolderPath = Path.Combine(Path.GetDirectoryName(Plugin.Instance.Info.Location), "HitSounds");
            string targetFolderPath = Path.Combine(Paths.BepInExRootPath, "HitSounds");
            FileHelper.TryMigrateFolder(sourceFolderPath, targetFolderPath, false);

            settingPage = TootTallySettingsManager.AddNewPage(new CustomHitSoundSettingPage());
            TootTallySettings.Plugin.TryAddThunderstoreIconToPageButton(Instance.Info.Location, Name, settingPage);

            _harmony.PatchAll(typeof(HitSoundPatches));
            LogInfo($"Module loaded!");
        }

        public void UnloadModule()
        {
            _harmony.UnpatchSelf();
            settingPage.Remove();
            LogInfo($"Module unloaded!");
        }

        public static class HitSoundPatches
        {
            private static bool _lastIsActive;
            private static AudioSource _hitsound;
            private static float _volume;
            public static bool isClipLoaded;
            public static AudioSource testHitSound;

            [HarmonyPatch(typeof(HomeController), nameof(HomeController.Start))]
            [HarmonyPostfix]
            public static void LoadTestHitSound(HomeController __instance)
            {
                testHitSound = __instance.gameObject.AddComponent<AudioSource>();
                testHitSound.volume = Plugin.Instance.Volume.Value;
                isClipLoaded = false;
                if (Plugin.Instance.HitSoundName.Value != DEFAULT_HITSOUND)
                    Plugin.Instance.StartCoroutine(TryLoadingAudioClipLocal($"{Plugin.Instance.HitSoundName.Value}.wav", clip =>
                    {
                        testHitSound.clip = clip;
                        isClipLoaded = true;
                    }));
            }

            [HarmonyPatch(typeof(GameController), nameof(GameController.Start))]
            [HarmonyPostfix]
            public static void LoadHitSound(GameController __instance)
            {
                isClipLoaded = false;

                if (Plugin.Instance.HitSoundName.Value == DEFAULT_HITSOUND) return;

                _isStarted = false;
                _lastIsActive = false;
                _isSlider = false;
                _lastIndex = -1;
                _time = -__instance.noteoffset;
                _nextTiming = __instance.leveldata.Count > 0 ? B2s(__instance.leveldata[0][0], __instance.tempo) : 0;
                _hitsound = __instance.gameObject.AddComponent<AudioSource>();
                _hitsound.volume = _volume = Plugin.Instance.Volume.Value * GlobalVariables.localsettings.maxvolume;
                Plugin.Instance.StartCoroutine(TryLoadingAudioClipLocal($"{Plugin.Instance.HitSoundName.Value}.wav", clip =>
                {
                    _hitsound.clip = clip;
                    isClipLoaded = true;
                }));
            }

            [HarmonyPatch(typeof(GameController), nameof(GameController.playsong))]
            [HarmonyPostfix]
            public static void OnPlaySong() =>
                _isStarted = true;

            [HarmonyPatch(typeof(GameController), nameof(GameController.syncTrackPositions))]
            [HarmonyPostfix]
            public static void OnSyncTrack(GameController __instance) =>
                _time = __instance.musictrack.time - __instance.noteoffset;

            private static bool _isSlider;
            private static int _lastIndex;
            private static double _time;
            private static float _nextTiming;
            private static bool _isStarted;

            [HarmonyPatch(typeof(GameController), nameof(GameController.grabNoteRefs))]
            [HarmonyPrefix]
            public static void GetIsSlider(GameController __instance)
            {
                if (__instance.currentnoteindex + 1 >= __instance.leveldata.Count) return;

                _isSlider = Mathf.Abs(__instance.leveldata[__instance.currentnoteindex + 1][0] - (__instance.leveldata[__instance.currentnoteindex][0] + __instance.leveldata[__instance.currentnoteindex][1])) < 0.05f;
                _nextTiming = B2s(__instance.leveldata[__instance.currentnoteindex + 1][0], __instance.tempo);
            }

            [HarmonyPatch(typeof(GameController), nameof(GameController.Update))]
            [HarmonyPostfix]
            public static void PlaySoundOnNewNoteActive(GameController __instance)
            {
                if (_hitsound == null || !isClipLoaded || !_isStarted) return;

                if (ShouldPlayHitSound(__instance))
                {
                    _lastIndex = __instance.currentnoteindex;
                    PlayHitSound();
                }

                FadeOutVolume();

                _lastIsActive = __instance.noteactive;
            }

            public static float B2s(float time, float bpm) => time / bpm * 60f;

            public static bool ShouldPlayHitSound(GameController __instance)
            {
                if (Plugin.Instance.SyncWithNotes.Value)
                    return __instance.noteactive && !_lastIsActive && !_isSlider;

                if (!__instance.paused)
                    _time += Time.deltaTime * TootTallyGlobalVariables.gameSpeedMultiplier;
                return _time > _nextTiming
                    && _lastIndex != __instance.currentnoteindex
                    && !_isSlider;
            }

            public static void PlayHitSound()
            {
                _volume = Plugin.Instance.Volume.Value * GlobalVariables.localsettings.maxvolume;
                _hitsound.Play();
            }

            public static void FadeOutVolume()
            {
                if (_volume < 0)
                {
                    _volume = 0;
                    _hitsound.Stop();
                }
                else if (_volume > 0)
                {
                    _volume -= Time.unscaledDeltaTime / 2f * Plugin.Instance.Volume.Value * GlobalVariables.localsettings.maxvolume;
                    _hitsound.volume = Mathf.Clamp(_volume, 0, 1);
                }
            }

            public static IEnumerator<UnityWebRequestAsyncOperation> TryLoadingAudioClipLocal(string fileName, Action<AudioClip> callback)
            {
                string assetDir = Path.Combine(Paths.BepInExRootPath, "HitSounds", fileName);
                UnityWebRequest webRequest = UnityWebRequestMultimedia.GetAudioClip("file://" + assetDir, AudioType.WAV);
                yield return webRequest.SendWebRequest();
                if (!webRequest.isHttpError && !webRequest.isNetworkError)
                    callback(DownloadHandlerAudioClip.GetContent(webRequest));
            }
        }

        public ConfigEntry<float> Volume { get; set; }
        public ConfigEntry<string> HitSoundName { get; set; }
        public ConfigEntry<bool> SyncWithNotes { get; set; }
        public ConfigEntry<bool> SyncWithSong { get; set; }
    }
}