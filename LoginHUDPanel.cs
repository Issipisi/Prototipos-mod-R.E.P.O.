using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Networking;

namespace VitaSync
{
    /// <summary>
    /// Panel de autenticacion LSG — P7 rediseño estético.
    /// Paleta REPO industrial: grises cálidos, café oscuro, amarillo apagado.
    /// Layout: etiqueta encima del campo, fuentes más grandes, botón ojo contraseña.
    /// </summary>
    public class LoginHUDPanel : MonoBehaviour
    {
        private static LoginHUDPanel _instance;

        private GameObject _canvasGO;
        private InputField _userInput;
        private InputField _passInput;
        private Text _statusText;
        private Button _loginBtn;
        private bool _loginInProgress = false;
        private bool _passVisible = false;

        private static readonly string[] SPIN = { "/", "-", "\\", "|" };
        private int _spinIdx = 0; private float _spinTimer = 0f;
        private const float SPIN_DT = 0.12f;

        // ── Paleta REPO industrial ─────────────────────────────────────
        // Fondo principal: gris oscuro cálido (no negro puro)
        private static readonly Color C_BG = new Color(0.13f, 0.11f, 0.09f, 0.93f);
        // Cabecera: café oscuro
        private static readonly Color C_HDR = new Color(0.18f, 0.14f, 0.10f, 0.97f);
        // Amarillo REPO: apagado, industrial, no fluorescente
        private static readonly Color C_GOLD = new Color(0.78f, 0.60f, 0.18f, 1f);
        // Texto principal: gris claro cálido
        private static readonly Color C_TEXT = new Color(0.80f, 0.76f, 0.70f, 1f);
        // Texto secundario/dim: gris medio cálido
        private static readonly Color C_DIM = new Color(0.48f, 0.44f, 0.38f, 1f);
        // Fondo campo: gris oscuro cálido con tinte café
        private static readonly Color C_INPUT_BG = new Color(0.10f, 0.09f, 0.07f, 1f);
        // Texto activo en campo
        private static readonly Color C_INPUT_TXT = new Color(0.86f, 0.82f, 0.74f, 1f);
        // Placeholder: gris muy apagado
        private static readonly Color C_INPUT_PH = new Color(0.35f, 0.31f, 0.26f, 1f);
        // Línea subrayado campo: amarillo REPO semitransparente
        private static readonly Color C_INPUT_LINE = new Color(0.78f, 0.60f, 0.18f, 0.50f);
        // Botón login: verde oscuro industrial
        private static readonly Color C_BTN_OK = new Color(0.12f, 0.20f, 0.08f, 1f);
        private static readonly Color C_BTN_OK_T = new Color(0.80f, 0.76f, 0.70f, 1f);
        // Botón omitir: gris café oscuro
        private static readonly Color C_BTN_SKIP = new Color(0.16f, 0.13f, 0.10f, 1f);
        private static readonly Color C_BTN_SKIP_T = new Color(0.45f, 0.41f, 0.35f, 1f);
        // Botón ojo contraseña
        private static readonly Color C_BTN_EYE = new Color(0.20f, 0.16f, 0.11f, 1f);
        private static readonly Color C_BTN_EYE_T = new Color(0.78f, 0.60f, 0.18f, 1f);
        // Error y advertencia
        private static readonly Color C_ERROR = new Color(0.90f, 0.25f, 0.18f, 1f);
        private static readonly Color C_WARN = new Color(0.78f, 0.60f, 0.18f, 1f);

        public static void Initialize()
        {
            if (_instance != null) return;
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
            Destroy(_instance.gameObject); _instance = null;
        }

        private void Update()
        {
            if (_canvasGO != null && _canvasGO.activeSelf)
            { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }

            if (_loginInProgress && _statusText != null)
            {
                _spinTimer += Time.deltaTime;
                if (_spinTimer >= SPIN_DT)
                {
                    _spinTimer = 0f; _spinIdx = (_spinIdx + 1) % SPIN.Length;
                    string s = _statusText.text;
                    if (s.Length > 0 &&
                        System.Array.IndexOf(SPIN, s[s.Length - 1].ToString()) >= 0)
                        _statusText.text = s.Substring(0, s.Length - 1) + SPIN[_spinIdx];
                }
            }
            if (!_loginInProgress)
            {
                if (Input.GetKeyDown(KeyCode.Tab))
                {
                    if (_userInput != null && _userInput.isFocused) _passInput?.Select();
                    else _userInput?.Select();
                }
                if (Input.GetKeyDown(KeyCode.Return) ||
                    Input.GetKeyDown(KeyCode.KeypadEnter))
                    HandleLogin();
            }
        }

        private static Font GetFont() =>
            Resources.GetBuiltinResource<Font>("Arial.ttf");

