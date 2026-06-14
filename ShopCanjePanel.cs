using UnityEngine;
using UnityEngine.UI;

namespace VitaSync
{
    /// <summary>
    /// Panel de canje que se despliega en la tienda de R.E.P.O.
    /// Muestra el balance disponible, costos y maneja los eventos de click.
    /// </summary>
    public class ShopCanjePanel : MonoBehaviour
    {
        private static ShopCanjePanel _instance;

        // Propiedades de acceso para el Singleton
        public static ShopCanjePanel Instance => _instance;

        public static void EnsureInstance(LifeSyncClient.PhysicalProfile profile)
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
            Destroy(_instance.gameObject);
            _instance = null;
        }

        // Elementos de la interfaz gráfica
        private GameObject _canvasGO;
        private Text _headerText;
        private Text _balanceText;
        private Text _slotsText;
        private Button[] _attributeButtons;
        private Text[] _attributeLabels;

        private LifeSyncClient.PhysicalProfile _currentProfile;
        private static readonly string[] ATTRIBUTE_NAMES = { "Stamina", "Grip", "Health", "Speed" };

        private static BepInEx.Logging.ManualLogSource Log => VitaSyncPlugin.Log;

        private void Build(LifeSyncClient.PhysicalProfile profile)
        {
            _currentProfile = profile;

            try
            {
                // Crear el Canvas raíz sobre la pantalla
                _canvasGO = new GameObject("VitaSync_CanjeCanvas");
                DontDestroyOnLoad(_canvasGO);
                Canvas canvas = _canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 200;
                _canvasGO.AddComponent<CanvasScaler>();
                _canvasGO.AddComponent<GraphicRaycaster>();

                // Crear el contenedor del panel (Fondo oscuro)
                GameObject panel = CreatePanel(_canvasGO.transform,
                    new Vector2(1f, 0f),    // Ancla inferior derecha
                    new Vector2(1f, 0f),    // Pivot
                    new Vector2(-10f, 10f), // Margen de desfase
                    new Vector2(220f, 260f));

                // Texto del Encabezado
                _headerText = CreateLabel(panel.transform,
                    "LifeSync-Games Store",
                    new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0f, -30f), new Vector2(0f, 0f),
                    13, TextAnchor.MiddleCenter, Color.cyan);

                // Texto del Balance de Puntos
                _balanceText = CreateLabel(panel.transform,
                    "",
                    new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0f, -52f), new Vector2(0f, -32f),
                    11, TextAnchor.MiddleCenter, new Color(0.2f, 1f, 0.4f));

