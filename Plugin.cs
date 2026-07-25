using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;

namespace CaptainValheim;

[BepInPlugin(ModGUID, ModName, ModVersion)]
public class CaptainValheimPlugin : BaseUnityPlugin
{
    internal const string ModName = "CaptainValheim";
    internal const string ModVersion = "1.0.8";
    internal const string Author = "sighsorry";
    private const string ModGUID = $"{Author}.{ModName}";
    private static string ConfigFileName = $"{ModGUID}.cfg";
    private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
    private readonly Harmony _harmony = new(ModGUID);
    public static readonly ManualLogSource ModLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
    internal static readonly ConfigSync ConfigSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };
    internal static PluginSettings Settings { get; } = new();
    private FileSystemWatcher? _watcher;
    private readonly object _reloadLock = new();
    private DateTime _configReloadDueUtc;
    private bool _hasPendingConfigReload;
    private string? _lastConfigFileText;
    private static readonly TimeSpan ConfigReloadDelay = TimeSpan.FromSeconds(1);

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
        _serverConfigLocked = Settings.General.LockConfiguration;
        _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);
        PatchCaptainValheimHooks();
        CaptainValheimLocalization.Load();
        SecondaryAttackFacade.Initialize();
        SetupWatcher();

        Config.Save();
        _lastConfigFileText = ReadFileTextIfExists(ConfigFileFullPath);
        if (saveOnSet)
        {
            Config.SaveOnConfigSet = saveOnSet;
        }
    }

    private void Update()
    {
        TryReloadConfigValues();
        SecondaryAttackFacade.TryApplyPendingConfig();
    }

    private void OnDestroy()
    {
        RunCleanup("secondary attack state", SecondaryAttackFacade.Dispose);
        RunCleanup("configuration save", () => SaveWithRespectToConfigSet());
        RunCleanup("configuration watcher", DisposeWatcher);
        RunCleanup("shield key hints", ShieldOnlyKeyHintSystem.Dispose);
        RunCleanup("shield compendium", ShieldTechniqueCompendiumManager.Dispose);
        RunCleanup("Harmony patches", _harmony.UnpatchSelf);
    }

    private static void RunCleanup(string operation, Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            ModLogger.LogError($"Failed to clean up {operation}: {exception.Message}");
        }
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

    private void DisposeWatcher()
    {
        FileSystemWatcher? watcher = _watcher;
        _watcher = null;
        if (watcher == null)
        {
            return;
        }

        watcher.EnableRaisingEvents = false;
        watcher.Changed -= ReadConfigValues;
        watcher.Created -= ReadConfigValues;
        watcher.Renamed -= ReadConfigValues;
        watcher.Dispose();
        lock (_reloadLock)
        {
            _hasPendingConfigReload = false;
            _configReloadDueUtc = default;
        }
    }

    private void ReadConfigValues(object sender, FileSystemEventArgs e)
    {
        lock (_reloadLock)
        {
            _hasPendingConfigReload = true;
            _configReloadDueUtc = DateTime.UtcNow + ConfigReloadDelay;
        }
    }

    private void TryReloadConfigValues()
    {
        lock (_reloadLock)
        {
            if (!_hasPendingConfigReload || DateTime.UtcNow < _configReloadDueUtc)
            {
                return;
            }

            _hasPendingConfigReload = false;
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

                SaveWithRespectToConfigSet(true);

                _lastConfigFileText = ReadFileTextIfExists(ConfigFileFullPath);
                ModLogger.LogInfo("Configuration reload complete.");
            }
            catch (Exception ex)
            {
                ModLogger.LogError($"Error reloading configuration: {ex.Message}");
            }
        }
    }

    private static string? ReadFileTextIfExists(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private void SaveWithRespectToConfigSet(bool reload = false)
    {
        bool originalSaveOnSet = Config.SaveOnConfigSet;
        Config.SaveOnConfigSet = false;
        try
        {
            if (reload)
            {
                Config.Reload();
            }

            Config.Save();
        }
        finally
        {
            Config.SaveOnConfigSet = originalSaveOnSet;
        }
    }

    private void PatchCaptainValheimHooks()
    {
        _harmony.PatchAll(typeof(CaptainValheimPlugin).Assembly);
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
        internal ConfigEntry<Toggle> ShowShieldTooltip = null!;

        internal void Bind(CaptainValheimPlugin plugin)
        {
            const string group = "1 - General";
            LockConfiguration = plugin.config(group, "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
            ShowShieldTooltip = plugin.config(group, "Show Shield Tooltip", Toggle.On, "Shows CaptainValheim guidance on shield item tooltips.", synchronizedSetting: false);
        }
    }

    #region ConfigOptions

    private static ConfigEntry<Toggle> _serverConfigLocked = null!;

    private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
    {
        ConfigDescription extendedDescription = new(description.Description + (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"), description.AcceptableValues, description.Tags);
        ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);

        SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

        return configEntry;
    }

    private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true)
    {
        return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
    }

    #endregion
}
