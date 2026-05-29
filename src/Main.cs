using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace MultiKeyboardProbeClean
{
    public static class Main
    {
        internal static UnityModManager.ModEntry ModEntry;

        private static readonly string DefaultP1Group = "0416:9258";
        private static readonly string DefaultP2Group = "1B1C:1BC6";

        private static string loadedAt = "-";
        private static Vector2 scroll;
        private static List<RawInputProbe.KeyboardGroup> groups = new List<RawInputProbe.KeyboardGroup>();
        private static Harmony harmony;
        private static bool patchesInstalled;
        private static string lastRoute = "아직 없음";
        private static string lastPlayerInputs = "아직 없음";
        private static string lastForceResult = "아직 없음";
        private static int routeLogBudget = 80;
        private static int lastEscapePauseFrame = -1000;

        internal static bool Enabled = true;
        internal static string Player1GroupKey = DefaultP1Group;
        internal static string Player2GroupKey = DefaultP2Group;
        internal static bool RequireMultiplayer = true;

        public static bool Load(UnityModManager.ModEntry entry)
        {
            ModEntry = entry;
            loadedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            entry.OnGUI = OnGUI;
            entry.OnShowGUI = _ => Refresh(entry);
            entry.OnUnload = OnUnload;

            InstallPatches(entry);
            RawKeyboardRouter.Start(entry);
            Refresh(entry);

            entry.Logger.Log("MultiKeyboardProbeClean loaded as RDInput router");
            return true;
        }

        private static bool OnUnload(UnityModManager.ModEntry entry)
        {
            RawKeyboardRouter.Stop();
            if (harmony != null)
            {
                harmony.UnpatchAll("openclaw.multikeyboardprobe.rdinputrouter");
                harmony = null;
            }
            return true;
        }

        private static void InstallPatches(UnityModManager.ModEntry entry)
        {
            try
            {
                harmony = new Harmony("openclaw.multikeyboardprobe.rdinputrouter");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                patchesInstalled = true;
            }
            catch (Exception ex)
            {
                patchesInstalled = false;
                entry.Logger.Log("Patch install failed: " + ex);
            }
        }

        private static void OnGUI(UnityModManager.ModEntry entry)
        {
            GUILayout.Label("MultiKeyboardProbeClean - RDInput router");
            GUILayout.Label("로드 시각: " + loadedAt);
            GUILayout.Label("RDInput 패치 상태: " + (patchesInstalled ? "켜짐" : "꺼짐"));
            GUILayout.Label("Raw Input 상태: " + RawKeyboardRouter.StatusText);
            GUILayout.Label("1P 장치: " + Player1GroupKey + " / 2P 장치: " + Player2GroupKey);
            GUILayout.Label("입력 카운트: 1P " + RawKeyboardRouter.GetPressTotal(0) + " / 2P " + RawKeyboardRouter.GetPressTotal(1));
            GUILayout.Label("Raw 상태: " + RawKeyboardRouter.DebugState);
            GUILayout.Label("마지막 RDInput 라우팅: " + lastRoute);
            GUILayout.Label("playerInputs: " + lastPlayerInputs);
            GUILayout.Label("강제 적용 결과: " + lastForceResult);

            bool nextEnabled = GUILayout.Toggle(Enabled, "키보드별 입력을 RDInput에 연결");
            if (nextEnabled != Enabled)
            {
                Enabled = nextEnabled;
                RawKeyboardRouter.ClearAll();
                entry.Logger.Log("Raw router enabled=" + Enabled);
            }

            bool nextRequireMultiplayer = GUILayout.Toggle(RequireMultiplayer, "로컬 멀티/코옵일 때만 기존 입력 대체");
            if (nextRequireMultiplayer != RequireMultiplayer)
            {
                RequireMultiplayer = nextRequireMultiplayer;
                RawKeyboardRouter.ClearAll();
                entry.Logger.Log("Require multiplayer=" + RequireMultiplayer);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("2P 키보드 분리 강제 적용", GUILayout.Width(190f)))
            {
                ForceTwoKeyboardSplit();
                RefreshPlayerInputsDebug();
            }
            if (GUILayout.Button("Raw Input 재시작", GUILayout.Width(150f)))
            {
                RawKeyboardRouter.Stop();
                RawKeyboardRouter.Start(entry);
            }
            if (GUILayout.Button("키 상태 초기화", GUILayout.Width(150f)))
            {
                RawKeyboardRouter.ClearAll();
            }
            if (GUILayout.Button("키보드 목록 새로고침", GUILayout.Width(180f)))
            {
                Refresh(entry);
                RefreshPlayerInputsDebug();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(360f));
            foreach (RawInputProbe.KeyboardGroup group in groups)
            {
                GUILayout.BeginVertical("box");
                GUILayout.Label(group.DisplayName);
                GUILayout.Label("VID/PID: " + group.VendorId + " / " + group.ProductId);
                GUILayout.Label("인터페이스 수: " + group.InterfaceCount);
                GUILayout.Label("인스턴스: " + group.InstancePreview);
                GUILayout.Label("GroupKey: " + group.GroupKey);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("1P로 지정", GUILayout.Width(110f)))
                {
                    Player1GroupKey = group.GroupKey;
                    RawKeyboardRouter.ClearAll();
                }
                if (GUILayout.Button("2P로 지정", GUILayout.Width(110f)))
                {
                    Player2GroupKey = group.GroupKey;
                    RawKeyboardRouter.ClearAll();
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
        }

        private static void Refresh(UnityModManager.ModEntry entry)
        {
            groups = RawInputProbe.GetInterestingKeyboardGroups();
            entry.Logger.Log("Interesting keyboard groups: " + groups.Count);
            foreach (RawInputProbe.KeyboardGroup group in groups)
                entry.Logger.Log(group.DisplayName + " | " + group.GroupKey + " | interfaces=" + group.InterfaceCount);
            RefreshPlayerInputsDebug();
        }

        private static void ForceTwoKeyboardSplit()
        {
            try
            {
                ControllerType[] controllerTypes = new[] { ControllerType.KeyboardLeft, ControllerType.KeyboardRight };
                scrPlayerManager.SetPlayerCount(2);
                RDInput.ReassignControllers(2, controllerTypes, new Rewired.Joystick[0]);
                ForcePlayerInputsToKeyboardOnly();

                scrController controller = ADOBase.controller;
                if (controller != null)
                {
                    controller.RestartProgress();
                    controller.Restart(false);
                }

                RawKeyboardRouter.ClearAll();
                lastForceResult = "성공: playerCount=2, 1P=KeyboardLeft, 2P=KeyboardRight only, level restarted";
                Log("ForceTwoKeyboardSplit succeeded");
            }
            catch (Exception ex)
            {
                lastForceResult = "실패: " + ex.GetType().Name + " - " + ex.Message;
                Log("ForceTwoKeyboardSplit failed: " + ex);
            }
        }

        private static void RefreshPlayerInputsDebug()
        {
            try
            {
                if (RDInput.playerInputs == null)
                {
                    lastPlayerInputs = "null";
                    return;
                }

                List<string> slots = new List<string>();
                for (int i = 0; i < RDInput.playerInputs.Count; i++)
                {
                    List<string> names = new List<string>();
                    foreach (RDInputType input in RDInput.playerInputs[i])
                        names.Add(DescribeInput(input));
                    slots.Add("P" + (i + 1) + "=" + string.Join("+", names.ToArray()));
                }
                lastPlayerInputs = "count=" + RDInput.playerInputs.Count + " | " + string.Join(" | ", slots.ToArray());
            }
            catch (Exception ex)
            {
                lastPlayerInputs = "err: " + ex.Message;
            }
        }

        private static void ForcePlayerInputsToKeyboardOnly()
        {
            RDInput.playerInputs.Clear();

            HashSet<RDInputType> p1 = new HashSet<RDInputType>();
            p1.Add(RDInput.keyboardLeft);
            RDInput.playerInputs.Add(p1);

            HashSet<RDInputType> p2 = new HashSet<RDInputType>();
            p2.Add(RDInput.keyboardRight);
            RDInput.playerInputs.Add(p2);
        }

        private static string DescribeInput(RDInputType input)
        {
            if (input == null)
                return "null";
            if (ReferenceEquals(input, RDInput.keyboardInput))
                return "keyboardFull";
            if (ReferenceEquals(input, RDInput.keyboardLeft))
                return "keyboardLeft";
            if (ReferenceEquals(input, RDInput.keyboardRight))
                return "keyboardRight";
            if (ReferenceEquals(input, RDInput.asyncKeyboard))
                return "asyncFull";
            if (ReferenceEquals(input, RDInput.asyncKeyboardLeft))
                return "asyncLeft";
            if (ReferenceEquals(input, RDInput.asyncKeyboardRight))
                return "asyncRight";
            if (ReferenceEquals(input, RDInput.mouseInput))
                return "mouse";
            return input.GetType().Name;
        }

        internal static int PlayerIdOf(scrPlayer player)
        {
            if (player == null)
                return -1;
            if (player.playerID == 0 || player.playerID == 1)
                return player.playerID;
            return -1;
        }

        internal static bool ShouldRoute(scrPlayer player)
        {
            if (!Enabled || !RawKeyboardRouter.IsRunning || PlayerIdOf(player) < 0)
                return false;

            if (!RequireMultiplayer)
                return true;

            try
            {
                scrController controller = ADOBase.controller;
                if (controller == null)
                    return false;

                if (scrController.coopMode)
                    return true;

                scrPlayerManager manager = controller.playerManager;
                if (manager != null && manager.players != null && manager.players.Length >= 2)
                    return true;
            }
            catch
            {
            }

            return false;
        }

        internal static int PlayerForInputInstance(object inputInstance)
        {
            try
            {
                if (ReferenceEquals(inputInstance, RDInput.keyboardLeft))
                    return 0;
                if (ReferenceEquals(inputInstance, RDInput.keyboardRight))
                    return 1;
            }
            catch
            {
            }
            return -1;
        }

        internal static void UpdateRouteDebug(object inputInstance, ButtonState state, int playerId, int result)
        {
            string typeName = inputInstance == null ? "null" : inputInstance.GetType().Name;
            lastRoute = typeName + " state=" + state + " player=" + playerId + " result=" + result + " frame=" + Time.frameCount;
            if (routeLogBudget > 0 && result > 0)
            {
                routeLogBudget--;
                Log("RDInput route => " + lastRoute);
            }
        }

        internal static void Log(string message)
        {
            if (ModEntry != null)
                ModEntry.Logger.Log(message);
        }

        internal static void TryTogglePauseFromEscape(scrController controller)
        {
            if (!Enabled || controller == null)
                return;

            int frame = Time.frameCount;
            if (frame == lastEscapePauseFrame)
                return;

            if (!UnityEngine.Input.GetKeyDown(KeyCode.Escape))
                return;

            lastEscapePauseFrame = frame;
            try
            {
                if (controller.paused)
                {
                    if (controller.pauseMenu != null)
                    {
                        MethodInfo unpause = AccessTools.Method(typeof(PauseMenu), "Unpause");
                        if (unpause != null)
                            unpause.Invoke(controller.pauseMenu, null);
                        else
                            controller.TogglePauseGame();
                    }
                    else
                        controller.TogglePauseGame();
                    Log("ESC pause close at frame " + frame);
                }
                else
                {
                    controller.TogglePauseGame();
                    Log("ESC pause open at frame " + frame);
                }
            }
            catch (Exception ex)
            {
                Log("ESC pause toggle failed: " + ex.Message);
            }
        }

        internal static void TryUnpauseFromEscape(PauseMenu pauseMenu)
        {
            if (!Enabled || pauseMenu == null)
                return;

            int frame = Time.frameCount;
            if (frame == lastEscapePauseFrame)
                return;

            if (!UnityEngine.Input.GetKeyDown(KeyCode.Escape))
                return;

            scrController controller = ADOBase.controller;
            if (controller == null || !controller.paused)
                return;

            lastEscapePauseFrame = frame;
            try
            {
                MethodInfo unpause = AccessTools.Method(typeof(PauseMenu), "Unpause");
                if (unpause != null)
                    unpause.Invoke(pauseMenu, null);
                else
                    controller.TogglePauseGame();
                Log("ESC pause menu close at frame " + frame);
            }
            catch (Exception ex)
            {
                Log("ESC pause menu close failed: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(scrController), "Update")]
    internal static class ControllerUpdateEscapePatch
    {
        private static void Prefix(scrController __instance)
        {
            Main.TryTogglePauseFromEscape(__instance);
        }
    }

    [HarmonyPatch(typeof(PauseMenu), "Update")]
    internal static class PauseMenuUpdateEscapePatch
    {
        private static void Prefix(PauseMenu __instance)
        {
            Main.TryUnpauseFromEscape(__instance);
        }
    }

    [HarmonyPatch(typeof(RDInputType_Keyboard), "Main")]
    internal static class KeyboardMainPatch
    {
        private static bool Prefix(object __instance, ButtonState state, ref int __result)
        {
            return MainPrefix(__instance, state, ref __result);
        }

        internal static bool MainPrefix(object inputInstance, ButtonState state, ref int result)
        {
            if (!Main.Enabled || !RawKeyboardRouter.IsRunning)
                return true;

            int playerId = Main.PlayerForInputInstance(inputInstance);
            if (playerId < 0)
                return true;

            if (state == ButtonState.WentDown)
            {
                result = RawKeyboardRouter.GetFramePressCount(playerId);
                Main.UpdateRouteDebug(inputInstance, state, playerId, result);
                return false;
            }

            if (state == ButtonState.IsDown)
            {
                result = RawKeyboardRouter.GetHeldCount(playerId);
                Main.UpdateRouteDebug(inputInstance, state, playerId, result);
                return false;
            }

            result = 0;
            Main.UpdateRouteDebug(inputInstance, state, playerId, result);
            return false;
        }
    }

    [HarmonyPatch(typeof(RDInputType_AsyncKeyboard), "Main")]
    internal static class AsyncKeyboardMainPatch
    {
        private static bool Prefix(object __instance, ButtonState state, ref int __result)
        {
            return KeyboardMainPatch.MainPrefix(__instance, state, ref __result);
        }
    }

    [HarmonyPatch(typeof(RDInputType_Keyboard), "Cancel")]
    internal static class KeyboardCancelPatch
    {
        private static bool Prefix(object __instance, ButtonState state, ref bool __result)
        {
            return EscapePrefix(__instance, state, ref __result);
        }

        internal static bool EscapePrefix(object inputInstance, ButtonState state, ref bool result)
        {
            if (!Main.Enabled || Main.PlayerForInputInstance(inputInstance) < 0)
                return true;

            if (state == ButtonState.WentDown)
                result = UnityEngine.Input.GetKeyDown(KeyCode.Escape);
            else if (state == ButtonState.IsDown)
                result = UnityEngine.Input.GetKey(KeyCode.Escape);
            else if (state == ButtonState.WentUp)
                result = UnityEngine.Input.GetKeyUp(KeyCode.Escape);
            else
                result = !UnityEngine.Input.GetKey(KeyCode.Escape);

            return false;
        }
    }

    [HarmonyPatch(typeof(RDInputType_Keyboard), "Back")]
    internal static class KeyboardBackPatch
    {
        private static bool Prefix(object __instance, ButtonState state, ref bool __result)
        {
            return KeyboardCancelPatch.EscapePrefix(__instance, state, ref __result);
        }
    }

    [HarmonyPatch(typeof(RDInputType_Keyboard), "Quit")]
    internal static class KeyboardQuitPatch
    {
        private static bool Prefix(object __instance, ButtonState state, ref bool __result)
        {
            return KeyboardCancelPatch.EscapePrefix(__instance, state, ref __result);
        }
    }
}