                // Texto de Slots Usados
                _slotsText = CreateLabel(panel.transform,
                    "",
                    new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0f, -70f), new Vector2(0f, -52f),
                    11, TextAnchor.MiddleCenter, Color.white);

                // Línea divisoria estética
                CreateLabel(panel.transform,
                    "────────────────",
                    new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0f, -82f), new Vector2(0f, -70f),
                    10, TextAnchor.MiddleCenter, new Color(0.4f, 0.4f, 0.4f));

                // Inicializar vectores de botones y etiquetas
                _attributeButtons = new Button[4];
                _attributeLabels = new Text[4];

                int[] costs = {
                    profile.CostoStamina,
                    profile.CostoGrip,
                    profile.CostoHealth,
                    profile.CostoSpeed
                };

                // Construcción iterativa de la botonera
                for (int i = 0; i < 4; i++)
                {
                    float yTop = -88f - (i * 42f);
                    int index = i; // Captura de índice para la expresión lambda en C# 7.3

                    var targetBtn = CreateAttributeButton(
                        panel.transform,
                        ATTRIBUTE_NAMES[i], costs[i],
                        yTop,
                        () => OnRedeemClick(index));

                    _attributeButtons[i] = targetBtn.btn;
                    _attributeLabels[i] = targetBtn.lbl;
                }

                _canvasGO.SetActive(true);
                RefreshUI();
                Log.LogInfo("[P3-UI] Panel de interfaz gráfica construido exitosamente.");
            }
            catch (System.Exception ex)
            {
                Log.LogError("[P3-UI] Error crítico construyendo la interfaz: " + ex.Message);
            }
        }

        private void OnRedeemClick(int attributeIndex)
        {
            if (_currentProfile == null) return;

            // Determinar costo usando estructuras condicionales tradicionales de C# 7.3
            int selectedCost = 999;
            if (attributeIndex == 0) selectedCost = _currentProfile.CostoStamina;
            else if (attributeIndex == 1) selectedCost = _currentProfile.CostoGrip;
            else if (attributeIndex == 2) selectedCost = _currentProfile.CostoHealth;
            else if (attributeIndex == 3) selectedCost = _currentProfile.CostoSpeed;

            if (!_currentProfile.PuedePagar(selectedCost))
            {
                Log.LogWarning("[P3-UI] Intento de canje rechazado. Saldo o slots insuficientes.");
                return;
            }

            // Simulación del flujo de descuento para el prototipo P3
            _currentProfile.CanjesUsados++;
            Log.LogInfo("[P3-UI] Botón presionado de forma exitosa: " + ATTRIBUTE_NAMES[attributeIndex] + ". Procesando evento...");

            RefreshUI();
        }

        public void Refresh(LifeSyncClient.PhysicalProfile profile)
        {
            _currentProfile = profile;
            if (_canvasGO != null) _canvasGO.SetActive(true);
            RefreshUI();
        }

        private void RefreshUI()
        {
            if (_currentProfile == null) return;

            _balanceText.text = "Puntos disponibles: " + _currentProfile.Puntos;
            _slotsText.text = "Mejoras: " + _currentProfile.CanjesUsados + "/" + _currentProfile.CanjesMax;

            int[] costs = {
                _currentProfile.CostoStamina,
                _currentProfile.CostoGrip,
                _currentProfile.CostoHealth,
                _currentProfile.CostoSpeed
            };

            for (int i = 0; i < 4; i++)
            {
                bool canAfford = _currentProfile.PuedePagar(costs[i]);
                _attributeLabels[i].text = ATTRIBUTE_NAMES[i] + "  [" + costs[i] + " pts]";
                _attributeLabels[i].color = canAfford ? new Color(0.2f, 1f, 0.4f) : new Color(0.5f, 0.5f, 0.5f);
                _attributeButtons[i].interactable = canAfford;
            }
        }

        public void Hide()
        {
            if (_canvasGO != null) _canvasGO.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_canvasGO != null) Destroy(_canvasGO);
        }

        // ── MÉTODOS AUXILIARES (HELPERS) DE CONSTRUCCIÓN GRÁFICA ──

        private static GameObject CreatePanel(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            GameObject go = new GameObject("PanelBackground");
            go.transform.SetParent(parent, false);
            Image img = go.AddComponent<Image>();
            img.color = new Color(0.05f, 0.05f, 0.1f, 0.94f); // Fondo azul marino traslúcido
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = anchorMin;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            return go;
        }

        private static Text CreateLabel(Transform parent, string text, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, int fontSize, TextAnchor anchor, Color color)
        {
            GameObject go = new GameObject("UILabel");
            go.transform.SetParent(parent, false);
            Text lbl = go.AddComponent<Text>();
            lbl.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            lbl.fontSize = fontSize;
            lbl.color = color;
            lbl.alignment = anchor;
            lbl.text = text;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return lbl;
        }

        // Estructura de retorno compatible con C# 7.3 para el empaquetado del botón
        private struct ButtonPair
        {
            public Button btn;
            public Text lbl;
            public ButtonPair(Button b, Text l) { btn = b; lbl = l; }
        }

        private static ButtonPair CreateAttributeButton(Transform parent, string name, int cost, float yTop, UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = new GameObject("Btn_" + name);
            go.transform.SetParent(parent, false);
            Image img = go.AddComponent<Image>();
            img.color = new Color(0.1f, 0.15f, 0.25f, 0.95f);
            Button btn = go.AddComponent<Button>();
            btn.onClick.AddListener(onClick);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.05f, 1f);
            rt.anchorMax = new Vector2(0.95f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, yTop);
            rt.sizeDelta = new Vector2(0f, 34f);

            Text lbl = CreateLabel(go.transform, name + "  [" + cost + " pts]", Vector2.zero, Vector2.one, new Vector2(4f, 2f), new Vector2(-4f, -2f), 11, TextAnchor.MiddleCenter, new Color(0.2f, 1f, 0.4f));

            return new ButtonPair(btn, lbl);
        }
    }
}