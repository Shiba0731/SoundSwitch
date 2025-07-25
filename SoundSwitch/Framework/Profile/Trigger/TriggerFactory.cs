﻿using SoundSwitch.Framework.Factory;
using SoundSwitch.Localization;

namespace SoundSwitch.Framework.Profile.Trigger;

public class TriggerFactory() : AbstractFactory<TriggerFactory.Enum, ITriggerDefinition>(Impl)
{
    public enum Enum
    {
        HotKey,
        Window,
        Process,
        Steam,
        Startup,
        UwpApp,
        TrayMenu,
        Changed
    }

    private static readonly IEnumImplList<Enum, ITriggerDefinition> Impl =
        new EnumImplList<Enum, ITriggerDefinition>()
        {
            new HotKeyTrigger(),
            new ProcessTrigger(),
            new WindowTrigger(),
            new SteamBigPictureTrigger(),
            new Startup(),
            new UwpApp(),
            new TrayMenu(),
            new DeviceChanged()
        };
}

public interface ITriggerDefinition : IEnumImpl<TriggerFactory.Enum>
{
    /// <summary>
    /// Maximum number of occurence of this trigger in a profile
    /// </summary>
    public int MaxOccurence { get; }

    /// <summary>
    /// Maximum number of occurence of this trigger globally
    /// </summary>
    public int MaxGlobalOccurence { get; }

    public string Description { get; }

    /// <summary>
    /// This trigger let the user choose if the devices can be restored after the trigger has ended
    /// </summary>
    public bool CanRestoreDevices { get; }

    /// <summary>
    /// Does this trigger always restore the default devices
    /// </summary>
    public bool AlwaysDefaultAndRestoreDevice { get; }
}

public abstract class BaseTrigger : ITriggerDefinition
{
    public override string ToString()
    {
        return Label;
    }

    public virtual TriggerFactory.Enum TypeEnum { get; }
    public virtual string Label { get; }
    public virtual int MaxOccurence => -1;
    public virtual int MaxGlobalOccurence => -1;
    public abstract string Description { get; }
    public virtual bool CanRestoreDevices => false;
    public virtual bool AlwaysDefaultAndRestoreDevice => false;
}

public class HotKeyTrigger : BaseTrigger
{
    public override TriggerFactory.Enum TypeEnum => TriggerFactory.Enum.HotKey;
    public override string Label => SettingsStrings.hotkey;
    public override string Description { get; } = SettingsStrings.profile_trigger_hotkey_desc;
    public override int MaxOccurence => 1;
}

public class WindowTrigger : BaseTrigger
{
    public override TriggerFactory.Enum TypeEnum => TriggerFactory.Enum.Window;
    public override string Label => SettingsStrings.profile_trigger_window;
    public override string Description { get; } = SettingsStrings.profile_trigger_window_desc;
    public override bool CanRestoreDevices => true;
}

public class ProcessTrigger : BaseTrigger
{
    public override TriggerFactory.Enum TypeEnum => TriggerFactory.Enum.Process;
    public override string Label => SettingsStrings.profile_trigger_process;
    public override string Description { get; } = SettingsStrings.profile_trigger_process_desc;
    public override bool CanRestoreDevices => true;
}

public class SteamBigPictureTrigger : BaseTrigger
{
    public override TriggerFactory.Enum TypeEnum => TriggerFactory.Enum.Steam;
    public override string Label => SettingsStrings.profile_trigger_steam;

    public override int MaxOccurence => 1;
    public override int MaxGlobalOccurence => 1;
    public override string Description { get; } = SettingsStrings.profile_trigger_steam_desc;
    public override bool CanRestoreDevices => true;
    public override bool AlwaysDefaultAndRestoreDevice => true;
}

public class Startup : BaseTrigger
{
    public override TriggerFactory.Enum TypeEnum => TriggerFactory.Enum.Startup;
    public override string Label => SettingsStrings.profile_trigger_startup;

    public override int MaxOccurence => 1;
    public override int MaxGlobalOccurence => 1;

    public override string Description { get; } = SettingsStrings.profile_trigger_startup_desc;
}

public class UwpApp : BaseTrigger
{
    public override TriggerFactory.Enum TypeEnum => TriggerFactory.Enum.UwpApp;
    public override string Label => SettingsStrings.profile_trigger_uwp;
    public override bool CanRestoreDevices => true;
    public override string Description => SettingsStrings.profile_trigger_uwp_desc;
    public override bool AlwaysDefaultAndRestoreDevice => true;
}

public class TrayMenu : BaseTrigger
{
    public override string Description => SettingsStrings.profile_trigger_trayMenu_desc;
    public override TriggerFactory.Enum TypeEnum => TriggerFactory.Enum.TrayMenu;
    public override string Label => SettingsStrings.profile_trigger_trayMenu;
    public override int MaxOccurence => 1;
}

public class DeviceChanged : BaseTrigger
{
    public override TriggerFactory.Enum TypeEnum => TriggerFactory.Enum.Changed;
    public override string Label => SettingsStrings.profile_trigger_deviceChanged;

    public override int MaxOccurence => 1;
    public override int MaxGlobalOccurence => 1;

    public override string Description { get; } = SettingsStrings.profile_trigger_deviceChanged_desc;
}