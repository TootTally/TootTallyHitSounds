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

        private TootTallySettingSlider _volumeSlider;
        private TootTallySettingDropdown _dropdown;

        public CustomHitSoundSettingPage() : base("HitSounds", "HitSounds", 40f, new Color(0, 0, 0, 0), _pageBtnColors)
        {
            _volumeSlider = AddSlider("Volume", 0, 1, Plugin.Instance.Volume, false);
            _dropdown = CreateDropdownFromFolder("HitSounds", Plugin.Instance.HitSoundName, Plugin.DEFAULT_HITSOUND);
            AddButton("Test Sound", TestSound);
        }

        public override void Initialize()
        {
            base.Initialize();
            _volumeSlider.slider.onValueChanged.AddListener(OnVolumeChange);
            _dropdown.dropdown.onValueChanged.AddListener(OnHitSoundChange);
        }

        private void TestSound()
        {
            if (HitSoundPatches.isClipLoaded)
                HitSoundPatches.testHitSound.Play();
        }

        private void OnHitSoundChange(int _)
        {
            HitSoundPatches.isClipLoaded = false;
            if (Plugin.Instance.HitSoundName.Value != DEFAULT_HITSOUND)
                Plugin.Instance.StartCoroutine(HitSoundPatches.TryLoadingAudioClipLocal($"{Plugin.Instance.HitSoundName.Value}.wav", clip =>
                {
                    HitSoundPatches.testHitSound.clip = clip;
                    HitSoundPatches.isClipLoaded = true;
                }));
        }

        private void OnVolumeChange(float value) => HitSoundPatches.testHitSound.volume = value;

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
