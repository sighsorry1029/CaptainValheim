using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private static readonly Dictionary<SecondaryAttackYamlDomainId, CustomSyncedValue<string>> SyncedYamlValues = new();
    private static SecondaryAttackCompiledSnapshot _currentCompiledSnapshot = SecondaryAttackCompiledSnapshot.Empty;
    private static SecondaryAttackCompiledSnapshot? _pendingCompiledSnapshot;
    private static SecondaryAttackAppliedWorldSnapshot _currentAppliedWorldSnapshot = SecondaryAttackAppliedWorldSnapshot.Empty;
    private static DateTime _lastYamlReloadTime;
    private static bool _hasPendingConfig;
    private static bool _hasPendingWorldReapply;
    private static int _nextSnapshotId = 1;
    private static bool _suppressSyncedYamlChanged;
    private static YamlAuthorityMode _yamlAuthorityMode;
    private static string _currentYamlFingerprint = string.Empty;
    private static string? _pendingYamlFingerprint;

    internal static SecondaryAttackCompiledSnapshot CurrentCompiledSnapshot => _currentCompiledSnapshot;

    internal static SecondaryAttackAppliedWorldSnapshot CurrentAppliedWorldSnapshot => _currentAppliedWorldSnapshot;

    public static void Initialize()
    {
        SecondaryAttackConfigLoader.EnsureLocalFilesExist();
        InitializeSyncedYamlValues();

        RefreshYamlAuthorityMode(force: true);
    }

    public static void Dispose()
    {
        DisposeSyncedYamlValues();

        _watcher?.Dispose();
        _watcher = null;
    }

    public static void ApplyToObjectDb(ObjectDB objectDb, bool emitMissingWarnings)
    {
        RefreshYamlAuthorityMode();
        ApplyCompiledSnapshotToObjectDb(objectDb, _currentCompiledSnapshot, emitMissingWarnings);
    }

    internal static void TryApplyPendingConfig()
    {
        RefreshYamlAuthorityMode();
        if (CommitPendingConfig(force: false, applyToObjectDbImmediately: true))
        {
            return;
        }

        CommitPendingWorldReapply(force: false);
    }

    internal static void RequestCurrentWorldReapply()
    {
        lock (ReloadLock)
        {
            StageWorldReapply();
        }
    }

    internal static void ApplyPendingConfigToObjectDb(ObjectDB objectDb, bool emitMissingWarnings)
    {
        RefreshYamlAuthorityMode();
        bool appliedPendingConfig = CommitPendingConfig(force: true, applyToObjectDbImmediately: false);
        ApplyCompiledSnapshotToObjectDb(objectDb, _currentCompiledSnapshot, emitMissingWarnings);
        if (appliedPendingConfig)
        {
            CaptainValheimPlugin.ModLogger.LogInfo("Applied staged YAML config changes.");
        }
    }

    internal static void ApplyPendingConfigToZNetScene(ZNetScene scene, bool emitMissingWarnings)
    {
        RefreshYamlAuthorityMode();
        bool appliedPendingConfig = CommitPendingConfig(force: true, applyToObjectDbImmediately: false);
        ApplyCompiledSnapshotToZNetScene(scene, _currentCompiledSnapshot, emitMissingWarnings);
        if (ObjectDB.instance != null)
        {
            ApplyCompiledSnapshotToObjectDb(ObjectDB.instance, _currentCompiledSnapshot, emitMissingWarnings, applyZNetScene: false);
        }

        if (appliedPendingConfig)
        {
            CaptainValheimPlugin.ModLogger.LogInfo("Applied staged YAML config changes.");
        }
    }

    private static void SetupWatcher()
    {
        if (_watcher != null)
        {
            return;
        }

        Directory.CreateDirectory(SecondaryAttackYamlDomainRegistry.ConfigDirectoryPath);
        _watcher = new FileSystemWatcher(SecondaryAttackYamlDomainRegistry.ConfigDirectoryPath, SecondaryAttackYamlDomainRegistry.ShieldsYamlFileName);
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
        if (now.Ticks - _lastYamlReloadTime.Ticks < SecondaryAttackYamlDomainRegistry.ReloadDelayTicks)
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

        SecondaryAttackConfigLoader.EnsureLocalFilesExist();
        SecondaryAttackYamlTexts yamlTexts = SecondaryAttackConfigLoader.ReadLocalYamlTexts();

        if (SyncedYamlValues.Count == SecondaryAttackYamlDomainRegistry.Domains.Count)
        {
            _suppressSyncedYamlChanged = true;
            try
            {
                foreach (SecondaryAttackYamlDomain domain in SecondaryAttackYamlDomainRegistry.Domains)
                {
                    SyncedYamlValues[domain.Id].AssignLocalValue(yamlTexts.Get(domain.Id));
                }
            }
            finally
            {
                _suppressSyncedYamlChanged = false;
            }
        }

        ApplyYamlTexts(yamlTexts);
    }

    private static void OnSyncedYamlChanged()
    {
        if (_suppressSyncedYamlChanged)
        {
            return;
        }

        ApplyYamlTexts(ReadSyncedYamlTexts());
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
                if (AnySyncedYamlHasValue())
                {
                    ApplyYamlTexts(ReadSyncedYamlTexts());
                }
                else
                {
                    _pendingCompiledSnapshot = null;
                    _pendingYamlFingerprint = null;
                    _hasPendingConfig = false;
                    _hasPendingWorldReapply = false;
                    _currentCompiledSnapshot = SecondaryAttackCompiledSnapshot.Empty;
                    _currentYamlFingerprint = string.Empty;
                    _currentAppliedWorldSnapshot = SecondaryAttackAppliedWorldSnapshot.Empty;
                    if (ZNetScene.instance != null)
                    {
                        ApplyCompiledSnapshotToZNetScene(ZNetScene.instance, _currentCompiledSnapshot, emitMissingWarnings: true);
                    }
                }

                CaptainValheimPlugin.ModLogger.LogInfo("CaptainValheim YAML authority mode: SyncedOnly.");
                break;
        }
    }

    private static YamlAuthorityMode DetermineYamlAuthorityMode()
    {
        return ZNet.instance != null && !ZNet.instance.IsServer()
            ? YamlAuthorityMode.SyncedOnly
            : YamlAuthorityMode.LocalFiles;
    }

    private static void InitializeSyncedYamlValues()
    {
        DisposeSyncedYamlValues();
        foreach (SecondaryAttackYamlDomain domain in SecondaryAttackYamlDomainRegistry.Domains)
        {
            CustomSyncedValue<string> syncedValue = new(CaptainValheimPlugin.ConfigSync, domain.SyncedIdentifier, "");
            syncedValue.ValueChanged += OnSyncedYamlChanged;
            SyncedYamlValues[domain.Id] = syncedValue;
        }
    }

    private static void DisposeSyncedYamlValues()
    {
        foreach (CustomSyncedValue<string> syncedValue in SyncedYamlValues.Values)
        {
            syncedValue.ValueChanged -= OnSyncedYamlChanged;
        }

        SyncedYamlValues.Clear();
    }

    private static SecondaryAttackYamlTexts ReadSyncedYamlTexts()
    {
        Dictionary<SecondaryAttackYamlDomainId, string> texts = new();
        foreach (SecondaryAttackYamlDomain domain in SecondaryAttackYamlDomainRegistry.Domains)
        {
            texts[domain.Id] = SyncedYamlValues.TryGetValue(domain.Id, out CustomSyncedValue<string>? syncedValue)
                ? syncedValue.Value
                : string.Empty;
        }

        return new SecondaryAttackYamlTexts(texts);
    }

    private static bool AnySyncedYamlHasValue()
    {
        return SyncedYamlValues.Values.Any(syncedValue => !string.IsNullOrEmpty(syncedValue.Value));
    }

    private static void DisposeWatcher()
    {
        if (_watcher == null)
        {
            return;
        }

        _watcher.Dispose();
        _watcher = null;
    }

    private static void ApplyYamlTexts(SecondaryAttackYamlTexts yamlTexts)
    {
        string fingerprint = yamlTexts.GetContentFingerprint();
        if (string.Equals(_currentYamlFingerprint, fingerprint, StringComparison.Ordinal) ||
            (_hasPendingConfig && string.Equals(_pendingYamlFingerprint, fingerprint, StringComparison.Ordinal)))
        {
            return;
        }

        if (!SecondaryAttackConfigLoader.TryCompileSnapshot(_nextSnapshotId++, yamlTexts, out SecondaryAttackCompiledSnapshot? snapshot))
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
        CommitPendingConfig(force: true, applyToObjectDbImmediately: true);
    }

    private static void StageWorldReapply()
    {
        _hasPendingWorldReapply = true;
        CommitPendingWorldReapply(force: true);
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

    private static bool CommitPendingWorldReapply(bool force)
    {
        if (!_hasPendingWorldReapply)
        {
            return false;
        }

        if (!force && !CanApplyPendingConfigNow())
        {
            return false;
        }

        if (ObjectDB.instance == null)
        {
            return false;
        }

        ApplyCompiledSnapshotToObjectDb(ObjectDB.instance, _currentCompiledSnapshot, emitMissingWarnings: true);
        CaptainValheimPlugin.ModLogger.LogInfo("Applied staged world-apply config changes.");
        return true;
    }

    private static void ApplyCompiledSnapshotToObjectDb(
        ObjectDB objectDb,
        SecondaryAttackCompiledSnapshot compiledSnapshot,
        bool emitMissingWarnings,
        bool applyZNetScene = true)
    {
        _hasPendingWorldReapply = false;
        if (applyZNetScene && ZNetScene.instance != null)
        {
            ApplyCompiledSnapshotToZNetScene(ZNetScene.instance, compiledSnapshot, emitMissingWarnings);
        }

        _currentAppliedWorldSnapshot = SecondaryAttackWorldApplySystem.Apply(objectDb, compiledSnapshot, emitMissingWarnings);
    }

    private static void ApplyCompiledSnapshotToZNetScene(
        ZNetScene scene,
        SecondaryAttackCompiledSnapshot compiledSnapshot,
        bool emitMissingWarnings)
    {
        SecondaryAttackWorldApplyContributors.ApplyToZNetScene(scene, compiledSnapshot, emitMissingWarnings);
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

        return !ShieldRuntimeSystem.IsShieldChargeActiveForDebug(localPlayer) &&
               !SecondaryAttackManager.HasActiveAsyncSecondaryWorkForFacade(localPlayer);
    }

}
