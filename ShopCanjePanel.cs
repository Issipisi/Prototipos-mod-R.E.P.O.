using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;

namespace VitaSync
{
    /// <summary>
    /// Panel de canje LSG — P7 rediseño estético.
    /// Paleta REPO industrial: grises cálidos, café oscuro, amarillo apagado.
    /// Fixes: bloque PTS alineado correctamente, botones subidos dentro del panel.
    /// </summary>
    public class ShopCanjePanel : MonoBehaviour
    {
        private static ShopCanjePanel _instance;

        private GameObject _canvasGO;
        private Text _balanceText;
        private Text _slotsText;
        private Button[] _attrButtons;
        private Text[] _attrNameLabels;
        private Text[] _attrPriceLabels;
        private Image[] _attrRowImages;
        private Text _processingText;

        private LifeSyncClient.PhysicalProfile _profile;
        private bool _syncInProgress = false;

        private static readonly string[] ATTR_NAMES =
            { "STAMINA", "GRIP", "HEALTH", "SPEED" };

        private static readonly string[] PROC = { "", ".", "..", "..." };
        private int _procIdx = 0;
        private float _procTimer = 0f;
        private const float PROC_DT = 0.3f;

        // ── Paleta REPO industrial ─────────────────────────────────────
        // Fondo: gris oscuro cálido
        private static readonly Color C_BG = new Color(0.13f, 0.11f, 0.09f, 0.93f);
        // Cabecera: café oscuro
        private static readonly Color C_HDR = new Color(0.18f, 0.14f, 0.10f, 0.97f);
        // Amarillo REPO apagado
        private static readonly Color C_GOLD = new Color(0.78f, 0.60f, 0.18f, 1f);
        // Texto principal: gris claro cálido
        private static readonly Color C_TEXT = new Color(0.80f, 0.76f, 0.70f, 1f);
        // Texto secundario
        private static readonly Color C_DIM = new Color(0.48f, 0.44f, 0.38f, 1f);
        // Blanco cálido para valores destacados
        private static readonly Color C_WHITE = new Color(0.94f, 0.90f, 0.84f, 1f);
        // Fila disponible: gris cálido sutil
        private static readonly Color C_ROW_ON = new Color(0.20f, 0.17f, 0.13f, 1f);
        // Fila bloqueada: gris oscuro cálido
        private static readonly Color C_ROW_OFF = new Color(0.12f, 0.10f, 0.08f, 1f);
        // Texto bloqueado
        private static readonly Color C_LOCKED = new Color(0.30f, 0.27f, 0.22f, 1f);
        // Precio disponible: amarillo REPO
        private static readonly Color C_PRICE_ON = new Color(0.78f, 0.60f, 0.18f, 1f);
        // Botón ocultar: rojo oscuro industrial
        private static readonly Color C_BTN_RED = new Color(0.32f, 0.08f, 0.06f, 1f);
        private static readonly Color C_BTN_RED_T = new Color(0.88f, 0.44f, 0.38f, 1f);
        // Botón cerrar sesión: café oscuro
        private static readonly Color C_BTN_BRN = new Color(0.26f, 0.16f, 0.06f, 1f);
        private static readonly Color C_BTN_BRN_T = new Color(0.88f, 0.64f, 0.28f, 1f);

        // ── Ciclo de vida ─────────────────────────────────────────────
        public static void EnsureInstance(LifeSyncClient.PhysicalProfile profile)
        {
            if (_instance != null) { _instance.Refresh(profile); return; }
            GameObject go = new GameObject("VitaSync_ShopPanel");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ShopCanjePanel>();
            _instance.Build(profile);
        }

        public static void DestroyInstance()
        {
            if (_instance == null) return;
            if (_instance._canvasGO != null) Destroy(_instance._canvasGO);
            Destroy(_instance.gameObject);
            _instance = null;
            VitaSyncPlugin.Log.LogInfo("[Shop] Panel destruido de forma limpia.");
        }

