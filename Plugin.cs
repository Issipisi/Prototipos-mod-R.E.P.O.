using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VitaSync
{
    [BepInPlugin("com.diinf.vitasync", "VitaSync", "0.6.0")]
    public class VitaSyncPlugin : BaseUnityPlugin
    {
        public static VitaSyncPlugin Instance { get; private set; }
        public static BepInEx.Logging.ManualLogSource Log => Instance.Logger;

        private LifeSyncClient.PhysicalProfile _activeProfile;
        private GameObject _monitorGO;

        // URLs del framework LS-G
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

            _monitorGO = new GameObject("VitaSync_Monitor");
            _monitorGO.AddComponent<SceneMonitor>();
            DontDestroyOnLoad(_monitorGO);

            Log.LogInfo("VitaSync v0.6.0 cargado.");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Log.LogInfo("[VitaSync] Escena cargada: " + scene.name
                        + " (modo: " + mode + ")");

            // La escena Main aditiva es ruido arquitectónico de R.E.P.O.: ignorar
            if (scene.name == "Main" && mode == LoadSceneMode.Additive)
            {
                Log.LogInfo("[VitaSync] Main aditiva ignorada.");
                return;
            }

            bool esTienda = scene.name.StartsWith("Level - Shop");
            bool esNivel = scene.name.StartsWith("Level") && !esTienda;

            // ── NIVEL DE EXPLORACIÓN ──────────────────────────────────
            if (esNivel)
            {
                ShopCanjePanel.DestroyInstance();
                if (_activeProfile != null)
                {
                    _activeProfile.CanjesUsados = 0;
                    Log.LogInfo("[VitaSync] Nivel exploración (" +
                                scene.name + "). Canjes reseteados a 0/2.");
                }
                return;
            }

            // ── TIENDA ────────────────────────────────────────────────
            if (esTienda)
            {
                Log.LogInfo("[VitaSync] Tienda detectada: " + scene.name);
                if (_activeProfile != null)
                {
                    _activeProfile.CanjesUsados = 0;
                    Log.LogInfo("[VitaSync] Canjes restablecidos a 0/2.");
                }
                // El panel lo construye ShopInitializePatch vía SemiFunc.RunIsShop().
                // Antes de construirlo, intentamos refrescar el token para sesiones largas.
                if (SessionManager.IsActive)
                {
                    TokenRefresher.TryRefresh();
                }
                return;
            }

            // Escena desconocida: limpiar panel por seguridad
            ShopCanjePanel.DestroyInstance();
        }

        public void SetActiveProfile(LifeSyncClient.PhysicalProfile p) => _activeProfile = p;
        public LifeSyncClient.PhysicalProfile GetActiveProfile() => _activeProfile;

        // ── HARMONY PATCH: MenuPageMain.Start() ──────────────────────
        /// <summary>
        /// Punto de inyección del HUD de login.
        /// Se ejecuta cuando el menú principal ya está completamente
        /// instanciado y estable, evitando el reset de InputFields que
        /// ocurría al disparar el login durante la carga volátil de la
        /// escena Main.
        /// </summary>
        [HarmonyPatch(typeof(MenuPageMain), "Start")]
        public static class MenuPageMainStartPatch
        {
            static void Postfix()
            {
                if (SessionManager.IsActive)
                {
                    VitaSyncPlugin.Log.LogInfo(
                        "[Login] MenuPageMain listo. Sesión activa, login omitido.");
                    return;
                }

                VitaSyncPlugin.Log.LogInfo(
                    "[Login] MenuPageMain listo. Desplegando HUD de login.");
                LoginHUDPanel.Initialize();
            }
        }

        // ── HARMONY PATCH: ShopManager.ShopInitialize() ──────────────
        [HarmonyPatch(typeof(ShopManager), "ShopInitialize")]
        public static class ShopInitializePatch
        {
            static void Postfix()
            {
                if (!SemiFunc.RunIsShop())
                {
                    VitaSyncPlugin.Log.LogInfo(
                        "[Shop] ShopInitialize fuera de contexto de tienda. Ignorado.");
                    return;
                }

                var profile = Instance.GetActiveProfile();
                if (profile == null)
                {
                    VitaSyncPlugin.Log.LogInfo(
                        "[Shop] Sin perfil activo. Canje omitido.");
                    return;
                }

                if (!SessionManager.IsActive)
                {
                    VitaSyncPlugin.Log.LogInfo(
                        "[Shop] Sin sesión LSG. Canje omitido.");
                    return;
                }

                VitaSyncPlugin.Log.LogInfo(
                    "[Shop] RunIsShop=true. Inicializando panel de canje LSG.");
                ShopCanjePanel.EnsureInstance(profile);
            }
        }
    }

    // ── MONITOR DE ESCENA ─────────────────────────────────────────────
    /// <summary>
    /// Destruye el panel de canje si el juego salió de la tienda
    /// sin disparar OnSceneLoaded (ej: cierre abrupto, reload rápido).
    /// </summary>
    public class SceneMonitor : MonoBehaviour
    {
        private float _timer = 0f;

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < 2f) return;
            _timer = 0f;

            if (!SemiFunc.RunIsShop())
                ShopCanjePanel.DestroyInstance();
        }
    }
}