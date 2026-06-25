using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace VitaSync
{
    /// <summary>
    /// Componente auxiliar que consume POST /token/refresh para renovar
    /// el JWT sin necesidad de re-autenticar con usuario y contraseña.
    /// Se invoca automáticamente al entrar a cada escena de tienda,
    /// mitigando la expiración de 120 minutos en sesiones largas.
    /// </summary>
    public static class TokenRefresher
    {
        private static bool _refreshInProgress = false;

        /// <summary>
        /// Solicita el refresco del token. Solo ejecuta si no hay un
        /// refresco ya en curso y la sesión está activa.
        /// La coroutine se lanza en el SceneMonitor (MonoBehaviour
        /// persistente) para no depender de ningún panel de UI.
        /// </summary>
        public static void TryRefresh()
        {
            if (_refreshInProgress)
            {
                VitaSyncPlugin.Log.LogInfo(
                    "[Refresh] Refresco ya en progreso. Ignorado.");
                return;
            }

            if (!SessionManager.IsActive)
            {
                VitaSyncPlugin.Log.LogInfo(
                    "[Refresh] Sin sesión activa. Refresco omitido.");
                return;
            }

            // Usamos el SceneMonitor como runner de la coroutine porque
            // es un MonoBehaviour con DontDestroyOnLoad garantizado.
            GameObject go = GameObject.Find("VitaSync_Monitor");
            if (go == null)
            {
                VitaSyncPlugin.Log.LogWarning(
                    "[Refresh] VitaSync_Monitor no encontrado. " +
                    "Refresco omitido.");
                return;
            }

            SceneMonitor monitor = go.GetComponent<SceneMonitor>();
            if (monitor == null) return;

            monitor.StartCoroutine(RefreshCoroutine());
        }

        private static IEnumerator RefreshCoroutine()
        {
            _refreshInProgress = true;
            VitaSyncPlugin.Log.LogInfo("[Refresh] Iniciando refresco de token...");

            // El endpoint POST /token/refresh recibe el token actual
            // en el header Authorization y devuelve un nuevo access_token.
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
                            "[Refresh] Token actualizado en SessionManager.");
                    }
                    else
                    {
                        VitaSyncPlugin.Log.LogWarning(
                            "[Refresh] Respuesta 200 pero sin access_token. " +
                            "Token anterior conservado.");
                    }
                }
                else
                {
                    // El token antiguo sigue siendo válido hasta que expire:
                    // solo logueamos el error, no cortamos la sesión.
                    VitaSyncPlugin.Log.LogWarning(
                        "[Refresh] Error HTTP " + req.responseCode +
                        ": " + req.error +
                        ". Token anterior conservado.");
                }
            }

            _refreshInProgress = false;
        }
    }
}