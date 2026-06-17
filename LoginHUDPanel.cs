using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Networking;

namespace VitaSync
{
    public class LoginHUDPanel : MonoBehaviour
    {
        private static LoginHUDPanel _instance;
        private GameObject _canvasGO;
        private InputField _usernameInput;
        private InputField _passwordInput;
        private Text _statusText;
        private Button _loginButton;

        private string _bearerToken = null;
        private int _playerId = -1;

        public static void Initialize()
        {
            if (_instance != null) return;

            if (FindObjectOfType<EventSystem>() == null)
            {
                GameObject eventSystem = new GameObject("VitaSync_EventSystem");
                eventSystem.AddComponent<EventSystem>();
                eventSystem.AddComponent<StandaloneInputModule>();
            }

            GameObject go = new GameObject("VitaSync_LoginHUD");
            _instance = go.AddComponent<LoginHUDPanel>();
            _instance.BuildUI();
        }

        private void Update()
        {
            // Forzar visualización del cursor del mouse por encima del juego
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // --- NAVEGACIÓN POR TECLADO (TABULAR Y ENTER) ---
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                if (_usernameInput != null && _usernameInput.isFocused && _passwordInput != null)
                {
                    _passwordInput.Select();
                }
                else if (_passwordInput != null && _passwordInput.isFocused && _usernameInput != null)
                {
                    _usernameInput.Select();
                }
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                HandleLoginSubmit();
            }
        }

        private static Font GetFont() => Resources.GetBuiltinResource<Font>("Arial.ttf");

        private void BuildUI()
        {
            _canvasGO = new GameObject("VitaSync_LoginCanvas");
            Canvas canvas = _canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;

            _canvasGO.AddComponent<CanvasScaler>();
            _canvasGO.AddComponent<GraphicRaycaster>();

            GameObject panel = new GameObject("CenterPanel");
            panel.transform.SetParent(_canvasGO.transform, false);
            Image bg = panel.AddComponent<Image>();
            bg.color = new Color(0.09f, 0.1f, 0.13f, 0.99f);

            RectTransform panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(340f, 260f);

            CreateLabel(panel.transform, "VITASYNC - AUTENTICACIÓN LSG", new Vector2(0f, 100f), 13, Color.cyan);

            // Campos vacíos con placeholders nativos
            _usernameInput = CreateInputField(panel.transform, "Usuario", "Ingrese su correo...", 45f);
            _passwordInput = CreateInputField(panel.transform, "Contraseña", "Ingrese contraseña...", -10f, true);

            _statusText = CreateLabel(panel.transform, "Inicie sesión en perfil LifeSync-Games", new Vector2(0f, -55f), 11, Color.gray);

            GameObject btnGo = new GameObject("BtnSubmit");
            btnGo.transform.SetParent(panel.transform, false);
            Image btnImg = btnGo.AddComponent<Image>();
            btnImg.color = new Color(0.15f, 0.45f, 0.25f);
            _loginButton = btnGo.AddComponent<Button>();

            RectTransform btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.sizeDelta = new Vector2(160f, 34f);
            btnRt.anchoredPosition = new Vector2(0f, -95f);

            CreateLabel(btnGo.transform, "INICIAR SESIÓN", Vector2.zero, 11, Color.white);
            _loginButton.onClick.AddListener(HandleLoginSubmit);
        }

        private void HandleLoginSubmit()
        {
            if (string.IsNullOrEmpty(_usernameInput.text) || string.IsNullOrEmpty(_passwordInput.text))
            {
                _statusText.text = "Por favor, complete ambos campos.";
                _statusText.color = Color.red;
                return;
            }

            _statusText.text = "Conectando con lsg.diinf.usach.cl...";
            _statusText.color = Color.yellow;

            StartCoroutine(AuthAndFetchProfileFlow(_usernameInput.text, _passwordInput.text));
        }

        // --- FLUJO COMPLETO DE CONEXIÓN CON EL SERVIDOR ---
        private IEnumerator AuthAndFetchProfileFlow(string user, string pass)
        {
            WWWForm form = new WWWForm();
            form.AddField("username", user);
            form.AddField("password", pass);
            form.AddField("grant_type", "");
            form.AddField("scope", "");
            form.AddField("client_id", "");
            form.AddField("client_secret", "");

            using (var req = UnityWebRequest.Post(VitaSyncPlugin.AUTH_URL, form))
            {
                req.SetRequestHeader("Accept", "application/json");
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    _statusText.text = "Credenciales incorrectas o error de red.";
                    _statusText.color = Color.red;
                    yield break;
                }

                string loginJson = req.downloadHandler.text;
                _bearerToken = ExtractJsonString(loginJson, "access_token");
            }

            if (string.IsNullOrEmpty(_bearerToken))
            {
                _statusText.text = "Error de respuesta del servidor (Token).";
                _statusText.color = Color.red;
                yield break;
            }