        private void Build(LifeSyncClient.PhysicalProfile profile)
        {
            _profile = profile;

            _canvasGO = new GameObject("VitaSync_CanjeCanvas");
            DontDestroyOnLoad(_canvasGO);
            Canvas cv = _canvasGO.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 3000;
            _canvasGO.AddComponent<CanvasScaler>();
            _canvasGO.AddComponent<GraphicRaycaster>();

            const float PW = 240f;
            const float PH = 400f;  // ligeramente más alto para que los botones quepan

            // Panel anclado a la derecha
            GameObject panel = new GameObject("Panel");
            panel.transform.SetParent(_canvasGO.transform, false);
            panel.AddComponent<Image>().color = C_BG;
            RectTransform pr = panel.GetComponent<RectTransform>();
            pr.anchorMin = new Vector2(1f, 0.5f);
            pr.anchorMax = new Vector2(1f, 0.5f);
            pr.pivot = new Vector2(1f, 0.5f);
            pr.sizeDelta = new Vector2(PW, PH);
            pr.anchoredPosition = new Vector2(-14f, 0f);

            // Borde izquierdo dorado
            MakeRect(panel.transform, "Accent",
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(3f, 0f), Vector2.zero, C_GOLD);

            // ── Cabecera ──────────────────────────────────────────────
            GameObject hdr = new GameObject("Hdr");
            hdr.transform.SetParent(panel.transform, false);
            hdr.AddComponent<Image>().color = C_HDR;
            RectTransform hr = hdr.GetComponent<RectTransform>();
            hr.anchorMin = new Vector2(0f, 1f);
            hr.anchorMax = new Vector2(1f, 1f);
            hr.pivot = new Vector2(0.5f, 1f);
            hr.sizeDelta = new Vector2(0f, 42f);
            hr.anchoredPosition = Vector2.zero;

            MakeRect(hdr.transform, "HdrLine",
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 2f), Vector2.zero, C_GOLD);

            MakeLbl(hdr.transform, "LIFESYNC STORE",
                new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(PW, 42f), 13, C_GOLD, true, TextAnchor.MiddleCenter);

            // ── Bloque PTS / CANJES ───────────────────────────────────
            // Layout calculado para distribuir el contenido uniformemente
            // en los 358px disponibles bajo la cabecera (Y=+158 a Y=-200).
            // Margen inferior: ~13px.
            const float INFO_Y = 126f;  // centro fila PTS — bajado
            const float CANJES_Y = 98f;  // centro fila CANJES
            const float SEP1_Y = 78f;  // separador superior
            const float SPIN_Y = 62f;  // spinner (h=16)

            // "PTS" — etiqueta pequeña izquierda
            MakeLbl(panel.transform, "PTS",
                new Vector2(0f, 0.5f), new Vector2(14f, INFO_Y),
                new Vector2(36f, 28f),
                9, C_DIM, false, TextAnchor.MiddleLeft);

            // Número grande — alineado a la derecha
            _balanceText = MakeLbl(panel.transform, "---",
                new Vector2(1f, 0.5f), new Vector2(-14f, INFO_Y),
                new Vector2(100f, 28f),
                22, C_WHITE, true, TextAnchor.MiddleRight);

            // "CANJES" — segunda fila
            MakeLbl(panel.transform, "CANJES",
                new Vector2(0f, 0.5f), new Vector2(14f, CANJES_Y),
                new Vector2(56f, 20f),
                9, C_DIM, false, TextAnchor.MiddleLeft);

            _slotsText = MakeLbl(panel.transform, "0/2",
                new Vector2(1f, 0.5f), new Vector2(-14f, CANJES_Y),
                new Vector2(50f, 20f),
                11, C_TEXT, true, TextAnchor.MiddleRight);

            // Separador superior filas
            MakeRect(panel.transform, "Sep1",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(PW - 18f, 1f), new Vector2(0f, SEP1_Y),
                new Color(C_GOLD.r, C_GOLD.g, C_GOLD.b, 0.30f));

            // Spinner sync
            _processingText = MakeLbl(panel.transform, "",
                new Vector2(0.5f, 0.5f), new Vector2(0f, SPIN_Y),
                new Vector2(PW - 20f, 16f),
                9, C_GOLD, false, TextAnchor.MiddleCenter);

            // ── Filas de atributo ─────────────────────────────────────
            const float RW = PW - 14f;
            const float RH = 34f;
            const float RY0 = 52f;   // STAMINA center
            const float RDY = 40f;

            int[] costs = {
                profile.CostoStamina, profile.CostoGrip,
                profile.CostoHealth,  profile.CostoSpeed
            };

            _attrButtons = new Button[4];
            _attrNameLabels = new Text[4];
            _attrPriceLabels = new Text[4];
            _attrRowImages = new Image[4];

            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                float ry = RY0 - i * RDY;

