using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;

public class OpeningQuestionController : MonoBehaviour
{
    [Serializable]
    public class RoleAudioData
    {
        public string roleId;
        public AudioClip audioClip;
    }

    [Header("UI")]
    public GameObject questionPanel;
    public TMP_Text titleText;
    public TMP_Text questionText;
    public TMP_Text progressText;
    public TMP_Text loadingText;
    public Transform optionRoot;
    public Button optionButtonPrefab;
    public GameObject blackScreenPanel;

    [Header("Question Audio")]
    public AudioSource questionAudioSource;
    public AudioClip openingAudioClip;
    public AudioClip[] questionAudios;

    [Header("Opening Video")]
    public string openingVideoFileName = "opening_video.mp4";
    public VideoPlayer openingVideoPlayer;

    [Header("Role Audio")]
    public AudioSource roleAudioSource;
    public RoleAudioData[] roleAudios;

    [Header("API")]
    public string apiBaseUrl = "http://localhost:3000";
    public string roleApiUrl = "http://localhost:3000/api/assign-role";

    [Header("Game")]
    public FMVDemoController fmvController;
    public float enterGameDelay = 1.5f;
    public GameObject videoDisplay;
    public bool waitForAccessKeySetup = true;

    private readonly List<QuestionData> questions = new List<QuestionData>();
    private readonly List<PlayerAnswer> answers = new List<PlayerAnswer>();
    private readonly List<Button> generatedOptionButtons = new List<Button>();
    private int currentQuestionIndex;
    private bool isSubmitting;
    private bool isChangingQuestion;
    private bool openingFlowStarted;
    private bool isOpeningFlowPaused;
    private bool openingVideoFinished;
    private bool wasOpeningVideoPausedForMenu;
    private bool wasQuestionAudioPausedForMenu;
    private bool wasRoleAudioPausedForMenu;

    public bool IsOpeningFlowPaused => isOpeningFlowPaused;

    private void Awake()
    {
        EnsureEventSystem();
        EnsureCanvasRaycaster();
        ResolveVideoDisplay();
        ResolveOpeningVideoPlayer();
        ResolveQuestionAudioSource();
        ResolveRoleAudioSource();
        ResolveBlackScreenPanel();
        HideVideoDisplay();
    }

    private void Start()
    {
        if (waitForAccessKeySetup)
        {
            PrepareOpeningFlowState();
            EnsureAccessKeySetupController();
            return;
        }

        BeginOpeningFlowFromAccessKeySetup();
    }

    public void BeginOpeningFlowFromAccessKeySetup()
    {
        if (openingFlowStarted)
        {
            return;
        }

        openingFlowStarted = true;
        PrepareOpeningFlowState();
        StartCoroutine(PlayOpeningVideoThenStartQuestions());
    }

    private void PrepareOpeningFlowState()
    {
        InitQuestions();
        answers.Clear();
        currentQuestionIndex = 0;
        isSubmitting = false;
        isChangingQuestion = false;

        if (loadingText != null)
        {
            loadingText.gameObject.SetActive(false);
        }

        if (questionPanel != null)
        {
            questionPanel.SetActive(false);
        }

        HideVideoDisplay();
    }

