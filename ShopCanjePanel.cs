using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;

namespace VitaSync
{
    public class ShopCanjePanel : MonoBehaviour
    {
        private static ShopCanjePanel _instance;
        private GameObject _canvasGO;
        private Text _balanceText;
        private Text _slotsText;
        private Button[] _attributeButtons;
        private Text[] _attributeLabels;
        private LifeSyncClient.PhysicalProfile _currentProfile;

        private static readonly string[] ATTRIBUTE_NAMES = { "Stamina", "Grip", "Health", "Speed" };

        public static void EnsureInstance(LifeSyncClient.PhysicalProfile profile)
        {
            if (_instance != null)
            {
                _instance.Refresh(profile);
                return;
            }
            GameObject go = new GameObject("VitaSync_ShopPanel");
            _instance = go.AddComponent<ShopCanjePanel>();
            _instance.Build(profile);
        }

        public static void DestroyInstance()
        {
            if (_instance != null)
            {
                if (_instance._canvasGO != null) Destroy(_instance._canvasGO);
                Destroy(_instance.gameObject);
                _instance = null;
                VitaSyncPlugin.Log.LogInfo("[P4-Shop] Interfaz de la tienda destruida de forma limpia.");
            }
        }

        private static Font GetFont()
        {
            Font f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (f == null)
            {
                Font[] fonts = Resources.FindObjectsOfTypeAll<Font>();
                if (fonts != null && fonts.Length > 0) f = fonts[0];
            }
            return f;
        }

        private void Build(LifeSyncClient.PhysicalProfile profile)
        {
            _currentProfile = profile;

            _canvasGO = new GameObject("VitaSync_CanjeCanvas");
            Canvas canvas = _canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 3000;

            _canvasGO.AddComponent<CanvasScaler>();
            _canvasGO.AddComponent<GraphicRaycaster>();

            GameObject panel = new GameObject("RightPanel");
            panel.transform.SetParent(_canvasGO.transform, false);
            Image img = panel.AddComponent<Image>();
            img.color = new Color(0.06f, 0.06f, 0.1f, 0.96f);

            RectTransform rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-20f, 0f);
            rt.sizeDelta = new Vector2(230f, 320f);

            CreateLabel(panel.transform, "LifeSync-Games Store", new Vector2(0f, 130f), 13, Color.cyan);
            _balanceText = CreateLabel(panel.transform, "", new Vector2(0f, 105f), 11, new Color(0.2f, 1f, 0.4f));
            _slotsText = CreateLabel(panel.transform, "", new Vector2(0f, 85f), 11, Color.white);

            GameObject closeGo = new GameObject("Btn_CloseShop");
            closeGo.transform.SetParent(panel.transform, false);
            Image closeImg = closeGo.AddComponent<Image>();
            closeImg.color = new Color(0.5f, 0.1f, 0.1f);
            Button closeBtn = closeGo.AddComponent<Button>();
            RectTransform closeRt = closeGo.GetComponent<RectTransform>();
            closeRt.sizeDelta = new Vector2(180f, 26f);
            closeRt.anchoredPosition = new Vector2(0f, -130f);
            CreateLabel(closeGo.transform, "OCULTAR TIENDA", Vector2.zero, 10, Color.white);
            closeBtn.onClick.AddListener(DestroyInstance);

            _attributeButtons = new Button[4];
            _attributeLabels = new Text[4];

            for (int i = 0; i < 4; i++)
            {
                float yTop = 40f - (i * 42f);
                int index = i;

                GameObject btnGo = new GameObject("Btn_" + ATTRIBUTE_NAMES[i]);
                btnGo.transform.SetParent(panel.transform, false);
                Image btnImg = btnGo.AddComponent<Image>();
                btnImg.color = new Color(0.12f, 0.16f, 0.24f);

                Button btn = btnGo.AddComponent<Button>();
                RectTransform btnRt = btnGo.GetComponent<RectTransform>();
                btnRt.sizeDelta = new Vector2(200f, 34f);
                btnRt.anchoredPosition = new Vector2(0f, yTop);

                Text lbl = CreateLabel(btnGo.transform, "", Vector2.zero, 11, Color.white);
                btn.onClick.AddListener(() => OnRedeemClick(index));

                _attributeButtons[i] = btn;
                _attributeLabels[i] = lbl;
            }

            RefreshUI();
        }

