using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;

namespace VitaSync
{
    /// <summary>
    /// Panel de canje LS-G en la tienda nativa de R.E.P.O.
    /// Se despliega exclusivamente en escenas Level - Shop [nombre].
    /// Aplica upgrades vía PunManager y descuenta puntos en el cloud.
    /// </summary>
    public class ShopCanjePanel : MonoBehaviour
    {
        private static ShopCanjePanel _instance;

        private GameObject _canvasGO;
        private Text _balanceText;
        private Text _slotsText;
        private Button[] _attributeButtons;
        private Text[] _attributeLabels;

        private LifeSyncClient.PhysicalProfile _profile;

        private static readonly string[] ATTR_NAMES =
            { "Stamina", "Grip", "Health", "Speed" };

        // ── CICLO DE VIDA ─────────────────────────────────────────────
        public static void EnsureInstance(
            LifeSyncClient.PhysicalProfile profile)
        {
            if (_instance != null)
            {
                _instance.Refresh(profile);
                return;
            }
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
            VitaSyncPlugin.Log.LogInfo(
                "[P5-Shop] Panel de canje destruido de forma limpia.");
        }

        // ── CONSTRUCCIÓN ──────────────────────────────────────────────
        private void Build(LifeSyncClient.PhysicalProfile profile)
        {
            _profile = profile;

            _canvasGO = new GameObject("VitaSync_CanjeCanvas");
            DontDestroyOnLoad(_canvasGO);

            Canvas canvas = _canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 3000;
            _canvasGO.AddComponent<CanvasScaler>();
            _canvasGO.AddComponent<GraphicRaycaster>();

            // Panel derecho
            GameObject panel = new GameObject("RightPanel");
            panel.transform.SetParent(_canvasGO.transform, false);
            panel.AddComponent<Image>().color =
                new Color(0.06f, 0.06f, 0.1f, 0.96f);

            RectTransform rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-20f, 0f);
            rt.sizeDelta = new Vector2(230f, 320f);

            CreateLabel(panel.transform, "LifeSync-Games Store",
                new Vector2(0f, 130f), 13, Color.cyan);

            _balanceText = CreateLabel(panel.transform, "",
                new Vector2(0f, 105f), 11, new Color(0.2f, 1f, 0.4f));

            _slotsText = CreateLabel(panel.transform, "",
                new Vector2(0f, 85f), 11, Color.white);

            // Botón cerrar
            GameObject closeGo = new GameObject("Btn_Close");
            closeGo.transform.SetParent(panel.transform, false);
            closeGo.AddComponent<Image>().color = new Color(0.5f, 0.1f, 0.1f);
            Button closeBtn = closeGo.AddComponent<Button>();
            RectTransform closeRt = closeGo.GetComponent<RectTransform>();
            closeRt.sizeDelta = new Vector2(180f, 26f);
            closeRt.anchoredPosition = new Vector2(0f, -130f);
            CreateLabel(closeGo.transform,
                "OCULTAR TIENDA", Vector2.zero, 10, Color.white);
            closeBtn.onClick.AddListener(DestroyInstance);

            // Botones de atributo
            _attributeButtons = new Button[4];
            _attributeLabels = new Text[4];

            for (int i = 0; i < 4; i++)
            {
                float yPos = 40f - (i * 42f);
                int idx = i;

                GameObject btnGo = new GameObject("Btn_" + ATTR_NAMES[i]);
                btnGo.transform.SetParent(panel.transform, false);
                btnGo.AddComponent<Image>().color =
                    new Color(0.12f, 0.16f, 0.24f);

                Button btn = btnGo.AddComponent<Button>();
                RectTransform btnRt = btnGo.GetComponent<RectTransform>();
                btnRt.sizeDelta = new Vector2(200f, 34f);
                btnRt.anchoredPosition = new Vector2(0f, yPos);

                Text lbl = CreateLabel(btnGo.transform,
                    "", Vector2.zero, 11, Color.white);

                btn.onClick.AddListener(() => OnRedeemClick(idx));

                _attributeButtons[i] = btn;
                _attributeLabels[i] = lbl;
            }

            RefreshUI();
            VitaSyncPlugin.Log.LogInfo("[P5-Shop] Panel de canje construido.");
        }

