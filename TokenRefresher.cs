using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace VitaSync
{
    /// <summary>
    /// Renueva el JWT via POST /token/refresh al entrar a cada tienda.
    /// P7: eliminado GameObject.Find() — usa VitaSyncPlugin.Monitor directamente.
    /// GameObject.Find() no busca en objetos DontDestroyOnLoad en Unity 2022,
    /// por lo que siempre retornaba null y el refresco nunca se ejecutaba.
    /// </summary>
    public static class TokenRefresher
    {
        private static bool _refreshInProgress = false;

        /// <summary>
        /// Llamado desde SceneMonitor.StartTokenRefresh() con referencia directa.
        /// </summary>
        public static void TryRefreshDirect(MonoBehaviour runner)
        {
            if (_refreshInProgress)
            {
                VitaSyncPlugin.Log.LogInfo(
                    "[Refresh] Ya en progreso. Ignorado.");
                return;
            }
            if (!SessionManager.IsActive)
            {
                VitaSyncPlugin.Log.LogInfo(
                    "[Refresh] Sin sesion activa. Omitido.");
                return;
            }
            if (runner == null)
            {
                VitaSyncPlugin.Log.LogWarning(
                    "[Refresh] Runner nulo. Omitido.");
                return;
            }

            runner.StartCoroutine(RefreshCoroutine());
        }

        private static IEnumerator RefreshCoroutine()
        {
            _refreshInProgress = true;
            VitaSyncPlugin.Log.LogInfo("[Refresh] Iniciando refresco de token...");

            string jsonBody =
                "{\"token\":\"" + SessionManager.BearerToken + "\"}";

            using (UnityWebRequest req =
                new UnityWebRequest(VitaSyncPlugin.AUTH_REFRESH, "POST"))
            {
                byte[] raw = Encoding.UTF8.GetBytes(jsonBody);
                req.uploadHandler = new UploadHandlerRaw(raw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Authorization",
                    "Bearer " + SessionManager.BearerToken);
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Accept", "application/json");
                req.timeout = 10;

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    string newToken = LifeSyncClient.ExtractString(
                        req.downloadHandler.text, "access_token");

                    if (!string.IsNullOrEmpty(newToken))
                    {
                        SessionManager.UpdateToken(newToken);
                        VitaSyncPlugin.Log.LogInfo(
                            "[Refresh] Token actualizado correctamente.");
                    }
                    else
                    {
                        VitaSyncPlugin.Log.LogWarning(
                            "[Refresh] Respuesta 200 sin access_token. " +
                            "Token anterior conservado.");
                    }
                }
                else
                {
                    VitaSyncPlugin.Log.LogWarning(
                        "[Refresh] Error HTTP " + req.responseCode +
                        ": " + req.error + ". Token anterior conservado.");
                }
            }

            _refreshInProgress = false;
        }
    }
}