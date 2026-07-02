using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Timers;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using ServerSync;
using UnityEngine;

namespace CaptainValheim;

[BepInPlugin(ModGUID, ModName, ModVersion)]
public class CaptainValheimPlugin : BaseUnityPlugin
{
    internal const string ModName = "CaptainValheim";
    internal const string ModVersion = "1.0.1";
    internal const string Author = "sighsorry";
    private const string ModGUID = $"{Author}.{ModName}";
    private static string ConfigFileName = $"{ModGUID}.cfg";
    private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
    internal static string ConnectionError = "";
    private readonly Harmony _harmony = new(ModGUID);
    public static readonly ManualLogSource ModLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
    internal static readonly ConfigSync ConfigSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };
    internal static PluginSettings Settings { get; } = new();
    private FileSystemWatcher? _watcher;
    private readonly object _reloadLock = new();
    private DateTime _lastConfigReloadTime;
    private string? _lastConfigFileText;
    private bool _suppressWorldApplySettingChange;
    private const long RELOAD_DELAY = 10000000; // One second

    public enum Toggle
    {
        On = 1,
        Off = 0
    }

    public void Awake()
    {
        bool saveOnSet = Config.SaveOnConfigSet;
        Config.SaveOnConfigSet = false;

        Settings.Bind(this);
        RegisterWorldApplySettingHandlers();
        _serverConfigLocked = Settings.General.LockConfiguration;
        _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);
        PatchCaptainValheimHooks();
        SecondaryAttackFacade.Initialize();
        SetupWatcher();

        Config.Save();
        _lastConfigFileText = ReadFileTextIfExists(ConfigFileFullPath);
        if (saveOnSet)
        {
            Config.SaveOnConfigSet = saveOnSet;
        }
    }

    private void OnDestroy()
    {
        UnregisterWorldApplySettingHandlers();
        SecondaryAttackFacade.Dispose();
        SaveWithRespectToConfigSet();
        _watcher?.Dispose();
    }

    private void SetupWatcher()
    {
        _watcher = new FileSystemWatcher(Paths.ConfigPath, ConfigFileName);
        _watcher.Changed += ReadConfigValues;
        _watcher.Created += ReadConfigValues;
        _watcher.Renamed += ReadConfigValues;
        _watcher.IncludeSubdirectories = true;
        _watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
        _watcher.EnableRaisingEvents = true;
    }

    private void ReadConfigValues(object sender, FileSystemEventArgs e)
    {
        DateTime now = DateTime.Now;
        long time = now.Ticks - _lastConfigReloadTime.Ticks;
        if (time < RELOAD_DELAY)
        {
            return;
        }

        lock (_reloadLock)
        {
            if (!File.Exists(ConfigFileFullPath))
            {
                ModLogger.LogWarning("Config file does not exist. Skipping reload.");
                return;
            }

            try
            {
                string configFileText = File.ReadAllText(ConfigFileFullPath);
                if (string.Equals(_lastConfigFileText, configFileText, StringComparison.Ordinal))
                {
                    return;
                }

                _suppressWorldApplySettingChange = true;
                try
                {
                    SaveWithRespectToConfigSet(true);
                }
                finally
                {
                    _suppressWorldApplySettingChange = false;
                }

                SecondaryAttackFacade.RequestCurrentWorldReapply();
                _lastConfigFileText = ReadFileTextIfExists(ConfigFileFullPath);
                ModLogger.LogInfo("Configuration reload complete.");
            }
            catch (Exception ex)
            {
                ModLogger.LogError($"Error reloading configuration: {ex.Message}");
            }
        }

        _lastConfigReloadTime = now;
    }

    private static string? ReadFileTextIfExists(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private void SaveWithRespectToConfigSet(bool reload = false)
    {
        bool originalSaveOnSet = Config.SaveOnConfigSet;
        Config.SaveOnConfigSet = false;
        if (reload)
            Config.Reload();
        Config.Save();
        if (originalSaveOnSet)
        {
            Config.SaveOnConfigSet = originalSaveOnSet;
        }
    }

    private void PatchCaptainValheimHooks()
    {
        Type[] patchTypes =
        [
            typeof(ProjectileUpdateVisualPatch),
            typeof(ProjectileOnHitPatch),
            typeof(CharacterAwakeCaptainValheimPatch),
            typeof(PlayerUpdatePendingConfigPatch),
            typeof(ObjectDbAwakePatch),
            typeof(ObjectDbCopyOtherDbPatch),
            typeof(HumanoidGetCurrentWeaponPatch),
            typeof(HumanoidPickupThrownShieldPatch),
            typeof(HumanoidBlockAttackPatch),
            typeof(HumanoidStartAttackPatch),
            typeof(AttackOnAttackTriggerPatch),
            typeof(AttackDoMeleeAttackSecondaryDurabilityFactorPatch),
            typeof(AttackDoAreaAttackSecondaryDurabilityFactorPatch),
            typeof(AttackProjectileAttackTriggeredSecondaryDurabilityFactorPatch),
            typeof(KeyHintsAwakeShieldOnlyPatch),
            typeof(KeyHintsUpdateShieldOnlyPatch)
        ];

        foreach (Type patchType in patchTypes)
        {
            _harmony.CreateClassProcessor(patchType).Patch();
        }
    }

    private void RegisterWorldApplySettingHandlers()
    {
    }

    private void UnregisterWorldApplySettingHandlers()
    {
    }

    private void OnWorldApplySettingChanged(object? sender, EventArgs e)
    {
        if (_suppressWorldApplySettingChange)
        {
            return;
        }

        SecondaryAttackFacade.RequestCurrentWorldReapply();
    }

    internal sealed class PluginSettings
    {
        internal GeneralSettings General { get; } = new();

        internal void Bind(CaptainValheimPlugin plugin)
        {
            General.Bind(plugin);
        }
    }

    internal sealed class GeneralSettings
    {
        internal ConfigEntry<Toggle> LockConfiguration = null!;
        internal ConfigEntry<Toggle> ShieldReflectDebugLogging = null!;

        internal void Bind(CaptainValheimPlugin plugin)
        {
            const string group = "1 - General";
            LockConfiguration = plugin.config(group, "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
            ShieldReflectDebugLogging = plugin.config(group, "Shield Reflect Debug Logging", Toggle.Off, "Logs shield projectile reflection RPC/context diagnostics. Keep off unless diagnosing multiplayer reflection ownership issues.", synchronizedSetting: false);
        }
    }

    #region ConfigOptions

    private static ConfigEntry<Toggle> _serverConfigLocked = null!;

    private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
    {
        ConfigDescription extendedDescription = new(description.Description + (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"), description.AcceptableValues, description.Tags);
        ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
        //var configEntry = Config.Bind(group, name, value, description);

        SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

        return configEntry;
    }

    private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true)
    {
        return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
    }

    private class ConfigurationManagerAttributes
    {
        [UsedImplicitly] public int? Order = null!;
        [UsedImplicitly] public bool? Browsable = null!;
        [UsedImplicitly] public string? Category = null!;
        [UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer = null!;
    }

    class AcceptableShortcuts() : AcceptableValueBase(typeof(KeyboardShortcut))
    {
        public override object Clamp(object value) => value;
        public override bool IsValid(object value) => true;

        public override string ToDescriptionString() => $"# Acceptable values: {string.Join(", ", UnityInput.Current.SupportedKeyCodes)}";
    }

    #endregion
}

public static class KeyboardExtensions
{
    extension(KeyboardShortcut shortcut)
    {
        public bool IsKeyDown()
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKeyDown(shortcut.MainKey) && shortcut.Modifiers.All(Input.GetKey);
        }

        public bool IsKeyHeld()
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKey(shortcut.MainKey) && shortcut.Modifiers.All(Input.GetKey);
        }
    }
}

public static class ToggleExtentions
{
    extension(CaptainValheimPlugin.Toggle value)
    {
        public bool IsOn()
        {
            return value == CaptainValheimPlugin.Toggle.On;
        }

        public bool IsOff()
        {
            return value == CaptainValheimPlugin.Toggle.Off;
        }
    }
}
