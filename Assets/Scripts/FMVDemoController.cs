using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class FMVDemoController : MonoBehaviour
{
    [Header("Video")]
    public VideoPlayer videoPlayer;
    public GameObject videoDisplay;

    [Header("Choice UI")]
    public GameObject choicePanel;
    public Transform choiceButtonRoot;
    public Button choiceButtonPrefab;

    [Header("Ending UI")]
    public Text endingText;

    [Header("Start")]
    public bool autoPlayOnStart = false;

    [Header("Events")]
    public VoiceDialogueController voiceDialogueController;

    private const string StoryResourceName = "story";
    private const string SaveKey = "FMV_DEMO_CURRENT_NODE";

    private StoryData storyData;
    private readonly Dictionary<string, StoryNode> nodesById = new Dictionary<string, StoryNode>();
    private StoryNode currentNode;
    private bool storyLoaded;
    private bool choicesVisible;
    private bool currentVideoStarted;
    private bool previewPlayback;
    private bool isMenuPaused;
    private bool wasVideoPausedForMenu;

    public bool IsMenuPaused => isMenuPaused;

    private void Awake()
    {
        ResolveVideoDisplay();
        ResolveVoiceDialogueController();

        if (choicePanel != null)
        {
            choicePanel.SetActive(false);
        }

        if (endingText != null)
        {
            endingText.gameObject.SetActive(false);
        }

        if (videoPlayer != null)
        {
            videoPlayer.playOnAwake = false;
            videoPlayer.loopPointReached += OnVideoFinished;
            videoPlayer.errorReceived += OnVideoError;
            videoPlayer.prepareCompleted += OnVideoPrepared;
        }

        if (videoDisplay != null)
        {
            videoDisplay.SetActive(false);
        }

        ClearVideoOutput();
    }

    private void ResolveVoiceDialogueController()
    {
        if (voiceDialogueController != null)
        {
            return;
        }

        voiceDialogueController = FindObjectOfType<VoiceDialogueController>(true);
    }

    private void ResolveVideoDisplay()
    {
        if (videoDisplay != null)
        {
            return;
        }

        GameObject found = GameObject.Find("RawImageVideo");
        if (found != null)
        {
            videoDisplay = found;
        }
    }

    private void Start()
    {
        LoadStory();

        if (autoPlayOnStart)
        {
            StartDefaultStory();
        }
    }

    private void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached -= OnVideoFinished;
            videoPlayer.errorReceived -= OnVideoError;
            videoPlayer.prepareCompleted -= OnVideoPrepared;
        }
    }

    private void Update()
    {
        if (currentNode == null || choicesVisible || videoPlayer == null || previewPlayback || isMenuPaused)
        {
            return;
        }

        if (currentNode.choiceTime < 0f || currentNode.choices == null || currentNode.choices.Length == 0)
        {
            return;
        }

        if (videoPlayer.isPlaying && videoPlayer.time >= currentNode.choiceTime)
        {
            choicesVisible = true;
            videoPlayer.Pause();
            ShowChoices(currentNode.choices);
        }
    }

    public void StartDefaultStory()
    {
        if (!EnsureStoryLoaded())
        {
            return;
        }

        PlayNode(storyData.startNodeId);
    }

    public void PlayNodeFromOutside(string nodeId)
    {
        HideChoiceUIForExternalJump();
        if (voiceDialogueController != null)
        {
            voiceDialogueController.HideDialoguePanelForExternalJump();
        }

        PlayNode(nodeId);
    }

    public void PlayNodePreviewFromOutside(string nodeId)
    {
        PlayNode(nodeId, true);
    }

    public List<StoryNode> GetMenuNodes()
    {
        if (!EnsureStoryLoaded())
        {
            return new List<StoryNode>();
        }

        List<StoryNode> menuNodes = new List<StoryNode>();
        foreach (StoryNode node in storyData.nodes)
        {
            if (node != null && node.showInMenu)
            {
                menuNodes.Add(node);
            }
        }

        return menuNodes;
    }

    public void PauseCurrentVideo()
    {
        PausePlaybackForMenu();
    }

    public void ResumeCurrentVideo()
    {
        ResumePlaybackFromMenu();
    }

    public void PausePlaybackForMenu()
    {
        if (videoPlayer != null && videoPlayer.isPlaying)
        {
            wasVideoPausedForMenu = true;
            videoPlayer.Pause();
        }
        else
        {
            wasVideoPausedForMenu = false;
        }

        isMenuPaused = true;
    }

    public void ResumePlaybackFromMenu()
    {
        isMenuPaused = false;

        if (wasVideoPausedForMenu && videoPlayer != null)
        {
            videoPlayer.Play();
        }

        wasVideoPausedForMenu = false;
    }

    public void CancelMenuPauseForExternalJump()
    {
        isMenuPaused = false;
        wasVideoPausedForMenu = false;
    }

    public void PlayNode(string nodeId)
    {
        PlayNode(nodeId, false);
    }

    private void PlayNode(string nodeId, bool previewOnly)
    {
        isMenuPaused = false;

        if (!EnsureStoryLoaded())
        {
            return;
        }

        if (videoPlayer == null)
        {
            Debug.LogError("FMVDemoController: VideoPlayer is not assigned.");
            return;
        }

        if (string.IsNullOrEmpty(nodeId))
        {
            Debug.LogError("FMVDemoController: nodeId is empty.");
            return;
        }

        if (!nodesById.TryGetValue(nodeId, out StoryNode node))
        {
            Debug.LogError("FMVDemoController: node not found: " + nodeId);
            return;
        }

        if (string.IsNullOrEmpty(node.video))
        {
            if (node.eventType == "voiceDialogue")
            {
                OpenVoiceDialogueNode(node);
                return;
            }

            Debug.LogError("FMVDemoController: video is empty for node: " + nodeId);
            return;
        }

        if (node.eventType == "voiceDialogue")
        {
            OpenVoiceDialogueNode(node);
            return;
        }

        ClearChoices();

        currentNode = node;
        choicesVisible = false;
        currentVideoStarted = false;
        previewPlayback = previewOnly;

        if (endingText != null)
        {
            endingText.gameObject.SetActive(false);
        }

        if (!previewOnly)
        {
            PlayerPrefs.SetString(SaveKey, nodeId);
            PlayerPrefs.Save();
        }

        videoPlayer.Stop();
        ClearVideoOutput();
        if (videoDisplay != null)
        {
            RestoreVideoDisplay();
            videoDisplay.SetActive(true);
        }

        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = BuildVideoUrl(node.video);
        videoPlayer.Prepare();

        Debug.Log("FMVDemoController: preparing node " + node.id + " with video " + videoPlayer.url + (previewOnly ? " preview" : ""));
    }

    public void RestartGame()
    {
        PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();

        currentNode = null;
        choicesVisible = false;
        currentVideoStarted = false;
        ClearChoices();

        if (endingText != null)
        {
            endingText.gameObject.SetActive(false);
        }

        StartDefaultStory();
    }

    private bool EnsureStoryLoaded()
    {
        if (!storyLoaded)
        {
            LoadStory();
        }

        return storyLoaded;
    }

    private void LoadStory()
    {
        nodesById.Clear();
        storyLoaded = false;

        TextAsset jsonAsset = Resources.Load<TextAsset>(StoryResourceName);
        if (jsonAsset == null)
        {
            Debug.LogError("FMVDemoController: cannot find Assets/Resources/story.json.");
            return;
        }

        if (string.IsNullOrWhiteSpace(jsonAsset.text))
        {
            Debug.LogError("FMVDemoController: Assets/Resources/story.json is empty.");
            return;
        }

        try
        {
            storyData = JsonUtility.FromJson<StoryData>(jsonAsset.text);
        }
        catch (Exception exception)
        {
            Debug.LogError("FMVDemoController: failed to parse story.json. " + exception.Message);
            return;
        }

        if (storyData == null || storyData.nodes == null || storyData.nodes.Length == 0)
        {
            Debug.LogError("FMVDemoController: story.json has no nodes.");
            return;
        }

        foreach (StoryNode node in storyData.nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.id))
            {
                Debug.LogWarning("FMVDemoController: skipped a story node without id.");
                continue;
            }

            if (nodesById.ContainsKey(node.id))
            {
                Debug.LogWarning("FMVDemoController: duplicate node id skipped: " + node.id);
                continue;
            }

            nodesById.Add(node.id, node);
        }

        if (nodesById.Count == 0)
        {
            Debug.LogError("FMVDemoController: no valid story nodes loaded.");
            return;
        }

        if (string.IsNullOrEmpty(storyData.startNodeId))
        {
            storyData.startNodeId = storyData.nodes[0].id;
        }

        storyLoaded = true;
        Debug.Log("FMVDemoController: loaded story nodes: " + nodesById.Count);
    }

    private string BuildVideoUrl(string videoFileName)
    {
        if (videoFileName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            videoFileName.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return videoFileName;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        return Application.streamingAssetsPath + "/Videos/" + videoFileName;
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        string windowsPath = Path.Combine(Application.streamingAssetsPath, "Videos", videoFileName);
        return "file:///" + windowsPath.Replace("\\", "/");
#else
        return Path.Combine(Application.streamingAssetsPath, "Videos", videoFileName);
#endif
    }

    private void ShowChoices(ChoiceData[] choices)
    {
        if (choices == null || choices.Length == 0)
        {
            return;
        }

        if (choicePanel == null || choiceButtonRoot == null || choiceButtonPrefab == null)
        {
            Debug.LogError("FMVDemoController: choice UI is not assigned.");
            return;
        }

        ClearChoices();
        choicesVisible = true;
        choicePanel.SetActive(true);

        foreach (ChoiceData choice in choices)
        {
            if (choice == null || string.IsNullOrEmpty(choice.next))
            {
                continue;
            }

            Button button = Instantiate(choiceButtonPrefab, choiceButtonRoot);
            RectTransform rectTransform = button.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.localScale = Vector3.one;
                rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 70f);
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

            SetButtonLabel(button, choice.text);

            string nextNodeId = choice.next;
            button.onClick.AddListener(() => PlayNode(nextNodeId));
        }
    }

    private void ClearChoices()
    {
        if (choicePanel != null)
        {
            choicePanel.SetActive(false);
        }

        if (choiceButtonRoot == null)
        {
            return;
        }

        for (int i = choiceButtonRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(choiceButtonRoot.GetChild(i).gameObject);
        }
    }

    public void HideChoiceUIForExternalJump()
    {
        choicesVisible = false;
        ClearChoices();

        if (endingText != null)
        {
            endingText.gameObject.SetActive(false);
        }
    }

    private void OnVideoFinished(VideoPlayer player)
    {
        if (isMenuPaused)
        {
            Debug.Log("FMVDemoController: video finished while menu paused, skip auto next.");
            return;
        }

        if (currentNode == null)
        {
            return;
        }

        if (!currentVideoStarted)
        {
            Debug.LogWarning("FMVDemoController: video finished before it started, skip auto next. url=" + player.url);
            return;
        }

        Debug.Log("FMVDemoController: video finished for node " + currentNode.id);

        if (previewPlayback)
        {
            currentVideoStarted = false;
            return;
        }

        if (!string.IsNullOrEmpty(currentNode.defaultNext))
        {
            PlayNode(currentNode.defaultNext);
            return;
        }

        ShowEnding();
    }

    private void OnVideoPrepared(VideoPlayer player)
    {
        Debug.Log("FMVDemoController: video prepared, play " + player.url);
        player.Play();
        currentVideoStarted = true;
    }

    private void SetButtonLabel(Button button, string label)
    {
        TMP_Text tmpText = button.GetComponentInChildren<TMP_Text>(true);
        if (tmpText != null)
        {
            tmpText.text = label;
            return;
        }

        Text unityText = button.GetComponentInChildren<Text>(true);
        if (unityText != null)
        {
            unityText.text = label;
        }
    }

    private void OnVideoError(VideoPlayer player, string message)
    {
        Debug.LogError("FMVDemoController: video error: " + message + " url=" + player.url);
    }

    private void ClearVideoOutput()
    {
        if (videoPlayer != null && videoPlayer.targetTexture != null)
        {
            RenderTexture activeTexture = RenderTexture.active;
            RenderTexture.active = videoPlayer.targetTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = activeTexture;
        }

        if (videoDisplay != null)
        {
            RawImage rawImage = videoDisplay.GetComponent<RawImage>();
            if (rawImage == null)
            {
                rawImage = videoDisplay.GetComponentInChildren<RawImage>(true);
            }

            if (rawImage != null)
            {
                Color color = rawImage.color;
                color.a = 0f;
                rawImage.color = color;
            }
        }
    }

    private void RestoreVideoDisplay()
    {
        if (videoDisplay == null)
        {
            return;
        }

        RawImage rawImage = videoDisplay.GetComponent<RawImage>();
        if (rawImage == null)
        {
            rawImage = videoDisplay.GetComponentInChildren<RawImage>(true);
        }

        if (rawImage == null)
        {
            return;
        }

        if (rawImage.texture == null && videoPlayer != null && videoPlayer.targetTexture != null)
        {
            rawImage.texture = videoPlayer.targetTexture;
        }

        Color color = rawImage.color;
        color.a = 1f;
        rawImage.color = color;
    }

    private void OpenVoiceDialogueNode(StoryNode node)
    {
        ClearChoices();

        currentNode = node;
        choicesVisible = true;
        currentVideoStarted = false;

        if (videoPlayer != null)
        {
            videoPlayer.Stop();
        }

        if (videoDisplay != null)
        {
            videoDisplay.SetActive(false);
        }

        if (voiceDialogueController == null)
        {
            Debug.LogError("FMVDemoController: VoiceDialogueController is not assigned.");
            return;
        }

        string roleId = PlayerPrefs.GetString("PLAYER_ROLE_ID", "investigator");
        string roleName = PlayerPrefs.GetString("PLAYER_ROLE_NAME", "调查者");
        voiceDialogueController.OpenDialogue(roleId, roleName, node.id);

        Debug.Log("FMVDemoController: opened voice dialogue node " + node.id);
    }

    private void ShowEnding()
    {
        currentNode = null;
        choicesVisible = false;
        currentVideoStarted = false;
        ClearChoices();

        PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();

        if (endingText != null)
        {
            endingText.gameObject.SetActive(true);
            endingText.text = "Demo finished";
        }

        if (videoDisplay != null)
        {
            videoDisplay.SetActive(false);
        }

        Debug.Log("FMVDemoController: story finished.");
    }
}

[Serializable]
public class StoryData
{
    public string startNodeId;
    public StoryNode[] nodes;
}

[Serializable]
public class StoryNode
{
    public string id;
    public string video;
    public string eventType;
    public float choiceTime = -1f;
    public string defaultNext;
    public bool showInMenu;
    public string menuTitle;
    public ChoiceData[] choices;
}

[Serializable]
public class ChoiceData
{
    public string text;
    public string next;
}