                GameObject row = new GameObject("Row_" + ATTR_NAMES[i]);
                row.transform.SetParent(panel.transform, false);
                Image rowImg = row.AddComponent<Image>();
                rowImg.color = C_ROW_OFF;

                Button btn = row.AddComponent<Button>();
                ColorBlock cb = ColorBlock.defaultColorBlock;
                cb.normalColor = Color.white;
                cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
                cb.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
                cb.disabledColor = Color.white;
                cb.colorMultiplier = 1f;
                btn.colors = cb; btn.targetGraphic = rowImg;

                RectTransform rr = row.GetComponent<RectTransform>();
                rr.anchorMin = new Vector2(0.5f, 0.5f);
                rr.anchorMax = new Vector2(0.5f, 0.5f);
                rr.sizeDelta = new Vector2(RW, RH);
                rr.anchoredPosition = new Vector2(4f, ry);

                Text nameT = MakeLbl(row.transform, ATTR_NAMES[i],
                    new Vector2(0f, 0.5f), new Vector2(10f, 0f),
                    new Vector2(120f, RH),
                    11, C_LOCKED, true, TextAnchor.MiddleLeft);

                Text priceT = MakeLbl(row.transform, costs[i] + " PTS",
                    new Vector2(1f, 0.5f), new Vector2(-10f, 0f),
                    new Vector2(72f, RH),
                    10, C_LOCKED, false, TextAnchor.MiddleRight);

                btn.onClick.AddListener(() => OnRedeemClick(idx));
                _attrButtons[i] = btn;
                _attrNameLabels[i] = nameT;
                _attrPriceLabels[i] = priceT;
                _attrRowImages[i] = rowImg;
            }

            // Separador inferior filas — posición fija calculada
            const float SEP2_Y = -93f;
            MakeRect(panel.transform, "Sep2",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(PW - 18f, 1f), new Vector2(0f, SEP2_Y),
                new Color(C_GOLD.r, C_GOLD.g, C_GOLD.b, 0.30f));

            // ── Botones de control ────────────────────────────────────
            // BTN1_Y=-119, BTN2_Y=-172: margen inferior ~13px
            const float BTN1_Y = -130f;
            const float BTN2_Y = -166f;  // 36px de separación (antes 53)

            MakeBtn(panel.transform, "BtnClose", "OCULTAR",
                new Vector2(4f, BTN1_Y), new Vector2(RW, 30f),
                C_BTN_RED, C_BTN_RED_T)
                .GetComponent<Button>().onClick.AddListener(DestroyInstance);

            MakeBtn(panel.transform, "BtnLogout", "CERRAR SESION",
                new Vector2(4f, BTN2_Y), new Vector2(RW, 30f),
                C_BTN_BRN, C_BTN_BRN_T)
                .GetComponent<Button>().onClick.AddListener(OnLogoutClick);

