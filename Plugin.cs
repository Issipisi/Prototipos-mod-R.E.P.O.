using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace VitaSync
{
    [BepInPlugin("cl.usach.vitasync", "VitaSync", "0.2.0")]
    public class VitaSyncPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static VitaSyncPlugin Instance;
        private static LifeSyncClient _client;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Log.LogInfo("VitaSync 0.2.0 inicializado en modo Prototipo P2.");

            _client = gameObject.AddComponent<LifeSyncClient>();
            _client.Connect();

            var harmony = new Harmony(Info.Metadata.GUID);
            harmony.PatchAll();
        }

        public static LifeSyncClient.PhysicalProfile GetProfile() => _client?.Profile;
        public static LifeSyncClient GetClient() => _client;

        [HarmonyPatch(typeof(ShopManager), "ShopInitialize")]
        public static class ShopInitializePatch
        {
            static void Postfix()
            {
                var profile = GetProfile();
                if (profile == null || !GetClient().IsAuthenticated)
                {
                    Log.LogInfo("[P3-Shop] Esperando perfil activo de LifeSync-Games...");
                    return;
                }

                // Reinicia las mejoras simuladas al cargar el nivel
                profile.CanjesUsados = 0;

                Log.LogInfo("[P3-Shop] Inicializando hook gráfico de la tienda. Registrados: " + profile.Puntos + " pts.");

                // Invoca la instanciación e inyección del canvas gráfico
                ShopCanjePanel.EnsureInstance(profile);
            }
        }
    }
}