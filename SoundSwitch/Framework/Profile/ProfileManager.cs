﻿#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using NAudio.CoreAudioApi;
using RailSharp;
using RailSharp.Internal.Result;
using Serilog;
using SoundSwitch.Audio.Manager;
using SoundSwitch.Audio.Manager.Interop.Com.User;
using SoundSwitch.Audio.Manager.Interop.Enum;
using SoundSwitch.Common.Framework.Audio.Device;
using SoundSwitch.Framework.Configuration;
using SoundSwitch.Framework.Profile.Hotkey;
using SoundSwitch.Framework.Profile.Trigger;
using SoundSwitch.Framework.WinApi;
using SoundSwitch.Localization;
using SoundSwitch.Model;
using SoundSwitch.Util;

namespace SoundSwitch.Framework.Profile;

public class ProfileManager
{
    public delegate void ShowError(string errorMessage, string errorTitle);

    private readonly WindowMonitor _windowMonitor;
    private readonly AudioSwitcher _audioSwitcher;
    private readonly IAudioDeviceLister _activeDeviceLister;
    private readonly ShowError _showError;
    private readonly TriggerFactory _triggerFactory;
    private readonly NotificationManager.NotificationManager _notificationManager;

    private Profile? _steamProfile;
    private Profile? _forcedProfile;

    private readonly ConcurrentDictionary<User32.NativeMethods.HWND, Profile> _activeWindowsTrigger = new();

    private readonly Dictionary<string, (Profile Profile, Trigger.Trigger Trigger)> _profileByApplication = new();
    private readonly Dictionary<string, (Profile Profile, Trigger.Trigger Trigger)> _profilesByWindowName = new();
    private readonly Dictionary<string, (Profile Profile, Trigger.Trigger Trigger)> _profilesByUwpApp = new();

    private readonly ProfileHotkeyManager _profileHotkeyManager;
    private readonly ILogger _logger;


    public IReadOnlyCollection<Profile> Profiles => AppConfigs.Configuration.Profiles;

    public ProfileManager(WindowMonitor windowMonitor,
        AudioSwitcher audioSwitcher,
        IAudioDeviceLister activeDeviceLister,
        ShowError showError,
        TriggerFactory triggerFactory,
        NotificationManager.NotificationManager notificationManager)
    {
        _windowMonitor = windowMonitor;
        _audioSwitcher = audioSwitcher;
        _activeDeviceLister = activeDeviceLister;
        _showError = showError;
        _triggerFactory = triggerFactory;
        _notificationManager = notificationManager;
        _profileHotkeyManager = new(this);
        _logger = Log.ForContext(GetType());
    }

    private bool RegisterTriggers(Profile profile, bool onInit = false)
    {
        var success = true;
        foreach (var trigger in profile.Triggers)
        {
            success &= trigger.Type.Match(() => _profileHotkeyManager.Add(trigger.HotKey, profile),
                () =>
                {
                    _profilesByWindowName.Add(trigger.WindowName.ToLower(), (profile, trigger));
                    return true;
                },
                () =>
                {
                    _profileByApplication.Add(trigger.ApplicationPath.ToLower(), (profile, trigger));
                    return true;
                },
                () =>
                {
                    _steamProfile = profile;
                    return true;
                },
                () =>
                {
                    if (!onInit)
                    {
                        return true;
                    }

                    SwitchAudio(profile);
                    return true;
                }, () =>
                {
                    _profilesByUwpApp.Add(trigger.WindowName.ToLower(), (profile, trigger));
                    return true;
                },
                () => true,
                () =>
                {
                    _forcedProfile = profile;

                    SwitchAudio(profile);
                    return true;
                });
        }

        return success;
    }

    private void UnRegisterTriggers(Profile profile)
    {
        foreach (var trigger in profile.Triggers)
        {
            trigger.Type.Switch(() => { _profileHotkeyManager.Remove(trigger.HotKey, profile); },
                () => { _profilesByWindowName.Remove(trigger.WindowName.ToLower()); },
                () => { _profileByApplication.Remove(trigger.ApplicationPath.ToLower()); },
                () => { _steamProfile = null; }, () => { },
                () => { _profilesByUwpApp.Remove(trigger.WindowName.ToLower()); },
                () => { },
                () => { _forcedProfile = null; });
        }
    }

