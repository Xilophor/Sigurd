using GameNetcodeStuff;
using HarmonyLib;
using Sigurd.ServerAPI.Events.EventArgs.Player;

namespace Sigurd.ServerAPI.Events.Patches.Player;

[HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.OnPlayerDC))]
internal static class Left
{
    private static void Prefix(StartOfRound __instance, int playerObjectNumber, ulong clientId)
    {
        PlayerControllerB player = __instance.allPlayerScripts[playerObjectNumber];
        if (__instance.ClientPlayerList.ContainsKey(clientId) &&
            Cache.Player.ConnectedPlayers.Contains(player.actualClientId))
        {
            Cache.Player.ConnectedPlayers.Remove(player.actualClientId);
            Handlers.Player.OnLeft(new LeftEventArgs(Features.Player.GetOrAdd(player)));
        }
    }
}

[HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.OnLocalDisconnect))]
internal static class LocalLeft
{
    private static void Prefix(StartOfRound __instance)
    {
        PlayerControllerB player = __instance.localPlayerController;
        Handlers.Player.OnLeft(new LeftEventArgs(Features.Player.GetOrAdd(player)));
        Cache.Player.ConnectedPlayers.Clear();

        foreach (PlayerControllerB playerController in StartOfRound.Instance.allPlayerScripts)
        {
            playerController.isPlayerDead = false;
            playerController.isPlayerControlled = false;
        }

        Features.Player.Dictionary.Clear();
    }
}