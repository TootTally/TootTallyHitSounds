using BepInEx.Configuration;
using BepInEx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TootTallySettings;
using TootTallySettings.TootTallySettingsObjects;
using UnityEngine;
using UnityEngine.UI;
using static TootTallyHitSounds.Plugin;

namespace TootTallyHitSounds
{
    internal class CustomHitSoundSettingPage : TootTallySettingPage
    {
        private static ColorBlock _pageBtnColors = new ColorBlock()
        {
            colorMultiplier = 1f,
            fadeDuration = .2f,
            disabledColor = Color.gray,
            normalColor = new Color(.6f, .6f, .6f),
            pressedColor = new Color(.2f, .2f, .2f),
            highlightedColor = new Color(.8f, .8f, .8f),
            selectedColor = new Color(.6f, .6f, .6f)
        };

        private TootTallySettingSlider _hitVolumeSlider, _missVolumeSlider;
        private TootTallySettingDropdown _hitSoundsDropdown, _missSoundsDropdown;
        private TootTallySettingToggle _toggleSyncWithNotes, _toggleSyncWithSong;

        public CustomHitSoundSettingPage() : base("HitSounds", "HitSounds", 40f, new Color(0, 0, 0, 0), _pageBtnColors)
        {
            _hitVolumeSlider = AddSlider("HitSound Volume", 0, 1, Plugin.Instance.HitSoundVolume, false);
            _hitSoundsDropdown = CreateDropdownFromFolder("HitSounds", Plugin.Instance.HitSoundName, Plugin.DEFAULT_HITSOUND);

            _missVolumeSlider = AddSlider("MissSound Volume", 0, 1, Plugin.Instance.MissSoundVolume, false);
            _missSoundsDropdown = CreateDropdownFromFolder("MissSounds", Plugin.Instance.MissSoundName, Plugin.DEFAULT_MISSSOUND);
            _toggleSyncWithNotes = AddToggle("Sync With Notes", Plugin.Instance.SyncWithNotes, v =>
            {
                Plugin.Instance.SyncWithSong.Value = !v;
                _toggleSyncWithSong.toggle.SetIsOnWithoutNotify(!v);
            });
            _toggleSyncWithSong = AddToggle("Sync With Song", Plugin.Instance.SyncWithSong, v =>
            {
                Plugin.Instance.SyncWithNotes.Value = !v;
                _toggleSyncWithNotes.toggle.SetIsOnWithoutNotify(!v);
            });
            AddToggle("Add Audio Latency To Sync", Plugin.Instance.AddAudioLatencyToSync);
            AddButton("Test HitSound", TestHitSound);
            AddButton("Test MissSound", TestMissSound);
        }

        public override void Initialize()
        {
            base.Initialize();
            _hitVolumeSlider.slider.onValueChanged.AddListener(OnHitVolumeChange);
            _missVolumeSlider.slider.onValueChanged.AddListener(OnMissVolumeChange);
            _hitSoundsDropdown.dropdown.onValueChanged.AddListener(OnHitSoundChange);
            _missSoundsDropdown.dropdown.onValueChanged.AddListener(OnMissSoundChange);
        }

        private void TestHitSound()
        {
            if (HitSoundPatches.isHitClipLoaded)
                HitSoundPatches.testHitSound.Play();
        }

        private void TestMissSound()
        {
            if (HitSoundPatches.isMissClipLoaded)
                HitSoundPatches.testMissSound.Play();
        }

        private void OnHitSoundChange(int _)
        {
            HitSoundPatches.isHitClipLoaded = false;
            if (Plugin.Instance.HitSoundName.Value != DEFAULT_HITSOUND)
                Plugin.Instance.StartCoroutine(HitSoundPatches.TryLoadingAudioClipLocal("HitSounds", $"{Plugin.Instance.HitSoundName.Value}.wav", clip =>
                {
                    HitSoundPatches.testHitSound.clip = clip;
                    HitSoundPatches.isHitClipLoaded = true;
                }));
        }

        private void OnMissSoundChange(int _)
        {
            HitSoundPatches.isMissClipLoaded = false;
            if (Plugin.Instance.MissSoundName.Value != DEFAULT_MISSSOUND)
                Plugin.Instance.StartCoroutine(HitSoundPatches.TryLoadingAudioClipLocal("MissSounds", $"{Plugin.Instance.MissSoundName.Value}.wav", clip =>
                {
                    HitSoundPatches.testMissSound.clip = clip;
                    HitSoundPatches.isMissClipLoaded = true;
                }));
        }

        private void OnHitVolumeChange(float value) => HitSoundPatches.testHitSound.volume = value;
        private void OnMissVolumeChange(float value) => HitSoundPatches.testMissSound.volume = value;

        private TootTallySettingDropdown CreateDropdownFromFolder(string folderName, ConfigEntry<string> config, string defaultValue)
        {
            var folderNames = new List<string> { defaultValue };
            var folderPath = Path.Combine(Paths.BepInExRootPath, folderName);
            if (Directory.Exists(folderPath))
            {
                var directories = Directory.GetFiles(folderPath).ToList();
                directories.ForEach(d =>
                {
                    if (Path.GetExtension(d).ToLower().Contains("wav"))
                        folderNames.Add(Path.GetFileNameWithoutExtension(d));
                });
            }
            AddLabel(folderName, folderName, 24, TMPro.FontStyles.Normal, TMPro.TextAlignmentOptions.BottomLeft);
            return AddDropdown($"{folderName}Dropdown", config, folderNames.ToArray());
        }
    }
}
