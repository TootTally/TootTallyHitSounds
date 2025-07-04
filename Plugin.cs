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
        public const string DEFAULT_MISSSOUND = "None";
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
            HitSoundVolume = config.Bind("General", nameof(HitSoundVolume), 1f, "Volume of the hitsounds.");
            MissSoundVolume = config.Bind("General", nameof(MissSoundVolume), 1f, "Volume of the misssounds.");
            HitSoundName = config.Bind("General", nameof(HitSoundName), DEFAULT_HITSOUND, "Name of the hitsound wav file.");
            MissSoundName = config.Bind("General", nameof(MissSoundName), DEFAULT_MISSSOUND, "Name of the misssound wav file.");
            SyncWithNotes = config.Bind("General", nameof(SyncWithNotes), false, "Useful for charters to make sure your map is on time. Use with 0ms audio latency for best results.");
            SyncWithSong = config.Bind("General", nameof(SyncWithSong), true, "Better for players since the clicking sound will (most of the time) match the music's timing.");
            AddAudioLatencyToSync = Config.Bind("General", nameof(AddAudioLatencyToSync), true, "Adds the audio offset to tracktime for hitsound syncing.");
            
            TryMigrateSoundsFolder("HitSounds");
            TryMigrateSoundsFolder("MissSounds");

            settingPage = TootTallySettingsManager.AddNewPage(new CustomHitSoundSettingPage());
            TootTallySettings.Plugin.TryAddThunderstoreIconToPageButton(Instance.Info.Location, Name, settingPage);

            _harmony.PatchAll(typeof(HitSoundPatches));
            LogInfo($"Module loaded!");
        }

        private static void TryMigrateSoundsFolder(string folderName)
        {
            string sourceFolderPath = Path.Combine(Path.GetDirectoryName(Plugin.Instance.Info.Location), folderName);
            string targetFolderPath = Path.Combine(Paths.BepInExRootPath, folderName);
            FileHelper.TryMigrateFolder(sourceFolderPath, targetFolderPath, false);
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
            private static AudioSource _hitSound, _missSound;
            private static float _hitVolume, _missVolume;
            public static bool isHitClipLoaded, isMissClipLoaded;
            public static AudioSource testHitSound, testMissSound;

            [HarmonyPatch(typeof(HomeController), nameof(HomeController.Start))]
            [HarmonyPostfix]
            public static void LoadTestSounds(HomeController __instance)
            {
                testHitSound = __instance.gameObject.AddComponent<AudioSource>();
                testHitSound.volume = Plugin.Instance.HitSoundVolume.Value;
                isHitClipLoaded = false;
                if (Plugin.Instance.HitSoundName.Value != DEFAULT_HITSOUND)
                    Plugin.Instance.StartCoroutine(TryLoadingAudioClipLocal("HitSounds", $"{Plugin.Instance.HitSoundName.Value}.wav", clip =>
                    {
                        testHitSound.clip = clip;
                        isHitClipLoaded = true;
                    }));

                testMissSound = __instance.gameObject.AddComponent<AudioSource>();
                testMissSound.volume = Plugin.Instance.HitSoundVolume.Value;
                isMissClipLoaded = false;
                if (Plugin.Instance.MissSoundName.Value != DEFAULT_MISSSOUND)
                    Plugin.Instance.StartCoroutine(TryLoadingAudioClipLocal("MissSounds", $"{Plugin.Instance.MissSoundName.Value}.wav", clip =>
                    {
                        testMissSound.clip = clip;
                        isMissClipLoaded = true;
                    }));
            }

            [HarmonyPatch(typeof(GameController), nameof(GameController.Start))]
            [HarmonyPostfix]
            public static void LoadHitSound(GameController __instance)
            {
                isHitClipLoaded = false;

                if (Plugin.Instance.HitSoundName.Value == DEFAULT_HITSOUND) return;

                _isStarted = false;
                _lastChamp = false;
                _lastMult = 0;
                _lastIsActive = false;
                _isSlider = false;
                _lastIndex = -1;
                _trackTime = 0;
                _nextTiming = __instance.leveldata.Count > 0 ? B2s(__instance.leveldata[0][0], __instance.tempo) : 0;
                _hitSound = __instance.gameObject.AddComponent<AudioSource>();
                _hitSound.volume = _hitVolume = Plugin.Instance.HitSoundVolume.Value * GlobalVariables.localsettings.maxvolume;
                Plugin.Instance.StartCoroutine(TryLoadingAudioClipLocal("HitSounds", $"{Plugin.Instance.HitSoundName.Value}.wav", clip =>
                {
                    Plugin.LogInfo($"Hit Sounds {Plugin.Instance.HitSoundName.Value} Loaded.");
                    _hitSound.clip = clip;
                    isHitClipLoaded = true;
                }));
            }

            [HarmonyPatch(typeof(GameController), nameof(GameController.Start))]
            [HarmonyPostfix]
            public static void LoadMissSound(GameController __instance)
            {
                isMissClipLoaded = false;

                if (Plugin.Instance.MissSoundName.Value == DEFAULT_MISSSOUND) return;

                _missSound = __instance.gameObject.AddComponent<AudioSource>();
                _missSound.volume = _hitVolume = Plugin.Instance.MissSoundVolume.Value * GlobalVariables.localsettings.maxvolume;
                Plugin.Instance.StartCoroutine(TryLoadingAudioClipLocal("MissSounds", $"{Plugin.Instance.MissSoundName.Value}.wav", clip =>
                {
                    Plugin.LogInfo($"Miss Sounds {Plugin.Instance.MissSoundName.Value} Loaded.");
                    _missSound.clip = clip;
                    isMissClipLoaded = true;
                }));
            }

            [HarmonyPatch(typeof(GameController), nameof(GameController.playsong))]
            [HarmonyPostfix]
            public static void OnPlaySong() =>
                _isStarted = true;

            private static bool _isSlider;
            private static int _lastIndex;
            private static double _trackTime;
            private static int _lastSample;
            private static float _nextTiming;
            private static bool _isStarted;
            private static int _lastMult;
            private static bool _lastChamp;

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
                if (_hitSound == null || !isHitClipLoaded || !_isStarted) return;
                if (!_isStarted || __instance.paused || __instance.quitting || __instance.retrying) return;

                _trackTime += Time.deltaTime * TootTallyGlobalVariables.gameSpeedMultiplier;
                if (_lastSample != __instance.musictrack.timeSamples)
                {
                    _trackTime = __instance.musictrack.time - (Instance.AddAudioLatencyToSync.Value ? __instance.latency_offset : 0);
                    _lastSample = __instance.musictrack.timeSamples;
                }

                if (ShouldPlayHitSound(__instance))
                {
                    _lastIndex = __instance.currentnoteindex;
                    PlaySound(ref _hitVolume, ref _hitSound, Plugin.Instance.HitSoundVolume.Value);
                }

                FadeOutVolume(ref _hitVolume,ref _hitSound, Plugin.Instance.HitSoundVolume.Value);
                FadeOutVolume(ref _missVolume, ref _missSound, Plugin.Instance.MissSoundVolume.Value);

                _lastIsActive = __instance.noteactive;
            }

            [HarmonyPatch(typeof(GameController), nameof(GameController.getScoreAverage))]
            [HarmonyPostfix]
            public static void OnNoteScoringPostfix(GameController __instance)
            {
                if (_missSound == null || !isMissClipLoaded || !_isStarted) return;

                if (ShouldPlayMissSount(__instance.multiplier, __instance.rainbowcontroller.champmode))
                {
                    PlaySound(ref _missVolume, ref _missSound, Plugin.Instance.MissSoundVolume.Value);
                }

                _lastMult = __instance.multiplier;
                _lastChamp = __instance.rainbowcontroller.champmode;

            }

            public static float B2s(float time, float bpm) => time / bpm * 60f;

            public static bool ShouldPlayHitSound(GameController __instance)
            {
                if (Plugin.Instance.SyncWithNotes.Value)
                    return __instance.noteactive && !_lastIsActive && !_isSlider;

                return _trackTime > _nextTiming
                    && _lastIndex != __instance.currentnoteindex
                    && !_isSlider;
            }

            //Either lose combo or lose champ
            public static bool ShouldPlayMissSount(int multiplier, bool champ) => (_lastMult >= 10 && multiplier == 0) || (_lastChamp && !champ);

            public static void PlaySound(ref float volume, ref AudioSource audioSource, float maxVolume)
            {
                volume = maxVolume * GlobalVariables.localsettings.maxvolume;
                audioSource.time = 0;
                audioSource.Play();
            }

            public static void FadeOutVolume(ref float volume, ref AudioSource audioSource, float maxVolume)
            {
                if (volume < 0)
                {
                    volume = 0;
                    audioSource.Stop();
                }
                else if (volume > 0)
                {
                    volume -= Time.unscaledDeltaTime / 2f * maxVolume * GlobalVariables.localsettings.maxvolume;
                    audioSource.volume = Mathf.Clamp(volume, 0, 1);
                }
            }

            public static IEnumerator<UnityWebRequestAsyncOperation> TryLoadingAudioClipLocal(string folderName, string fileName, Action<AudioClip> callback)
            {
                string assetDir = Path.Combine(Paths.BepInExRootPath, folderName, fileName);
                UnityWebRequest webRequest = UnityWebRequestMultimedia.GetAudioClip("file://" + assetDir, AudioType.WAV);
                yield return webRequest.SendWebRequest();
                if (!webRequest.isHttpError && !webRequest.isNetworkError)
                    callback(DownloadHandlerAudioClip.GetContent(webRequest));
            }
        }

        public ConfigEntry<float> HitSoundVolume { get; set; }
        public ConfigEntry<float> MissSoundVolume { get; set; }
        public ConfigEntry<string> HitSoundName { get; set; }
        public ConfigEntry<string> MissSoundName { get; set; }
        public ConfigEntry<bool> SyncWithNotes { get; set; }
        public ConfigEntry<bool> SyncWithSong { get; set; }
        public ConfigEntry<bool> AddAudioLatencyToSync { get; set; }
    }
}