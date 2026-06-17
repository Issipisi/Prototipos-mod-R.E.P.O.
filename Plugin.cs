using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VitaSync
{
    [BepInPlugin("com.diinf.vitasync", "VitaSync", "0.4.0")]
    public class VitaSyncPlugin : BaseUnityPlugin
    {
        public static VitaSyncPlugin Instance { get; private set; }
        public static BepInEx.Logging.ManualLogSource Log => Instance.Logger;

        private LifeSyncClient.PhysicalProfile _activeProfile;
        private GameObject _updaterGO;

        // URLs Oficiales del Framework Universitario
        public const string AUTH_URL = "https://lsg.diinf.usach.cl/lsg-auth/login";
        public const string AUTH_WHOAMI = "https://lsg.diinf.usach.cl/lsg-auth/whoami";
        public const string CORE_URL = "https://lsg.diinf.usach.cl/lsg-core-api";

        private void Awake()
        {
            Instance = this;

            Harmony harmony = new Harmony("com.diinf.vitasync");
            harmony.PatchAll();

            SceneManager.sceneLoaded += OnSceneLoaded;

            _updaterGO = new GameObject("VitaSync_SceneMonitor");
            _updaterGO.AddComponent<SceneMonitor>();
            DontDestroyOnLoad(_updaterGO);

            Log.LogInfo("VitaSync v0.4.0 (Infraestructura de Red Local) cargado.");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // 1. Inicializar panel si entramos al menú principal o recarga
            if ((scene.name == "Main" || scene.name == "Reload") && _activeProfile == null)
            {
                LoginHUDPanel.Initialize();
            }

            // 2. DETECCIÓN DE NUEVO NIVEL: Si cambia a un nivel de juego/tienda, reseteamos los canjes por ronda
            if (_activeProfile != null && scene.name.Contains("Level"))
            {
                _activeProfile.CanjesUsados = 0;
                Log.LogInfo($"[VitaSync] Cambio de nivel detectado ({scene.name}). Canjes restablecidos a 0/2.");
            }
        }

        public void SetActiveProfile(LifeSyncClient.PhysicalProfile profile) => _activeProfile = profile;
        public LifeSyncClient.PhysicalProfile GetActiveProfile() => _activeProfile;

        [HarmonyPatch(typeof(ShopManager), "ShopInitialize")]
        public static class ShopInitializePatch
        {
            static void Postfix()
            {
                var profile = Instance.GetActiveProfile();
                if (profile == null) return;
                ShopCanjePanel.EnsureInstance(profile);
            }
        }
    }

    public class SceneMonitor : MonoBehaviour
    {
        private float _checkTimer = 0f;
        private void Update()
        {
            _checkTimer += Time.deltaTime;
            if (_checkTimer >= 1.5f)
            {
                _checkTimer = 0f;
                ShopManager shopInst = FindObjectOfType<ShopManager>();
                if (shopInst == null || !shopInst.gameObject.activeInHierarchy)
                {
                    ShopCanjePanel.DestroyInstance();
                }
            }
        }
    }
}