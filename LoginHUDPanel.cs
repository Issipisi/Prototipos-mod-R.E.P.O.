using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Networking;

namespace VitaSync
{
    /// <summary>
    /// Panel de autenticación LS-G.
    /// P6: se inyecta mediante Postfix sobre MenuPageMain.Start() en lugar
    /// de OnSceneLoaded, garantizando que el menú esté completamente
    /// estable antes de construir los InputFields.
    /// Añade botón "Ocultar" y manejo de errores con reintento infinito:
    /// ante credenciales incorrectas el panel permanece en pantalla,
    /// limpia el campo de contraseña y muestra un mensaje de error.
    /// </summary>
    public class LoginHUDPanel : MonoBehaviour
    {
        private static LoginHUDPanel _instance;

        private GameObject _canvasGO;
        private InputField _usernameInput;
        private InputField _passwordInput;
        private Text _statusText;
        private Button _loginButton;
        private bool _loginInProgress = false;

        // ── CICLO DE VIDA ─────────────────────────────────────────────
        public static void Initialize()
        {
            if (_instance != null) return;

            // EventSystem requerido para InputField
            if (FindObjectOfType<EventSystem>() == null)
            {
                GameObject es = new GameObject("VitaSync_EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
                DontDestroyOnLoad(es);
            }

            GameObject go = new GameObject("VitaSync_LoginHUD");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<LoginHUDPanel>();
            _instance.BuildUI();
        }

        public static void DestroyIfExists()
        {
            if (_instance == null) return;
            if (_instance._canvasGO != null) Destroy(_instance._canvasGO);
            Destroy(_instance.gameObject);
            _instance = null;
        }

        // ── INPUT ─────────────────────────────────────────────────────
        private void Update()
        {
            // Solo gestionar cursor cuando el panel está visible
            if (_canvasGO != null && _canvasGO.activeSelf)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (!_loginInProgress)
            {
                if (Input.GetKeyDown(KeyCode.Tab))
                {
                    if (_usernameInput != null && _usernameInput.isFocused)
                        _passwordInput?.Select();
                    else if (_passwordInput != null && _passwordInput.isFocused)
                        _usernameInput?.Select();
                }

                if (Input.GetKeyDown(KeyCode.Return) ||
                    Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    HandleLoginSubmit();
                }
            }
        }

        // ── CONSTRUCCIÓN UI ───────────────────────────────────────────
        private static Font GetFont()
            => Resources.GetBuiltinResource<Font>("Arial.ttf");

        private void BuildUI()
        {
            _canvasGO = new GameObject("VitaSync_LoginCanvas");
            DontDestroyOnLoad(_canvasGO);

            Canvas canvas = _canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;
            _canvasGO.AddComponent<CanvasScaler>();
            _canvasGO.AddComponent<GraphicRaycaster>();

            // Panel central
            GameObject panel = new GameObject("CenterPanel");
            panel.transform.SetParent(_canvasGO.transform, false);
            panel.AddComponent<Image>().color =
                new Color(0.09f, 0.1f, 0.13f, 0.99f);

            RectTransform panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(340f, 270f);

            CreateLabel(panel.transform,
                "VITASYNC — AUTENTICACIÓN LSG",
                new Vector2(0f, 108f), 13, Color.cyan);

            _usernameInput = CreateInputField(
                panel.transform, "Usuario",
                "Ingrese su correo...", 53f);

            _passwordInput = CreateInputField(
                panel.transform, "Contraseña",
                "Ingrese contraseña...", 8f, isPass: true);

            _statusText = CreateLabel(panel.transform,
                "Inicie sesión con su cuenta LifeSync-Games",
                new Vector2(0f, -42f), 11, Color.gray);

            // Botón INICIAR SESIÓN
            GameObject btnLogin = CreateButton(
                panel.transform,
                "BtnSubmit",
                "INICIAR SESIÓN",
                new Vector2(0f, -86f),
                new Vector2(160f, 34f),
                new Color(0.15f, 0.45f, 0.25f));
            btnLogin.GetComponent<Button>().onClick
                .AddListener(HandleLoginSubmit);
            _loginButton = btnLogin.GetComponent<Button>();

            // Botón OCULTAR  ←  nuevo en P6
            GameObject btnHide = CreateButton(
                panel.transform,
                "BtnHide",
                "OCULTAR",
                new Vector2(0f, -122f),
                new Vector2(85f, 22f),
                new Color(0.25f, 0.25f, 0.28f));
            btnHide.GetComponent<Button>().onClick.AddListener(HidePanel);
        }

        // ── LÓGICA ────────────────────────────────────────────────────
        private void HidePanel()
        {
            VitaSyncPlugin.Log.LogInfo(
                "[Login] Panel descartado por el jugador. " +
                "Modo pasivo activo. Se redesplegará al volver al menú.");

            // Devolver el cursor al control del juego antes de destruir
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // Destruir completamente en lugar de ocultar:
            // así _instance queda null y MenuPageMainStartPatch
            // puede volver a crear el panel al regresar al lobby.
            if (_canvasGO != null) Destroy(_canvasGO);
            Destroy(gameObject);
            _instance = null;
        }

        private void HandleLoginSubmit()
        {
            if (_loginInProgress) return;

            if (string.IsNullOrEmpty(_usernameInput.text) ||
                string.IsNullOrEmpty(_passwordInput.text))
            {
                SetStatus("Complete ambos campos.", Color.red);
                return;
            }

            SetStatus("Conectando con lsg.diinf.usach.cl...", Color.yellow);
            _loginInProgress = true;
            _loginButton.interactable = false;

            StartCoroutine(LoginFlow(
                _usernameInput.text.Trim(),
                _passwordInput.text));
        }

        private IEnumerator LoginFlow(string user, string pass)
        {
            // ── 1. LOGIN ──────────────────────────────────────────────
            string token = null;

            WWWForm form = new WWWForm();
            form.AddField("username", user);
            form.AddField("password", pass);
            form.AddField("grant_type", "");
            form.AddField("scope", "");
            form.AddField("client_id", "");
            form.AddField("client_secret", "");

            using (UnityWebRequest req =
                UnityWebRequest.Post(VitaSyncPlugin.AUTH_URL, form))
            {
                req.SetRequestHeader("Accept", "application/json");
                req.timeout = 10;
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success ||
                    req.responseCode == 401 || req.responseCode == 400)
                {
                    // ── ERROR RECUPERABLE: panel permanece abierto ────
                    OnLoginError("Credenciales incorrectas. Intente nuevamente.");
                    yield break;
                }

                token = ExtractJson(req.downloadHandler.text, "access_token");
            }

            if (string.IsNullOrEmpty(token))
            {
                OnLoginError("Error en respuesta del servidor.");
                yield break;
            }

            // ── 2. WHOAMI ─────────────────────────────────────────────
            int playerId = -1;

            using (UnityWebRequest req =
                UnityWebRequest.Get(VitaSyncPlugin.AUTH_WHOAMI))
            {
                req.SetRequestHeader("Authorization", "Bearer " + token);
                req.SetRequestHeader("Accept", "application/json");
                req.timeout = 10;
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    string idStr = ExtractJson(
                        req.downloadHandler.text, "id_players");
                    int.TryParse(idStr, out playerId);
                }
            }

