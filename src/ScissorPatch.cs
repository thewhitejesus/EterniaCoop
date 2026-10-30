// The renderer clips what GameNative draws to the picture area (a scissor rectangle). Zoomed out past the game's own
// limit, the battle view reaches below the original picture, so the continued ground (BattleGround) would be cut off
// there. While the co-op camera has zoomed out that far in battle, the full-width scissor is switched off right after
// the game (and any other mod, e.g. Uncropped Backgrounds, which widens it) has set it. Narrower scissors (menu lists
// and the like) are left alone.
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace EterniaCoop
{
    [HarmonyPatch("UgxGraphicsGlobalStateManager, Assembly-CSharp", "ApplyScissorTest")]
    static class ScissorPatch
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix(CommandBuffer commandBuffer, bool isEnable, int scissorWidth)
        {
            if (commandBuffer == null || !isEnable || scissorWidth < Screen.width * 0.9f || !Active()) return;
            commandBuffer.DisableScissorRect();
        }

        static bool Active()
        {
            return Features.Ground && BattleGround.Usable && Settings.CoopCamera.Value && Native.Base != 0 &&
                Battle.Actors() != System.IntPtr.Zero &&
                System.Runtime.InteropServices.Marshal.ReadInt16(Native.At(Rva.ZoomNow)) > BattleGround.ZoomGameMax;
        }
    }
}
