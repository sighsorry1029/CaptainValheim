using System;
using System.IO;
using BepInEx;
using ServerSync;
using UnityEngine;

namespace CaptainValheim;

internal static class SecondaryAttackFacade
{
    private enum YamlAuthorityMode
    {
        LocalFiles,
        SyncedOnly
    }

    private static readonly object ReloadLock = new();
    private static FileSystemWatcher? _watcher;
    private static CustomSyncedValue<string>? _syncedYamlValue;
    private static SecondaryAttackCompiledSnapshot _currentCompiledSnapshot = SecondaryAttackCompiledSnapshot.Empty;
    private static SecondaryAttackCompiledSnapshot? _pendingCompiledSnapshot;
    private static SecondaryAttackAppliedWorldSnapshot _currentAppliedWorldSnapshot = SecondaryAttackAppliedWorldSnapshot.Empty;
    private static DateTime _lastYamlReloadTime;
    private static bool _hasPendingConfig;
    private static bool _suppressSyncedYamlChanged;
    private static YamlAuthorityMode _yamlAuthorityMode;
    private static string _currentYamlFingerprint = string.Empty;
    private static string? _pendingYamlFingerprint;

    internal static SecondaryAttackAppliedWorldSnapshot CurrentAppliedWorldSnapshot => _currentAppliedWorldSnapshot;

    public static void Initialize()
    {
        SecondaryAttackConfigLoader.EnsureLocalFileExists();
        InitializeSyncedYamlValue();
        RefreshYamlAuthorityMode(force: true);
    }

    public static void Dispose()
    {
        DisposeSyncedYamlValue();
        DisposeWatcher();
    }

    internal static void TryApplyPendingConfig()
    {
        RefreshYamlAuthorityMode();
        CommitPendingConfig(force: false, applyToObjectDbImmediately: true);
    }

    internal static void ApplyPendingConfigToObjectDb(ObjectDB objectDb, bool emitMissingWarnings)
    {
        RefreshYamlAuthorityMode();
        CommitPendingConfig(force: true, applyToObjectDbImmediately: false);
        ApplyCompiledSnapshotToObjectDb(objectDb, _currentCompiledSnapshot, emitMissingWarnings);
    }

    private static void SetupWatcher()
    {
        if (_watcher != null)
        {
            return;
        }

        Directory.CreateDirectory(SecondaryAttackYamlConfig.DirectoryPath);
        _watcher = new FileSystemWatcher(SecondaryAttackYamlConfig.DirectoryPath, SecondaryAttackYamlConfig.FileName);
        _watcher.Changed += OnYamlFileChanged;
        _watcher.Created += OnYamlFileChanged;
        _watcher.Renamed += OnYamlFileChanged;
        _watcher.IncludeSubdirectories = false;
        _watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
        _watcher.EnableRaisingEvents = true;
    }

