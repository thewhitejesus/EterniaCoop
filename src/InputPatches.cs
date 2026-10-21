// Harmony patches on the game's C# input layer (Assembly-CSharp).
using System;
using HarmonyLib;

namespace EterniaCoop
{
    // GameNative's input setup calls UnityInputFunctions.InitializeInput -> UgxInputManager.Initialize(1) before it
    // writes its own player count, so this is the moment to create more players and patch the native side.
    [HarmonyPatch("UgxInputManager, Assembly-CSharp", "Initialize")]
    static class Patch_InputManagerInitialize
    {
        static void Prefix(object __instance, ref int playerNum)
        {
            Players.InputManager = __instance;
            int players = Settings.PlayerCount.Value;
            if (Compatibility.Ok && players >= 2 && players <= 4) Features.Check();
            if (!Compatibility.Ok || players < 2 || players > 4)
            {
                Plugin.Log.LogMessage("UgxInputManager.Initialize(playerNum=" + playerNum + "): co-op not active");
                return;
            }
            Plugin.Log.LogMessage("UgxInputManager.Initialize(playerNum=" + playerNum + ") -> " + players);
            playerNum = players;
            NativePatches.Apply(players);
            if (Settings.CoopBattle.Value && Features.CoopBattle) CoopBattleInput.Install();
            if (Settings.PerPlayerRumble.Value && Features.Rumble) RumbleRouter.Install();
            Plugin.CoopActive = true;
        }
    }

    // GameNative calls these every frame for every player. Players 2-4 without a device of their own get
    // all-zero input instead of whatever every unassigned device is doing.
    [HarmonyPatch("UnityInputFunctions, Assembly-CSharp", "GetInputInfo")]
    static class Patch_GetInputInfo
    {
        const int EntrySize = 20;   // sizeof(UgxInputParam): 3 bools (+pad), float X, Y, Axis, int id

        static bool Prefix(IntPtr outParamArray, int outParamArraySize, int playerNo)
        {
#if COOP_DEBUG
            ResearchTools.CountInfo(playerNo);
#endif
            if (!Players.ShouldBlank(playerNo) || outParamArray == IntPtr.Zero) return true;
            Native.Clear(outParamArray, outParamArraySize * EntrySize);
            return false;
        }
#if COOP_DEBUG
        static void Postfix(IntPtr outParamArray, int outParamArraySize, int playerNo) { ResearchTools.OnInputInfo(outParamArray, outParamArraySize, playerNo); }
#endif
    }

    [HarmonyPatch("UnityInputFunctions, Assembly-CSharp", "GetStickInfo")]
    static class Patch_GetStickInfo
    {
        static bool Prefix(IntPtr outParam, int playerNo)
        {
#if COOP_DEBUG
            ResearchTools.CountStick(playerNo);
#endif
            if (!Players.ShouldBlank(playerNo) || outParam == IntPtr.Zero) return true;
            Native.Clear(outParam, 20);
            return false;
        }
#if COOP_DEBUG
        static void Postfix(IntPtr outParam, bool bLeftStick, int playerNo) { ResearchTools.OnStickInfo(outParam, bLeftStick, playerNo); }
#endif
    }
}
