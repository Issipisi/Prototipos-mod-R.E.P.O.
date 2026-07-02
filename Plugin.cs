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
            if (scene.name == "LobbyJoin" || scene.name == "Reload") Monitor?.OnExitRun();
        }

        public void SetActiveProfile(LifeSyncClient.PhysicalProfile p) => _activeProfile = p;
        public LifeSyncClient.PhysicalProfile GetActiveProfile() => _activeProfile;

        // ── LOGIN: Menú principal ─────────────────────────────────────
        [HarmonyPatch(typeof(MenuPageMain), "Start")]
        public static class MenuPageMainStartPatch
        {
            static void Postfix()
            {
                // Resetear upgrades siempre al llegar al menú principal.
                // Esto cubre tanto "salir de partida" como "nueva partida".
                Monitor?.ResetUpgrades();

                if (SessionManager.IsActive) { return; }
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
                if (!SemiFunc.RunIsShop()) { return; }
                var profile = Instance.GetActiveProfile();
                if (profile == null || !SessionManager.IsActive) return;
                profile.CanjesUsados = 0;
                TokenRefresher.TryRefresh();
                ShopCanjePanel.EnsureInstance(profile);
            }
        }

        // ── UPGRADES: ChangeLevel Prefix ──────────────────────────────
        // ChangeLevel() se llama con levelCurrent==levelLobby cuando el
        // jugador sale del Lobby hacia el nivel real. En ese momento:
        //   - RunIsShop() = false → SetPlayerHealth() no está bloqueado
        //   - PlayerController del Lobby todavía existe (podría ser null)
        //   - StatsManager persiste entre escenas (DontDestroyOnLoad)
        //
        // Escribimos directamente en StatsManager aquí para que cuando
        // el nivel real cargue y LateStart()/Fetch() corran, lean los
        // valores de upgrades ya persistidos.
        //
        // Usamos Prefix para actuar ANTES de que SetRunLevel() cambie
        // levelCurrent y antes de que RestartScene() destruya la escena.
        [HarmonyPatch(typeof(RunManager), "ChangeLevel")]
        public static class ChangeLevelPatch
        {
            static void Prefix(bool _completedLevel, bool _levelFailed)
            {
                if (!SessionManager.IsActive) return;
                if (!SessionManager.TieneUpgradesPendientes) return;

                // Solo actuar cuando salimos del Lobby hacia el nivel real.
                // El flujo confirmado: levelCurrent==levelLobby &&
                // !_levelFailed → SetRunLevel() → nivel real.
                if (RunManager.instance == null) return;
                if (!SemiFunc.RunIsLobby()) return;
                if (_levelFailed) return;

                Monitor?.AplicarUpgradesSync();
            }
        }
    }

    // ── SCENE MONITOR ─────────────────────────────────────────────────
    public class SceneMonitor : MonoBehaviour
    {
        private float _timer = 0f;

        // Upgrades ya aplicados en Lobbies anteriores de esta run.
        // Solo el delta (UpgradesX - _aplicadosX) se envía a PunManager.
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

        public void OnExitRun()
        {
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
            // Calcular delta: solo lo canjeado en esta tienda.
            int deltaStamina = SessionManager.UpgradesStamina - _aplicadosStamina;
            int deltaGrip = SessionManager.UpgradesGrip - _aplicadosGrip;
            int deltaHealth = SessionManager.UpgradesHealth - _aplicadosHealth;
            int deltaSpeed = SessionManager.UpgradesSpeed - _aplicadosSpeed;

            if (deltaStamina == 0 && deltaGrip == 0 && deltaHealth == 0 && deltaSpeed == 0)
            {
                VitaSyncPlugin.Log.LogInfo("[LSG] Sin upgrades nuevos que aplicar.");
                return;
            }

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
                VitaSyncPlugin.Log.LogWarning("[LSG] steamID no disponible. Abortando.");
                return;
            }
            if (PunManager.instance == null || StatsManager.instance == null)
            {
                VitaSyncPlugin.Log.LogWarning("[LSG] PunManager o StatsManager nulos. Abortando.");
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
                VitaSyncPlugin.Log.LogInfo(
                    "[LSG] Health persistido=" + nuevoMax +
                    " RunIsShop=" + SemiFunc.RunIsShop());
            }

            if (deltaSpeed > 0)
                PunManager.instance.UpgradePlayerSprintSpeed(sid, deltaSpeed);

            // Actualizar lo ya aplicado.
            _aplicadosStamina = SessionManager.UpgradesStamina;
            _aplicadosGrip = SessionManager.UpgradesGrip;
            _aplicadosHealth = SessionManager.UpgradesHealth;
            _aplicadosSpeed = SessionManager.UpgradesSpeed;

            VitaSyncPlugin.Log.LogInfo("[LSG] Upgrades aplicados. sid=" + sid);
        }
    }
}