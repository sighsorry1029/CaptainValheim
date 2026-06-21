using System;
using HarmonyLib;

namespace CaptainValheim;

internal static class SecondaryAttackNetworkCompat
{
    private static string VersionCheckRpcName => $"{CaptainValheimPlugin.ModName}_VersionCheck";

    public static void HandleVersionCheck(ZRpc rpc, ZPackage pkg)
    {
        string? version = pkg.ReadString();
        CaptainValheimPlugin.ModLogger.LogInfo($"Version check, local: {CaptainValheimPlugin.ModVersion}, remote: {version}");
        if (version != CaptainValheimPlugin.ModVersion)
        {
            CaptainValheimPlugin.ConnectionError = $"{CaptainValheimPlugin.ModName} Installed: {CaptainValheimPlugin.ModVersion}\n Needed: {version}";
            if (!ZNet.instance.IsServer())
            {
                return;
            }

            CaptainValheimPlugin.ModLogger.LogWarning($"Peer ({rpc.m_socket.GetHostName()}) has incompatible version, disconnecting...");
            rpc.Invoke("Error", 3);
            return;
        }

        if (!ZNet.instance.IsServer())
        {
            CaptainValheimPlugin.ModLogger.LogInfo("Received same version from server.");
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection))]
    private static class RegisterAndSendVersionCheckPatch
    {
        private static void Prefix(ZNetPeer peer)
        {
            CaptainValheimPlugin.ModLogger.LogDebug("Registering version RPC handler");
            peer.m_rpc.Register(VersionCheckRpcName, new Action<ZRpc, ZPackage>(HandleVersionCheck));

            CaptainValheimPlugin.ModLogger.LogInfo("Invoking version check");
            ZPackage zpackage = new();
            zpackage.Write(CaptainValheimPlugin.ModVersion);
            peer.m_rpc.Invoke(VersionCheckRpcName, zpackage);
        }
    }

    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.ShowConnectError))]
    private static class ShowConnectionErrorPatch
    {
        private static void Postfix(FejdStartup __instance)
        {
            if (!__instance.m_connectionFailedPanel.activeSelf)
            {
                return;
            }

            __instance.m_connectionFailedError.fontSizeMax = 25;
            __instance.m_connectionFailedError.fontSizeMin = 15;
            __instance.m_connectionFailedError.text += $"\n{CaptainValheimPlugin.ConnectionError}";
        }
    }
}
