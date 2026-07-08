using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

namespace VitaSync
{
    [BepInPlugin("com.diinf.vitasync", "VitaSync", "0.9.0")]
    public class VitaSyncPlugin : BaseUnityPlugin
    {
        public static VitaSyncPlugin Instance { get; private set; }
        public static BepInEx.Logging.ManualLogSource Log => Instance.Logger;
        public static SceneMonitor Monitor { get; private set; }

        private LifeSyncClient.PhysicalProfile _activeProfile;

        public const string AUTH_URL = "https://lsg.diinf.usach.cl/lsg-auth/login";
        public const string AUTH_WHOAMI = "https://lsg.diinf.usach.cl/lsg-auth/whoami";
        public const string AUTH_REFRESH = "https://lsg.diinf.usach.cl/lsg-auth/token/refresh";
        public const string CORE_URL = "https://lsg.diinf.usach.cl/lsg-core-api";

        private void Awake()
        {
            Instance = this;
            var harmony = new Harmony("com.diinf.vitasync");
            harmony.PatchAll();
            SceneManager.sceneLoaded += OnSceneLoaded;
            GameObject go = new GameObject("VitaSync_Monitor");
            Monitor = go.AddComponent<SceneMonitor>();
            DontDestroyOnLoad(go);
            Log.LogInfo("VitaSync v0.9.0 cargado.");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "Main" && mode == LoadSceneMode.Additive) return;
            if (!scene.name.Contains("Shop")) ShopCanjePanel.DestroyInstance();
            if (scene.name == "LobbyJoin" || scene.name == "Reload")
            {
                SessionLogger.EndSession();
                Monitor?.ResetUpgrades();
            }
        }

        private void OnApplicationQuit()
        {
            SessionLogger.EndSession();
        }

        public void SetActiveProfile(LifeSyncClient.PhysicalProfile p) => _activeProfile = p;
        public LifeSyncClient.PhysicalProfile GetActiveProfile() => _activeProfile;

        // ── LOGIN: Menú principal ─────────────────────────────────────
        [HarmonyPatch(typeof(MenuPageMain), "Start")]
        public static class MenuPageMainStartPatch
        {
            static void Postfix()
            {
                Monitor?.ResetUpgrades();
                if (SessionManager.IsActive) return;
                Log.LogInfo("[Login] MenuPageMain: desplegando HUD.");
                LoginHUDPanel.Initialize();
            }
        }

        // ── LOGIN: Lobby multijugador ─────────────────────────────────
        [HarmonyPatch(typeof(MenuPageLobby), "Start")]
        public static class MenuPageLobbyStartPatch
        {
            static void Postfix()
            {
                if (SessionManager.IsActive) return;
                Log.LogInfo("[Login] MenuPageLobby: desplegando HUD.");
                LoginHUDPanel.Initialize();
            }
        }

        // ── TIENDA ────────────────────────────────────────────────────
        [HarmonyPatch(typeof(ShopManager), "ShopInitialize")]
        public static class ShopInitializePatch
        {
            static void Postfix()
            {
                if (!SemiFunc.RunIsShop()) return;
                var profile = Instance.GetActiveProfile();
                if (profile == null || !SessionManager.IsActive) return;
                profile.CanjesUsados = 0;
                SessionLogger.LogShopOpen(profile.Puntos);
                TokenRefresher.TryRefresh();
                ShopCanjePanel.EnsureInstance(profile);
            }
        }

        // ── MUERTE DEL EQUIPO ─────────────────────────────────────────
        [HarmonyPatch(typeof(RunManager), "AllPlayersDeadSet")]
        public static class AllPlayersDeadSetPatch
        {
            static void Postfix(bool _set)
            {
                if (!SessionManager.IsActive) return;
                if (!_set) return;
                int completados = RunManager.instance?.levelsCompleted ?? 0;
                SessionLogger.LogTeamDead(completados);
            }
        }

        // ── UPGRADES y LOGGER: ChangeLevel ────────────────────────────
        // Prefix: actúa ANTES del cambio de nivel.
        //   - Registra fin de nivel real saliente.
        //   - Registra cierre de tienda si salimos de una.
        //   - Aplica upgrades LSG al salir del Lobby hacia nivel real.
        // Postfix: actúa DESPUÉS del cambio de nivel.
        //   - Inicia sesión de logger si es el primer nivel de la run.
        //   - Registra inicio del nuevo nivel real.
        [HarmonyPatch(typeof(RunManager), "ChangeLevel")]
        public static class ChangeLevelPatch
        {
            static void Prefix(bool _completedLevel, bool _levelFailed)
            {
                if (!SessionManager.IsActive) return;

                if (SemiFunc.RunIsLevel())
                {
                    string lvl = RunManager.instance?.levelCurrent?.name ?? "unknown";
                    SessionLogger.LogLevelEnd(lvl);
                }

                if (SemiFunc.RunIsShop())
                    SessionLogger.LogShopClose(
                        Instance.GetActiveProfile()?.CanjesUsados ?? 0);

                if (!SessionManager.TieneUpgradesPendientes) return;
                if (RunManager.instance == null) return;
                if (!SemiFunc.RunIsLobby()) return;
                if (_levelFailed) return;

                Monitor?.AplicarUpgradesSync();
            }