            // 2. Obtener Player ID (WhoAmI)
            using (var req = UnityWebRequest.Get(VitaSyncPlugin.AUTH_WHOAMI))
            {
                req.SetRequestHeader("Authorization", $"Bearer {_bearerToken}");
                req.SetRequestHeader("Accept", "application/json");
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    string whoamiJson = req.downloadHandler.text;
                    string idStr = ExtractJsonString(whoamiJson, "id_players");
                    int.TryParse(idStr, out _playerId);
                }
            }

            if (_playerId < 0)
            {
                _statusText.text = "No se pudo recuperar el ID del jugador.";
                _statusText.color = Color.red;
                yield break;
            }

            // 3. Consultar Balance de Puntos Físicos
            string balanceUrl = $"{VitaSyncPlugin.CORE_URL}/players/{_playerId}/points/balance";
            using (var req = UnityWebRequest.Get(balanceUrl))
            {
                req.SetRequestHeader("Authorization", $"Bearer {_bearerToken}");
                req.SetRequestHeader("Accept", "application/json");
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    string balanceJson = req.downloadHandler.text;
                    int puntosReales = ParsePhysicalPoints(balanceJson);

                    // Mapear los datos al contenedor estructural
                    var realProfile = new LifeSyncClient.PhysicalProfile();
                    realProfile.Puntos = puntosReales;
                    realProfile.CanjesUsados = 0;
                    realProfile.CanjesMax = 2;  // Forzado estrictamente a máximo 2 canjes por nivel laboral/ronda

                    VitaSyncPlugin.Instance.SetActiveProfile(realProfile);
                    VitaSyncPlugin.Log.LogInfo($"[LSG] Login exitoso. Balance físico asignado: {puntosReales}");

                    // Destruir panel de login tras éxito
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    Destroy(_canvasGO);
                    Destroy(gameObject);
                    _instance = null;
                }
                else
                {
                    _statusText.text = "Error al obtener balance universitario.";
                    _statusText.color = Color.red;
                }
            }
        }

        // --- MANIPULADORES NATIVOS DE STRING JSON ---
        private static string ExtractJsonString(string json, string key)
        {
            string searchKey = $"\"{key}\"";
            int keyIdx = json.IndexOf(searchKey, StringComparison.Ordinal);
            if (keyIdx < 0) return null;
            int colonIdx = json.IndexOf(':', keyIdx + searchKey.Length);
            if (colonIdx < 0) return null;
            int valStart = colonIdx + 1;
            while (valStart < json.Length && char.IsWhiteSpace(json[valStart])) valStart++;
            if (valStart >= json.Length) return null;

            if (json[valStart] == '"')
            {
                int valEnd = json.IndexOf('"', valStart + 1);
                if (valEnd < 0) return null;
                return json.Substring(valStart + 1, valEnd - valStart - 1);
            }
            int end = valStart;
            while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != ']') end++;
            return json.Substring(valStart, end - valStart).Trim();
        }

        private static int ParsePhysicalPoints(string json)
        {
            int searchIdx = 0;
            while (true)
            {
                int dimIdx = json.IndexOf("\"id_point_dimension\"", searchIdx, StringComparison.Ordinal);
                if (dimIdx < 0) break;
                int objEnd = json.IndexOf('}', dimIdx);
                if (objEnd < 0) break;

                string objSlice = json.Substring(dimIdx, objEnd - dimIdx + 1);
                string dimVal = ExtractJsonString(objSlice, "id_point_dimension");

                if (dimVal == "2") // ID 2 = Dimensión Física USACH
                {
                    string balStr = ExtractJsonString(objSlice, "balance");
                    if (int.TryParse(balStr, out int balance)) return balance;
                }
                searchIdx = objEnd + 1;
            }
            return 0;
        }

        private Text CreateLabel(Transform parent, string text, Vector2 pos, int size, Color col)
        {
            GameObject go = new GameObject("TextLabel");
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

        private InputField CreateInputField(Transform parent, string title, string placeholderText, float yPos, bool isPass = false)
        {
            GameObject go = new GameObject("InputField_" + title);
            go.transform.SetParent(parent, false);
            Image img = go.AddComponent<Image>();
            img.color = new Color(0.18f, 0.19f, 0.24f);

            InputField input = go.AddComponent<InputField>();
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(260f, 34f);
            rt.anchoredPosition = new Vector2(0f, yPos);

            // Texto de sugerencia (Placeholder)
            GameObject placeholderGo = new GameObject("Placeholder");
            placeholderGo.transform.SetParent(go.transform, false);
            Text placeholderTextComp = placeholderGo.AddComponent<Text>();
            placeholderTextComp.font = GetFont();
            placeholderTextComp.text = placeholderText;
            placeholderTextComp.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            placeholderTextComp.fontSize = 11;
            placeholderTextComp.alignment = TextAnchor.MiddleLeft;
            RectTransform placeholderRt = placeholderGo.GetComponent<RectTransform>();
            placeholderRt.anchorMin = Vector2.zero;
            placeholderRt.anchorMax = Vector2.one;
            placeholderRt.sizeDelta = new Vector2(-16f, -4f);

            // Texto de entrada de usuario real
            GameObject textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            Text t = textGo.AddComponent<Text>();
            t.font = GetFont();
            t.color = Color.white;
            t.fontSize = 12;
            t.alignment = TextAnchor.MiddleLeft;
            RectTransform textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = new Vector2(-16f, -4f);

            input.placeholder = placeholderTextComp;
            input.textComponent = t;
            if (isPass) input.inputType = InputField.InputType.Password;

            return input;
        }
    }
}