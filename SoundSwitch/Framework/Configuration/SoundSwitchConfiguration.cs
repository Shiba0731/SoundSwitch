﻿/********************************************************************
 * Copyright (C) 2015-2017 Antoine Aflalo
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 ********************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using NAudio.CoreAudioApi;
using Newtonsoft.Json;
using Serilog;
using SoundSwitch.Audio.Manager;
using SoundSwitch.Common.Framework.Audio.Device;
using SoundSwitch.Framework.Banner.BannerPosition;
using SoundSwitch.Framework.Banner.MicrophoneMute;
using SoundSwitch.Framework.Banner.MicrophoneMute.Type;
using SoundSwitch.Framework.DeviceCyclerManager;
using SoundSwitch.Framework.NotificationManager;
using SoundSwitch.Framework.Profile;
using SoundSwitch.Framework.Profile.Trigger;
using SoundSwitch.Framework.TrayIcon.IconChanger;
using SoundSwitch.Framework.TrayIcon.IconDoubleClick;
using SoundSwitch.Framework.TrayIcon.TooltipInfoManager.TootipInfo;
using SoundSwitch.Framework.Updater;
using SoundSwitch.Framework.Updater.Remind;
using SoundSwitch.Framework.WinApi.Keyboard;
using SoundSwitch.Localization.Factory;

namespace SoundSwitch.Framework.Configuration;

public class SoundSwitchConfiguration : ISoundSwitchConfiguration
{
    // Basic Settings
    public bool FirstRun { get; set; } = true;
    public IconChangerEnum SwitchIcon { get; set; } = IconChangerEnum.Never;
    public IconDoubleClickEnum IconDoubleClick { get; set; } = IconDoubleClickEnum.SwitchDevice;

    // Audio Settings
    public bool ChangeCommunications { get; set; } = false;
    public bool SwitchForegroundProgram { get; set; }

    /// <summary>
    /// Is keep volume enabled
    /// </summary>
    public bool KeepVolumeEnabled { get; set; } = false;

    /// <summary>
    /// Is the quick menu showed when using a hotkey
    /// </summary>
    public bool QuickMenuEnabled { get; set; } = false;
    public TooltipInfoTypeEnum TooltipInfo { get; set; } = TooltipInfoTypeEnum.Playback;
    public DeviceCyclerTypeEnum CyclerType { get; set; } = DeviceCyclerTypeEnum.Available;

    // Update Settings
    public uint UpdateCheckInterval { get; set; } = 3600 * 24; // 24 hours
    public UpdateMode UpdateMode { get; set; } = UpdateMode.Notify;
    public bool IncludeBetaVersions { get; set; } = false;

    /// <summary>
    /// Is telemetry enabled
    /// </summary>
    public bool Telemetry { get; set; } = true;

    // Language Settings
    public Language Language { get; set; } = new LanguageFactory().GetWindowsLanguage();

    // Notification Settings
    public NotificationTypeEnum NotificationSettings { get; set; } = NotificationTypeEnum.BannerNotification;
    public BannerPositionEnum BannerPosition { get; set; } = BannerPositionEnum.TopLeft;
    public MicrophoneMuteEnum MicrophoneMuteNotification { get; set; } = MicrophoneMuteEnum.Persistent;
    public string CustomNotificationFilePath { get; set; }
    public TimeSpan BannerOnScreenTime { get; set; } = TimeSpan.FromSeconds(3);
    public int MaxNumberNotification { get; set; } = 5;
    public bool NotifyUsingPrimaryScreen { get; set; }
    [Obsolete("Replaced by " + nameof(MicrophoneMutePersistent))]
    public bool PersistentMuteNotification { get; set; }

    // Profile Settings
    public HashSet<Profile.Profile> Profiles { get; set; } = new();
    public HashSet<string> SelectedPlaybackDeviceListId { get; } = new();
    public HashSet<string> SelectedRecordingDeviceListId { get; } = new();
    public HashSet<DeviceInfo> SelectedDevices { get; set; } = new();
    public HotKey PlaybackHotKey { get; set; } = new(Keys.F11, HotKey.ModifierKeys.Alt | HotKey.ModifierKeys.Control);
    public HotKey RecordingHotKey { get; set; } = new(Keys.F7, HotKey.ModifierKeys.Alt | HotKey.ModifierKeys.Control);
    public HotKey MuteRecordingHotKey { get; set; } = new(Keys.M, HotKey.ModifierKeys.Control | HotKey.ModifierKeys.Alt);
    
    [Obsolete("Feature has been removed")]
    public bool AutoAddNewConnectedDevices { get; set; } = false;
    
    public DateTime LastDonationNagTime { get; set; }

    [JsonIgnore]
    public TimeSpan TimeBetweenDonateNag { get; set; } = TimeSpan.FromDays(90);

    [Obsolete]
    public bool KeepSystrayIcon { get; set; }

    
    [Obsolete]
    public HashSet<ProfileSetting> ProfileSettings { get; set; } = new();

    public ReleasePostponed Postponed { get; set; }

    /// <summary>
    /// Fields of the config that got migrated
    /// </summary>
    public HashSet<string> MigratedFields { get; } = new();

    public Guid UniqueInstallationId { get; set; } = Guid.NewGuid();

    // Needed by Interface
    [JsonIgnore]
    public string FileLocation { get; set; }

    /// <summary>
    /// Migrate configuration to a new schema
    /// </summary>
    public bool Migrate()
    {
        var migrated = false;
        if (SelectedPlaybackDeviceListId.Count > 0)
        {
            SelectedDevices.UnionWith(
                SelectedPlaybackDeviceListId.Select((s => new DeviceInfo("", s, DataFlow.Render, false, DateTime.UtcNow))));
            SelectedPlaybackDeviceListId.Clear();
            migrated = true;
        }

        if (SelectedRecordingDeviceListId.Count > 0)
        {
            SelectedDevices.UnionWith(
                SelectedRecordingDeviceListId.Select((s => new DeviceInfo("", s, DataFlow.Capture, false, DateTime.UtcNow))));
            SelectedRecordingDeviceListId.Clear();
            migrated = true;
        }

        if (NotificationSettings == NotificationTypeEnum.ToastNotification)
        {
            NotificationSettings = NotificationTypeEnum.BannerNotification;
            migrated = true;
        }

        if (NotificationSettings == NotificationTypeEnum.CustomNotification)
        {
            NotificationSettings = NotificationTypeEnum.SoundNotification;
            migrated = true;
        }
#pragma warning disable 612
#pragma warning disable CS0618 // Type or member is obsolete
        if (!PersistentMuteNotification && !MigratedFields.Contains(nameof(PersistentMuteNotification)))
        {
            MicrophoneMuteNotification = MicrophoneMuteEnum.None;
            MigratedFields.Add(nameof(PersistentMuteNotification));
            migrated = true;
        }
        if (!MigratedFields.Contains(nameof(KeepSystrayIcon)))
        {
            SwitchIcon = KeepSystrayIcon ? IconChangerEnum.Never : IconChangerEnum.Always;
            MigratedFields.Add(nameof(KeepSystrayIcon));
            migrated = true;
        }

        if (!MigratedFields.Contains(nameof(ProfileSettings) + "_final"))
        {
            Profiles = ProfileSettings
                .Select(setting =>
                {
                    var profile = new Profile.Profile
                    {
                        AlsoSwitchDefaultDevice = setting.AlsoSwitchDefaultDevice,
                        Communication = null,
                        Playback = setting.Playback,
                        Name = setting.ProfileName,
                        Recording = setting.Recording
                    };
                    if (setting.HotKey != null)
                    {
                        profile.Triggers.Add(new Trigger(TriggerFactory.Enum.HotKey)
                        {
                            HotKey = setting.HotKey
                        });
                    }

                    if (!string.IsNullOrEmpty(setting.ApplicationPath))
                    {
                        profile.Triggers.Add(new Trigger(TriggerFactory.Enum.Process)
                        {
                            ApplicationPath = setting.ApplicationPath
                        });
                    }

                    return profile;
                })
                .ToHashSet();
            MigratedFields.Add(nameof(ProfileSettings) + "_final");
            migrated = true;
        }

        if (!MigratedFields.Contains(nameof(LastDonationNagTime)))
        {
            LastDonationNagTime = DateTime.UtcNow - TimeSpan.FromDays(10);
            MigratedFields.Add(nameof(LastDonationNagTime));
            migrated = true;
        }

        if (!MigratedFields.Contains($"{nameof(SwitchForegroundProgram)}_force_off"))
        {
            SwitchForegroundProgram = false;
            MigratedFields.Add($"{nameof(SwitchForegroundProgram)}_force_off");
            migrated = true;
        }

        if (!MigratedFields.Contains("CleanupSelectedDevices"))
        {
            SelectedDevices = SelectedDevices.DistinctBy(info => info.NameClean).ToHashSet();
            MigratedFields.Add("CleanupSelectedDevices");
            migrated = true;
        }

        if (!MigratedFields.Contains("CleanupProfilesForeground"))
        {
            foreach (var profile in Profiles)
            {
                profile.SwitchForegroundApp = false;
            }

            MigratedFields.Add("CleanupProfilesForeground");
            migrated = true;
        }

        if (Environment.OSVersion.Version.Major < 10 && !MigratedFields.Contains("ProfileWin7"))
        {
            Profiles = Profiles.Select(profile =>
                {
                    profile.SwitchForegroundApp = false;
                    return profile;
                })
                .ToHashSet();
            MigratedFields.Add("ProfileWin7");
            migrated = true;
        }

        var switchForegroundFix = $"{nameof(SwitchForegroundProgram)}_fix";
        if (!MigratedFields.Contains(switchForegroundFix) && (SwitchForegroundProgram || Profiles.Any(profile => profile.SwitchForegroundApp)))
        {
            AudioSwitcher.Instance.ResetProcessDeviceConfiguration();
            MigratedFields.Add(switchForegroundFix);
            migrated = true;
        }

        return migrated;
#pragma warning restore 612
#pragma warning restore CS0618 // Type or member is obsolete
    }

    public void Save()
    {
        Log.Debug("Saving configuration {configuration}", this);
        ConfigurationManager.SaveConfiguration(this);
    }

    public override string ToString()
    {
        return $"{GetType().Name}({FileLocation})";
    }
}