            static void Postfix()
            {
                if (!SessionManager.IsActive) return;
                string lvl = RunManager.instance?.levelCurrent?.name ?? "";
                if (lvl.Contains("Lobby") || lvl.Contains("Shop")) return;

                int completados = RunManager.instance?.levelsCompleted ?? 0;
                if (completados == 0)
                    SessionLogger.StartSession();

                SessionLogger.LogLevelStart(lvl);
            }
        }
    }

    // ── SCENE MONITOR ─────────────────────────────────────────────────
    public class SceneMonitor : MonoBehaviour
    {
        private float _timer = 0f;

        private int _aplicadosStamina = 0;
        private int _aplicadosGrip = 0;
        private int _aplicadosHealth = 0;
        private int _aplicadosSpeed = 0;

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= 2f)
            {
                _timer = 0f;
                if (!SemiFunc.RunIsShop()) ShopCanjePanel.DestroyInstance();
            }
        }

        public void ResetUpgrades()
        {
            SessionManager.ClearUpgrades();
            _aplicadosStamina = 0;
            _aplicadosGrip = 0;
            _aplicadosHealth = 0;
            _aplicadosSpeed = 0;
        }

        public void AplicarUpgradesSync()
        {
            int deltaStamina = SessionManager.UpgradesStamina - _aplicadosStamina;
            int deltaGrip = SessionManager.UpgradesGrip - _aplicadosGrip;
            int deltaHealth = SessionManager.UpgradesHealth - _aplicadosHealth;
            int deltaSpeed = SessionManager.UpgradesSpeed - _aplicadosSpeed;

            if (deltaStamina == 0 && deltaGrip == 0 &&
                deltaHealth == 0 && deltaSpeed == 0) return;

            string sid = null;
            var ctrl = PlayerController.instance;
            if (ctrl != null && ctrl.playerAvatarScript != null)
                sid = SemiFunc.PlayerGetSteamID(ctrl.playerAvatarScript);
            if (string.IsNullOrEmpty(sid))
            {
                var local = SemiFunc.PlayerAvatarLocal();
                if (local != null) sid = SemiFunc.PlayerGetSteamID(local);
            }
            if (string.IsNullOrEmpty(sid))
            {
                VitaSyncPlugin.Log.LogWarning("[LSG] steamID no disponible.");
                return;
            }
            if (PunManager.instance == null || StatsManager.instance == null)
            {
                VitaSyncPlugin.Log.LogWarning("[LSG] PunManager o StatsManager nulos.");
                return;
            }

            if (deltaStamina > 0)
                PunManager.instance.UpgradePlayerEnergy(sid, deltaStamina);

            if (deltaGrip > 0)
                PunManager.instance.UpgradePlayerGrabStrength(sid, deltaGrip);

            if (deltaHealth > 0)
            {
                PunManager.instance.UpgradePlayerHealth(sid, deltaHealth);
                int nativos;
                StatsManager.instance.playerUpgradeHealth.TryGetValue(sid, out nativos);
                int nuevoMax = 100 + nativos * 20;
                StatsManager.instance.SetPlayerHealth(sid, nuevoMax, false);
                VitaSyncPlugin.Log.LogInfo("[LSG] Health persistido=" + nuevoMax);
            }

            if (deltaSpeed > 0)
                PunManager.instance.UpgradePlayerSprintSpeed(sid, deltaSpeed);

            _aplicadosStamina = SessionManager.UpgradesStamina;
            _aplicadosGrip = SessionManager.UpgradesGrip;
            _aplicadosHealth = SessionManager.UpgradesHealth;
            _aplicadosSpeed = SessionManager.UpgradesSpeed;

            VitaSyncPlugin.Log.LogInfo("[LSG] Upgrades aplicados. sid=" + sid);
            SessionLogger.LogUpgradesApplied(
                _aplicadosHealth, _aplicadosStamina,
                _aplicadosGrip, _aplicadosSpeed);
        }
    }
}