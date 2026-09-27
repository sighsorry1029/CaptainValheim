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

    private enum PendingCommitResult
    {
        None,
        Deferred,
        Committed,
        Failed
    }

    private static readonly object ReloadLock = new();
    private static FileSystemWatcher? _watcher;
    private static CustomSyncedValue<string>? _syncedYamlValue;
    private static SecondaryAttackCompiledSnapshot _currentCompiledSnapshot = SecondaryAttackCompiledSnapshot.Empty;
    private static SecondaryAttackCompiledSnapshot? _pendingCompiledSnapshot;
    private static SecondaryAttackAppliedWorldSnapshot _currentAppliedWorldSnapshot = SecondaryAttackAppliedWorldSnapshot.Empty;
    private static DateTime _localYamlReloadDueUtc;
    private static bool _hasPendingLocalYamlReload;
    private static bool _suppressSyncedYamlChanged;
    private static YamlAuthorityMode _yamlAuthorityMode;
    private static string _currentYamlFingerprint = string.Empty;
    private static string? _pendingYamlFingerprint;
    private static string? _rejectedYamlFingerprint;
    private static string _lastObservedSyncedYamlFingerprint = string.Empty;
    private static bool _hasObservedSyncedYaml;

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
        ClearConfigState();
    }

    internal static void TryApplyPendingConfig()
    {
        RefreshYamlAuthorityMode();
        StageSyncedYamlIfReady();
        TryReloadDebouncedLocalYaml();
        CommitPendingConfig(
            force: false,
            objectDb: ObjectDB.instance,
            emitMissingWarnings: true);
    }

    internal static void ApplyPendingConfigToObjectDb(ObjectDB objectDb, bool emitMissingWarnings)
    {
        try
        {
            RefreshYamlAuthorityMode();
            StageSyncedYamlIfReady();
            TryReloadDebouncedLocalYaml();
            PendingCommitResult result = CommitPendingConfig(
                force: true,
                objectDb,
                emitMissingWarnings);
            if (result is PendingCommitResult.None or PendingCommitResult.Deferred)
            {
                ApplyCompiledSnapshotToObjectDb(objectDb, _currentCompiledSnapshot, emitMissingWarnings);
            }
        }
        catch (Exception exception)
        {
            _currentAppliedWorldSnapshot = SecondaryAttackAppliedWorldSnapshot.Empty;
            StageCurrentConfigForRetry();
            CaptainValheimPlugin.ModLogger.LogError(
                $"Failed to apply CaptainValheim config during ObjectDB initialization: {exception.Message}");
        }
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

        lock (ReloadLock)
        {
            _hasPendingLocalYamlReload = true;
            _localYamlReloadDueUtc = DateTime.UtcNow.AddTicks(SecondaryAttackYamlConfig.ReloadDelayTicks);
        }
    }

    private static void TryReloadDebouncedLocalYaml()
    {
        if (_yamlAuthorityMode != YamlAuthorityMode.LocalFiles)
        {
            return;
        }

        lock (ReloadLock)
        {
            if (!_hasPendingLocalYamlReload || DateTime.UtcNow < _localYamlReloadDueUtc)
            {
                return;
            }

            _hasPendingLocalYamlReload = false;
            if (!ReloadLocalYaml())
            {
                _hasPendingLocalYamlReload = true;
                _localYamlReloadDueUtc = DateTime.UtcNow.AddTicks(SecondaryAttackYamlConfig.ReloadDelayTicks);
            }
        }
    }

    private static bool ReloadLocalYaml()
    {
        if (_yamlAuthorityMode != YamlAuthorityMode.LocalFiles)
        {
            return true;
        }

        string yamlText;
        try
        {
            SecondaryAttackConfigLoader.EnsureLocalFileExists();
            yamlText = SecondaryAttackConfigLoader.ReadLocalYamlText();
        }
        catch (Exception exception)
        {
            CaptainValheimPlugin.ModLogger.LogError(
                $"Failed to read {SecondaryAttackYamlConfig.FileName}: {exception.Message}");
            return false;
        }

        ApplyYamlText(yamlText);
        return true;
    }

    private static void OnSyncedYamlChanged()
    {
        if (!_suppressSyncedYamlChanged)
        {
            StageSyncedYamlIfReady();
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
                _hasObservedSyncedYaml = false;
                _lastObservedSyncedYamlFingerprint = string.Empty;
                SetupWatcher();
                ReloadLocalYaml();
                CaptainValheimPlugin.ModLogger.LogInfo("CaptainValheim YAML authority mode: LocalFiles.");
                break;
            case YamlAuthorityMode.SyncedOnly:
                _hasObservedSyncedYaml = false;
                _lastObservedSyncedYamlFingerprint = string.Empty;
                DisposeWatcher();
                ClearConfigState();
                StageSyncedYamlIfReady();
                CaptainValheimPlugin.ModLogger.LogInfo("CaptainValheim YAML authority mode: SyncedOnly.");
                break;
        }
    }

    private static void StageSyncedYamlIfReady()
    {
        if (_yamlAuthorityMode != YamlAuthorityMode.SyncedOnly ||
            !CaptainValheimPlugin.ConfigSync.InitialSyncDone)
        {
            return;
        }

        string yamlText = _syncedYamlValue?.Value ?? string.Empty;
        if (_hasObservedSyncedYaml &&
            string.Equals(
                _lastObservedSyncedYamlFingerprint,
                yamlText,
                StringComparison.Ordinal))
        {
            return;
        }

        _hasObservedSyncedYaml = true;
        _lastObservedSyncedYamlFingerprint = yamlText;
        if (string.IsNullOrWhiteSpace(yamlText))
        {
            if (!ReferenceEquals(_currentCompiledSnapshot, SecondaryAttackCompiledSnapshot.Empty) ||
                _pendingCompiledSnapshot != null)
            {
                ClearConfigState();
            }

            return;
        }

        ApplyYamlText(yamlText);
    }

    private static void ClearConfigState()
    {
        _pendingCompiledSnapshot = null;
        _pendingYamlFingerprint = null;
        _currentCompiledSnapshot = SecondaryAttackCompiledSnapshot.Empty;
        _currentYamlFingerprint = string.Empty;
        _rejectedYamlFingerprint = null;
        if (_yamlAuthorityMode != YamlAuthorityMode.SyncedOnly)
        {
            _hasObservedSyncedYaml = false;
            _lastObservedSyncedYamlFingerprint = string.Empty;
        }
        _currentAppliedWorldSnapshot = SecondaryAttackAppliedWorldSnapshot.Empty;
        if (ObjectDB.instance != null)
        {
            SecondaryAttackObjectDbStateStore.Restore(ObjectDB.instance);
            ShieldChargeCooldownStatusSystem.UnregisterStatusEffect(ObjectDB.instance);
        }

        ShieldRuntimeSystem.ResetTransientState();
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
        lock (ReloadLock)
        {
            _hasPendingLocalYamlReload = false;
            _localYamlReloadDueUtc = default;
        }
    }

    private static bool ApplyYamlText(string yamlText)
    {
        string fingerprint = yamlText ?? string.Empty;
        if (string.Equals(_rejectedYamlFingerprint, fingerprint, StringComparison.Ordinal))
        {
            DiscardPendingConfig();
            return false;
        }

        if (!ReferenceEquals(_currentCompiledSnapshot, SecondaryAttackCompiledSnapshot.Empty) &&
            !ReferenceEquals(_currentAppliedWorldSnapshot, SecondaryAttackAppliedWorldSnapshot.Empty) &&
            string.Equals(_currentYamlFingerprint, fingerprint, StringComparison.Ordinal))
        {
            _rejectedYamlFingerprint = null;
            DiscardPendingConfig();
            return true;
        }

        if (_pendingCompiledSnapshot != null &&
            string.Equals(_pendingYamlFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return true;
        }

        if (!SecondaryAttackConfigLoader.TryCompileSnapshot(fingerprint, out SecondaryAttackCompiledSnapshot? snapshot))
        {
            _rejectedYamlFingerprint = fingerprint;
            DiscardPendingConfig();
            return false;
        }

        _rejectedYamlFingerprint = null;
        StageConfig(snapshot!, fingerprint);
        return true;
    }

    private static void StageConfig(SecondaryAttackCompiledSnapshot snapshot, string fingerprint)
    {
        _pendingCompiledSnapshot = snapshot;
        _pendingYamlFingerprint = fingerprint;
    }

    private static void StageCurrentConfigForRetry()
    {
        if (_pendingCompiledSnapshot != null)
        {
            return;
        }

        SecondaryAttackCompiledSnapshot retrySnapshot = _currentCompiledSnapshot;
        string retryFingerprint = _currentYamlFingerprint;
        _currentYamlFingerprint = string.Empty;
        if (!ReferenceEquals(retrySnapshot, SecondaryAttackCompiledSnapshot.Empty) &&
            !string.IsNullOrWhiteSpace(retryFingerprint))
        {
            StageConfig(retrySnapshot, retryFingerprint);
        }
    }

    private static PendingCommitResult CommitPendingConfig(
        bool force,
        ObjectDB? objectDb,
        bool emitMissingWarnings)
    {
        if (_pendingCompiledSnapshot == null)
        {
            return PendingCommitResult.None;
        }

        if (HasPendingLocalYamlReload() ||
            objectDb == null ||
            (!force && !CanApplyPendingConfigNow()))
        {
            return PendingCommitResult.Deferred;
        }

        SecondaryAttackCompiledSnapshot nextSnapshot = _pendingCompiledSnapshot;
        string nextFingerprint = _pendingYamlFingerprint ?? string.Empty;
        SecondaryAttackAppliedWorldSnapshot nextAppliedWorldSnapshot;
        try
        {
            nextAppliedWorldSnapshot = SecondaryAttackWorldApplySystem.Apply(
                objectDb,
                nextSnapshot,
                emitMissingWarnings);
        }
        catch (Exception applyException)
        {
            CaptainValheimPlugin.ModLogger.LogError(
                $"Failed to apply staged YAML config; keeping the last working config: {applyException.Message}");
            DiscardPendingConfig();
            try
            {
                ApplyCompiledSnapshotToObjectDb(
                    objectDb,
                    _currentCompiledSnapshot,
                    emitMissingWarnings: false);
            }
            catch (Exception restoreException)
            {
                _currentAppliedWorldSnapshot = SecondaryAttackAppliedWorldSnapshot.Empty;
                StageCurrentConfigForRetry();
                CaptainValheimPlugin.ModLogger.LogError(
                    $"Failed to restore the last working YAML config: {restoreException.Message}");
            }

            return PendingCommitResult.Failed;
        }

        _currentCompiledSnapshot = nextSnapshot;
        _currentYamlFingerprint = nextFingerprint;
        _currentAppliedWorldSnapshot = nextAppliedWorldSnapshot;
        DiscardPendingConfig();
        PublishCommittedLocalYaml(nextFingerprint);
        CaptainValheimPlugin.ModLogger.LogInfo("Applied staged YAML config changes.");
        return PendingCommitResult.Committed;
    }

    private static bool HasPendingLocalYamlReload()
    {
        if (_yamlAuthorityMode != YamlAuthorityMode.LocalFiles)
        {
            return false;
        }

        lock (ReloadLock)
        {
            return _hasPendingLocalYamlReload;
        }
    }

    private static void PublishCommittedLocalYaml(string yamlText)
    {
        if (_yamlAuthorityMode != YamlAuthorityMode.LocalFiles || _syncedYamlValue == null)
        {
            return;
        }

        _suppressSyncedYamlChanged = true;
        try
        {
            _syncedYamlValue.AssignLocalValue(yamlText);
        }
        catch (Exception exception)
        {
            CaptainValheimPlugin.ModLogger.LogError(
                $"Failed to synchronize the committed YAML config: {exception.Message}");
        }
        finally
        {
            _suppressSyncedYamlChanged = false;
        }
    }

    private static void DiscardPendingConfig()
    {
        _pendingCompiledSnapshot = null;
        _pendingYamlFingerprint = null;
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

        if (GameAccess.CurrentAttack(localPlayer) != null)
        {
            return false;
        }

        return !ShieldRuntimeSystem.IsShieldChargeActive(localPlayer) &&
               !SecondaryAttackManager.HasActiveAsyncSecondaryWorkForFacade(localPlayer);
    }
}