    private static void OnYamlFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_yamlAuthorityMode != YamlAuthorityMode.LocalFiles)
        {
            return;
        }

        DateTime now = DateTime.Now;
        if (now.Ticks - _lastYamlReloadTime.Ticks < SecondaryAttackYamlConfig.ReloadDelayTicks)
        {
            return;
        }

        lock (ReloadLock)
        {
            ReloadLocalYaml();
            _lastYamlReloadTime = now;
        }
    }

    private static void ReloadLocalYaml()
    {
        if (_yamlAuthorityMode != YamlAuthorityMode.LocalFiles)
        {
            return;
        }

        SecondaryAttackConfigLoader.EnsureLocalFileExists();
        string yamlText = SecondaryAttackConfigLoader.ReadLocalYamlText();
        if (_syncedYamlValue != null)
        {
            _suppressSyncedYamlChanged = true;
            try
            {
                _syncedYamlValue.AssignLocalValue(yamlText);
            }
            finally
            {
                _suppressSyncedYamlChanged = false;
            }
        }

        ApplyYamlText(yamlText);
    }

    private static void OnSyncedYamlChanged()
    {
        if (!_suppressSyncedYamlChanged)
        {
            ApplyYamlText(_syncedYamlValue?.Value ?? string.Empty);
        }
    }

    private static void RefreshYamlAuthorityMode(bool force = false)
    {
        YamlAuthorityMode nextMode = DetermineYamlAuthorityMode();
        if (!force && nextMode == _yamlAuthorityMode)
        {
            return;
        }

        _yamlAuthorityMode = nextMode;
        switch (nextMode)
        {
            case YamlAuthorityMode.LocalFiles:
                SetupWatcher();
                ReloadLocalYaml();
                CaptainValheimPlugin.ModLogger.LogInfo("CaptainValheim YAML authority mode: LocalFiles.");
                break;
            case YamlAuthorityMode.SyncedOnly:
                DisposeWatcher();
                if (!string.IsNullOrEmpty(_syncedYamlValue?.Value))
                {
                    ApplyYamlText(_syncedYamlValue!.Value);
                }
                else
                {
                    ClearConfigWhileWaitingForServer();
                }

                CaptainValheimPlugin.ModLogger.LogInfo("CaptainValheim YAML authority mode: SyncedOnly.");
                break;
        }
    }

    private static void ClearConfigWhileWaitingForServer()
    {
        _pendingCompiledSnapshot = null;
        _pendingYamlFingerprint = null;
        _hasPendingConfig = false;
        _currentCompiledSnapshot = SecondaryAttackCompiledSnapshot.Empty;
        _currentYamlFingerprint = string.Empty;
        _currentAppliedWorldSnapshot = SecondaryAttackAppliedWorldSnapshot.Empty;
        if (ObjectDB.instance != null)
        {
            SecondaryAttackObjectDbStateStore.Restore(ObjectDB.instance);
            ShieldRuntimeSystem.ResetTransientState();
        }
    }

    private static YamlAuthorityMode DetermineYamlAuthorityMode()
    {
        return ZNet.instance != null && !ZNet.instance.IsServer()
            ? YamlAuthorityMode.SyncedOnly
            : YamlAuthorityMode.LocalFiles;
    }

    private static void InitializeSyncedYamlValue()
    {
        DisposeSyncedYamlValue();
        _syncedYamlValue = new CustomSyncedValue<string>(
            CaptainValheimPlugin.ConfigSync,
            SecondaryAttackYamlConfig.SyncedIdentifier,
            string.Empty);
        _syncedYamlValue.ValueChanged += OnSyncedYamlChanged;
    }

    private static void DisposeSyncedYamlValue()
    {
        if (_syncedYamlValue == null)
        {
            return;
        }

        _syncedYamlValue.ValueChanged -= OnSyncedYamlChanged;
        _syncedYamlValue = null;
    }

    private static void DisposeWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private static void ApplyYamlText(string yamlText)
    {
        string fingerprint = yamlText ?? string.Empty;
        if (string.Equals(_currentYamlFingerprint, fingerprint, StringComparison.Ordinal) ||
            (_hasPendingConfig && string.Equals(_pendingYamlFingerprint, fingerprint, StringComparison.Ordinal)))
        {
            return;
        }

        if (!SecondaryAttackConfigLoader.TryCompileSnapshot(fingerprint, out SecondaryAttackCompiledSnapshot? snapshot))
        {
            return;
        }

        StageConfig(snapshot!, fingerprint);
    }

    private static void StageConfig(SecondaryAttackCompiledSnapshot snapshot, string fingerprint)
    {
        _pendingCompiledSnapshot = snapshot;
        _pendingYamlFingerprint = fingerprint;
        _hasPendingConfig = true;
        CommitPendingConfig(force: false, applyToObjectDbImmediately: true);
    }

    private static bool CommitPendingConfig(bool force, bool applyToObjectDbImmediately)
    {
        if (!_hasPendingConfig || _pendingCompiledSnapshot == null)
        {
            return false;
        }

        if (!force && !CanApplyPendingConfigNow())
        {
            return false;
        }

        _currentCompiledSnapshot = _pendingCompiledSnapshot;
        _currentYamlFingerprint = _pendingYamlFingerprint ?? _currentYamlFingerprint;
        _pendingCompiledSnapshot = null;
        _pendingYamlFingerprint = null;
        _hasPendingConfig = false;

        if (applyToObjectDbImmediately && ObjectDB.instance != null)
        {
            ApplyCompiledSnapshotToObjectDb(ObjectDB.instance, _currentCompiledSnapshot, emitMissingWarnings: true);
        }

        CaptainValheimPlugin.ModLogger.LogInfo("Applied staged YAML config changes.");
        return true;
    }

    private static void ApplyCompiledSnapshotToObjectDb(
        ObjectDB objectDb,
        SecondaryAttackCompiledSnapshot compiledSnapshot,
        bool emitMissingWarnings)
    {
        _currentAppliedWorldSnapshot = SecondaryAttackWorldApplySystem.Apply(
            objectDb,
            compiledSnapshot,
            emitMissingWarnings);
    }

    private static bool CanApplyPendingConfigNow()
    {
        Player? localPlayer = Player.m_localPlayer;
        if (localPlayer == null)
        {
            return true;
        }

        if (((Humanoid)localPlayer).m_currentAttack != null)
        {
            return false;
        }

        return !ShieldRuntimeSystem.IsShieldChargeActive(localPlayer) &&
               !SecondaryAttackManager.HasActiveAsyncSecondaryWorkForFacade(localPlayer);
    }
}
