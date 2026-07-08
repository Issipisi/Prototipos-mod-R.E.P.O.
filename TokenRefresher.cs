using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace VitaSync
{
    public static class TokenRefresher
    {
        private static bool _refreshInProgress = false;

        public static void TryRefresh()
        {
            if (_refreshInProgress) return;
            if (!SessionManager.IsActive) return;

            // Usar referencia estática directa — GameObject.Find() no
            // encuentra objetos en DontDestroyOnLoad en Unity 2022.
            SceneMonitor monitor = VitaSyncPlugin.Monitor;
            if (monitor == null)
            {
                VitaSyncPlugin.Log.LogWarning("[Refresh] Monitor nulo. Omitido.");
                return;
            }

            monitor.StartCoroutine(RefreshCoroutine(monitor));
        }

        private static IEnumerator RefreshCoroutine(MonoBehaviour runner)
        {
            _refreshInProgress = true;

            string jsonBody = "{\"token\":\"" + SessionManager.BearerToken + "\"}";

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
                req.timeout = 8;

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    string newToken = LifeSyncClient.ExtractString(
                        req.downloadHandler.text, "access_token");
                    if (!string.IsNullOrEmpty(newToken))
                    {
                        SessionManager.UpdateToken(newToken);
                        VitaSyncPlugin.Log.LogInfo("[Refresh] Token actualizado.");
                    }
                    else
                    {
                        VitaSyncPlugin.Log.LogWarning("[Refresh] Token vacío en respuesta 200.");
                    }
                }
                else
                {
                    VitaSyncPlugin.Log.LogWarning(
                        "[Refresh] Error HTTP " + req.responseCode +
                        ": " + req.error);
                }
            }

            _refreshInProgress = false;
        }
    }
}