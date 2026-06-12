using System;
using System.Collections;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Networking;

namespace VitaSync
{
    /// <summary>
    /// Cliente REST del módulo cloud de LifeSync-Games.
    /// Gestiona autenticación JWT y consulta de saldo físico para construir el perfil.
    /// </summary>
    public class LifeSyncClient : MonoBehaviour
    {
        private const string AUTH_LOGIN = "https://lsg.diinf.usach.cl/lsg-auth/login";
        private const string AUTH_WHOAMI = "https://lsg.diinf.usach.cl/lsg-auth/whoami";
        private const string CORE_BASE = "https://lsg.diinf.usach.cl/lsg-core-api";
        private const string FISICO_BASE_ID = "2";

        private const string USERNAME = "isidora.rojas.a@usach.cl";
        private const string PASSWORD = "irojas2026";

        private string _token = null;
        private int _playerId = -1;

        public bool IsAuthenticated => !string.IsNullOrEmpty(_token);
        public PhysicalProfile Profile { get; private set; }

        private static ManualLogSource Log => VitaSyncPlugin.Log;

        public void Connect()
        {
            StartCoroutine(AuthAndFetch());
        }

        private IEnumerator AuthAndFetch()
        {
            yield return StartCoroutine(Login());
            if (!IsAuthenticated)
            {
                Log.LogError("[LSG] Autenticación fallida. Modo pasivo.");
                Profile = new PhysicalProfile();
                yield break;
            }

            yield return StartCoroutine(WhoAmI());
            if (_playerId < 0)
            {
                Log.LogError("[LSG] No se obtuvo player_id. Modo pasivo.");
                Profile = new PhysicalProfile();
                yield break;
            }

            Log.LogInfo("[LSG] Player ID obtenido con éxito: " + _playerId);
            yield return StartCoroutine(FetchBalance());
        }

        private IEnumerator Login()
        {
            Log.LogInfo("[LSG] Iniciando autenticación...");
            WWWForm form = new WWWForm();
            form.AddField("username", USERNAME);
            form.AddField("password", PASSWORD);
            form.AddField("grant_type", "");
            form.AddField("scope", "");
            form.AddField("client_id", "");
            form.AddField("client_secret", "");

            using (UnityWebRequest req = UnityWebRequest.Post(AUTH_LOGIN, form))
            {
                req.SetRequestHeader("Accept", "application/json");
                req.timeout = 10;
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    _token = ExtractString(req.downloadHandler.text, "access_token");
                    if (!string.IsNullOrEmpty(_token))
                        Log.LogInfo("[LSG] Token JWT obtenido de forma correcta.");
                    else
                        Log.LogError("[LSG] Token no encontrado en la respuesta.");
                }
                else
                {
                    Log.LogError("[LSG] Login HTTP " + req.responseCode + ": " + req.error);
                }
            }
        }

        private IEnumerator WhoAmI()
        {
            using (UnityWebRequest req = UnityWebRequest.Get(AUTH_WHOAMI))
            {
                req.SetRequestHeader("Authorization", "Bearer " + _token);
                req.SetRequestHeader("Accept", "application/json");
                req.timeout = 10;
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    string idStr = ExtractString(req.downloadHandler.text, "id_players");
                    if (!int.TryParse(idStr, out _playerId))
                    {
                        Log.LogError("[LSG] No se pudo parsear id_players.");
                    }
                }
                else
                {
                    Log.LogError("[LSG] WhoAmI HTTP " + req.responseCode + ": " + req.error);
                }
            }
        }

        private IEnumerator FetchBalance()
        {
            string url = CORE_BASE + "/players/" + _playerId + "/points/balance";
            Log.LogInfo("[LSG] Consultando balance: " + url);

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("Authorization", "Bearer " + _token);
                req.SetRequestHeader("Accept", "application/json");
                req.timeout = 10;
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    int pts = ParseDimensionBalance(req.downloadHandler.text, FISICO_BASE_ID);
                    Log.LogInfo("[LSG] Puntos físicos recuperados: " + pts);
                    Profile = PhysicalProfile.FromPoints(pts);
                    Log.LogInfo("[LSG] Objeto PhysicalProfile construido: " + Profile.ToString());
                }
                else
                {
                    Log.LogWarning("[LSG] Balance HTTP " + req.responseCode + ". Activando modo pasivo.");
                    Profile = new PhysicalProfile();
                }
            }
        }

        public void DeductPoints(int amount, Action onSuccess = null)
        {
            // Placeholder funcional para mantener compatibilidad en P2 sin ejecutar red
            if (Profile != null) Profile.Puntos -= amount;
            if (onSuccess != null) onSuccess();
        }

        internal static string ExtractString(string json, string key)
        {
            string k = "\"" + key + "\"";
            int ki = json.IndexOf(k, StringComparison.Ordinal);
            if (ki < 0) return null;
            int ci = json.IndexOf(':', ki + k.Length);
            if (ci < 0) return null;
            int vs = ci + 1;
            while (vs < json.Length && char.IsWhiteSpace(json[vs])) vs++;
            if (vs >= json.Length) return null;
            if (json[vs] == '"')
            {
                int ve = json.IndexOf('"', vs + 1);
                return ve < 0 ? null : json.Substring(vs + 1, ve - vs - 1);
            }
            int end = vs;
            while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != ']') end++;
            return json.Substring(vs, end - vs).Trim();
        }

        private static int ParseDimensionBalance(string json, string dimId)
        {
            int idx = 0;
            while (true)
            {
                int di = json.IndexOf("\"id_point_dimension\"", idx, StringComparison.Ordinal);
                if (di < 0) break;
                int oe = json.IndexOf('}', di);
                if (oe < 0) break;
                string slice = json.Substring(di, oe - di + 1);
                string dimVal = ExtractString(slice, "id_point_dimension");
                if (dimVal == dimId)
                {
                    string balStr = ExtractString(slice, "balance");
                    if (int.TryParse(balStr, out int b)) return b;
                }
                idx = oe + 1;
            }
            return 0;
        }

        public class PhysicalProfile
        {
            public int Puntos { get; set; }
            public int CostoStamina { get; private set; }
            public int CostoGrip { get; private set; }
            public int CostoHealth { get; private set; }
            public int CostoSpeed { get; private set; }
            public int CanjesUsados { get; set; }
            public int CanjesMax { get; private set; }

            public bool PuedeCanjeaMas => CanjesUsados < CanjesMax;

            public PhysicalProfile()
            {
                Puntos = 0;
                CostoStamina = 20;
                CostoGrip = 20;
                CostoHealth = 30;
                CostoSpeed = 30;
                CanjesUsados = 0;
                CanjesMax = 2;
            }

            public static PhysicalProfile FromPoints(int pts)
            {
                return new PhysicalProfile { Puntos = pts };
            }

            public bool PuedePagar(int costo)
            {
                return PuedeCanjeaMas && Puntos >= costo;
            }

            public override string ToString()
            {
                return "Puntos=" + Puntos + " | Canjes=" + CanjesUsados + "/" + CanjesMax;
            }
        }
    }
}