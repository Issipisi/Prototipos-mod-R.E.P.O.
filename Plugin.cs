using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

namespace VitaSync
{
    [BepInPlugin("com.diinf.vitasync", "VitaSync", "0.7.0")]
    public class VitaSyncPlugin : BaseUnityPlugin
    {
        public static VitaSyncPlugin Instance { get; private set; }
        public static BepInEx.Logging.ManualLogSource Log => Instance.Logger;

        // Referencia directa al SceneMonitor.
        // GameObject.Find() NO busca en DontDestroyOnLoad en Unity 2022.
        public static SceneMonitor Monitor { get; private set; }

        private LifeSyncClient.PhysicalProfile _activeProfile;

        public const string AUTH_URL = "https://lsg.diinf.usach.cl/lsg-auth/login";
        public const string AUTH_WHOAMI = "https://lsg.diinf.usach.cl/lsg-auth/whoami";
        public const string AUTH_REFRESH = "https://lsg.diinf.usach.cl/lsg-auth/token/refresh";
        public const string CORE_URL = "https://lsg.diinf.usach.cl/lsg-core-api";

        private void Awake()
        {
            Instance = this;

            // Patchear con PatchAll() pero capturando excepciones de patches
            // opcionales (ej: RoundDirector.SetupLevel que puede no existir).
            var harmony = new Harmony("com.diinf.vitasync");
            foreach (var type in System.Reflection.Assembly.GetExecutingAssembly().GetTypes())
            {
                try { harmony.CreateClassProcessor(type).Patch(); }
                catch (System.Exception ex)
                {
                    Log.LogWarning("[Harmony] Patch omitido para " +
                        type.Name + ": " + ex.Message);
                }
            }

            SceneManager.sceneLoaded += OnSceneLoaded;

            GameObject go = new GameObject("VitaSync_Monitor");
            Monitor = go.AddComponent<SceneMonitor>();
            DontDestroyOnLoad(go);

            Log.LogInfo("VitaSync v0.7.0 cargado.");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Log.LogInfo("[VitaSync] Escena: " + scene.name + " (" + mode + ")");

            if (scene.name == "Main" && mode == LoadSceneMode.Additive)
            {
                Log.LogInfo("[VitaSync] Main aditiva ignorada.");
                return;
            }

            // FIX: destruir el panel de canje INMEDIATAMENTE en cualquier
            // cambio de escena que no sea tienda. Esto cubre el caso donde
            // el panel persistia al pasar al nivel intermedio (Lobby) y
            // luego al nivel nuevo.
            // RunIsShop() puede tardar en actualizarse — usar el nombre de
            // escena como señal inmediata es mas fiable.
            bool esEscenaTienda = scene.name.Contains("Shop");
            if (!esEscenaTienda)
            {
                ShopCanjePanel.DestroyInstance();
                Log.LogInfo("[VitaSync] Panel de canje destruido (escena no-tienda).");
            }

            // Al salir completamente de la partida, resetear el estado del monitor.
            if (scene.name == "LobbyJoin" || scene.name == "Reload")
            {
                Monitor?.OnExitRun();
            }
        }

        public void SetActiveProfile(LifeSyncClient.PhysicalProfile p) => _activeProfile = p;
        public LifeSyncClient.PhysicalProfile GetActiveProfile() => _activeProfile;

        // ── LOGIN: Menu principal ─────────────────────────────────────
        [HarmonyPatch(typeof(MenuPageMain), "Start")]
        public static class MenuPageMainStartPatch
        {
            static void Postfix()
            {
                if (SessionManager.IsActive)
                {
                    VitaSyncPlugin.Log.LogInfo(
                        "[Login] MenuPageMain: sesion activa, login omitido.");
                    return;
                }
                VitaSyncPlugin.Log.LogInfo("[Login] MenuPageMain: desplegando HUD.");
                LoginHUDPanel.Initialize();
            }
        }

