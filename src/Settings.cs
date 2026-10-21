// All user-facing settings (BepInEx\config\eterniacoop.cfg).
using BepInEx.Configuration;

namespace EterniaCoop
{
    static class Settings
    {
        // [Coop]
        public static ConfigEntry<int> PlayerCount, KeyboardPlayer, JoinMode;
        public static ConfigEntry<bool> AutoAssignControllers, KeepSlotModesAfterBattle, MenuModeTabForAll, LiveModeSync, CoopBattle, PerPlayerRumble;
        public static ConfigEntry<float> RumbleSeconds;
        public static ConfigEntry<string> DeviceAssignments, ManagerKey, ManagerControllerShortcut;
        // [Display]
        public static ConfigEntry<bool> ShowWatermark;
        // [Advanced]
        public static ConfigEntry<bool> ScaleRemapBuffer, HelpersPlayer0Only, StrictVersionCheck;

        public static void Bind(ConfigFile c)
        {
            PlayerCount = c.Bind("Coop", "ForcePlayerCount", 4,
                "Input players to create (the game itself makes 1). 2-4 enables co-op; 0 turns the mod's co-op off.");
            AutoAssignControllers = c.Bind("Coop", "AutoAssignControllers", true,
                "Give each controller its own player automatically (checked every second, so plugging in or out works).");
            KeyboardPlayer = c.Bind("Coop", "KeyboardPlayer", 0,
                "Which player (0-3) gets keyboard + mouse. Controllers fill the other players in order. " +
                "0 = normal (player 1 also gets the first controller). 1 = test co-op with the keyboard as player 2.");
            JoinMode = c.Bind("Coop", "JoinMode", 1,
                "Control mode set for party slots 2-4 when a player gets a controller (1 = Semi-Auto). " +
                "When a controller disconnects that slot goes back to Auto (0).");
            KeepSlotModesAfterBattle = c.Bind("Coop", "KeepSlotModesAfterBattle", true,
                "Stop the game from resetting party slots 2-4 to Auto at the end of every battle.");
            MenuModeTabForAll = c.Bind("Coop", "MenuModeTabForAll", true,
                "Show the Semi-Auto/Auto tab in the Skills menu for every party member, not just the leader.");
            LiveModeSync = c.Bind("Coop", "LiveModeSync", true,
                "Drop in/out mid-battle: slots 2-4 follow the menu setting during battle and go Auto while their player has no controller.");
            CoopBattle = c.Bind("Coop", "CoopBattle", true,
                "Feed players 2-4 into the battle input of party slots 2-4 when those slots aren't on Auto.");
            PerPlayerRumble = c.Bind("Coop", "PerPlayerRumble", true,
                "Rumble the controller of the character that hit or got hit (players 2-4), like the PS1. Uses the game's rumble setting.");
            RumbleSeconds = c.Bind("Coop", "RumbleSeconds", 0.15f, "How long a hit rumbles on players 2-4's controllers.");
            DeviceAssignments = c.Bind("Coop", "DeviceAssignments", "",
                "Saved by the in-game controller manager (F1, or hold L3 + R3): device path = player (0-3), separated by ;. " +
                "Devices not listed follow the automatic order. Clear it to go back to fully automatic.");
            ManagerKey = c.Bind("Coop", "ManagerKey", "F1",
                "Keyboard key that opens the controller manager (Unity key name, e.g. F1, F2, Insert, Home).");
            ManagerControllerShortcut = c.Bind("Coop", "ManagerControllerShortcut", "L3+R3",
                "Controller buttons to hold for half a second to open the controller manager: L3+R3, LB+RB+Back, Back+Start or None. " +
                "Use LB+RB+Back with mods that also use L3+R3 (e.g. Guests from Phantasia's camp menu toggle).");

            ShowWatermark = c.Bind("Display", "ShowWatermark", true,
                "Show a small \"Eternia Co-op\" label in the corner of the main menu so you can tell the mod is running.");

            ScaleRemapBuffer = c.Bind("Advanced", "ScaleRemapBuffer", true,
                "Grow GameNative's per-player remap table with the player count. Must stay on: the load menu crashes otherwise.");
            HelpersPlayer0Only = c.Bind("Advanced", "HelpersPlayer0Only", true,
                "Run GameNative's per-player button-latch helpers for player 1 only. Fixes the spinning camera.");
            StrictVersionCheck = c.Bind("Advanced", "StrictVersionCheck", false,
                "Stay off completely unless GameNative.dll is exactly the build this mod was made for. When off (default), " +
                "a game update or another mod that changed GameNative.dll only turns off the features whose game code changed.");
        }
    }
}
