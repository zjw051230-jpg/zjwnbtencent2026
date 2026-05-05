using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

public class AccessKeySetupController : MonoBehaviour
{
    [Serializable]
    private class DoubaoRuntimeConfigRequest
    {
        public string accessKey;
    }

    [Header("UI")]
    public GameObject setupPanel;
    public TMP_InputField accessKeyInput;
    public Button startRealVoiceButton;
    public Button skipDemoButton;
    public TMP_Text statusText;

    [Header("Flow")]
    public OpeningQuestionController openingQuestionController;
    public string apiBaseUrl = "http://localhost:3000";
    public float requestTimeoutSeconds = 8f;

    private bool flowStarted;
    private TMP_FontAsset fontAsset;

    private void Awake()
    {
        ResolveOpeningQuestionController();
        ResolveFontAsset();
        EnsureEventSystem();
        EnsureSetupPanel();
        BindButtons();
    }

    private void Start()
    {
        if (setupPanel != null)
        {
            setupPanel.SetActive(true);
        }

        SetStatus("如果配置失败，将自动进入 Demo 模式");
    }

    private void ResolveOpeningQuestionController()
    {
        if (openingQuestionController == null)
        {
            openingQuestionController = FindObjectOfType<OpeningQuestionController>();
        }

        if (openingQuestionController != null && string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            apiBaseUrl = openingQuestionController.apiBaseUrl;
        }
    }

