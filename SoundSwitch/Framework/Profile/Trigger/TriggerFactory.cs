// SoundSwitch/Framework/Profile/Trigger/TriggerFactory.cs

using System.ComponentModel;

namespace SoundSwitch.Framework.Profile.Trigger
{
    public class TriggerFactory
    {
        public enum Enum
        {
            [Description("Hotkey")]
            HotKey,
            [Description("Application")]
            Application,
            [Description("Window")]
            Window,
            [Description("Steam")]
            Steam,
            [Description("Startup")]
            Startup,
            [Description("UWP App")]
            UwpApp,
            [Description("Device Change")]
            DeviceChange,
            [Description("Forced")]
            Forced,
            [Description("Application Exit")] // 新しいトリガータイプを追加
            ApplicationExit,
        }

        public ITriggerDefinition Get(Enum type)
        {
            return type switch
            {
                Enum.HotKey => new HotKeyTrigger(),
                Enum.Application => new ApplicationTrigger(),
                Enum.Window => new WindowTrigger(),
                Enum.Steam => new SteamTrigger(),
                Enum.Startup => new StartupTrigger(),
                Enum.UwpApp => new UwpAppTrigger(),
                Enum.DeviceChange => new DeviceChangeTrigger(),
                Enum.Forced => new ForcedTrigger(),
                Enum.ApplicationExit => new ApplicationExitTrigger(), // ここも追加
                _ => throw new InvalidEnumArgumentException(nameof(type), (int)type, typeof(Enum))
            };
        }

        public System.Collections.Generic.IReadOnlyDictionary<Enum, ITriggerDefinition> AllImplementations =>
            new System.Collections.Generic.Dictionary<Enum, ITriggerDefinition>
            {
                { Enum.HotKey, new HotKeyTrigger() },
                { Enum.Application, new ApplicationTrigger() },
                { Enum.Window, new WindowTrigger() },
                { Enum.Steam, new SteamTrigger() },
                { Enum.Startup, new StartupTrigger() },
                { Enum.UwpApp, new UwpAppTrigger() },
                { Enum.DeviceChange, new DeviceChangeTrigger() },
                { Enum.Forced, new ForcedTrigger() },
                { Enum.ApplicationExit, new ApplicationExitTrigger() }, // ここも追加
            };
    }

    // 新しいトリガー定義クラスも追加する必要があります
    public class ApplicationExitTrigger : ITriggerDefinition
    {
        public TriggerFactory.Enum Type => TriggerFactory.Enum.ApplicationExit;
        public string Label => "アプリケーション終了時";
        public bool NeedsArgument => true; // 監視するアプリケーションパスが必要
        public bool AlwaysDefaultAndRestoreDevice => false;
        public bool CanRestoreDevices => false; // 終了時は復元できない
        public int MaxGlobalOccurence => 999; // 複数設定可能
    }

    // 既存のトリガー定義クラスもここに含めるか、元のファイルからコピーしてください
    // 例:
    public interface ITriggerDefinition
    {
        TriggerFactory.Enum Type { get; }
        string Label { get; }
        bool NeedsArgument { get; }
        bool AlwaysDefaultAndRestoreDevice { get; }
        bool CanRestoreDevices { get; }
        int MaxGlobalOccurence { get; }
    }

    public class HotKeyTrigger : ITriggerDefinition
    {
        public TriggerFactory.Enum Type => TriggerFactory.Enum.HotKey;
        public string Label => "ホットキー";
        public bool NeedsArgument => true;
        public bool AlwaysDefaultAndRestoreDevice => false;
        public bool CanRestoreDevices => false;
        public int MaxGlobalOccurence => 999;
    }

    public class ApplicationTrigger : ITriggerDefinition
    {
        public TriggerFactory.Enum Type => TriggerFactory.Enum.Application;
        public string Label => "アプリケーション起動時";
        public bool NeedsArgument => true;
        public bool AlwaysDefaultAndRestoreDevice => false;
        public bool CanRestoreDevices => false;
        public int MaxGlobalOccurence => 1; // アプリケーションごとに1つ
    }

    public class WindowTrigger : ITriggerDefinition
    {
        public TriggerFactory.Enum Type => TriggerFactory.Enum.Window;
        public string Label => "ウィンドウ起動時";
        public bool NeedsArgument => true;
        public bool AlwaysDefaultAndRestoreDevice => false;
        public bool CanRestoreDevices => true;
        public int MaxGlobalOccurence => 1; // ウィンドウ名ごとに1つ
    }

    public class SteamTrigger : ITriggerDefinition
    {
        public TriggerFactory.Enum Type => TriggerFactory.Enum.Steam;
        public string Label => "Steam Big Picture";
        public bool NeedsArgument => false;
        public bool AlwaysDefaultAndRestoreDevice => true;
        public bool CanRestoreDevices => true;
        public int MaxGlobalOccurence => 1;
    }

    public class StartupTrigger : ITriggerDefinition
    {
        public TriggerFactory.Enum Type => TriggerFactory.Enum.Startup;
        public string Label => "起動時";
        public bool NeedsArgument => false;
        public bool AlwaysDefaultAndRestoreDevice => false;
        public bool CanRestoreDevices => false;
        public int MaxGlobalOccurence => 1;
    }

    public class UwpAppTrigger : ITriggerDefinition
    {
        public TriggerFactory.Enum Type => TriggerFactory.Enum.UwpApp;
        public string Label => "UWPアプリ";
        public bool NeedsArgument => true;
        public bool AlwaysDefaultAndRestoreDevice => false;
        public bool CanRestoreDevices => true;
        public int MaxGlobalOccurence => 1;
    }

    public class DeviceChangeTrigger : ITriggerDefinition
    {
        public TriggerFactory.Enum Type => TriggerFactory.Enum.DeviceChange;
        public string Label => "デバイス変更時";
        public bool NeedsArgument => false;
        public bool AlwaysDefaultAndRestoreDevice => false;
        public bool CanRestoreDevices => false;
        public int MaxGlobalOccurence => 1;
    }

    public class ForcedTrigger : ITriggerDefinition
    {
        public TriggerFactory.Enum Type => TriggerFactory.Enum.Forced;
        public string Label => "強制";
        public bool NeedsArgument => false;
        public bool AlwaysDefaultAndRestoreDevice => false;
        public bool CanRestoreDevices => false;
        public int MaxGlobalOccurence => 1;
    }
}