    /// <summary>
    /// Initialize the profile manager. Return the list of Profile that it couldn't register hotkeys for.
    /// </summary>
    /// <returns></returns>
    public Result<ProfileError[], VoidSuccess> Init()
    {
        var errors = Array.Empty<ProfileError>();
        try
        {
            errors = AppConfigs.Configuration.Profiles.Select(profile => (Profile: profile, Failure: ValidateProfile(profile, true).UnwrapFailure()))
                .Select(tuple =>
                {
                    if (tuple.Failure == null)
                    {
                        RegisterTriggers(tuple.Profile, true);
                    }

                    return tuple;
                })
                .Where(tuple => tuple.Failure != null)
                .Select(tuple => new ProfileError(tuple.Profile, tuple.Failure))
                .ToArray();

            RegisterEvents();
            InitializeProfileExistingProcess();

            if (errors.Length > 0)
            {
                _logger.Warning("Couldn't initiate all profiles: {profiles}", errors);
                return errors;
            }

            return Result.Success();
        }
        finally
        {
            _logger.Information("Profile manager initiated {profiles} with {errors} errors", AppConfigs.Configuration.Profiles.Count, errors.Length);
        }
    }

    private void RegisterEvents()
    {
        _windowMonitor.ForegroundChanged += (sender, @event) =>
        {
            Log.Verbose("Foreground changed: [{0}] {1} - {2}", @event.ProcessName, @event.WindowName, @event.WindowClass);
            if (HandleSteamBigPicture(@event)) return;

            if (HandleApplication(@event)) return;

            if (HandleUwpApp(@event)) return;

            if (HandleWindowName(@event)) return;
        };
        _logger.Information("Windows Monitor Registered");

        WindowsAPIAdapter.WindowDestroyed += (sender, @event) => { RestoreState(@event.Hwnd); };
        _logger.Information("Windows Destroyed Registered");

        AppModel.Instance.DefaultDeviceChanged += (sender, audioChangeEvent) =>
        {
            if (HandleDeviceChanged(audioChangeEvent)) return;
        };
    }

    private bool HandleUwpApp(WindowMonitor.Event @event)
    {
        (Profile Profile, Trigger.Trigger Trigger) profileTuple;

        var windowNameLowerCase = @event.WindowName.ToLower();

        profileTuple = _profilesByUwpApp.FirstOrDefault(pair => windowNameLowerCase.Contains(pair.Key)).Value;
        if (profileTuple != default && @event.WindowClass == "ApplicationFrameWindow")
        {
            SaveCurrentState(@event.Hwnd, profileTuple.Profile, profileTuple.Trigger);
            SwitchAudio(profileTuple.Profile);
            return true;
        }

        return false;
    }

    private bool HandleWindowName(WindowMonitor.Event @event)
    {
        var windowNameLower = @event.WindowName.ToLower();

        var profileTuple = _profilesByWindowName.FirstOrDefault(pair => windowNameLower.Contains(pair.Key)).Value;
        if (profileTuple != default)
        {
            SaveCurrentState(@event.Hwnd, profileTuple.Profile, profileTuple.Trigger);
            SwitchAudio(profileTuple.Profile, @event.ProcessId);
            return true;
        }

        return false;
    }

    private bool HandleApplication(WindowMonitor.Event @event)
    {
        (Profile Profile, Trigger.Trigger Trigger) profileTuple;
        if (_profileByApplication.TryGetValue(@event.ProcessName.ToLower(), out profileTuple))
        {
            SaveCurrentState(@event.Hwnd, profileTuple.Profile, profileTuple.Trigger);
            SwitchAudio(profileTuple.Profile, @event.ProcessId);
            return true;
        }

        return false;
    }

    private bool HandleDeviceChanged(DeviceDefaultChangedEvent audioChangedEvent)
    {
        if (_forcedProfile == null)
        {
            return false;
        }

        _logger.Debug("We have a force profile set and audio device changed. Checking ....");

        //No need to trigger force profile if
        if (_forcedProfile.Devices.Any(wrapper => wrapper.DeviceInfo.Equals(audioChangedEvent.Device) && wrapper.Role.HasFlag((ERole)audioChangedEvent.Role)))
        {
            _logger.Debug("No need to force switching, already using forced profile.");
            return false;
        }

        _logger.Debug("Forced profile activated: {profile}", _forcedProfile);

        SwitchAudio(_forcedProfile);
        return true;
    }

