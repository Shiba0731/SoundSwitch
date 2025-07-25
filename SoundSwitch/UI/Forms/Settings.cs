﻿/********************************************************************
 * Copyright (C) 2015 Jeroen Pelgrims
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
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;
using NAudio.CoreAudioApi;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using SoundSwitch.Audio.Manager;
using SoundSwitch.Common.Framework.Audio.Device;
using SoundSwitch.Common.Framework.Icon;
using SoundSwitch.Framework;
using SoundSwitch.Framework.Audio;
using SoundSwitch.Framework.Banner.BannerPosition;
using SoundSwitch.Framework.Banner.MicrophoneMute;
using SoundSwitch.Framework.Configuration;
using SoundSwitch.Framework.DeviceCyclerManager;
using SoundSwitch.Framework.Factory;
using SoundSwitch.Framework.Logger.Configuration;
using SoundSwitch.Framework.NotificationManager;
using SoundSwitch.Framework.Profile;
using SoundSwitch.Framework.Profile.Trigger;
using SoundSwitch.Framework.TrayIcon.IconChanger;
using SoundSwitch.Framework.TrayIcon.IconDoubleClick;
using SoundSwitch.Framework.TrayIcon.TooltipInfoManager;
using SoundSwitch.Framework.TrayIcon.TooltipInfoManager.TootipInfo;
using SoundSwitch.Framework.Updater;
using SoundSwitch.Framework.WinApi.Keyboard;
using SoundSwitch.Localization;
using SoundSwitch.Localization.Factory;
using SoundSwitch.Model;
using SoundSwitch.Properties;
using SoundSwitch.UI.Component;
using SoundSwitch.UI.Component.ListView;
using SoundSwitch.Util;
using SoundSwitch.Util.Url;

namespace SoundSwitch.UI.Forms;

public sealed partial class SettingsForm : Form
{
    private static readonly Icon ResourceSettingsIcon = Resources.SettingsIcon;

    private bool _loaded;
    private IAudioDeviceLister _audioDeviceLister;

    public SettingsForm(IAudioDeviceLister audioDeviceLister)
    {
        _audioDeviceLister = audioDeviceLister;
        // Form itself
        InitializeComponent();
        Icon = ResourceSettingsIcon;
        Text = AssemblyUtils.GetReleaseState() == AssemblyUtils.ReleaseState.Beta
            ? $"{SettingsStrings.settings} {AssemblyUtils.GetReleaseState()}"
            : SettingsStrings.settings;
        LocalizeForm();

        new ToolTip().SetToolTip(closeButton, SettingsStrings.buttonClose_tooltip);

        hotKeyControl.HotKey = AppConfigs.Configuration.PlaybackHotKey;
        hotKeyControl.Tag =
            new Tuple<HotKeyAction, HotKey>(HotKeyAction.Playback, AppConfigs.Configuration.PlaybackHotKey);
        hotKeyControl.Enabled = hotKeyCheckBox.Checked = AppConfigs.Configuration.PlaybackHotKey.Enabled;

        muteHotKey.HotKey = AppConfigs.Configuration.MuteRecordingHotKey;
        muteHotKey.Tag = new Tuple<HotKeyAction, HotKey>(HotKeyAction.Mute, AppConfigs.Configuration.MuteRecordingHotKey);
        muteHotKey.Enabled = muteHotKeyCheckBox.Checked = AppConfigs.Configuration.MuteRecordingHotKey.Enabled;

        new ToolTip().SetToolTip(hotKeyCheckBox, SettingsStrings.hotkey_tooltip);

        // Settings - Basic
        startWithWindowsCheckBox.Checked = AppModel.Instance.RunAtStartup;

        new IconChangerFactory().ConfigureListControl(iconChangeChoicesComboBox);
        iconChangeChoicesComboBox.SelectedValue = AppConfigs.Configuration.SwitchIcon;
        new ToolTip().SetToolTip(iconChangeChoicesComboBox, SettingsStrings.iconChange_tooltip);

        new IconDoubleClickFactory().ConfigureListControl(iconDoubleClickComboBox);
        iconDoubleClickComboBox.SelectedValue = AppConfigs.Configuration.IconDoubleClick;
        new ToolTip().SetToolTip(iconDoubleClickComboBox, SettingsStrings.iconDoubleClick_tooltip);

        // Settings - Audio
        switchCommunicationDeviceCheckBox.Checked = AppModel.Instance.SetCommunications;
        new ToolTip().SetToolTip(switchCommunicationDeviceCheckBox, SettingsStrings.communicationsDevice_tooltip);

        foregroundAppCheckbox.Checked = AppModel.Instance.SwitchForegroundProgram;
        new ToolTip().SetToolTip(foregroundAppCheckbox, SettingsStrings.foregroundApp_tooltip);

        quickMenuCheckbox.DataBindings.Add(nameof(CheckBox.Checked), AppModel.Instance, nameof(AppModel.QuickMenuEnabled), false, DataSourceUpdateMode.OnPropertyChanged);
        new ToolTip().SetToolTip(quickMenuCheckbox, SettingsStrings.quickMenu_tooltip);

        keepVolumeCheckbox.DataBindings.Add(nameof(CheckBox.Checked), AppModel.Instance, nameof(AppModel.KeepVolumeEnabled), false, DataSourceUpdateMode.OnPropertyChanged);
        new ToolTip().SetToolTip(keepVolumeCheckbox, SettingsStrings.keepVolume_tooltip);

        new TooltipInfoFactory().ConfigureListControl(tooltipInfoComboBox);
        tooltipInfoComboBox.SelectedValue = TooltipInfoManager.CurrentTooltipInfo;

        new DeviceCyclerFactory().ConfigureListControl(cycleThroughComboBox);
        cycleThroughComboBox.SelectedValue = DeviceCyclerManager.CurrentCycler;
        new ToolTip().SetToolTip(cycleThroughComboBox, SettingsStrings.cycleThrough_tooltip);

        // Settings - Notification
        var notificationFactory = new NotificationFactory();
        notificationFactory.ConfigureListControl(notificationComboBox);
        notificationComboBox.SelectedValue = AppModel.Instance.NotificationSettings;
        new ToolTip().SetToolTip(notificationComboBox, SettingsStrings.notification_tooltip);

        new BannerPositionFactory().ConfigureListControl(positionComboBox);
        positionComboBox.SelectedValue = AppModel.Instance.BannerPosition;
        new ToolTip().SetToolTip(positionComboBox, SettingsStrings.position_tooltip);

        var onScreenTimeTooltip = new ToolTip();
        onScreenTimeTooltip.SetToolTip(onScreenUpDown, SettingsStrings.notification_banner_onscreen_time_tooltip);
        onScreenTimeTooltip.SetToolTip(onScreenTimeLabel, SettingsStrings.notification_banner_onscreen_time_tooltip);
        onScreenUpDown.DataBindings.Add(nameof(NumericUpDown.Value), AppModel.Instance, nameof(AppModel.BannerOnScreenTimeSecs), false, DataSourceUpdateMode.OnPropertyChanged);

        new ToolTip().SetToolTip(singleNotificationCheckbox, SettingsStrings.notification_single_tooltip);
        singleNotificationCheckbox.DataBindings.Add(nameof(CheckBox.Checked), AppModel.Instance, nameof(AppModel.IsSingleNotification), false, DataSourceUpdateMode.OnPropertyChanged);

        usePrimaryScreenCheckbox.Checked = AppModel.Instance.NotifyUsingPrimaryScreen;
        new ToolTip().SetToolTip(usePrimaryScreenCheckbox, SettingsStrings.usePrimaryScreen_tooltip);

        new MicrophoneMuteFactory().ConfigureListControl(microphoneMuteComboBox);
        microphoneMuteComboBox.SelectedValue = AppModel.Instance.MicrophoneMuteNotification;
        new ToolTip().SetToolTip(microphoneMuteComboBox, SettingsStrings.banner_mute_tooltip);

        bannerGroupBox.Enabled = AppModel.Instance.NotificationSettings == NotificationTypeEnum.BannerNotification;

        selectSoundFileDialog.Filter = SettingsStrings.audioFiles + @" (*.wav;*.mp3)|*.wav;*.mp3;*.aiff";
        selectSoundFileDialog.FileOk += SelectSoundFileDialogOnFileOk;
        selectSoundFileDialog.CheckFileExists = true;
        selectSoundFileDialog.CheckPathExists = true;

        var supportCustomSound = notificationFactory.Get(AppModel.Instance.NotificationSettings).SupportCustomSound();
        selectSoundButton.Visible = supportCustomSound;
        new ToolTip().SetToolTip(selectSoundButton, SettingsStrings.selectSoundButton_tooltip);

        DeleteSoundButton_Visible(supportCustomSound);
        new ToolTip().SetToolTip(deleteSoundButton, SettingsStrings.disableCustomSound_tooltip);

        // Settings - Update
        includeBetaVersionsCheckBox.Checked = AppModel.Instance.IncludeBetaVersions;

        switch (AppModel.Instance.UpdateMode)
        {
            case UpdateMode.Silent:
                updateSilentRadioButton.Checked = true;
                break;
            case UpdateMode.Notify:
                updateNotifyRadioButton.Checked = true;
                break;
            case UpdateMode.Never:
                updateNeverRadioButton.Checked = true;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        new ToolTip().SetToolTip(updateSilentRadioButton, SettingsStrings.updateInstallAutomatically_tooltip);
        new ToolTip().SetToolTip(updateNotifyRadioButton, SettingsStrings.updateNotify_tooltip);
        new ToolTip().SetToolTip(updateNeverRadioButton, SettingsStrings.updateNever_tooltip);

        new ToolTip().SetToolTip(includeBetaVersionsCheckBox, SettingsStrings.updateIncludeBetaVersions_tooltip);

        // Settings - Language
        new LanguageFactory().ConfigureListControl(languageComboBox);
        languageComboBox.SelectedValue = AppModel.Instance.Language;

        muteHotKey.Visible = false;
        muteHotKeyCheckBox.Visible = false;
        toggleMuteLabel.Visible = false;

        telemetryCheckbox.DataBindings.Add(nameof(CheckBox.Checked), AppModel.Instance, nameof(AppModel.Telemetry), false, DataSourceUpdateMode.OnPropertyChanged);
        new ToolTip().SetToolTip(telemetryCheckbox, SettingsStrings.telemetry_tooltip);

        PopulateSettings();

        _loaded = true;
    }

    private void PopulateSettings()
    {
        PopulateAudioDevices();
        playbackListView.SetGroupsState(ListViewGroupState.Collapsible);
        recordingListView.SetGroupsState(ListViewGroupState.Collapsible);
        // Profiles
        PopulateProfiles();
    }

    private void PopulateProfiles()
    {
        profilesListView.Columns.Add(SettingsStrings.profile_name, 50, HorizontalAlignment.Left);
        profilesListView.Columns.Add(SettingsStrings.profile_program, 100, HorizontalAlignment.Left);
        profilesListView.Columns.Add(SettingsStrings.hotkey, 150, HorizontalAlignment.Left);
        profilesListView.Columns.Add(SettingsStrings.playback, 150, HorizontalAlignment.Left);
        profilesListView.Columns.Add(SettingsStrings.recording, 150, HorizontalAlignment.Left);
        profilesListView.Columns.Add(SettingsStrings.communication, 150, HorizontalAlignment.Left);
        profilesListView.Columns.Add(SettingsStrings.communication, 150, HorizontalAlignment.Left);

        RefreshProfiles();
    }

    public void RefreshProfiles()
    {
        ListViewItem ProfileToListViewItem(Profile profile)
        {
            var listViewItem = new ListViewItem(profile.Name) { Tag = profile };
            Icon appIcon = null;
            DeviceFullInfo recording = null;
            DeviceFullInfo playback = null;
            DeviceFullInfo communication = null;
            DeviceFullInfo recordingCommunication = null;

            var applicationTrigger = profile.Triggers.FirstOrDefault(trig => trig.Type == TriggerFactory.Enum.Process);
            var hotkeyTrigger = profile.Triggers.FirstOrDefault(trig => trig.Type == TriggerFactory.Enum.HotKey);

            if (applicationTrigger != null)
            {
                try
                {
                    appIcon = IconExtractor.Extract(applicationTrigger.ApplicationPath, 0, false);
                }
                catch
                {
                    appIcon = Resources.program;
                }
            }

            if (profile.Playback != null)
            {
                playback = _audioDeviceLister.GetDevices(DataFlow.Render, DeviceState.Active).FirstOrDefault(info =>
                    info.Equals(profile.Playback));
            }

            if (profile.Recording != null)
            {
                recording = _audioDeviceLister.GetDevices(DataFlow.Capture, DeviceState.Active).FirstOrDefault(info =>
                    info.Equals(profile.Recording));
            }

            if (profile.Communication != null)
            {
                communication = _audioDeviceLister.GetDevices(DataFlow.Render, DeviceState.Active).FirstOrDefault(info =>
                    info.Equals(profile.Communication));
            }

            if (profile.RecordingCommunication != null)
            {
                recordingCommunication = _audioDeviceLister.GetDevices(DataFlow.Capture, DeviceState.Active).FirstOrDefault(info =>
                    info.Equals(profile.RecordingCommunication));
            }

            listViewItem.SubItems.AddRange(new[]
            {
                new ListViewItem.ListViewSubItem(listViewItem, applicationTrigger?.ApplicationPath.Split('\\').Last() ?? "")
                    { Tag = appIcon },
                new ListViewItem.ListViewSubItem(listViewItem, hotkeyTrigger?.HotKey.ToString() ?? ""),
                new ListViewItem.ListViewSubItem(listViewItem, playback?.NameClean ?? profile.Playback?.ToString() ?? "")
                    { Tag = playback?.SmallIcon },
                new ListViewItem.ListViewSubItem(listViewItem,
                    recording?.NameClean ?? profile.Recording?.ToString() ?? "") { Tag = recording?.SmallIcon },
                new ListViewItem.ListViewSubItem(listViewItem,
                    communication?.NameClean ?? profile.Communication?.ToString() ?? "") { Tag = communication?.SmallIcon },
                new ListViewItem.ListViewSubItem(listViewItem,
                    recordingCommunication?.NameClean ?? profile.RecordingCommunication?.ToString() ?? "") { Tag = recordingCommunication?.SmallIcon },
            });

            return listViewItem;
        }

        profilesListView.Items.Clear();

        foreach (var profile in AppModel.Instance.ProfileManager.Profiles)
        {
            var listViewItem = ProfileToListViewItem(profile);
            profilesListView.Items.Add(listViewItem);
        }

        if (AppModel.Instance.ProfileManager.Profiles.Count <= 0) return;
        foreach (ColumnHeader column in profilesListView.Columns)
            column.Width = -2;
    }

    private void PopulateAudioDevices()
    {
        var selectedDevices = AppModel.Instance.SelectedDevices;
        PopulateAudioList(playbackListView, selectedDevices, _audioDeviceLister.GetDevices(DataFlow.Render, DeviceState.Active | DeviceState.Unplugged));
        PopulateAudioList(recordingListView, selectedDevices, _audioDeviceLister.GetDevices(DataFlow.Capture, DeviceState.Active | DeviceState.Unplugged));
    }

    private void LocalizeForm()
    {
        RightToLeft = new LanguageFactory().Get(AppModel.Instance.Language).IsRightToLeft ? RightToLeft.Yes : RightToLeft.No;
        // TabPages
        playbackTabPage.Text = SettingsStrings.playback;
        playbackListView.Groups[0].Header = SettingsStrings.selected;

        recordingTabPage.Text = SettingsStrings.recording;
        recordingListView.Groups[0].Header = SettingsStrings.selected;

        profileTabPage.Text = SettingsStrings.profile_tab;
        appSettingTabPage.Text = SettingsStrings.settings;
        troubleshootingTabPage.Text = SettingsStrings.troubleshooting;

        // Settings - Basic
        basicSettingsGroupBox.Text = SettingsStrings.basicSettings;
        startWithWindowsCheckBox.Text = SettingsStrings.startWithWindows;
        iconChangeLabel.Text = SettingsStrings.iconChange;
        iconDoubleClickLabel.Text = SettingsStrings.iconDoubleClick;

        // Settings - Audio
        audioSettingsGroupBox.Text = SettingsStrings.audioSettings;
        switchCommunicationDeviceCheckBox.Text = SettingsStrings.communicationsDevice;
        tooltipOnHoverLabel.Text = SettingsStrings.tooltipOnHover;
        cycleThroughLabel.Text = SettingsStrings.cycleThrough;
        foregroundAppCheckbox.Text = SettingsStrings.foregroundApp;
        quickMenuCheckbox.Text = SettingsStrings.quickMenu;
        keepVolumeCheckbox.Text = SettingsStrings.keepVolume;

        // Settings - Profile
        profileExplanationLabel.Text = SettingsStrings.profile_explanation;
        addProfileButton.Text = SettingsStrings.buttonAdd;
        deleteProfileButton.Text = SettingsStrings.buttonDelete;
        editProfileButton.Text = SettingsStrings.buttonEdit;

        // Settings - Update
        updateSettingsGroupBox.Text = SettingsStrings.updateSettings;
        updateSilentRadioButton.Text = SettingsStrings.updateInstallAutomatically;
        updateNotifyRadioButton.Text = SettingsStrings.updateNotify;
        updateNeverRadioButton.Text = SettingsStrings.updateNever;
        includeBetaVersionsCheckBox.Text = SettingsStrings.updateIncludeBetaVersions;
        telemetryCheckbox.Text = SettingsStrings.telemetry;

        // Settings - Language
        languageGroupBox.Text = SettingsStrings.language;

        // Settings - Notification
        notificationsGroupBox.Text = SettingsStrings.notification;
        bannerGroupBox.Text = SettingsStrings.notification_bannerOptions;
        positionLabel.Text = SettingsStrings.position;
        onScreenTimeLabel.Text = SettingsStrings.notification_banner_onscreen_time;
        secondsLabel.Text = SettingsStrings.seconds;
        microphoneMuteLabel.Text = SettingsStrings.banner_mute;
        usePrimaryScreenCheckbox.Text = SettingsStrings.usePrimaryScreen;
        singleNotificationCheckbox.Text = SettingsStrings.notification_single;

        // Settings - Troubleshooting
        resetAudioDevicesGroupBox.Text = SettingsStrings.resetAudioDevices;
        resetAudioDevicesLabel.Text = SettingsStrings.resetAudioDevices_desc;
        resetAudioDevicesButton.Text = SettingsStrings.buttonReset;

        exportLogFilesGroupBox.Text = SettingsStrings.exportLogFiles;
        exportLogFilesLabel.Text = SettingsStrings.exportLogFiles_desc;
        exportLogFilesButton.Text = SettingsStrings.buttonExport;

        configFileGroupBox.Text = SettingsStrings.importExportConfigFile;
        configFileLabel.Text = SettingsStrings.importExportConfigFile_desc;
        exportConfigFileButton.Text = SettingsStrings.buttonExport;
        importConfigFileButton.Text = SettingsStrings.buttonImport;

        appNameLabel.Text = Application.ProductName;
        troubleshootingLabel.Text = SettingsStrings.troubleshooting_desc;
        gitHubHelpLinkLabel.Text = SettingsStrings.link_help;
        discordCommunityLinkLabel.Text = SettingsStrings.link_community;
        donateLinkLabel.Text = SettingsStrings.link_donate;

        // Misc
        hotKeyCheckBox.Text = SettingsStrings.hotkeyEnabled;
        closeButton.Text = SettingsStrings.buttonClose;
        switchDeviceLabel.Text = SettingsStrings.switchDevice;
        toggleMuteLabel.Text = SettingsStrings.mute_toggle_label;
        muteHotKeyCheckBox.Text = SettingsStrings.hotkeyEnabled;

        addProfileButton.Image = Resources.profile_menu_add;
        editProfileButton.Image = Resources.profile_menu_edit;
        deleteProfileButton.Image = Resources.profile_menu_delete;
    }

    private void CloseButton_Click(object sender, EventArgs e)
    {
        Close();
    }

    private void TabControl_SelectedIndexChanged(object sender, EventArgs e)
    {
        var tabControlSender = (TabControl)sender;
        if (tabControlSender.SelectedTab == playbackTabPage)
        {
            SetHotKeyFieldsVisibility(true, false);
            SetHotKeyValues(AppConfigs.Configuration.PlaybackHotKey, HotKeyAction.Playback);
        }
        else if (tabControlSender.SelectedTab == recordingTabPage)
        {
            SetHotKeyFieldsVisibility(true, true);
            SetHotKeyValues(AppConfigs.Configuration.RecordingHotKey, HotKeyAction.Recording);
        }
        else
        {
            SetHotKeyFieldsVisibility(false, false);
        }
    }

    private void SetHotKeyValues(HotKey hotkey, HotKeyAction action)
    {
        hotKeyControl.HotKey = hotkey;
        hotKeyControl.Tag = new Tuple<HotKeyAction, HotKey>(action, hotkey);
        hotKeyCheckBox.Checked = hotkey.Enabled;
    }

    private void SetHotKeyFieldsVisibility(bool switchHotKeyVisibility, bool muteHotKeyVisibility)
    {
        hotKeyControl.Visible = switchHotKeyVisibility;
        hotKeyCheckBox.Visible = switchHotKeyVisibility;
        switchDeviceLabel.Visible = switchHotKeyVisibility;

        muteHotKey.Visible = muteHotKeyVisibility;
        muteHotKeyCheckBox.Visible = muteHotKeyVisibility;
        toggleMuteLabel.Visible = muteHotKeyVisibility;
    }

    private void SelectSoundFileDialogOnFileOk(object sender, CancelEventArgs cancelEventArgs)
    {
        try
        {
            AppModel.Instance.CustomNotificationSound = new CachedSound(selectSoundFileDialog.FileName);
        }
        catch (Exception)
        {
            MessageBox.Show(@"Please select another file", @"Invalid Sound file", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        deleteSoundButton.Visible = true;
    }


    private void DeleteSoundButton_Visible(bool supportCustomSound)
    {
        deleteSoundButton.Visible = supportCustomSound && AppModel.Instance.CustomNotificationSound != null;
    }

    private void HotKeyCheckBox_CheckedChanged(object sender, EventArgs e)
    {
        ForceSetHotkeys(sender, hotKeyControl);
    }

    private void MuteHotKeyCheckBox_CheckedChanged(object sender, EventArgs e)
    {
        ForceSetHotkeys(sender, muteHotKey);
    }

    private void ForceSetHotkeys(object sender, HotKeyTextBox hotKeyTextBox)
    {
        var control = (CheckBox)sender;
        if (hotKeyTextBox.Tag == null) return;

        var (action, hotKey) = (Tuple<HotKeyAction, HotKey>)hotKeyTextBox.Tag;
        var currentState = hotKey.Enabled;
        hotKeyTextBox.Enabled = hotKey.Enabled = control.Checked;
        if (currentState != hotKey.Enabled)
            AppModel.Instance.SetHotkeyCombination(hotKey, action, true);
    }

    private void HotKeyControl_HotKeyChanged(object sender, HotKeyTextBox.Event e)
    {
        var control = (HotKeyTextBox)sender;
        var tuple = (Tuple<HotKeyAction, HotKey>)control.Tag;
        if (tuple == null) return;

        var newTuple = new Tuple<HotKeyAction, HotKey>(tuple.Item1, control.HotKey);
        hotKeyControl.Tag = newTuple;

        AppModel.Instance.SetHotkeyCombination(newTuple.Item2, newTuple.Item1);
    }

    #region Device List Playback

    private void PopulateAudioList(ListView listView, IEnumerable<DeviceInfo> selectedDevices, IEnumerable<DeviceFullInfo> audioDevices)
    {
        try
        {
            PopulateDeviceTypeGroups(listView);

            listView.SmallImageList = new ImageList
            {
                ImageSize = new Size(32, 32),
                ColorDepth = ColorDepth.Depth32Bit
            };

            listView.Columns.Add("Device", -3, HorizontalAlignment.Center);
            var items =
                audioDevices.Select(device =>
                    {
                        AddDeviceIconSmallImage(device, listView);

                        return GenerateListViewItem(device, selectedDevices, listView);
                    })
                    .OrderBy(item => item.Text);
            listView.Items.AddRange(items.ToArray());
        }
        finally
        {
            listView.ItemCheck += ListViewItemChecked;
        }
    }

    /// <summary>
    /// Using the information of the AudioDeviceWrapper, generate a ListViewItem
    /// </summary>
    /// <param name="device"></param>
    /// <param name="selected"></param>
    /// <param name="listView"></param>
    /// <returns></returns>
    private ListViewItem GenerateListViewItem(DeviceFullInfo device, IEnumerable<DeviceInfo> selected, ListView listView)
    {
        var listViewItem = new ListViewItem
        {
            Text = device.NameClean,
            ImageKey = device.IconPath,
            Tag = device
        };
        var isSelected = selected.Contains(device);
        if (device.State == DeviceState.Active && isSelected)
            listViewItem.Group = listView.Groups["selectedGroup"];
        else
            listViewItem.Group = GetGroup(device.State, listView);

        listViewItem.Checked = isSelected;
        return listViewItem;
    }

    /// <summary>
    /// Using the DeviceClassIconPath, get the Icon
    /// </summary>
    /// <param name="device"></param>
    /// <param name="listView"></param>
    private void AddDeviceIconSmallImage(DeviceFullInfo device, ListView listView)
    {
        if (!listView.SmallImageList.Images.ContainsKey(device.IconPath))
            listView.SmallImageList.Images.Add(device.IconPath, device.LargeIcon);
    }

    private void ListViewItemChecked(object sender, ItemCheckEventArgs e)
    {
        try
        {
            switch (e.NewValue)
            {
                case CheckState.Checked:
                    AppModel.Instance.SelectDevice((DeviceFullInfo)((ListView)sender).Items[e.Index].Tag);
                    break;
                case CheckState.Unchecked:
                    AppModel.Instance.UnselectDevice((DeviceFullInfo)((ListView)sender).Items[e.Index].Tag);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        catch (Exception exception)
        {
            e.NewValue = e.CurrentValue;
        }
    }

    #endregion

    #region Groups

    /// <summary>
    /// Get the ListViewItem group in which the device belongs.
    /// </summary>
    /// <param name="deviceState"></param>
    /// <param name="listView"></param>
    /// <returns></returns>
    private ListViewGroup GetGroup(DeviceState deviceState, ListView listView)
    {
        switch (deviceState)
        {
            case DeviceState.Active:
                return listView.Groups[nameof(DeviceState.Active)];
            default:
                return listView.Groups[nameof(DeviceState.NotPresent)];
        }
    }

    private void PopulateDeviceTypeGroups(ListView listView)
    {
        listView.Groups.Add(new ListViewGroup(nameof(DeviceState.Active), SettingsStrings.connected));
        listView.Groups.Add(new ListViewGroup(nameof(DeviceState.NotPresent), SettingsStrings.disconnected));
    }

    #endregion

    #region Profiles

    private void AddProfileButton_Click(object sender, EventArgs e)
    {
        var form = new UpsertProfileExtended(new Profile(), _audioDeviceLister.GetDevices(DataFlow.Render, DeviceState.Active | DeviceState.Unplugged), _audioDeviceLister.GetDevices(DataFlow.Capture, DeviceState.Active | DeviceState.Unplugged), this);
        form.Show(this);
    }

    private void ProfilesListView_SelectedIndexChanged(object sender, EventArgs e)
    {
        editProfileButton.Enabled = profilesListView.SelectedIndices.Count == 1;
        deleteProfileButton.Enabled = profilesListView.SelectedIndices.Count > 0;
    }

    private void DeleteProfileButton_Click(object sender, EventArgs e)
    {
        if (profilesListView.SelectedItems.Count <= 0) return;

        var profiles = profilesListView.SelectedItems.Cast<ListViewItem>()
            .Select(item => (Profile)item.Tag);
        AppModel.Instance.ProfileManager.DeleteProfiles(profiles);
        deleteProfileButton.Enabled = false;
        editProfileButton.Enabled = false;
        RefreshProfiles();
    }

    private void EditProfileButton_Click(object sender, EventArgs e)
    {
        if (profilesListView.SelectedItems.Count <= 0) return;

        var profile = (Profile)profilesListView.SelectedItems[0].Tag;
        var form = new UpsertProfileExtended(profile, _audioDeviceLister.GetDevices(DataFlow.Render, DeviceState.Active | DeviceState.Unplugged), _audioDeviceLister.GetDevices(DataFlow.Capture, DeviceState.Active | DeviceState.Unplugged), this, true);
        form.Show(this);
    }

    private void ProfilesListView_DoubleClick(object sender, EventArgs e)
    {
        EditProfileButton_Click(sender, e);
    }

    #endregion

    #region Basic Settings

    private void RunAtStartup_CheckedChanged(object sender, EventArgs e)
    {
        var ras = startWithWindowsCheckBox.Checked;
        try
        {
            AppModel.Instance.RunAtStartup = ras;
        }
        catch (Exception ex)
        {
            MessageBox.Show(@"Error changing run at startup setting: " + ex.Message);
            startWithWindowsCheckBox.Checked = AppModel.Instance.RunAtStartup;
        }
    }

    private void SetComboBoxValue<T>(object sender, Action<DisplayEnumObject<T>> saveSetting) where T : Enum, IConvertible
    {
        if (!_loaded) return;
        var selectedItem = (DisplayEnumObject<T>)((ComboBox)sender).SelectedItem;
        if (selectedItem == null) return;
        saveSetting(selectedItem);
    }

    private void IconChangeChoicesComboBox_SelectedIndexChanged(object sender, EventArgs e)
    {
        SetComboBoxValue<IconChangerEnum>(sender, selectedItem =>
        {
            AppConfigs.Configuration.SwitchIcon = selectedItem.Enum;
            AppConfigs.Configuration.Save();
            new IconChangerFactory().Get(selectedItem.Enum).ChangeIcon(AppModel.Instance.TrayIcon);
        });
    }

    private void IconDoubleClickComboBox_SelectedIndexChanged(object sender, EventArgs e)
    {
        SetComboBoxValue<IconDoubleClickEnum>(sender, selectedItem =>
        {
            AppModel.Instance.IconDoubleClick = selectedItem.Enum;
        });
    }

    #endregion

    #region Audio Settings

    private void CommunicationCheckbox_CheckedChanged(object sender, EventArgs e)
    {
        var comm = switchCommunicationDeviceCheckBox.Checked;
        try
        {
            AppModel.Instance.SetCommunications = comm;
        }
        catch (Exception ex)
        {
            MessageBox.Show(@"Error changing run at startup setting: " + ex.Message);
            switchCommunicationDeviceCheckBox.Checked = AppModel.Instance.SetCommunications;
        }
    }

    private void ForegroundAppCheckbox_CheckedChanged(object sender, EventArgs e)
    {
        AppModel.Instance.SwitchForegroundProgram = foregroundAppCheckbox.Checked;
    }

    private void KeepVolumeCheckbox_CheckedChanged(object sender, EventArgs e)
    {
        AppModel.Instance.KeepVolumeEnabled = keepVolumeCheckbox.Checked;
    }

    private void TooltipInfoComboBox_SelectedValueChanged(object sender, EventArgs e)
    {
        SetComboBoxValue<TooltipInfoTypeEnum>(sender, selectedItem =>
        {
            TooltipInfoManager.CurrentTooltipInfo = selectedItem.Enum;
        });
    }

    private void CyclerComboBox_SelectedValueChanged(object sender, EventArgs e)
    {
        SetComboBoxValue<DeviceCyclerTypeEnum>(sender, selectedItem =>
        {
            DeviceCyclerManager.CurrentCycler = selectedItem.Enum;
        });
    }

    #endregion

    #region Update Settings

    private void UpdateSilentRadioButton_CheckedChanged(object sender, EventArgs e)
    {
        if (updateSilentRadioButton.Checked)
            AppModel.Instance.UpdateMode = UpdateMode.Silent;
    }

    private void UpdateNotifyRadioButton_CheckedChanged(object sender, EventArgs e)
    {
        if (updateNotifyRadioButton.Checked)
            AppModel.Instance.UpdateMode = UpdateMode.Notify;
    }

    private void UpdateNeverRadioButton_CheckedChanged(object sender, EventArgs e)
    {
        if (updateNeverRadioButton.Checked)
            AppModel.Instance.UpdateMode = UpdateMode.Never;
    }

    private void BetaVersionCheckbox_CheckedChanged(object sender, EventArgs e)
    {
        AppModel.Instance.IncludeBetaVersions = includeBetaVersionsCheckBox.Checked;
    }

    #endregion

    #region Language

    private void LanguageComboBox_SelectedIndexChanged(object sender, EventArgs e)
    {
        SetComboBoxValue<Language>(sender, selectedItem =>
        {
            if (AppModel.Instance.Language == selectedItem.Enum) return;
            AppModel.Instance.Language = selectedItem.Enum;

            if (MessageBox.Show(SettingsStrings.languageRestartRequired,
                    SettingsStrings.restartRequired_caption,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes) return;
            Program.RestartApp();
        });
    }

    #endregion

    #region Notification

    private void NotificationComboBox_SelectedValueChanged(object sender, EventArgs e)
    {
        SetComboBoxValue<NotificationTypeEnum>(sender, selectedItem =>
        {
            var notificationType = selectedItem.Enum;
            if (notificationType == AppModel.Instance.NotificationSettings) return;

            var supportCustomSound = new NotificationFactory().Get(notificationType).SupportCustomSound();
            selectSoundButton.Visible = supportCustomSound;
            DeleteSoundButton_Visible(supportCustomSound);

            bannerGroupBox.Enabled = notificationType == NotificationTypeEnum.BannerNotification;
            AppModel.Instance.NotificationSettings = notificationType;
        });
    }

    private void PositionComboBox_SelectedValueChanged(object sender, EventArgs e)
    {
        SetComboBoxValue<BannerPositionEnum>(sender, selectedItem =>
        {
            AppModel.Instance.BannerPosition = selectedItem.Enum;
        });
    }

    private void SelectSoundButton_Click(object sender, EventArgs e)
    {
        selectSoundFileDialog.ShowDialog(this);
    }

    private void DeleteSoundButton_Click(object sender, EventArgs e)
    {
        AppModel.Instance.CustomNotificationSound = null;
        deleteSoundButton.Visible = false;
    }

    private void UsePrimaryScreenCheckbox_CheckedChanged(object sender, EventArgs e)
    {
        AppModel.Instance.NotifyUsingPrimaryScreen = usePrimaryScreenCheckbox.Checked;
    }

    private void MicrophoneMuteNotificationComboBox_SelectedValueChanged(object sender, EventArgs e)
    {
        SetComboBoxValue<MicrophoneMuteEnum>(sender, selectedItem =>
        {
            AppModel.Instance.MicrophoneMuteNotification = selectedItem.Enum;
        });
    }

    #endregion

    #region Troubleshooting

    private void ResetAudioDevicesButton_Click(object sender, EventArgs e)
    {
        AudioSwitcher.Instance.ResetProcessDeviceConfiguration();
    }

    private void PrepareZipArchive(string title, string fileName, Action<ZipArchive> exportArchive)
    {
        var saveFileDialog = new SaveFileDialog
        {
            Title = title,
            FileName = fileName,
            DefaultExt = "zip",
            Filter = "Zip Archive (*.zip)|*.zip",
            RestoreDirectory = true,
        };

        if (saveFileDialog.ShowDialog() != DialogResult.OK) return;
        if (File.Exists(saveFileDialog.FileName))
            File.Delete(saveFileDialog.FileName);

        using var archive = ZipFile.Open(saveFileDialog.FileName, ZipArchiveMode.Create);
        exportArchive(archive);
    }

    private void ExportLogFilesButton_Click(object sender, EventArgs e)
    {
        PrepareZipArchive(SettingsStrings.exportLogFiles, "soundswitch_logs", archive =>
        {
            Log.CloseAndFlush();

            var files = Directory.EnumerateFiles(ApplicationPath.Logs, "*.log");
            foreach (var file in files)
                // Add the entry for each file
                archive.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);

            LoggerConfigurator.ConfigureLogger();
        });
    }

    private void ExportConfigFileButton_Click(object sender, EventArgs e)
    {
        PrepareZipArchive(SettingsStrings.exportConfigFile, "soundswitch_config", archive =>
        {
            const string configFile = "SoundSwitchConfiguration.json";
            archive.CreateEntryFromFile(Path.Combine(ApplicationPath.Default, configFile), configFile, CompressionLevel.Optimal);
        });
    }

    private void ImportConfigFileButton_Click(object sender, EventArgs e)
    {
        const string configFile = "SoundSwitchConfiguration.json";
        var filePath = Path.Combine(ApplicationPath.Default, configFile);

        var openFileDialog = new OpenFileDialog
        {
            Title = SettingsStrings.importConfigFile,
            Filter = "Zip Archive (*.zip)|*.zip",
            RestoreDirectory = true,
        };

        while (true)
        {
            if (openFileDialog.ShowDialog() != DialogResult.OK) return;

            using var archive = ZipFile.Open(openFileDialog.FileName, ZipArchiveMode.Read);
            var entry = archive.GetEntry(configFile);
            if (entry == null)
            {
                MessageBox.Show(SettingsStrings.importConfigErrorMessage,
                    SettingsStrings.importConfigErrorMessage_caption,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Exclamation);
                continue;
            }

            // Read content of the entry
            string jsonContent;
            try
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                jsonContent = reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                var errorMessage = string.Format(SettingsStrings.importConfigErrorReadingFile, configFile, ex.Message);
                Log.Error(ex, "Error reading configuration file '{ConfigFile}' from archive during import.", configFile);
                MessageBox.Show(errorMessage,
                    SettingsStrings.importConfigErrorMessage_caption,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                continue;
            }

            try
            {
                // Validate JSON structure
                JObject.Parse(jsonContent);

                // Attempt to deserialize
                var config = JsonConvert.DeserializeObject<SoundSwitchConfiguration>(jsonContent);
                if (config == null)
                {
                    var errorMessage = string.Format(SettingsStrings.importConfigErrorDeserializationNull, configFile);
                    Log.Warning("Configuration file '{ConfigFile}' deserialized to null during import.", configFile);
                    MessageBox.Show(errorMessage,
                        SettingsStrings.importConfigErrorMessage_caption,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    continue;
                }
            }
            catch (JsonReaderException jsonEx)
            {
                var errorMessage = string.Format(SettingsStrings.importConfigErrorInvalidJson, configFile, jsonEx.Message);
                Log.Warning(jsonEx, "Configuration file '{ConfigFile}' is not valid JSON during import.", configFile);
                MessageBox.Show(errorMessage,
                    SettingsStrings.importConfigErrorMessage_caption,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                continue;
            }
            catch (Exception ex) // Catches other errors during deserialization
            {
                var errorMessage = string.Format(SettingsStrings.importConfigErrorDeserializationFailed, configFile, ex.Message);
                Log.Warning(ex, "Failed to deserialize configuration file '{ConfigFile}' during import.", configFile);
                MessageBox.Show(errorMessage,
                    SettingsStrings.importConfigErrorMessage_caption,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                continue;
            }

            if (MessageBox.Show(SettingsStrings.importConfigRestartRequired,
                    SettingsStrings.restartRequired_caption,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes) return;

            File.Copy(filePath, Path.ChangeExtension(filePath, ".old"), true);
            entry.ExtractToFile(filePath, true);
            break;
        }

        Program.RestartApp();
    }

    internal static void GitHubHelpLink(object sender, EventArgs e)
    {
        BrowserUtil.OpenUrl("https://github.com/Belphemur/SoundSwitch/discussions");
    }

    internal static void DiscordCommunityLink(object sender, EventArgs e)
    {
        BrowserUtil.OpenUrl("https://discord.gg/gUCw3Ue");
    }

    internal static void DonateLink(object sender, EventArgs e)
    {
        BrowserUtil.OpenUrl($"https://soundswitch.aaflalo.me/?utm_campaign=application&utm_source={Application.ProductVersion}#donate");
    }

    #endregion
}