    private void EnsureAccessKeySetupController()
    {
        AccessKeySetupController setupController = FindObjectOfType<AccessKeySetupController>();
        if (setupController == null)
        {
            setupController = gameObject.AddComponent<AccessKeySetupController>();
        }

        if (setupController.openingQuestionController == null)
        {
            setupController.openingQuestionController = this;
        }

        if (string.IsNullOrWhiteSpace(setupController.apiBaseUrl))
        {
            setupController.apiBaseUrl = apiBaseUrl;
        }
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

    private void ResolveOpeningVideoPlayer()
    {
        if (openingVideoPlayer != null)
        {
            return;
        }

        if (videoDisplay != null)
        {
            openingVideoPlayer = videoDisplay.GetComponent<VideoPlayer>();
            if (openingVideoPlayer == null)
            {
                openingVideoPlayer = videoDisplay.GetComponentInChildren<VideoPlayer>(true);
            }
        }

        if (openingVideoPlayer == null && fmvController != null)
        {
            openingVideoPlayer = fmvController.videoPlayer;
        }
    }

    private void ResolveQuestionAudioSource()
    {
        if (questionAudioSource != null)
        {
            return;
        }

        questionAudioSource = GetComponent<AudioSource>();
        if (questionAudioSource == null)
        {
            questionAudioSource = gameObject.AddComponent<AudioSource>();
        }
    }

    private void ResolveRoleAudioSource()
    {
        if (roleAudioSource != null)
        {
            return;
        }

        roleAudioSource = gameObject.AddComponent<AudioSource>();
    }

    private void ResolveBlackScreenPanel()
    {
        if (blackScreenPanel != null)
        {
            return;
        }

        GameObject found = GameObject.Find("BlackScreenPanel");
        if (found != null)
        {
            blackScreenPanel = found;
            return;
        }

        Canvas canvas = null;
        if (questionPanel != null)
        {
            canvas = questionPanel.GetComponentInParent<Canvas>(true);
        }

        if (canvas == null)
        {
            canvas = FindObjectOfType<Canvas>(true);
        }

        if (canvas == null)
        {
            Debug.LogWarning("OpeningQuestionController: BlackScreenPanel is not assigned and Canvas was not found.");
            return;
        }

        blackScreenPanel = new GameObject("BlackScreenPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        blackScreenPanel.transform.SetParent(canvas.transform, false);

        RectTransform rectTransform = blackScreenPanel.GetComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        Image image = blackScreenPanel.GetComponent<Image>();
        image.color = Color.black;

        blackScreenPanel.SetActive(false);
    }

    public void PauseOpeningFlowForMenu()
    {
        isOpeningFlowPaused = true;

        wasOpeningVideoPausedForMenu = openingVideoPlayer != null && openingVideoPlayer.isPlaying;
        if (wasOpeningVideoPausedForMenu)
        {
            openingVideoPlayer.Pause();
        }

        wasQuestionAudioPausedForMenu = questionAudioSource != null && questionAudioSource.isPlaying;
        if (wasQuestionAudioPausedForMenu)
        {
            questionAudioSource.Pause();
        }

        wasRoleAudioPausedForMenu = roleAudioSource != null && roleAudioSource.isPlaying;
        if (wasRoleAudioPausedForMenu)
        {
            roleAudioSource.Pause();
        }
    }

    public void ResumeOpeningFlowFromMenu()
    {
        isOpeningFlowPaused = false;

        if (wasOpeningVideoPausedForMenu && openingVideoPlayer != null)
        {
            openingVideoPlayer.Play();
        }

        if (wasQuestionAudioPausedForMenu && questionAudioSource != null)
        {
            questionAudioSource.UnPause();
        }

        if (wasRoleAudioPausedForMenu && roleAudioSource != null)
        {
            roleAudioSource.UnPause();
        }

        wasOpeningVideoPausedForMenu = false;
        wasQuestionAudioPausedForMenu = false;
        wasRoleAudioPausedForMenu = false;
    }

    public void CancelMenuPauseForExternalJump()
    {
        isOpeningFlowPaused = false;
        wasOpeningVideoPausedForMenu = false;
        wasQuestionAudioPausedForMenu = false;
        wasRoleAudioPausedForMenu = false;
    }

    private IEnumerator PlayOpeningVideoThenStartQuestions()
    {
        HideVideoDisplay();
        ClearOptions();

        if (questionPanel != null)
        {
            questionPanel.SetActive(false);
        }

        if (questionText != null)
        {
            questionText.gameObject.SetActive(false);
        }

        if (progressText != null)
        {
            progressText.gameObject.SetActive(false);
        }

        if (loadingText != null)
        {
            loadingText.gameObject.SetActive(false);
        }

        if (optionRoot != null)
        {
            optionRoot.gameObject.SetActive(false);
        }

        if (blackScreenPanel != null)
        {
            blackScreenPanel.SetActive(false);
        }

        if (string.IsNullOrEmpty(openingVideoFileName))
        {
            Debug.LogWarning("OpeningQuestionController: opening video file name is empty, skip opening video.");
        }
        else if (openingVideoPlayer == null)
        {
            Debug.LogWarning("OpeningQuestionController: Opening Video Player is not assigned, skip opening video.");
        }
        else if (!OpeningVideoExists(openingVideoFileName))
        {
            Debug.LogWarning("OpeningQuestionController: opening video not found: " + openingVideoFileName + ", skip opening video.");
        }
        else
        {
            ShowVideoDisplay();

            openingVideoFinished = false;
            VideoPlayer.EventHandler onFinished = player => openingVideoFinished = true;
            openingVideoPlayer.loopPointReached += onFinished;

            openingVideoPlayer.Stop();
            openingVideoPlayer.playOnAwake = false;
            openingVideoPlayer.isLooping = false;
            openingVideoPlayer.source = VideoSource.Url;
            openingVideoPlayer.url = BuildOpeningVideoUrl(openingVideoFileName);
            openingVideoPlayer.Prepare();

            float prepareDeadline = Time.realtimeSinceStartup + 8f;
            yield return new WaitUntil(() => openingVideoPlayer == null || openingVideoPlayer.isPrepared || Time.realtimeSinceStartup >= prepareDeadline);

            if (openingVideoPlayer != null && openingVideoPlayer.isPrepared)
            {
                openingVideoPlayer.Play();
                Debug.Log("OpeningQuestionController: playing opening video " + openingVideoPlayer.url);

                while (openingVideoPlayer != null && !openingVideoFinished)
                {
                    yield return null;
                }
            }
            else
            {
                Debug.LogWarning("OpeningQuestionController: opening video prepare timeout, skip opening video.");
            }

            if (openingVideoPlayer != null)
            {
                openingVideoPlayer.loopPointReached -= onFinished;
                openingVideoPlayer.Stop();
            }

            HideVideoDisplay();
        }

        if (questionPanel != null)
        {
            questionPanel.SetActive(true);
        }

        if (progressText != null)
        {
            progressText.gameObject.SetActive(true);
        }

        ShowQuestion(0);
    }

    private bool OpeningVideoExists(string fileName)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return true;
#else
        string localPath = Path.Combine(Application.streamingAssetsPath, "Videos", fileName);
        return File.Exists(localPath);
#endif
    }

    private string BuildOpeningVideoUrl(string videoFileName)
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

    private void ShowVideoDisplay()
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

        if (rawImage != null)
        {
            if (rawImage.texture == null && openingVideoPlayer != null && openingVideoPlayer.targetTexture != null)
            {
                rawImage.texture = openingVideoPlayer.targetTexture;
            }

            Color color = rawImage.color;
            color.a = 1f;
            rawImage.color = color;
        }

        videoDisplay.SetActive(true);
    }

