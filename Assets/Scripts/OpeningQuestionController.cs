using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;

public class OpeningQuestionController : MonoBehaviour
{
    [Header("UI")]
    public GameObject questionPanel;
    public TMP_Text titleText;
    public TMP_Text questionText;
    public TMP_Text progressText;
    public TMP_Text loadingText;
    public Transform optionRoot;
    public Button optionButtonPrefab;

    [Header("Question Audio")]
    public AudioSource questionAudioSource;
    public AudioClip[] questionAudios;

    [Header("API")]
    public string roleApiUrl = "http://localhost:3000/api/assign-role";

    [Header("Game")]
    public FMVDemoController fmvController;
    public float enterGameDelay = 1.5f;
    public GameObject videoDisplay;

    private readonly List<QuestionData> questions = new List<QuestionData>();
    private readonly List<PlayerAnswer> answers = new List<PlayerAnswer>();
    private readonly List<Button> generatedOptionButtons = new List<Button>();
    private int currentQuestionIndex;
    private bool isSubmitting;
    private bool isChangingQuestion;

    private void Awake()
    {
        EnsureEventSystem();
        EnsureCanvasRaycaster();
        ResolveVideoDisplay();
        ResolveQuestionAudioSource();
        HideVideoDisplay();
    }

    private void Start()
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
            questionPanel.SetActive(true);
        }

        HideVideoDisplay();

        ShowQuestion(0);
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
            questionText = "question1？",
            options = new[]
            {
                new OptionData { optionId = "q1_a", optionText = "option1" },
                new OptionData { optionId = "q1_b", optionText = "option2" },
                new OptionData { optionId = "q1_c", optionText = "option3" }
            }
        });

        questions.Add(new QuestionData
        {
            questionId = "q2",
            questionText = "The Most Important Thing?",
            options = new[]
            {
                new OptionData { optionId = "q2_a", optionText = "A. The life of the person you love most" },
                new OptionData { optionId = "q2_b", optionText = "B. The freedom of all humanity" },
                new OptionData { optionId = "q2_c", optionText = "C. The ideal you have upheld throughout your life" },
                 new OptionData { optionId = "q2_d", optionText = "D. Fairness and justice in the human world" }
            }
        });

        questions.Add(new QuestionData
        {
            questionId = "q3",
            questionText = "As a judge, would you sentence the following two people to death?",
            options = new[]
            {
                new OptionData { optionId = "q3_a", optionText = "A. Do everything you can to make up for it." },
                new OptionData { optionId = "q3_b", optionText = "B. Regret is a part of life. Let it go and move forward." }
               
            }
        });

        questions.Add(new QuestionData
        {
            questionId = "q4",
            questionText = "Which attitude toward regret is closer to your own?",
            options = new[]
            {
                new OptionData { optionId = "q4_a", optionText = "A. Do everything you can to make up for it." },
                new OptionData { optionId = "q4_b", optionText = "B. Regret is a part of life. Let it go and move forward." }
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

        yield return new WaitWhile(() => questionAudioSource != null && questionAudioSource.isPlaying);

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

        using (UnityWebRequest request = new UnityWebRequest(roleApiUrl, UnityWebRequest.kHttpVerbPOST))
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
                    loadingText.text = "Error,please try again";
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
            loadingText.text = "You are:" + role.roleName;
        }

        StartCoroutine(EnterGameAfterDelay(role.startNodeId));
    }

    private IEnumerator EnterGameAfterDelay(string startNodeId)
    {
        yield return new WaitForSeconds(enterGameDelay);

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