            if (playerId < 0)
            {
                OnLoginError("No se pudo recuperar el ID del jugador.");
                yield break;
            }

            // ── 3. BALANCE ────────────────────────────────────────────
            string balUrl = VitaSyncPlugin.CORE_URL +
                            "/players/" + playerId + "/points/balance";

            using (UnityWebRequest req = UnityWebRequest.Get(balUrl))
            {
                req.SetRequestHeader("Authorization", "Bearer " + token);
                req.SetRequestHeader("Accept", "application/json");
                req.timeout = 10;
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    OnLoginError("Error al obtener balance físico.");
                    yield break;
                }

                int puntos = ParseBalance(req.downloadHandler.text, "2");

                // Guardar sesión en SessionManager (persiste al destruir panel)
                SessionManager.Save(token, playerId);

                // Construir perfil y registrarlo en el plugin
                LifeSyncClient.PhysicalProfile profile =
                    new LifeSyncClient.PhysicalProfile();
                profile.Puntos = puntos;
                profile.CanjesUsados = 0;
                profile.CanjesMax = 2;
                VitaSyncPlugin.Instance.SetActiveProfile(profile);

                VitaSyncPlugin.Log.LogInfo(
                    "[LSG] Login exitoso. Balance físico: " + puntos);
            }

            // ── LIMPIAR Y CERRAR ──────────────────────────────────────
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            if (_canvasGO != null) Destroy(_canvasGO);
            Destroy(gameObject);
            _instance = null;
        }

        /// <summary>
        /// Reacción ante error recuperable: el panel permanece visible,
        /// el campo de contraseña se limpia y se reactivan los controles.
        /// El jugador puede reintentar sin reiniciar el juego.
        /// </summary>
        private void OnLoginError(string mensaje)
        {
            SetStatus(mensaje, Color.red);
            _passwordInput.text = "";
            _loginInProgress = false;
            _loginButton.interactable = true;
            _passwordInput.Select();
            VitaSyncPlugin.Log.LogWarning("[Login] Error recuperable: " + mensaje);
        }

        // ── HELPERS ───────────────────────────────────────────────────
        private void SetStatus(string msg, Color col)
        {
            if (_statusText == null) return;
            _statusText.text = msg;
            _statusText.color = col;
        }

        private static string ExtractJson(string json, string key)
            => LifeSyncClient.ExtractString(json, key);

        private static int ParseBalance(string json, string dimId)
        {
            int idx = 0;
            while (true)
            {
                int di = json.IndexOf(
                    "\"id_point_dimension\"", idx,
                    System.StringComparison.Ordinal);
                if (di < 0) break;
                int oe = json.IndexOf('}', di);
                if (oe < 0) break;
                string slice = json.Substring(di, oe - di + 1);
                string dimVal = ExtractJson(slice, "id_point_dimension");
                if (dimVal == dimId)
                {
                    string b = ExtractJson(slice, "balance");
                    if (int.TryParse(b, out int bal)) return bal;
                }
                idx = oe + 1;
            }
            return 0;
        }

        private Text CreateLabel(Transform parent, string text,
            Vector2 pos, int size, Color col)
        {
            GameObject go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            Text t = go.AddComponent<Text>();
            t.font = GetFont();
            t.text = text;
            t.fontSize = size;
            t.color = col;
            t.alignment = TextAnchor.MiddleCenter;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(320f, 25f);
            return t;
        }

        private GameObject CreateButton(Transform parent, string name,
            string label, Vector2 pos, Vector2 size, Color color)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = color;
            go.AddComponent<Button>();
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            CreateLabel(go.transform, label, Vector2.zero, 11, Color.white);
            return go;
        }

        private InputField CreateInputField(Transform parent, string name,
            string placeholder, float yPos, bool isPass = false)
        {
            GameObject go = new GameObject("InputField_" + name);
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = new Color(0.18f, 0.19f, 0.24f);

            InputField input = go.AddComponent<InputField>();
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(260f, 34f);
            rt.anchoredPosition = new Vector2(0f, yPos);

            // Placeholder
            GameObject phGo = new GameObject("Placeholder");
            phGo.transform.SetParent(go.transform, false);
            Text phText = phGo.AddComponent<Text>();
            phText.font = GetFont();
            phText.text = placeholder;
            phText.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            phText.fontSize = 11;
            phText.alignment = TextAnchor.MiddleLeft;
            RectTransform phRt = phGo.GetComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.sizeDelta = new Vector2(-16f, -4f);

            // Texto real
            GameObject txGo = new GameObject("Text");
            txGo.transform.SetParent(go.transform, false);
            Text tx = txGo.AddComponent<Text>();
            tx.font = GetFont();
            tx.color = Color.white;
            tx.fontSize = 12;
            tx.alignment = TextAnchor.MiddleLeft;
            RectTransform txRt = txGo.GetComponent<RectTransform>();
            txRt.anchorMin = Vector2.zero;
            txRt.anchorMax = Vector2.one;
            txRt.sizeDelta = new Vector2(-16f, -4f);

            input.placeholder = phText;
            input.textComponent = tx;
            if (isPass) input.inputType = InputField.InputType.Password;

            return input;
        }
    }
}