    private void HideVideoDisplay()
    {
        if (videoDisplay == null)
        {
            Debug.LogWarning("OpeningQuestionController: video display is not assigned and RawImageVideo was not found.");
            return;
        }

        VideoPlayer player = videoDisplay.GetComponent<VideoPlayer>();
        if (player == null)
        {
            player = videoDisplay.GetComponentInChildren<VideoPlayer>(true);
        }

            if (player != null)
            {
                player.Stop();
                if (player.targetTexture != null)
                {
                    RenderTexture activeTexture = RenderTexture.active;
                    RenderTexture.active = player.targetTexture;
                    GL.Clear(true, true, Color.clear);
                    RenderTexture.active = activeTexture;
                }
            }

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

        videoDisplay.SetActive(false);
        Debug.Log("OpeningQuestionController: video display hidden.");
    }

    private void InitQuestions()
    {
        questions.Clear();

        questions.Add(new QuestionData
        {
            questionId = "q1",
            questionText = "你究竟是顺从逻辑的秩序，还是臣服于心绪的本能?",
            options = new[]
            {
                new OptionData { optionId = "q1_a", optionText = "理性" },
                new OptionData { optionId = "q1_b", optionText = "感性" }
                
            }
        });

        questions.Add(new QuestionData
        {
            questionId = "q2",
            questionText = "满目凡尘物象，哪一样是你心底不可舍弃的重量？",
            options = new[]
            {
                new OptionData { optionId = "q2_a", optionText = "A. 挚爱之人的生命" },
                new OptionData { optionId = "q2_b", optionText = "B. 全人类的自由" },
                new OptionData { optionId = "q2_c", optionText = "C. 坚持一生的理想" },
                 new OptionData { optionId = "q2_d", optionText = "D. 人世间的公平与正义" }
            }
        });

        questions.Add(new QuestionData
        {
            questionId = "q3",
            questionText = "倘若执掌生死审判，你是否会降下裁决，终结二人性命？",
            options = new[]
            {
              new OptionData { optionId = "q2_a", optionText = "A. 处死婴儿时期的希特勒" },
                new OptionData { optionId = "q2_b", optionText = "B. 处死私下进行人体实验的顶尖科学家" },
                new OptionData { optionId = "q2_c", optionText = "C. 都处死" },
                 new OptionData { optionId = "q2_d", optionText = "D. 都不处死" }
            }
        });

        questions.Add(new QuestionData
        {
            questionId = "q4",
            questionText = "面对命中无从改写的遗憾，你会选择沉溺还是释怀？",
            options = new[]
            {
                new OptionData { optionId = "q4_a", optionText = "A. 竭尽所能，尽力挽回。" },
                new OptionData { optionId = "q4_b", optionText = "B. 遗憾是人生的一部分，放下遗憾，向前看。" }
            }
        });
    }