        private void OnRedeemClick(int attributeIndex)
        {
            int cost = attributeIndex == 0 ? _currentProfile.CostoStamina :
                       attributeIndex == 1 ? _currentProfile.CostoGrip :
                       attributeIndex == 2 ? _currentProfile.CostoHealth : _currentProfile.CostoSpeed;

            if (!_currentProfile.PuedePagar(cost)) return;

            // Transmitir mejoras a la infraestructura interna del juego original
            PlayerController controller = PlayerController.instance;
            if (controller != null && controller.playerAvatarScript != null && PunManager.instance != null)
            {
                string steamID = SemiFunc.PlayerGetSteamID(controller.playerAvatarScript);
                if (!string.IsNullOrEmpty(steamID))
                {
                    switch (attributeIndex)
                    {
                        case 0: PunManager.instance.UpgradePlayerEnergy(steamID, 1); break;
                        case 1: PunManager.instance.UpgradePlayerGrabStrength(steamID, 1); break;
                        case 2: PunManager.instance.UpgradePlayerHealth(steamID, 1); break;
                        case 3: PunManager.instance.UpgradePlayerSprintSpeed(steamID, 1); break;
                    }
                }
            }

            // Aplicar descuento local inmediato en memoria
            _currentProfile.Puntos -= cost;
            _currentProfile.CanjesUsados++;

            // --- NUEVO: EJECUTAR TRANSACCIÓN REAL HACIA EL BACKEND DE LA USACH ---
            SincronizarGastoConBackend(cost);

            RefreshUI();
        }

        public void Refresh(LifeSyncClient.PhysicalProfile profile)
        {
            _currentProfile = profile;
            RefreshUI();
        }

        private void RefreshUI()
        {
            if (_currentProfile == null) return;

            _balanceText.text = "Puntos: " + _currentProfile.Puntos;
            _slotsText.text = "Canjes: " + _currentProfile.CanjesUsados + "/" + _currentProfile.CanjesMax;

            int[] costs = { _currentProfile.CostoStamina, _currentProfile.CostoGrip, _currentProfile.CostoHealth, _currentProfile.CostoSpeed };

            for (int i = 0; i < 4; i++)
            {
                bool canAfford = _currentProfile.Puntos >= costs[i] && _currentProfile.CanjesUsados < _currentProfile.CanjesMax;
                _attributeLabels[i].text = ATTRIBUTE_NAMES[i] + " (" + costs[i] + " pts)";
                _attributeButtons[i].interactable = canAfford;
            }
        }

        private Text CreateLabel(Transform parent, string text, Vector2 pos, int size, Color col)
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

        private void Update()
        {
            if (_canvasGO != null && _canvasGO.activeInHierarchy)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        // --- SISTEMA DE COMUNICACIÓN ASÍNCRONA LSG USACH ---
        private void SincronizarGastoConBackend(int puntosRestados)
        {
            // Extraer de forma segura las variables de sesión del LoginHUDPanel usando Reflection simple
            string token = null;
            int playerId = -1;

            var loginHUDType = Type.GetType("VitaSync.LoginHUDPanel, VitaSync");
            if (loginHUDType != null)
            {
                var instanceField = loginHUDType.GetField("_instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                object hudInst = instanceField?.GetValue(null);
                if (hudInst != null)
                {
                    var tokenField = loginHUDType.GetField("_bearerToken", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    var idField = loginHUDType.GetField("_playerId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                    token = tokenField?.GetValue(hudInst) as string;
                    playerId = idField != null ? (int)idField.GetValue(hudInst) : -1;
                }
            }

            if (string.IsNullOrEmpty(token) || playerId < 0)
            {
                VitaSyncPlugin.Log.LogWarning("[LSG] Sincronización omitida: Faltan credenciales de sesión activa.");
                return;
            }

            StartCoroutine(EnviarTransaccionNegativa(playerId, token, puntosRestados));
        }

        private IEnumerator EnviarTransaccionNegativa(int playerId, string token, int cantidad)
        {
            // Endpoint estándar de transacciones para la infraestructura de LifeSync
            string url = $"{VitaSyncPlugin.CORE_URL}/players/{playerId}/points/dimensions/2/transactions";

            WWWForm form = new WWWForm();
            form.AddField("value", -cantidad); // El valor negativo impacta restando el balance global en la BD
            form.AddField("description", "Mejora VitaSync en R.E.P.O.");

            using (var req = UnityWebRequest.Post(url, form))
            {
                req.SetRequestHeader("Authorization", $"Bearer {token}");
                req.SetRequestHeader("Accept", "application/json");

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    VitaSyncPlugin.Log.LogInfo($"[LSG] Base de datos sincronizada con éxito. Servidor restó: {cantidad} pts.");
                }
                else
                {
                    VitaSyncPlugin.Log.LogError($"[LSG] Fallo al impactar balance en el servidor Core: {req.error}");
                }
            }
        }
    }
}