        // ── LOGIN: Lobby multijugador ─────────────────────────────────
        [HarmonyPatch(typeof(MenuPageLobby), "Start")]
        public static class MenuPageLobbyStartPatch
        {
            static void Postfix()
            {
                if (SessionManager.IsActive)
                {
                    VitaSyncPlugin.Log.LogInfo(
                        "[Login] MenuPageLobby: sesion activa, login omitido.");
                    return;
                }
                VitaSyncPlugin.Log.LogInfo("[Login] MenuPageLobby: desplegando HUD.");
                LoginHUDPanel.Initialize();
            }
        }

        // ── TIENDA: ShopManager.ShopInitialize() ─────────────────────
        [HarmonyPatch(typeof(ShopManager), "ShopInitialize")]
        public static class ShopInitializePatch
        {
            static void Postfix()
            {
                if (!SemiFunc.RunIsShop())
                {
                    VitaSyncPlugin.Log.LogInfo(
                        "[Shop] ShopInitialize: fuera de tienda, ignorado.");
                    return;
                }

                var profile = Instance.GetActiveProfile();
                if (profile == null)
                {
                    VitaSyncPlugin.Log.LogInfo("[Shop] Sin perfil activo. Omitido.");
                    return;
                }
                if (!SessionManager.IsActive)
                {
                    VitaSyncPlugin.Log.LogInfo("[Shop] Sin sesion LSG. Omitido.");
                    return;
                }

                profile.CanjesUsados = 0;
                VitaSyncPlugin.Log.LogInfo(
                    "[Shop] Canjes reseteados -> 0/" + profile.CanjesMax + ".");

                Monitor?.StartTokenRefresh();

                VitaSyncPlugin.Log.LogInfo("[Shop] Inicializando panel LSG.");
                ShopCanjePanel.EnsureInstance(profile);
            }
        }

        // ── UPGRADES EN NIVEL: RoundDirector ─────────────────────────
        // Este patch es OPCIONAL: RoundDirector puede tener el metodo con
        // otro nombre segun la version del juego. Si falla, el Awake()
        // captura la excepcion con un warning sin abortar el plugin.
        // La arquitectura de SceneMonitor con polling de SemiFunc.RunIsLevel()
        // cubre el caso donde este patch no se registre.
        [HarmonyPatch(typeof(RoundDirector), "SetupLevel")]
        public static class RoundDirectorSetupLevelPatch
        {
            static void Postfix()
            {
                if (!SessionManager.IsActive) return;
                if (!SessionManager.TieneUpgradesPendientes) return;

                VitaSyncPlugin.Log.LogInfo(
                    "[LSG] RoundDirector.SetupLevel() disparado. Encolando upgrades. " +
                    "STM=" + SessionManager.UpgradesStamina +
                    " GRP=" + SessionManager.UpgradesGrip +
                    " HP=" + SessionManager.UpgradesHealth +
                    " SPD=" + SessionManager.UpgradesSpeed);

                Monitor?.EnqueueUpgrades();
            }
        }
    }

    /// <summary>
    /// MonoBehaviour persistente (DontDestroyOnLoad).
    /// Responsabilidades:
    ///   1. Destruir ShopCanjePanel cuando RunIsShop() deja de ser true.
    ///   2. Aplicar upgrades LSG post-LateStart via coroutine con timeout.
    ///      El polling de SemiFunc.RunIsLevel() actua como fallback cuando
    ///      RoundDirectorSetupLevelPatch no se registra.
    ///   3. Ejecutar el refresco de token JWT sin GameObject.Find().
    /// </summary>
    public class SceneMonitor : MonoBehaviour
    {
        private float _shopTimer = 0f;
        private bool _upgradesPendientes = false;

        // Fallback: detectar inicio de nivel via polling de RunIsLevel().
        // Se activa solo si RoundDirectorSetupLevelPatch no pudo registrarse.
        private bool _eraLevel = false;

        // ── API publica ───────────────────────────────────────────────

        public void EnqueueUpgrades()
        {
            if (_upgradesPendientes)
            {
                VitaSyncPlugin.Log.LogInfo(
                    "[LSG] Upgrades ya encolados. Ignorando duplicado.");
                return;
            }
            _upgradesPendientes = true;
            StartCoroutine(AplicarUpgradesCoroutine());
        }