        // ── LÓGICA DE CANJE ───────────────────────────────────────────
        private void OnRedeemClick(int idx)
        {
            if (_profile == null) return;

            int cost = idx == 0 ? _profile.CostoStamina :
                       idx == 1 ? _profile.CostoGrip :
                       idx == 2 ? _profile.CostoHealth :
                                  _profile.CostoSpeed;

            if (!_profile.PuedePagar(cost))
            {
                VitaSyncPlugin.Log.LogWarning(
                    "[Shop] Canje denegado: pts=" + _profile.Puntos +
                    " costo=" + cost +
                    " slots=" + _profile.CanjesUsados +
                    "/" + _profile.CanjesMax);
                return;
            }

            // Aplicar upgrade vía PunManager (sincroniza en multijugador)
            PlayerController ctrl = PlayerController.instance;
            if (ctrl != null && ctrl.playerAvatarScript != null &&
                PunManager.instance != null)
            {
                string steamID =
                    SemiFunc.PlayerGetSteamID(ctrl.playerAvatarScript);

                if (!string.IsNullOrEmpty(steamID))
                {
                    switch (idx)
                    {
                        case 0:
                            PunManager.instance.UpgradePlayerEnergy(
                                steamID, 1);
                            break;
                        case 1:
                            PunManager.instance.UpgradePlayerGrabStrength(
                                steamID, 1);
                            break;
                        case 2:
                            PunManager.instance.UpgradePlayerHealth(
                                steamID, 1);
                            break;
                        case 3:
                            PunManager.instance.UpgradePlayerSprintSpeed(
                                steamID, 1);
                            break;
                    }
                    VitaSyncPlugin.Log.LogInfo(
                        "[Shop] Upgrade aplicado: " + ATTR_NAMES[idx] +
                        " (steamID=" + steamID + ")");
                }
            }
            else
            {
                VitaSyncPlugin.Log.LogWarning(
                    "[Shop] No se pudo aplicar upgrade: " +
                    "PlayerController o PunManager no disponibles.");
            }

            // Descuento local inmediato
            _profile.Puntos -= cost;
            _profile.CanjesUsados++;

            // Descuento en el cloud (asíncrono, no bloquea la UI)
            if (SessionManager.IsActive)
            {
                StartCoroutine(EnviarDescuento(
                    SessionManager.PlayerId,
                    SessionManager.BearerToken,
                    cost,
                    ATTR_NAMES[idx]));
            }
            else
            {
                VitaSyncPlugin.Log.LogWarning(
                    "[Shop] Sin sesión activa. " +
                    "Descuento solo aplicado localmente.");
            }

            RefreshUI();
        }

        private IEnumerator EnviarDescuento(
            int playerId, string token, int cantidad, string atributo)
        {
            // CORRECCIÓN CONTRATO: Endpoint POST verificado de ajuste manual de puntos
            string url = VitaSyncPlugin.CORE_URL + "/players/" + playerId + "/points/adjust";

            // Construcción manual del JSON crudo para evitar dependencias externas de librerías
            // Dimensión: 2 (Físico), Dirección: DEBIT (Resta), Videogame: 22 (REPO)
            string jsonBody = "{" +
                "\"point_dimension_id\":2," +
                "\"direction\":\"DEBIT\"," +
                "\"amount\":" + cantidad + "," +
                "\"reason\":\"Mejora VitaSync: " + atributo + "\"," +
                "\"videogame_id\":22" +
                "}";

            using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();

                req.SetRequestHeader("Authorization", "Bearer " + token);
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Accept", "application/json");
                req.timeout = 15;

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    VitaSyncPlugin.Log.LogInfo(
                        "[LSG] Cloud ajustado con éxito (/points/adjust). Descontados: " +
                        cantidad + " pts para " + atributo + " (Game 22). Respuesta: " + req.downloadHandler.text);
                }
                else
                {
                    VitaSyncPlugin.Log.LogError(
                        "[LSG] Error al ajustar puntos en cloud (" + req.responseCode + "): " +
                        req.error + " | Payload enviado: " + jsonBody);
                }
            }
        }

        // ── UI ────────────────────────────────────────────────────────
        public void Refresh(LifeSyncClient.PhysicalProfile profile)
        {
            _profile = profile;
            RefreshUI();
        }

        private void RefreshUI()
        {
            if (_profile == null) return;

            _balanceText.text = "Puntos: " + _profile.Puntos;
            _slotsText.text = "Canjes: " + _profile.CanjesUsados +
                                "/" + _profile.CanjesMax;

            int[] costs =
            {
                _profile.CostoStamina,
                _profile.CostoGrip,
                _profile.CostoHealth,
                _profile.CostoSpeed
            };

            for (int i = 0; i < 4; i++)
            {
                bool puede = _profile.Puntos >= costs[i] &&
                             _profile.CanjesUsados < _profile.CanjesMax;
                _attributeLabels[i].text =
                    ATTR_NAMES[i] + " (" + costs[i] + " pts)";
                _attributeButtons[i].interactable = puede;
            }
        }

        private void Update()
        {
            if (_canvasGO != null && _canvasGO.activeInHierarchy)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void OnDestroy()
        {
            if (_canvasGO != null) Destroy(_canvasGO);
        }

        private static Font GetFont()
        {
            Font f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (f == null)
            {
                Font[] all = Resources.FindObjectsOfTypeAll<Font>();
                if (all != null && all.Length > 0) f = all[0];
            }
            return f;
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
            rt.sizeDelta = new Vector2(210f, 20f);
            return t;
        }
    }
}