    private void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<StandaloneInputModule>();
        Debug.Log("OpeningQuestionController: EventSystem was missing and has been created.");
    }

    private void EnsureCanvasRaycaster()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null && questionPanel != null)
        {
            canvas = questionPanel.GetComponentInParent<Canvas>();
        }

        if (canvas == null)
        {
            Debug.LogWarning("OpeningQuestionController: Canvas was not found.");
            return;
        }

        if (canvas.GetComponent<GraphicRaycaster>() == null)
        {
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            Debug.Log("OpeningQuestionController: GraphicRaycaster was missing and has been created.");
        }
    }

    private void ShowQuestion(int index)
    {
        Debug.Log("OpeningQuestionController: ShowQuestion called with index " + index);

        if (index < 0 || index >= questions.Count)
        {
            Debug.LogError("OpeningQuestionController: question index out of range: " + index);
            return;
        }

        if (progressText == null || optionRoot == null || optionButtonPrefab == null)
        {
            Debug.LogError("OpeningQuestionController: question UI is not fully assigned.");
            return;
        }

        ClearOptions();

        currentQuestionIndex = index;
        QuestionData question = questions[index];
        isChangingQuestion = true;

        if (questionText != null)
        {
            questionText.text = "";
            questionText.gameObject.SetActive(false);
        }

        progressText.text = (index + 1) + " / " + questions.Count;
        progressText.ForceMeshUpdate();
        Canvas.ForceUpdateCanvases();

        Debug.Log(
            "OpeningQuestionController: UI updated. progressText=" + progressText.name +
            ", progress=" + progressText.text);

        if (loadingText != null)
        {
            loadingText.gameObject.SetActive(true);
            loadingText.text = "请听题...";
        }

        if (optionRoot != null)
        {
            optionRoot.gameObject.SetActive(false);
        }

        StartCoroutine(PlayQuestionAudioThenShowOptions(index));
    }

    private IEnumerator PlayQuestionAudioThenShowOptions(int index)
    {
        AudioClip clip = GetQuestionAudio(index);

        if (questionAudioSource == null)
        {
            Debug.LogWarning("OpeningQuestionController: Question Audio Source is not assigned, options will show immediately.");
            ShowOptionsForQuestion(index);
            yield break;
        }

        if (clip == null)
        {
            Debug.LogWarning("OpeningQuestionController: question audio is empty for index " + index + ", options will show immediately.");
            ShowOptionsForQuestion(index);
            yield break;
        }

        questionAudioSource.Stop();
        questionAudioSource.clip = clip;
        questionAudioSource.Play();

        while (questionAudioSource != null &&
               questionAudioSource.clip == clip &&
               questionAudioSource.time < clip.length - 0.05f)
        {
            yield return null;
        }

        ShowOptionsForQuestion(index);
    }

    private AudioClip GetQuestionAudio(int index)
    {
        if (questionAudios == null || index < 0 || index >= questionAudios.Length)
        {
            return null;
        }

        return questionAudios[index];
    }

    private void ShowOptionsForQuestion(int index)
    {
        if (index < 0 || index >= questions.Count)
        {
            return;
        }

        QuestionData question = questions[index];

        if (loadingText != null)
        {
            loadingText.gameObject.SetActive(false);
        }

        if (optionRoot != null)
        {
            optionRoot.gameObject.SetActive(true);
        }

        foreach (OptionData option in question.options)
        {
            Button button = Instantiate(optionButtonPrefab, optionRoot);
            button.gameObject.SetActive(true);
            button.interactable = true;
            button.onClick.RemoveAllListeners();
            generatedOptionButtons.Add(button);
            SetButtonLabel(button, option.optionText);

            string questionId = question.questionId;
            string questionTextValue = question.questionText;
            string optionId = option.optionId;
            string optionTextValue = option.optionText;

            AttachProxyToButtonTree(button.transform, questionId, questionTextValue, optionId, optionTextValue);

            button.onClick.AddListener(() =>
            {
                OnOptionClicked(questionId, questionTextValue, optionId, optionTextValue);
            });

            Debug.Log("OpeningQuestionController: created option button " + optionId + " / " + optionTextValue);
        }

        isChangingQuestion = false;
    }

    private void OnOptionClicked(string questionId, string questionTextValue, string optionId, string optionTextValue)
    {
        Debug.Log(
            "OpeningQuestionController: OnOptionClicked entered. currentQuestionIndex=" + currentQuestionIndex +
            ", isSubmitting=" + isSubmitting +
            ", isChangingQuestion=" + isChangingQuestion);

        if (isSubmitting || isChangingQuestion)
        {
            Debug.LogWarning("OpeningQuestionController: click ignored because controller is busy.");
            return;
        }

        answers.Add(new PlayerAnswer
        {
            questionId = questionId,
            questionText = questionTextValue,
            optionId = optionId,
            optionText = optionTextValue
        });

        Debug.Log("OpeningQuestionController: answer " + questionId + " / " + optionId + " / " + optionTextValue);

        int nextQuestionIndex = currentQuestionIndex + 1;
        if (nextQuestionIndex < questions.Count)
        {
            StartCoroutine(ShowQuestionNextFrame(nextQuestionIndex));
            return;
        }

        StartCoroutine(SendAnswersToBackend());
    }

    public void ClickOptionFromProxy(string questionId, string questionTextValue, string optionId, string optionTextValue)
    {
        OnOptionClicked(questionId, questionTextValue, optionId, optionTextValue);
    }

    private void AttachProxyToButtonTree(
        Transform root,
        string questionId,
        string questionTextValue,
        string optionId,
        string optionTextValue)
    {
        OpeningOptionButtonProxy proxy = root.GetComponent<OpeningOptionButtonProxy>();
        if (proxy == null)
        {
            proxy = root.gameObject.AddComponent<OpeningOptionButtonProxy>();
        }

        proxy.Init(this, questionId, questionTextValue, optionId, optionTextValue);

        for (int i = 0; i < root.childCount; i++)
        {
            AttachProxyToButtonTree(root.GetChild(i), questionId, questionTextValue, optionId, optionTextValue);
        }
    }

    private IEnumerator ShowQuestionNextFrame(int nextQuestionIndex)
    {
        isChangingQuestion = true;
        SetGeneratedButtonsInteractable(false);
        ClearOptions();

        yield return null;

        Debug.Log("OpeningQuestionController: show question " + (nextQuestionIndex + 1));
        ShowQuestion(nextQuestionIndex);
    }

    [ContextMenu("Debug/Force Next Question")]
    private void DebugForceNextQuestion()
    {
        int nextQuestionIndex = currentQuestionIndex + 1;
        if (nextQuestionIndex >= questions.Count)
        {
            nextQuestionIndex = 0;
        }

        Debug.Log("OpeningQuestionController: force next question " + (nextQuestionIndex + 1));
        ShowQuestion(nextQuestionIndex);
    }

    private IEnumerator SendAnswersToBackend()
    {
        isSubmitting = true;
        ClearOptions();
        HideQuestionHeader();

        if (loadingText != null)
        {
            loadingText.gameObject.SetActive(true);
            loadingText.text = "Matching your charactor...";
        }

        RoleAssignRequest requestData = new RoleAssignRequest
        {
            playerId = SystemInfo.deviceUniqueIdentifier,
            answers = answers.ToArray()
        };

        string json = JsonUtility.ToJson(requestData);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        string assignRoleUrl = BuildApiUrl("/api/assign-role", roleApiUrl);
        Debug.Log("OpeningQuestionController: assign-role url=" + assignRoleUrl);

        using (UnityWebRequest request = new UnityWebRequest(assignRoleUrl, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            Debug.Log("OpeningQuestionController: sending role request: " + json);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("OpeningQuestionController: role request failed: " + request.error);

                if (loadingText != null)
                {
                    loadingText.text = "连接后端失败，请稍后重试";
                }

                isSubmitting = false;
                yield break;
            }

            string responseJson = request.downloadHandler.text;
            Debug.Log("OpeningQuestionController: role response: " + responseJson);

            RoleAssignResponse role = JsonUtility.FromJson<RoleAssignResponse>(responseJson);
            ApplyRole(role);
        }
    }

    private void ApplyRole(RoleAssignResponse role)
    {
        if (role == null || string.IsNullOrEmpty(role.roleId) || string.IsNullOrEmpty(role.startNodeId))
        {
            Debug.LogError("OpeningQuestionController: invalid role JSON.");

            if (loadingText != null)
            {
                loadingText.text = "角色数据无效，请刷新重试";
            }

            isSubmitting = false;
            return;
        }

        PlayerPrefs.SetString("PLAYER_ROLE_ID", role.roleId);
        PlayerPrefs.SetString("PLAYER_ROLE_NAME", role.roleName);
        PlayerPrefs.SetString("PLAYER_ROLE_DESC", role.roleDescription);
        PlayerPrefs.SetString("PLAYER_START_NODE", role.startNodeId);
        PlayerPrefs.Save();

        if (loadingText != null)
        {
            loadingText.text = "正在进入角色...";
        }

        EnterRoleAndStartVideo(role);
    }

    private void EnterRoleAndStartVideo(RoleAssignResponse role)
    {
        HideQuestionHeader();
        ClearOptions();

        if (optionRoot != null)
        {
            optionRoot.gameObject.SetActive(false);
        }

        if (loadingText != null)
        {
            loadingText.gameObject.SetActive(false);
        }

        AudioClip roleClip = GetRoleAudioClip(role.roleId);
        if (roleClip != null && roleAudioSource != null)
        {
            roleAudioSource.Stop();
            roleAudioSource.clip = roleClip;
            roleAudioSource.Play();
        }
        else if (roleClip == null)
        {
            Debug.LogWarning("OpeningQuestionController: role audio not found or not assigned for roleId=" + role.roleId + ", entering node directly.");
        }
        else
        {
            Debug.LogWarning("OpeningQuestionController: Role Audio Source is not assigned, entering node directly.");
        }

        StartCoroutine(EnterGame(role.startNodeId));
    }

    public void EnterRoleAndStartVideoDirectly(RoleAssignResponse role)
    {
        if (role == null || string.IsNullOrEmpty(role.startNodeId))
        {
            Debug.LogError("OpeningQuestionController: direct role entry has empty startNodeId.");
            return;
        }

        StopAllCoroutines();
        isSubmitting = false;
        isChangingQuestion = false;
        HideOpeningQuestionUIForExternalJump();

        AudioClip roleClip = GetRoleAudioClip(role.roleId);
        if (roleClip != null && roleAudioSource != null)
        {
            roleAudioSource.Stop();
            roleAudioSource.clip = roleClip;
            roleAudioSource.Play();
        }
        else if (roleClip == null)
        {
            Debug.LogWarning("OpeningQuestionController: role audio not found or not assigned for roleId=" + role.roleId + ", entering node directly.");
        }
        else
        {
            Debug.LogWarning("OpeningQuestionController: Role Audio Source is not assigned, entering node directly.");
        }

        if (fmvController != null)
        {
            fmvController.PlayNodeFromOutside(role.startNodeId);
        }
        else
        {
            Debug.LogError("OpeningQuestionController: FMVDemoController is not assigned.");
        }
    }

    private IEnumerator EnterGameAfterDelay(string startNodeId)
    {
        yield return new WaitForSeconds(enterGameDelay);
        yield return EnterGame(startNodeId);
    }

    private IEnumerator EnterGame(string startNodeId)
    {
        if (string.IsNullOrEmpty(startNodeId))
        {
            Debug.LogError("OpeningQuestionController: startNodeId is empty.");
            isSubmitting = false;
            yield break;
        }

        if (questionPanel != null)
        {
            questionPanel.SetActive(false);
        }

        if (fmvController == null)
        {
            Debug.LogError("OpeningQuestionController: FMVDemoController is not assigned.");
            isSubmitting = false;
            yield break;
        }

        fmvController.PlayNodeFromOutside(startNodeId);
    }

    public void HideOpeningQuestionUIForExternalJump()
    {
        ClearOptions();

        if (questionPanel != null)
        {
            questionPanel.SetActive(false);
        }

        if (titleText != null)
        {
            titleText.gameObject.SetActive(false);
        }

        if (questionText != null)
        {
            questionText.gameObject.SetActive(false);
        }

        if (progressText != null)
        {
            progressText.gameObject.SetActive(false);
        }

        if (loadingText != null)
        {
            loadingText.text = "";
            loadingText.gameObject.SetActive(false);
        }

        if (optionRoot != null)
        {
            optionRoot.gameObject.SetActive(false);
        }
    }

    private AudioClip GetRoleAudioClip(string roleId)
    {
        if (roleAudios == null || string.IsNullOrEmpty(roleId))
        {
            return null;
        }

        for (int i = 0; i < roleAudios.Length; i++)
        {
            RoleAudioData roleAudio = roleAudios[i];
            if (roleAudio != null && roleAudio.roleId == roleId)
            {
                return roleAudio.audioClip;
            }
        }

        return null;
    }

    private void SetButtonLabel(Button button, string label)
    {
        TMP_Text tmpText = button.GetComponentInChildren<TMP_Text>();
        if (tmpText != null)
        {
            tmpText.text = label;
            return;
        }

        Text unityText = button.GetComponentInChildren<Text>();
        if (unityText != null)
        {
            unityText.text = label;
        }
    }

    private string BuildApiUrl(string path, string legacyUrl)
    {
        string baseUrl = string.IsNullOrWhiteSpace(apiBaseUrl) ? "" : apiBaseUrl.Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(baseUrl))
        {
            string normalizedPath = path.StartsWith("/") ? path : "/" + path;
            return baseUrl + normalizedPath;
        }

        return legacyUrl;
    }

    private void HideQuestionHeader()
    {
        if (titleText != null)
        {
            titleText.gameObject.SetActive(false);
        }

        if (questionText != null)
        {
            questionText.gameObject.SetActive(false);
        }

        if (progressText != null)
        {
            progressText.gameObject.SetActive(false);
        }
    }

    private void ClearOptions()
    {
        generatedOptionButtons.Clear();

        if (optionRoot == null)
        {
            return;
        }

        for (int i = optionRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = optionRoot.GetChild(i);
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
    }

    private void SetGeneratedButtonsInteractable(bool interactable)
    {
        foreach (Button button in generatedOptionButtons)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }
    }
}