        public void StartTokenRefresh()
        {
            TokenRefresher.TryRefreshDirect(this);
        }

        public void OnExitRun()
        {
            _upgradesPendientes = false;
            _eraLevel = false;
            VitaSyncPlugin.Log.LogInfo("[SceneMonitor] Estado reseteado al salir de run.");
        }

        // ── Update ────────────────────────────────────────────────────

        private void Update()
        {
            // ── 1. Destruir panel si no estamos en tienda ─────────────
            // Este check con timer es el fallback; la destruccion inmediata
            // ocurre en OnSceneLoaded. Aqui cubrimos transiciones internas
            // que no disparan OnSceneLoaded (ej: GameDirector cambia estado).
            _shopTimer += Time.deltaTime;
            if (_shopTimer >= 1f) // 1s, antes eran 2s
            {
                _shopTimer = 0f;
                if (!SemiFunc.RunIsShop())
                    ShopCanjePanel.DestroyInstance();
            }

            // ── 2. Fallback: detectar inicio de nivel via polling ──────
            // Solo actua si hay upgrades pendientes y sesion activa.
            if (!SessionManager.IsActive) return;
            if (!SessionManager.TieneUpgradesPendientes) return;
            if (_upgradesPendientes) return; // ya hay coroutine en vuelo

            bool esLevelAhora = SemiFunc.RunIsLevel();

            // Flanco ascendente: pasamos de no-nivel a nivel activo.
            if (esLevelAhora && !_eraLevel)
            {
                VitaSyncPlugin.Log.LogInfo(
                    "[LSG] Fallback: RunIsLevel() true detectado. Encolando upgrades.");
                EnqueueUpgrades();
            }

            _eraLevel = esLevelAhora;
        }

        // ── Coroutine de upgrades ─────────────────────────────────────

        private IEnumerator AplicarUpgradesCoroutine()
        {
            VitaSyncPlugin.Log.LogInfo(
                "[LSG] Coroutine iniciada. Esperando PlayerController + PunManager...");

            float elapsed = 0f;
            const float TIMEOUT = 40f;

            while (elapsed < TIMEOUT)
            {
                var ctrl = PlayerController.instance;
                if (ctrl != null &&
                    ctrl.playerAvatarScript != null &&
                    PunManager.instance != null)
                {
                    string sid = SemiFunc.PlayerGetSteamID(ctrl.playerAvatarScript);
                    if (!string.IsNullOrEmpty(sid))
                    {
                        // Esperar 3 frames para que LateStart() termine
                        // su propio calculo de stats antes de reaplicar.
                        yield return null;
                        yield return null;
                        yield return null;

                        VitaSyncPlugin.Log.LogInfo(
                            "[LSG] Aplicando upgrades. sid=" + sid +
                            " STM=" + SessionManager.UpgradesStamina +
                            " GRP=" + SessionManager.UpgradesGrip +
                            " HP=" + SessionManager.UpgradesHealth +
                            " SPD=" + SessionManager.UpgradesSpeed);

                        if (SessionManager.UpgradesStamina > 0)
                            PunManager.instance.UpgradePlayerEnergy(
                                sid, SessionManager.UpgradesStamina);

                        if (SessionManager.UpgradesGrip > 0)
                            PunManager.instance.UpgradePlayerGrabStrength(
                                sid, SessionManager.UpgradesGrip);

                        if (SessionManager.UpgradesHealth > 0)
                            PunManager.instance.UpgradePlayerHealth(
                                sid, SessionManager.UpgradesHealth);

                        if (SessionManager.UpgradesSpeed > 0)
                            PunManager.instance.UpgradePlayerSprintSpeed(
                                sid, SessionManager.UpgradesSpeed);

                        VitaSyncPlugin.Log.LogInfo(
                            "[LSG] Upgrades aplicados correctamente.");
                        _upgradesPendientes = false;
                        yield break;
                    }
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            VitaSyncPlugin.Log.LogWarning(
                "[LSG] TIMEOUT: PlayerController no disponible tras " +
                TIMEOUT + "s. Upgrades no aplicados en este nivel.");
            _upgradesPendientes = false;
        }
    }
}