            RefreshUI();
            VitaSyncPlugin.Log.LogInfo("[Shop] Panel construido (P8).");
        }

        private void Update()
        {
            if (_canvasGO != null && _canvasGO.activeInHierarchy)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (_syncInProgress && _processingText != null)
            {
                _procTimer += Time.deltaTime;
                if (_procTimer >= PROC_DT)
                {
                    _procTimer = 0f;
                    _procIdx = (_procIdx + 1) % PROC.Length;
                    _processingText.text = "SINCRONIZANDO" + PROC[_procIdx];
                }
            }
        }

        private void OnLogoutClick()
        {
            SessionManager.Clear();
            VitaSyncPlugin.Instance.SetActiveProfile(null);
            DestroyInstance();
        }

        private void OnRedeemClick(int idx)
        {
            if (_profile == null || _syncInProgress) return;
            int cost = idx == 0 ? _profile.CostoStamina :
                       idx == 1 ? _profile.CostoGrip :
                       idx == 2 ? _profile.CostoHealth :
                                  _profile.CostoSpeed;
            if (!_profile.PuedePagar(cost)) return;

            // Solo registrar el upgrade en SessionManager.
            // El upgrade real en StatsManager ocurre en Level - Lobby
            // (ChangeLevel Prefix) donde RunIsShop()=false y el guard
            // de SetPlayerHealth no bloquea.
            // NO llamar PunManager aquí porque causaría duplicación.
            SessionManager.AddUpgrade(idx);
            VitaSyncPlugin.Log.LogInfo("[Shop] Upgrade registrado para Lobby: " + ATTR_NAMES[idx]);

            _profile.Puntos -= cost;
            _profile.CanjesUsados++;
            if (SessionManager.IsActive)
                StartCoroutine(EnviarDescuento(
                    SessionManager.PlayerId, SessionManager.BearerToken,
                    cost, ATTR_NAMES[idx]));
            RefreshUI();
        }

        private IEnumerator EnviarDescuento(
            int playerId, string token, int cantidad, string attr)
        {
            _syncInProgress = true;
            _procIdx = 0; _procTimer = 0f;
            _processingText.text = "SINCRONIZANDO";
            SetButtonsInteractable(false);

            string url = VitaSyncPlugin.CORE_URL +
                          "/players/" + playerId + "/points/adjust";
            string body = "{\"point_dimension_id\":2,\"direction\":\"DEBIT\"," +
                          "\"amount\":" + cantidad + ",\"reason\":\"VitaSync: " +
                          attr + "\",\"videogame_id\":22}";

            using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
            {
                byte[] raw = Encoding.UTF8.GetBytes(body);
                req.uploadHandler = new UploadHandlerRaw(raw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Authorization", "Bearer " + token);
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Accept", "application/json");
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                    VitaSyncPlugin.Log.LogInfo("[LSG] Cloud -" + cantidad + " pts.");
                else
                    VitaSyncPlugin.Log.LogError("[LSG] Error HTTP " + req.responseCode);
            }
            _syncInProgress = false;
            _processingText.text = "";
            SetButtonsInteractable(true);
            RefreshUI();
        }

        public void Refresh(LifeSyncClient.PhysicalProfile profile)
        { _profile = profile; RefreshUI(); }

        private void RefreshUI()
        {
            if (_profile == null) return;
            _balanceText.text = _profile.Puntos.ToString();
            _slotsText.text = _profile.CanjesUsados + "/" + _profile.CanjesMax;

            int[] costs = { _profile.CostoStamina, _profile.CostoGrip,
                            _profile.CostoHealth,  _profile.CostoSpeed };
            for (int i = 0; i < 4; i++)
            {
                bool puede = _profile.Puntos >= costs[i] &&
                             _profile.CanjesUsados < _profile.CanjesMax &&
                             !_syncInProgress;
                _attrButtons[i].interactable = puede;
                _attrRowImages[i].color = puede ? C_ROW_ON : C_ROW_OFF;
                _attrNameLabels[i].color = puede ? C_TEXT : C_LOCKED;
                _attrPriceLabels[i].color = puede ? C_PRICE_ON : C_LOCKED;
            }
        }

        private void SetButtonsInteractable(bool v)
        { foreach (var b in _attrButtons) if (b != null) b.interactable = v; }

        private void OnDestroy()
        { if (_canvasGO != null) Destroy(_canvasGO); }

        // ── Helpers ───────────────────────────────────────────────────
        private static Font GetFont() =>
            Resources.GetBuiltinResource<Font>("Arial.ttf");

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

        private static Text MakeLbl(Transform p, string text,
            Vector2 anchor, Vector2 pos, Vector2 size,
            int fs, Color col, bool bold, TextAnchor align)
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

        private static GameObject MakeBtn(Transform p, string name,
            string label, Vector2 pos, Vector2 size, Color bg, Color fg)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(p, false);
            Image img = go.AddComponent<Image>(); img.color = bg;
            Button btn = go.AddComponent<Button>();
            ColorBlock cb = ColorBlock.defaultColorBlock;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            cb.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            cb.disabledColor = Color.white; cb.colorMultiplier = 1f;
            btn.colors = cb; btn.targetGraphic = img;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size; rt.anchoredPosition = pos;
            GameObject lGo = new GameObject("L");
            lGo.transform.SetParent(go.transform, false);
            Text t = lGo.AddComponent<Text>(); t.font = GetFont();
            t.text = label; t.fontSize = 11; t.color = fg;
            t.fontStyle = FontStyle.Bold; t.alignment = TextAnchor.MiddleCenter;
            RectTransform lr = lGo.GetComponent<RectTransform>();
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.sizeDelta = Vector2.zero;
            return go;
        }
    }
}