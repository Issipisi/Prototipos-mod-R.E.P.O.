using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VitaSync
{
    [BepInPlugin("com.diinf.vitasync", "VitaSync", "0.5.0")]
    public class VitaSyncPlugin : BaseUnityPlugin
    {
        public static VitaSyncPlugin Instance { get; private set; }
        public static BepInEx.Logging.ManualLogSource Log => Instance.Logger;

        private LifeSyncClient.PhysicalProfile _activeProfile;
        private GameObject _monitorGO;

        // URLs del framework LS-G
        public const string AUTH_URL = "https://lsg.diinf.usach.cl/lsg-auth/login";
        public const string AUTH_WHOAMI = "https://lsg.diinf.usach.cl/lsg-auth/whoami";
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

            Log.LogInfo("VitaSync v0.5.0 cargado.");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Log.LogInfo("[VitaSync] Escena cargada: " + scene.name
                        + " (modo: " + mode + ")");

            // Ignorar la escena base Main cuando se carga de forma aditiva
            // junto a un nivel, ya que no representa un cambio de estado real
            if (scene.name == "Main" && mode == LoadSceneMode.Additive)
            {
                Log.LogInfo("[VitaSync] Main aditiva ignorada.");
                return;
            }

            bool esMenu = scene.name == "Main" || scene.name == "Reload";
            bool esTienda = scene.name.StartsWith("Level - Shop");
            bool esNivel = scene.name.StartsWith("Level") && !esTienda;

            // ── MENÚ PRINCIPAL ────────────────────────────────────────
            if (esMenu)
            {
                ShopCanjePanel.DestroyInstance();

                if (!SessionManager.IsActive)
                {
                    Log.LogInfo("[VitaSync] Menú detectado. Mostrando login.");
                    LoginHUDPanel.Initialize();
                }
                else
                {
                    Log.LogInfo("[VitaSync] Menú detectado. " +
                                "Sesión activa, login omitido.");
                }
                return;
            }

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
                // El panel lo construye ShopInitializePatch via SemiFunc.RunIsShop()
                return;
            }

            // Escena desconocida: limpiar por seguridad
            ShopCanjePanel.DestroyInstance();
        }

        public void SetActiveProfile(LifeSyncClient.PhysicalProfile p)
        {
            _activeProfile = p;
        }

        public LifeSyncClient.PhysicalProfile GetActiveProfile()
        {
            return _activeProfile;
        }

        // ── HARMONY PATCH: ShopManager.ShopInitialize() ──────────────
        [HarmonyPatch(typeof(ShopManager), "ShopInitialize")]
        public static class ShopInitializePatch
        {
            static void Postfix()
            {
                // Usar la función nativa del juego en lugar de GetActiveScene()
                // SemiFunc.RunIsShop() es exactamente lo que ShopManager.Update() usa internamente
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
                        "[Shop] Sin sesión LS-G. Canje omitido.");
                    return;
                }

                VitaSyncPlugin.Log.LogInfo(
                    "[Shop] RunIsShop=true. Inicializando panel de canje LS-G.");
                ShopCanjePanel.EnsureInstance(profile);
            }
        }
    }

    // ── MONITOR DE ESCENA ─────────────────────────────────────────────
    /// <summary>
    /// Verifica periódicamente si ShopManager sigue activo.
    /// Destruye el panel de canje si el juego salió de la tienda
    /// sin disparar OnSceneLoaded (ej: crash parcial, reload rápido).
    /// </summary>
    public class SceneMonitor : MonoBehaviour
    {
        private float _timer = 0f;

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < 2f) return;
            _timer = 0f;

            // Si el juego ya no está en modo tienda, destruir el panel
            if (!SemiFunc.RunIsShop())
            {
                ShopCanjePanel.DestroyInstance();
            }
        }
    }
}