    /// <summary>
    /// Save the current state of the system if it wasn't saved already
    /// </summary>
    private bool SaveCurrentState(User32.NativeMethods.HWND windowHandle, Profile profile, Trigger.Trigger trigger)
    {
        var triggerDefinition = _triggerFactory.Get(trigger.Type);
        if (!(triggerDefinition.AlwaysDefaultAndRestoreDevice || (profile.RestoreDevices && triggerDefinition.CanRestoreDevices)))
        {
            return false;
        }

        if (_activeWindowsTrigger.ContainsKey(windowHandle))
        {
            return false;
        }

        var communication = _audioSwitcher.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eCommunications);
        var playback = _audioSwitcher.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia);
        var recording = _audioSwitcher.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eMultimedia);
        var recordingCommunication = _audioSwitcher.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eCommunications);

        var currentState = new Profile
        {
            AlsoSwitchDefaultDevice = true,
            Name = SettingsStrings.profile_trigger_restoreDevices_title,
            Communication = communication,
            Playback = playback,
            Recording = recording,
            RecordingCommunication = recordingCommunication,
            NotifyOnActivation = profile.NotifyOnActivation
        };
        _activeWindowsTrigger.TryAdd(windowHandle, currentState);
        return true;
    }

    /// <summary>
    /// Restore the old state
    /// </summary>
    /// <param name="windowHandle"></param>
    /// <returns></returns>
    private bool RestoreState(User32.NativeMethods.HWND windowHandle)
    {
        if (!_activeWindowsTrigger.TryGetValue(windowHandle, out var oldState))
        {
            return false;
        }

        SwitchAudio(oldState);
        oldState.Dispose();
        _activeWindowsTrigger.TryRemove(windowHandle, out _);
        return true;
    }

    /// <summary>
    /// Handle steam big picture
    /// </summary>
    /// <param name="event"></param>
    /// <returns></returns>
    private bool HandleSteamBigPicture(WindowMonitor.Event @event)
    {
        if (_steamProfile == null)
        {
            return false;
        }

        if (!IsSteamBigPicture(@event))
        {
            return false;
        }

        SaveCurrentState(@event.Hwnd, _steamProfile, _steamProfile.Triggers.First(trigger => trigger.Type == TriggerFactory.Enum.Steam));

        SwitchAudio(_steamProfile);
        return true;
    }

    private bool IsSteamBigPicture(WindowMonitor.Event @event)
    {
        if (!@event.ProcessName.ToLower().Contains("steam"))
        {
            return false;
        }

        var windowNameLowerCase = @event.WindowName.ToLowerInvariant();
        if (@event.WindowClass == "SDL_app" && windowNameLowerCase.Contains("big") && windowNameLowerCase.Contains("picture"))
        {
            return true;
        }

        switch (@event.WindowName)
        {
            case "Steam" when @event.WindowClass == "CUIEngineWin32":
            case "SP" when @event.WindowClass == "SDL_app":
                return true;
            default:
                return false;
        }
    }

    private DeviceInfo? CheckDeviceAvailable(DeviceInfo deviceInfo)
    {
        return AppModel.Instance.AudioDeviceLister.GetDevices(deviceInfo.Type, DeviceState.Active).FirstOrDefault(info => info.Equals(deviceInfo));
    }

    private void SwitchAudio(Profile profile, uint processId)
    {
        _notificationManager.NotifyProfileChanged(profile, processId);
        foreach (var device in profile.Devices)
        {
            var deviceToUse = CheckDeviceAvailable(device.DeviceInfo);
            if (deviceToUse == null)
            {
                _showError.Invoke(string.Format(SettingsStrings.profile_error_deviceNotFound, device.DeviceInfo.NameClean), $"{SettingsStrings.profile_error_title}: {profile.Name}");
                continue;
            }

            _audioSwitcher.SwitchProcessTo(
                deviceToUse.Id,
                device.Role,
                (EDataFlow)deviceToUse.Type,
                processId);

            if (profile.AlsoSwitchDefaultDevice)
            {
                _audioSwitcher.SwitchTo(deviceToUse.Id, device.Role);
            }
        }
    }


    public void SwitchAudio(Profile profile)
    {
        _notificationManager.NotifyProfileChanged(profile, null);
        foreach (var device in profile.Devices)
        {
            var deviceToUse = CheckDeviceAvailable(device.DeviceInfo);
            if (deviceToUse == null)
            {
                _showError.Invoke(string.Format(SettingsStrings.profile_error_deviceNotFound, device.DeviceInfo.NameClean), $"{SettingsStrings.profile_error_title}: {profile.Name}");
                continue;
            }

            _audioSwitcher.SwitchTo(deviceToUse.Id, device.Role);
            if (profile.SwitchForegroundApp)
            {
                _audioSwitcher.SwitchForegroundProcessTo(deviceToUse.Id, device.Role, (EDataFlow)deviceToUse.Type);
            }
        }
    }

    /// <summary>
    /// Return the globally available triggers
    /// Remove the one that aren't accessible anymore
    /// </summary>
    /// <returns></returns>
    public IEnumerable<ITriggerDefinition> AvailableTriggers()
    {
        var triggers = Profiles.SelectMany(profile => profile.Triggers).GroupBy(trigger => trigger.Type).ToDictionary(grouping => grouping.Key, grouping => grouping.Count());
        var triggerFactory = new TriggerFactory();
        return triggerFactory.AllImplementations
            .Where(pair =>
            {
                if (triggers.TryGetValue(pair.Key, out var count))
                {
                    return pair.Value.MaxGlobalOccurence < count;
                }

                return true;
            })
            .Select(pair => pair.Value);
    }

    /// <summary>
    /// Add a profile to the system
    /// </summary>
    public Result<string, VoidSuccess> AddProfile(Profile profile)
    {
        return ValidateProfile(profile)
            .Map(success =>
            {
                RegisterTriggers(profile);
                AppConfigs.Configuration.Profiles.Add(profile);
                AppConfigs.Configuration.Save();

                return success;
            });
    }

    /// <summary>
    /// Update a profile
    /// </summary>
    public Result<string, VoidSuccess> UpdateProfile(Profile oldProfile, Profile newProfile)
    {
        DeleteProfiles(new[] { oldProfile });
        return ValidateProfile(newProfile)
            .Map(success =>
            {
                RegisterTriggers(newProfile);
                AppConfigs.Configuration.Profiles.Add(newProfile);
                AppConfigs.Configuration.Save();
                return success;
            })
            .Catch(s =>
            {
                AddProfile(oldProfile);
                return s;
            });
    }

    private Result<string, VoidSuccess> ValidateProfile(Profile profile, bool init = false)
    {
        if (string.IsNullOrEmpty(profile.Name))
        {
            return SettingsStrings.profile_error_noName;
        }

        if (profile.Triggers.Count == 0)
        {
            return SettingsStrings.profile_error_triggers_min;
        }

        if (profile.Recording == null && profile.Playback == null)
        {
            return SettingsStrings.profile_error_needPlaybackOrRecording;
        }

        if (!init && AppConfigs.Configuration.Profiles.Contains(profile))
        {
            return string.Format(SettingsStrings.profile_error_name, profile.Name);
        }

        //Only hotkey doesn't need validation since you can have multiple profile with the same hotkey
        foreach (var groups in profile.Triggers.Where(trigger => trigger.Type != TriggerFactory.Enum.HotKey).GroupBy(trigger => trigger.Type).Where(triggers => triggers.Count() > 1))
        {
            //has different trigger of the same type, not a problem
            if (groups.Distinct().Count() > 1)
            {
                continue;
            }

            var trigger = groups.First();

            var error = groups.Key.Match(() => null,
                () => string.Format(SettingsStrings.profile_error_window, trigger.WindowName),
                () => string.Format(SettingsStrings.profile_error_application, trigger.ApplicationPath),
                () => SettingsStrings.profile_error_steam,
                () => null,
                () => string.Format(SettingsStrings.profile_error_window, trigger.WindowName),
                () => null,
                () => null);
            if (error != null)
            {
                return error;
            }
        }

        foreach (var trigger in profile.Triggers)
        {
            var error = trigger.Type.Match(() =>
                {
                    if (trigger.HotKey == null || !_profileHotkeyManager.IsValidHotkey(trigger.HotKey))
                    {
                        return string.Format(SettingsStrings.profile_error_hotkey, trigger.HotKey);
                    }

                    return null;
                }, () =>
                {
                    if (string.IsNullOrEmpty(trigger.WindowName) || _profilesByWindowName.ContainsKey(trigger.WindowName.ToLower()))
                    {
                        return string.Format(SettingsStrings.profile_error_window, trigger.WindowName);
                    }

                    return null;
                }, () =>
                {
                    if (string.IsNullOrEmpty(trigger.ApplicationPath) || _profileByApplication.ContainsKey(trigger.ApplicationPath.ToLower()))
                    {
                        return string.Format(SettingsStrings.profile_error_application, trigger.ApplicationPath);
                    }

                    return null;
                },
                () => _steamProfile != null ? SettingsStrings.profile_error_steam : null,
                () => null,
                () =>
                {
                    if (string.IsNullOrEmpty(trigger.WindowName) || _profilesByUwpApp.ContainsKey(trigger.WindowName.ToLower()))
                    {
                        return string.Format(SettingsStrings.profile_error_window, trigger.WindowName);
                    }

                    return null;
                },
                () => null,
                () => _forcedProfile != null ? SettingsStrings.profile_error_deviceChanged : null);
            if (error != null)
            {
                return error;
            }
        }

        return Result.Success();
    }

    private Result<string, VoidSuccess> ValidateAddProfile(ProfileSetting profile)
    {
        if (string.IsNullOrEmpty(profile.ProfileName))
        {
            return SettingsStrings.profile_error_noName;
        }

        if (string.IsNullOrEmpty(profile.ApplicationPath) && profile.HotKey == null)
        {
            return SettingsStrings.profile_error_needHKOrPath;
        }

        if (profile.Recording == null && profile.Playback == null)
        {
            return SettingsStrings.profile_error_needPlaybackOrRecording;
        }

        if (profile.HotKey != null && !_profileHotkeyManager.IsValidHotkey(profile.HotKey))
        {
            return string.Format(SettingsStrings.profile_error_hotkey, profile.HotKey);
        }

        if (!string.IsNullOrEmpty(profile.ApplicationPath) && _profileByApplication.ContainsKey(profile.ApplicationPath.ToLower()))
        {
            return string.Format(SettingsStrings.profile_error_application, profile.ApplicationPath);
        }

        if (AppConfigs.Configuration.ProfileSettings.Contains(profile))
        {
            return string.Format(SettingsStrings.profile_error_name, profile.ProfileName);
        }

        if (profile.HotKey != null && !WindowsAPIAdapter.RegisterHotKey(profile.HotKey))
        {
            return string.Format(SettingsStrings.profile_error_hotkey, profile.HotKey);
        }

        return Result.Success();
    }

    /// <summary>
    /// Delete the given profiles.
    ///
    /// Result Failure contains the profile that couldn't be deleted because they don't exists.
    /// </summary>
    public Result<Profile[], VoidSuccess> DeleteProfiles(IEnumerable<Profile> profilesToDelete)
    {
        var errors = new List<Profile>();
        var profiles = profilesToDelete.ToArray();
        var resetProcessAudio = profiles.Any(profile => profile.Triggers.Any(trigger => trigger.Type == TriggerFactory.Enum.Process || trigger.Type == TriggerFactory.Enum.Window));
        foreach (var profile in profiles)
        {
            if (!AppConfigs.Configuration.Profiles.Contains(profile))
            {
                errors.Add(profile);
                continue;
            }

            UnRegisterTriggers(profile);

            AppConfigs.Configuration.Profiles.Remove(profile);
        }

        AppConfigs.Configuration.Save();
        if (errors.Count > 0)
        {
            return errors.ToArray();
        }

        if (resetProcessAudio)
        {
            _audioSwitcher.ResetProcessDeviceConfiguration();
            InitializeProfileExistingProcess();
        }

        return Result.Success();
    }

    private void InitializeProfileExistingProcess()
    {
        if (_profileByApplication.Count == 0)
        {
            _logger.Information("No profile related to application to load");
            return;
        }

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                _logger.Verbose("Checking process: {process}", process);
                if (process.HasExited)
                    continue;
                if (!process.Responding)
                    continue;
                var filePath = process.MainModule?.FileName.ToLower();
                if (filePath == null)
                {
                    continue;
                }

                if (_profileByApplication.TryGetValue(filePath, out var profile))
                {
                    _logger.Debug("Profile {profile} match process {process}", profile, process);
                    var handle = User32.NativeMethods.HWND.Cast(process.Handle);
                    SaveCurrentState(handle, profile.Profile, profile.Trigger);
                    SwitchAudio(profile.Profile, (uint)process.Id);
                }
            }
            catch (Win32Exception)
            {
                //Happen when trying to access process MainModule belonging to windows like svchost
            }
            catch (InvalidOperationException)
            {
                //ApplicationPath in a weird state, can't get MainModule for it.
            }
            catch (Exception e)
            {
                _logger.Error(e, "Couldn't get information about the given process.");
            }
        }
    }
}