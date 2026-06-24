using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Networking;

namespace VitaSync
{
    /// <summary>
    /// Panel de autenticación LS-G. Se despliega en el menú principal.
    /// Tras login exitoso guarda las credenciales en SessionManager
    /// y se destruye a sí mismo.
    /// </summary>
    public class LoginHUDPanel : MonoBehaviour
    {
        private static LoginHUDPanel _instance;

        private GameObject _canvasGO;
        private InputField _usernameInput;
        private InputField _passwordInput;
        private Text _statusText;
        private Button _loginButton;

        public static void Initialize()
        {
            if (_instance != null) return;

            // EventSystem necesario para que InputField funcione
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

        private void Update()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

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

        private static Font GetFont()
        {
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

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
            Image bg = panel.AddComponent<Image>();
            bg.color = new Color(0.09f, 0.1f, 0.13f, 0.99f);

            RectTransform panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(340f, 260f);

            CreateLabel(panel.transform,
                "VITASYNC — AUTENTICACIÓN LSG",
                new Vector2(0f, 100f), 13, Color.cyan);

            _usernameInput = CreateInputField(
                panel.transform, "Usuario",
                "Ingrese su correo...", 45f);

            _passwordInput = CreateInputField(
                panel.transform, "Contraseña",
                "Ingrese contraseña...", -10f, isPass: true);

            _statusText = CreateLabel(panel.transform,
                "Inicie sesión con su cuenta LifeSync-Games",
                new Vector2(0f, -55f), 11, Color.gray);

            GameObject btnGo = new GameObject("BtnSubmit");
            btnGo.transform.SetParent(panel.transform, false);
            btnGo.AddComponent<Image>().color = new Color(0.15f, 0.45f, 0.25f);
            _loginButton = btnGo.AddComponent<Button>();

            RectTransform btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.sizeDelta = new Vector2(160f, 34f);
            btnRt.anchoredPosition = new Vector2(0f, -95f);

            CreateLabel(btnGo.transform,
                "INICIAR SESIÓN", Vector2.zero, 11, Color.white);

            _loginButton.onClick.AddListener(HandleLoginSubmit);
        }

        private void HandleLoginSubmit()
        {
            if (string.IsNullOrEmpty(_usernameInput.text) ||
                string.IsNullOrEmpty(_passwordInput.text))
            {
                SetStatus("Complete ambos campos.", Color.red);
                return;
            }
            SetStatus("Conectando con lsg.diinf.usach.cl...", Color.yellow);
            StartCoroutine(LoginFlow(
                _usernameInput.text, _passwordInput.text));
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

                if (req.result != UnityWebRequest.Result.Success)
                {
                    SetStatus("Credenciales incorrectas o error de red.",
                              Color.red);
                    yield break;
                }
                token = ExtractJson(req.downloadHandler.text, "access_token");
            }

            if (string.IsNullOrEmpty(token))
            {
                SetStatus("Error en respuesta del servidor.", Color.red);
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
                SetStatus("No se pudo recuperar el ID del jugador.",
                          Color.red);
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
                    SetStatus("Error al obtener balance.", Color.red);
                    yield break;
                }

                int puntos = ParseBalance(req.downloadHandler.text, "2");

                // Guardar sesión en SessionManager (persiste tras destruir este panel)
                SessionManager.Save(token, playerId);

                // Construir perfil y registrarlo en el plugin
                var profile = new LifeSyncClient.PhysicalProfile();
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

        // ── HELPERS ───────────────────────────────────────────────────
        private void SetStatus(string msg, Color col)
        {
            if (_statusText == null) return;
            _statusText.text = msg;
            _statusText.color = col;
        }

        private static string ExtractJson(string json, string key)
        {
            string k = "\"" + key + "\"";
            int ki = json.IndexOf(k, System.StringComparison.Ordinal);
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
            while (end < json.Length &&
                   json[end] != ',' &&
                   json[end] != '}' &&
                   json[end] != ']') end++;
            return json.Substring(vs, end - vs).Trim();
        }

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