[Serializable]
public class QuestionData
{
    public string questionId;
    public string questionText;
    public OptionData[] options;
}

[Serializable]
public class OptionData
{
    public string optionId;
    public string optionText;
}

[Serializable]
public class PlayerAnswer
{
    public string questionId;
    public string questionText;
    public string optionId;
    public string optionText;
}

[Serializable]
public class RoleAssignRequest
{
    public string playerId;
    public PlayerAnswer[] answers;
}

[Serializable]
public class RoleAssignResponse
{
    public string roleId;
    public string roleName;
    public string roleDescription;
    public string startNodeId;
    public string[] traits;
}

public class OpeningOptionButtonProxy : MonoBehaviour, IPointerClickHandler
{
    private OpeningQuestionController controller;
    private string questionId;
    private string questionText;
    private string optionId;
    private string optionText;

    public void Init(
        OpeningQuestionController controller,
        string questionId,
        string questionText,
        string optionId,
        string optionText)
    {
        this.controller = controller;
        this.questionId = questionId;
        this.questionText = questionText;
        this.optionId = optionId;
        this.optionText = optionText;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log("OpeningOptionButtonProxy: pointer click " + optionId);

        if (controller != null)
        {
            controller.ClickOptionFromProxy(questionId, questionText, optionId, optionText);
        }
    }
}