        private void BuildUI()
        {
            _canvasGO = new GameObject("VitaSync_LoginCanvas");
            DontDestroyOnLoad(_canvasGO);
            Canvas cv = _canvasGO.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 9999;
            _canvasGO.AddComponent<CanvasScaler>();
            _canvasGO.AddComponent<GraphicRaycaster>();

            // Panel más alto para acomodar layout etiqueta-sobre-campo
            const float PW = 360f;
            const float PH = 310f;

            GameObject panel = new GameObject("Panel");
            panel.transform.SetParent(_canvasGO.transform, false);
            panel.AddComponent<Image>().color = C_BG;
            RectTransform pr = panel.GetComponent<RectTransform>();
            pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 0.5f);
            pr.sizeDelta = new Vector2(PW, PH);
            pr.anchoredPosition = Vector2.zero;

            // Borde izquierdo dorado
            MakeRect(panel.transform, "Accent",
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(3f, 0f), Vector2.zero, C_GOLD);

            // ── Cabecera ──────────────────────────────────────────────
            GameObject hdr = new GameObject("Hdr");
            hdr.transform.SetParent(panel.transform, false);
            hdr.AddComponent<Image>().color = C_HDR;
            RectTransform hr = hdr.GetComponent<RectTransform>();
            hr.anchorMin = new Vector2(0f, 1f); hr.anchorMax = new Vector2(1f, 1f);
            hr.pivot = new Vector2(0.5f, 1f);
            hr.sizeDelta = new Vector2(0f, 42f); hr.anchoredPosition = Vector2.zero;

            MakeRect(hdr.transform, "HdrLine",
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 2f), Vector2.zero, C_GOLD);

            MakeLbl(hdr.transform, "LIFESYNC GAMES - ACCESO",
                new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(PW, 42f), 14, C_GOLD, true, TextAnchor.MiddleCenter);

            // ── Campo USUARIO (etiqueta encima, campo debajo) ─────────
            // Coordenadas Y relativas al centro del panel (0,0 = centro)
            // Cabecera ocupa top 42px → contenido desde y=100 hacia abajo

            const float LBL_FS = 11;   // tamaño etiqueta
            const float INP_FS = 12;   // tamaño texto campo
            const float IW = PW - 40f; // ancho campo
            const float IH = 30f;   // alto campo
            const float LH = 18f;   // alto etiqueta

            // Etiqueta USUARIO
            MakeLbl(panel.transform, "USUARIO",
                new Vector2(0.5f, 0.5f), new Vector2(0f, 96f),
                new Vector2(IW, LH), (int)LBL_FS, C_GOLD, true, TextAnchor.MiddleLeft);

            // Campo usuario
            _userInput = MakeInput(panel.transform, "User", "correo@usach.cl",
                new Vector2(0f, 72f), new Vector2(IW, IH), false, (int)INP_FS);

            // Etiqueta CLAVE
            MakeLbl(panel.transform, "CLAVE",
                new Vector2(0.5f, 0.5f), new Vector2(0f, 38f),
                new Vector2(IW, LH), (int)LBL_FS, C_GOLD, true, TextAnchor.MiddleLeft);

            // Campo contraseña (ancho reducido para dejar espacio al botón ojo)
            const float EYE_W = 34f;
            const float EYE_GAP = 6f;
            float passW = IW - EYE_W - EYE_GAP;
            float passX = -(EYE_W + EYE_GAP) / 2f; // desplazado a la izquierda

            _passInput = MakeInput(panel.transform, "Pass", "••••••••",
                new Vector2(passX, 14f), new Vector2(passW, IH), true, (int)INP_FS);

            // Botón ojo — mostrar/ocultar contraseña
            GameObject eyeBtn = MakeBtn(panel.transform, "BtnEye", "VER",
                new Vector2(IW / 2f - EYE_W / 2f + 2f, 14f),
                new Vector2(EYE_W, IH), C_BTN_EYE, C_BTN_EYE_T, 9);
            eyeBtn.GetComponent<Button>().onClick.AddListener(TogglePassVisibility);

            // Separador
            MakeRect(panel.transform, "Sep",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(PW - 20f, 1f), new Vector2(0f, -12f),
                new Color(C_GOLD.r, C_GOLD.g, C_GOLD.b, 0.25f));

            // Status
            _statusText = MakeLbl(panel.transform, "LISTO PARA AUTENTICAR",
                new Vector2(0.5f, 0.5f), new Vector2(0f, -30f),
                new Vector2(PW - 20f, 18f), 10, C_DIM, false, TextAnchor.MiddleCenter);

            // ── Botones ───────────────────────────────────────────────
            GameObject bLogin = MakeBtn(panel.transform, "BtnLogin",
                "INICIAR SESION",
                new Vector2(-54f, -80f), new Vector2(196f, 34f),
                C_BTN_OK, C_BTN_OK_T, 12);
            _loginBtn = bLogin.GetComponent<Button>();
            _loginBtn.onClick.AddListener(HandleLogin);