    private void ResolveFontAsset()
    {
        if (openingQuestionController != null)
        {
            if (openingQuestionController.loadingText != null)
            {
                fontAsset = openingQuestionController.loadingText.font;
            }
            else if (openingQuestionController.progressText != null)
            {
                fontAsset = openingQuestionController.progressText.font;
            }
        }
    }

    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(eventSystem);
        }
    }

    private void EnsureSetupPanel()
    {
        if (setupPanel != null && accessKeyInput != null && startRealVoiceButton != null && skipDemoButton != null && statusText != null)
        {
            return;
        }

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
        }

        setupPanel = new GameObject("AccessKeySetupPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        setupPanel.transform.SetParent(canvas.transform, false);

        RectTransform panelRect = setupPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image panelImage = setupPanel.GetComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.92f);

        TMP_Text titleText = CreateText("TitleText", setupPanel.transform, "语音服务配置", 34, new Vector2(0f, 190f), new Vector2(620f, 60f));
        titleText.alignment = TextAlignmentOptions.Center;

        accessKeyInput = CreateInputField(setupPanel.transform);
        startRealVoiceButton = CreateButton("StartRealVoiceButton", setupPanel.transform, "开始真实语音模式", new Vector2(0f, -20f));
        skipDemoButton = CreateButton("SkipDemoButton", setupPanel.transform, "跳过，使用 Demo 模式", new Vector2(0f, -90f));

        TMP_Text hintText = CreateText("HintText", setupPanel.transform, "如果配置失败，将自动进入 Demo 模式", 22, new Vector2(0f, -155f), new Vector2(720f, 42f));
        hintText.alignment = TextAlignmentOptions.Center;

        statusText = CreateText("StatusText", setupPanel.transform, "", 20, new Vector2(0f, -205f), new Vector2(760f, 46f));
        statusText.alignment = TextAlignmentOptions.Center;
    }

    private TMP_InputField CreateInputField(Transform parent)
    {
        GameObject inputObject = new GameObject("AccessKeyInputField", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
        inputObject.transform.SetParent(parent, false);

        RectTransform inputRect = inputObject.GetComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0.5f, 0.5f);
        inputRect.anchorMax = new Vector2(0.5f, 0.5f);
        inputRect.anchoredPosition = new Vector2(0f, 95f);
        inputRect.sizeDelta = new Vector2(620f, 54f);

        Image image = inputObject.GetComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.96f);

        TMP_Text text = CreateText("Text", inputObject.transform, "", 22, Vector2.zero, new Vector2(580f, 44f));
        text.color = new Color(0.08f, 0.08f, 0.08f, 1f);
        text.alignment = TextAlignmentOptions.MidlineLeft;

        TMP_Text placeholder = CreateText("Placeholder", inputObject.transform, "请输入豆包 Access Key", 22, Vector2.zero, new Vector2(580f, 44f));
        placeholder.color = new Color(0.42f, 0.42f, 0.42f, 1f);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;

        TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
        input.textComponent = text;
        input.placeholder = placeholder;
        input.contentType = TMP_InputField.ContentType.Password;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = 0;
        return input;
    }

    private Button CreateButton(string name, Transform parent, string label, Vector2 position)
    {
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(360f, 52f);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.92f, 0.92f, 0.92f, 1f);

        TMP_Text text = CreateText("Text", buttonObject.transform, label, 22, Vector2.zero, new Vector2(340f, 42f));
        text.color = new Color(0.08f, 0.08f, 0.08f, 1f);
        text.alignment = TextAlignmentOptions.Center;

        return buttonObject.GetComponent<Button>();
    }

    private TMP_Text CreateText(string name, Transform parent, string value, int fontSize, Vector2 position, Vector2 size)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        TMP_Text text = textObject.GetComponent<TMP_Text>();
        text.text = value;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;

        if (fontAsset != null)
        {
            text.font = fontAsset;
        }

        return text;
    }

    private void BindButtons()
    {
        if (startRealVoiceButton != null)
        {
            startRealVoiceButton.onClick.RemoveListener(OnStartRealVoiceClicked);
            startRealVoiceButton.onClick.AddListener(OnStartRealVoiceClicked);
        }

        if (skipDemoButton != null)
        {
            skipDemoButton.onClick.RemoveListener(OnSkipDemoClicked);
            skipDemoButton.onClick.AddListener(OnSkipDemoClicked);
        }
    }

    private void OnStartRealVoiceClicked()
    {
        if (flowStarted)
        {
            return;
        }

        string accessKey = accessKeyInput != null ? accessKeyInput.text : "";
        if (string.IsNullOrWhiteSpace(accessKey))
        {
            SetStatus("未输入 Access Key，将使用 Demo 模式");
            ContinueOpeningFlow();
            return;
        }

        StartCoroutine(SendRuntimeConfig(accessKey));
    }

    private void OnSkipDemoClicked()
    {
        if (flowStarted)
        {
            return;
        }

        SetStatus("已跳过配置，使用 Demo 模式");
        ContinueOpeningFlow();
    }

    private IEnumerator SendRuntimeConfig(string accessKey)
    {
        SetButtonsInteractable(false);
        SetStatus("正在配置语音服务...");

        string endpoint = BuildApiUrl("/api/runtime-config/doubao");
        DoubaoRuntimeConfigRequest payload = new DoubaoRuntimeConfigRequest
        {
            accessKey = accessKey
        };

        string json = JsonUtility.ToJson(payload);
        byte[] body = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = Mathf.Max(1, Mathf.CeilToInt(requestTimeoutSeconds));

            Debug.Log("AccessKeySetupController: sending runtime config request to " + endpoint);
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                SetStatus("语音服务配置成功");
                ContinueOpeningFlow();
                yield break;
            }

            if (request.result == UnityWebRequest.Result.ConnectionError && request.error != null && request.error.ToLowerInvariant().Contains("timeout"))
            {
                SetStatus("配置超时，已进入 Demo 模式");
            }
            else
            {
                SetStatus("配置失败，已进入 Demo 模式");
            }

            ContinueOpeningFlow();
        }
    }

    private string BuildApiUrl(string path)
    {
        string baseUrl = string.IsNullOrWhiteSpace(apiBaseUrl) ? "http://localhost:3000" : apiBaseUrl.Trim().TrimEnd('/');
        string normalizedPath = path.StartsWith("/") ? path : "/" + path;
        return baseUrl + normalizedPath;
    }

    private void ContinueOpeningFlow()
    {
        if (flowStarted)
        {
            return;
        }

        flowStarted = true;

        if (setupPanel != null)
        {
            setupPanel.SetActive(false);
        }

        if (openingQuestionController != null)
        {
            openingQuestionController.BeginOpeningFlowFromAccessKeySetup();
        }
        else
        {
            Debug.LogError("AccessKeySetupController: OpeningQuestionController is not assigned.");
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (startRealVoiceButton != null)
        {
            startRealVoiceButton.interactable = interactable;
        }

        if (skipDemoButton != null)
        {
            skipDemoButton.interactable = interactable;
        }
    }
}
