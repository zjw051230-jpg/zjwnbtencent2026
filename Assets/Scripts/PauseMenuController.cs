using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PauseMenuController : MonoBehaviour
{
    public GameObject pauseMenuPanel;
    public Transform menuButtonRoot;
    public Button menuButtonPrefab;
    public Button closeButton;
    public FMVDemoController fmvController;
    public OpeningQuestionController openingQuestionController;
    public VoiceDialogueController voiceDialogueController;

    private readonly List<Button> generatedButtons = new List<Button>();
    private bool isOpen;
    private TMP_FontAsset menuFontAsset;
    private Font unityMenuFont;

    private void Awake()
    {
        ResolveReferences();

        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(CloseMenu);
        }

        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(false);
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            ToggleMenu();
        }
    }

    public void ToggleMenu()
    {
        if (isOpen)
        {
            CloseMenu();
        }
        else
        {
            OpenMenu();
        }
    }

    public void OpenMenu()
    {
        ResolveReferences();

        if (pauseMenuPanel == null)
        {
            Debug.LogError("PauseMenuController: PauseMenuPanel is not assigned.");
            return;
        }

        if (fmvController == null)
        {
            Debug.LogError("PauseMenuController: FMVDemoController is not assigned.");
            return;
        }

        isOpen = true;
        pauseMenuPanel.SetActive(true);
        fmvController.PausePlaybackForMenu();
        if (openingQuestionController != null)
        {
            openingQuestionController.PauseOpeningFlowForMenu();
        }

        RebuildButtons();
    }

    public void CloseMenu()
    {
        ClearButtons();

        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(false);
        }

        isOpen = false;

        if (fmvController != null)
        {
            fmvController.ResumePlaybackFromMenu();
        }

        if (openingQuestionController != null)
        {
            openingQuestionController.ResumeOpeningFlowFromMenu();
        }
    }

    private void ResolveReferences()
    {
        ResolveMenuFonts();

        if (fmvController == null)
        {
            fmvController = FindObjectOfType<FMVDemoController>(true);
        }

        if (openingQuestionController == null)
        {
            openingQuestionController = FindObjectOfType<OpeningQuestionController>(true);
        }

        if (voiceDialogueController == null)
        {
            voiceDialogueController = FindObjectOfType<VoiceDialogueController>(true);
        }

        if (pauseMenuPanel == null)
        {
            pauseMenuPanel = GameObject.Find("PauseMenuPanel");
        }

        if (pauseMenuPanel == null)
        {
            BuildFallbackMenu();
        }

        if (pauseMenuPanel != null && menuButtonRoot == null)
        {
            Transform foundRoot = pauseMenuPanel.transform.Find("MenuButtonRoot");
            if (foundRoot != null)
            {
                menuButtonRoot = foundRoot;
            }
        }

        if (pauseMenuPanel != null && closeButton == null)
        {
            Transform foundClose = pauseMenuPanel.transform.Find("CloseButton");
            if (foundClose != null)
            {
                closeButton = foundClose.GetComponent<Button>();
            }
        }

        if (menuButtonPrefab == null)
        {
            GameObject prefabObject = Resources.Load<GameObject>("ChoiceButton");
            if (prefabObject != null)
            {
                menuButtonPrefab = prefabObject.GetComponent<Button>();
            }
        }
    }

    private void ResolveMenuFonts()
    {
        if (menuFontAsset == null)
        {
            TMP_Text[] tmpTexts = FindObjectsOfType<TMP_Text>(true);
            foreach (TMP_Text tmpText in tmpTexts)
            {
                if (tmpText != null && tmpText.font != null && tmpText.font.name.Contains("SIMFANG"))
                {
                    menuFontAsset = tmpText.font;
                    break;
                }
            }
        }

        if (unityMenuFont == null)
        {
            Text[] unityTexts = FindObjectsOfType<Text>(true);
            foreach (Text unityText in unityTexts)
            {
                if (unityText != null && unityText.font != null && unityText.font.name.Contains("SIMFANG"))
                {
                    unityMenuFont = unityText.font;
                    break;
                }
            }
        }
    }

    private void BuildFallbackMenu()
    {
        Canvas canvas = FindObjectOfType<Canvas>(true);
        if (canvas == null)
        {
            return;
        }

        pauseMenuPanel = new GameObject("PauseMenuPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        pauseMenuPanel.transform.SetParent(canvas.transform, false);

        RectTransform panelRect = pauseMenuPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image panelImage = pauseMenuPanel.GetComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.72f);

        TMP_Text titleText = CreateText("TitleText", pauseMenuPanel.transform, "Jump Menu", 38, new Vector2(0f, 250f), new Vector2(600f, 70f));
        titleText.alignment = TextAlignmentOptions.Center;

        GameObject rootObject = new GameObject("MenuButtonRoot", typeof(RectTransform), typeof(VerticalLayoutGroup));
        rootObject.transform.SetParent(pauseMenuPanel.transform, false);
        menuButtonRoot = rootObject.transform;

        RectTransform rootRect = rootObject.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.anchoredPosition = new Vector2(0f, 35f);
        rootRect.sizeDelta = new Vector2(700f, 300f);

        VerticalLayoutGroup layout = rootObject.GetComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 14f;

        closeButton = CreateButton("CloseButton", pauseMenuPanel.transform, "Close", new Vector2(0f, -270f), new Vector2(260f, 64f));
        pauseMenuPanel.SetActive(false);
    }

    private void RebuildButtons()
    {
        ClearButtons();

        if (menuButtonRoot == null)
        {
            Debug.LogError("PauseMenuController: MenuButtonRoot is not assigned.");
            return;
        }

        List<StoryNode> menuNodes = fmvController.GetMenuNodes();
        foreach (StoryNode node in menuNodes)
        {
            if (node == null || string.IsNullOrEmpty(node.id))
            {
                continue;
            }

            Button button = CreateMenuButton(node);
            generatedButtons.Add(button);

            string nodeId = node.id;
            button.onClick.AddListener(() =>
            {
                JumpToStoryNode(nodeId);
            });
        }

        AddDirectJumpButton("跳过问题，进入莫扎特故事线", "intro_Mozart");
        AddDirectJumpButton("跳过问题，进入爱因斯坦故事线", "intro_Einstein");
    }

    private void AddDirectJumpButton(string label, string nodeId)
    {
        Button button = CreateMenuButton(new StoryNode { id = nodeId, menuTitle = label });
        generatedButtons.Add(button);

        button.onClick.AddListener(() =>
        {
            JumpToStoryNode(nodeId);
        });
    }

    private void JumpToStoryNode(string nodeId)
    {
        CloseMenuForJump();
        if (fmvController != null)
        {
            fmvController.PlayNodeFromOutside(nodeId);
        }
    }

    private Button CreateMenuButton(StoryNode node)
    {
        Button button;
        if (menuButtonPrefab != null)
        {
            button = Instantiate(menuButtonPrefab, menuButtonRoot);
        }
        else
        {
            button = CreateButton("MenuButton", menuButtonRoot, "", Vector2.zero, new Vector2(700f, 70f));
        }

        button.gameObject.SetActive(true);
        button.interactable = true;
        button.onClick.RemoveAllListeners();
        ApplyButtonFont(button);

        RectTransform rectTransform = button.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.localScale = Vector3.one;
            rectTransform.sizeDelta = new Vector2(rectTransform.sizeDelta.x, 70f);
        }

        LayoutElement layoutElement = button.GetComponent<LayoutElement>();
        if (layoutElement == null)
        {
            layoutElement = button.gameObject.AddComponent<LayoutElement>();
        }

        layoutElement.minHeight = 70f;
        layoutElement.preferredHeight = 70f;
        layoutElement.flexibleHeight = 0f;

        string label = string.IsNullOrEmpty(node.menuTitle) ? node.id : node.menuTitle;
        SetButtonLabel(button, label);
        return button;
    }

    private Button CreateButton(string objectName, Transform parent, string label, Vector2 position, Vector2 size)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        RectTransform rectTransform = buttonObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = position;
        rectTransform.sizeDelta = size;

        Image image = buttonObject.GetComponent<Image>();
        image.color = Color.white;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;

        TMP_Text text = CreateText("Text (TMP)", buttonObject.transform, label, 24, Vector2.zero, size);
        text.color = Color.black;
        text.alignment = TextAlignmentOptions.Center;
        return button;
    }

    private TMP_Text CreateText(string objectName, Transform parent, string textValue, int fontSize, Vector2 position, Vector2 size)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);

        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = position;
        rectTransform.sizeDelta = size;

        TMP_Text text = textObject.GetComponent<TMP_Text>();
        text.text = textValue;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        if (menuFontAsset != null)
        {
            text.font = menuFontAsset;
        }
        return text;
    }

    private void CloseMenuForJump()
    {
        ClearButtons();

        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(false);
        }

        Time.timeScale = 1f;
        AudioListener.pause = false;
        isOpen = false;
    }

    private void CancelMenuPauseForJump()
    {
        if (fmvController != null)
        {
            fmvController.CancelMenuPauseForExternalJump();
        }

        if (openingQuestionController != null)
        {
            openingQuestionController.CancelMenuPauseForExternalJump();
        }
    }

    private void ClearUiBeforeJump()
    {
        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(false);
        }

        ClearButtons();

        if (openingQuestionController != null)
        {
            openingQuestionController.HideOpeningQuestionUIForExternalJump();
        }

        if (fmvController != null)
        {
            fmvController.HideChoiceUIForExternalJump();
        }

        if (voiceDialogueController != null)
        {
            voiceDialogueController.HideDialoguePanelForExternalJump();
        }
    }

    private void ClearButtons()
    {
        foreach (Button button in generatedButtons)
        {
            if (button != null)
            {
                Destroy(button.gameObject);
            }
        }

        generatedButtons.Clear();
    }

    private void SetButtonLabel(Button button, string label)
    {
        TMP_Text tmpText = button.GetComponentInChildren<TMP_Text>(true);
        if (tmpText != null)
        {
            if (menuFontAsset != null)
            {
                tmpText.font = menuFontAsset;
            }

            tmpText.text = label;
            return;
        }

        Text unityText = button.GetComponentInChildren<Text>(true);
        if (unityText != null)
        {
            if (unityMenuFont != null)
            {
                unityText.font = unityMenuFont;
            }

            unityText.text = label;
        }
    }

    private void ApplyButtonFont(Button button)
    {
        if (button == null)
        {
            return;
        }

        TMP_Text[] tmpTexts = button.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text tmpText in tmpTexts)
        {
            if (tmpText != null && menuFontAsset != null)
            {
                tmpText.font = menuFontAsset;
            }
        }

        Text[] unityTexts = button.GetComponentsInChildren<Text>(true);
        foreach (Text unityText in unityTexts)
        {
            if (unityText != null && unityMenuFont != null)
            {
                unityText.font = unityMenuFont;
            }
        }
    }
}