            MakeBtn(panel.transform, "BtnSkip",
                "OMITIR",
                new Vector2(116f, -80f), new Vector2(84f, 34f),
                C_BTN_SKIP, C_BTN_SKIP_T, 11)
                .GetComponent<Button>().onClick.AddListener(HidePanel);

            // Pie
            MakeLbl(panel.transform, "lsg.diinf.usach.cl  ·  DIINF USACH",
                new Vector2(0.5f, 0.5f), new Vector2(0f, -128f),
                new Vector2(PW - 20f, 14f), 8, C_DIM, false, TextAnchor.MiddleCenter);
        }

        // ── Lógica ────────────────────────────────────────────────────
        private void TogglePassVisibility()
        {
            _passVisible = !_passVisible;
            if (_passInput != null)
                _passInput.inputType = _passVisible
                    ? InputField.InputType.Standard
                    : InputField.InputType.Password;
            // Forzar refresco visual del InputField
            _passInput?.ForceLabelUpdate();
        }

        private void HidePanel()
        {
            VitaSyncPlugin.Log.LogInfo("[Login] Omitido. Modo pasivo.");
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            if (_canvasGO != null) Destroy(_canvasGO);
            Destroy(gameObject); _instance = null;
        }

        private void HandleLogin()
        {
            if (_loginInProgress) return;
            if (string.IsNullOrEmpty(_userInput.text) ||
                string.IsNullOrEmpty(_passInput.text))
            { SetStatus("COMPLETE AMBOS CAMPOS", C_ERROR); return; }
            SetStatus("CONECTANDO " + SPIN[0], C_WARN);
            _spinIdx = 0; _spinTimer = 0f;
            _loginInProgress = true; _loginBtn.interactable = false;
            StartCoroutine(LoginFlow(_userInput.text.Trim(), _passInput.text));
        }

        private IEnumerator LoginFlow(string user, string pass)
        {
            string token = null;
            WWWForm form = new WWWForm();
            form.AddField("username", user); form.AddField("password", pass);
            form.AddField("grant_type", ""); form.AddField("scope", "");
            form.AddField("client_id", ""); form.AddField("client_secret", "");

            using (UnityWebRequest req = UnityWebRequest.Post(
                VitaSyncPlugin.AUTH_URL, form))
            {
                req.SetRequestHeader("Accept", "application/json"); req.timeout = 10;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.ConnectionError)
                { OnNetErr("SIN CONEXION CON SERVIDOR"); yield break; }
                if (req.responseCode == 401 || req.responseCode == 400 ||
                    req.result != UnityWebRequest.Result.Success)
                { OnLoginErr("CREDENCIALES INCORRECTAS"); yield break; }
                token = LifeSyncClient.ExtractString(
                    req.downloadHandler.text, "access_token");
            }
            if (string.IsNullOrEmpty(token))
            { OnLoginErr("ERROR EN RESPUESTA"); yield break; }

            int playerId = -1;
            using (UnityWebRequest req = UnityWebRequest.Get(
                VitaSyncPlugin.AUTH_WHOAMI))
            {
                req.SetRequestHeader("Authorization", "Bearer " + token);
                req.SetRequestHeader("Accept", "application/json"); req.timeout = 10;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    string ids = LifeSyncClient.ExtractString(
                        req.downloadHandler.text, "id_players");
                    int.TryParse(ids, out playerId);
                }
            }
            if (playerId < 0)
            { OnLoginErr("ID DE JUGADOR NO ENCONTRADO"); yield break; }

            using (UnityWebRequest req = UnityWebRequest.Get(
                VitaSyncPlugin.CORE_URL +
                "/players/" + playerId + "/points/balance"))
            {
                req.SetRequestHeader("Authorization", "Bearer " + token);
                req.SetRequestHeader("Accept", "application/json"); req.timeout = 10;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                { OnLoginErr("ERROR AL OBTENER BALANCE"); yield break; }

                int puntos = ParseBalance(req.downloadHandler.text, "2");
                SessionManager.Save(token, playerId);
                var p = new LifeSyncClient.PhysicalProfile();
                p.Puntos = puntos; p.CanjesUsados = 0; p.CanjesMax = 2;
                VitaSyncPlugin.Instance.SetActiveProfile(p);
                VitaSyncPlugin.Log.LogInfo(
                    "[LSG] Login exitoso. Balance: " + puntos);
            }
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            if (_canvasGO != null) Destroy(_canvasGO);
            Destroy(gameObject); _instance = null;
        }

        private void OnLoginErr(string msg)
        {
            SetStatus(msg, C_ERROR); _passInput.text = "";
            _loginInProgress = false; _loginBtn.interactable = true;
            _passInput.Select();
            VitaSyncPlugin.Log.LogWarning("[Login] " + msg);
        }
        private void OnNetErr(string msg)
        {
            SetStatus(msg, C_WARN);
            _loginInProgress = false; _loginBtn.interactable = true;
            VitaSyncPlugin.Log.LogError("[Login] " + msg);
        }
        private void SetStatus(string msg, Color col)
        {
            if (_statusText == null) return;
            _statusText.text = msg; _statusText.color = col;
        }

        private static int ParseBalance(string json, string dimId)
        {
            int idx = 0;
            while (true)
            {
                int di = json.IndexOf("\"id_point_dimension\"", idx,
                    System.StringComparison.Ordinal);
                if (di < 0) break;
                int oe = json.IndexOf('}', di); if (oe < 0) break;
                string sl = json.Substring(di, oe - di + 1);
                string dv = LifeSyncClient.ExtractString(sl, "id_point_dimension");
                if (dv == dimId)
                {
                    string b = LifeSyncClient.ExtractString(sl, "balance");
                    if (int.TryParse(b, out int bal)) return bal;
                }
                idx = oe + 1;
            }
            return 0;
        }

        // ── Helpers ───────────────────────────────────────────────────
        private static void MakeRect(Transform p, string name,
            Vector2 aMin, Vector2 aMax, Vector2 size, Vector2 pos, Color col)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(p, false);
            go.AddComponent<Image>().color = col;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size; rt.anchoredPosition = pos;
        }

        private static Text MakeLbl(Transform p, string text, Vector2 anchor,
            Vector2 pos, Vector2 size, int fs, Color col, bool bold, TextAnchor align)
        {
            GameObject go = new GameObject("T");
            go.transform.SetParent(p, false);
            Text t = go.AddComponent<Text>();
            t.font = GetFont(); t.text = text; t.fontSize = fs; t.color = col;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.alignment = align;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = anchor; rt.sizeDelta = size; rt.anchoredPosition = pos;
            return t;
        }

        private static InputField MakeInput(Transform p, string name,
            string ph, Vector2 pos, Vector2 size, bool isPass, int fs)
        {
            GameObject go = new GameObject("Inp_" + name);
            go.transform.SetParent(p, false);
            go.AddComponent<Image>().color = C_INPUT_BG;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size; rt.anchoredPosition = pos;

            MakeRect(go.transform, "Line",
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), Vector2.zero, C_INPUT_LINE);

            InputField inp = go.AddComponent<InputField>();

            GameObject phGo = new GameObject("PH");
            phGo.transform.SetParent(go.transform, false);
            Text phT = phGo.AddComponent<Text>();
            phT.font = GetFont(); phT.text = ph; phT.color = C_INPUT_PH;
            phT.fontSize = fs - 1; phT.alignment = TextAnchor.MiddleLeft;
            RectTransform phR = phGo.GetComponent<RectTransform>();
            phR.anchorMin = Vector2.zero; phR.anchorMax = Vector2.one;
            phR.sizeDelta = new Vector2(-12f, -4f);

            GameObject txGo = new GameObject("TX");
            txGo.transform.SetParent(go.transform, false);
            Text txT = txGo.AddComponent<Text>();
            txT.font = GetFont(); txT.color = C_INPUT_TXT;
            txT.fontSize = fs; txT.alignment = TextAnchor.MiddleLeft;
            RectTransform txR = txGo.GetComponent<RectTransform>();
            txR.anchorMin = Vector2.zero; txR.anchorMax = Vector2.one;
            txR.sizeDelta = new Vector2(-12f, -4f);

            inp.placeholder = phT; inp.textComponent = txT;
            if (isPass) inp.inputType = InputField.InputType.Password;
            return inp;
        }

        private static GameObject MakeBtn(Transform p, string name,
            string label, Vector2 pos, Vector2 size,
            Color bg, Color fg, int fs = 11)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(p, false);
            Image img = go.AddComponent<Image>(); img.color = bg;
            Button btn = go.AddComponent<Button>();
            ColorBlock cb = ColorBlock.defaultColorBlock;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.22f, 1.22f, 1.22f, 1f);
            cb.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            cb.colorMultiplier = 1f;
            btn.colors = cb; btn.targetGraphic = img;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size; rt.anchoredPosition = pos;
            GameObject lGo = new GameObject("L");
            lGo.transform.SetParent(go.transform, false);
            Text t = lGo.AddComponent<Text>(); t.font = GetFont();
            t.text = label; t.fontSize = fs; t.color = fg;
            t.fontStyle = FontStyle.Bold; t.alignment = TextAnchor.MiddleCenter;
            RectTransform lr = lGo.GetComponent<RectTransform>();
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.sizeDelta = Vector2.zero;
            return go;
        }
    }
}