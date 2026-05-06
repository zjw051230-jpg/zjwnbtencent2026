using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;

public class VoiceDialogueController : MonoBehaviour
{
    private const float DefaultDialogueVideoAspectRatio = 1112f / 834f;
    private const string StatusReady = "\u70b9\u51fb\u6309\u94ae\u5f00\u59cb\u8bf4\u8bdd";
    private const string StatusRecording = "\u6b63\u5728\u5f55\u97f3\uff0c\u518d\u6b21\u70b9\u51fb\u7ed3\u675f";
    private const string StatusUploading = "\u6b63\u5728\u4e0a\u4f20\u8bed\u97f3...";
    private const string StatusWaitingAi = "\u6b63\u5728\u7b49\u5f85 AI \u56de\u590d...";
    private const string StatusPlayingAi = "\u6b63\u5728\u64ad\u653e AI \u56de\u590d...";
    private const string StatusDialogueDone = "\u5bf9\u8bdd\u5b8c\u6210";
    private const string StatusVoiceSendFailed = "\u8bed\u97f3\u53d1\u9001\u5931\u8d25\uff0c\u8bf7\u91cd\u8bd5";
    private const string StatusFallback = "\u672a\u68c0\u6d4b\u5230\u6709\u6548\u8bed\u97f3\uff0c\u64ad\u653e\u9884\u8bbe\u56de\u5e94";
    private const string StatusAudioPlaybackFailed = "\u8bed\u97f3\u97f3\u9891\u64ad\u653e\u5931\u8d25\uff0c\u4f46\u6587\u672c\u56de\u590d\u5df2\u6536\u5230";
    private const string StatusFallbackVideoMissing = "\u515c\u5e95\u89c6\u9891\u7f3a\u5931\uff0c\u8bf7\u68c0\u67e5\u8d44\u6e90";

    [System.Serializable]
    public class RoleFallbackVideoData
    {
        public string roleId;
        public string fallbackVideoFileName;
    }

    [System.Serializable]
    public class RoleDialogueVideoData
    {
        public string roleId;
        public string videoFileName;
    }

    [Header("API")]
    public string apiBaseUrl = "http://localhost:3000";
    public string voiceDialogueApiUrl = "http://localhost:3000/api/voice-dialogue";

    [Header("Role")]
    public string roleId = "investigator";
    public string roleName = "Mozart";
    public string sceneId = "dialogue_scene";
    public string speakingVideoFileName = "role_speaking.mp4";
    public string successSpeakingVideoFileName = "role_speaking.mp4";
    public string defaultSuccessVideoFileName = "dialogue_success_default.mp4";
    public RoleDialogueVideoData[] roleSuccessVideos;
    public string fallbackVideoFileName = "dialogue_fallback_default.mp4";
    public string defaultFallbackVideoFileName = "dialogue_fallback_default.mp4";
    public RoleFallbackVideoData[] roleFallbackVideos;
    private string dialogueVideoFileName;
    private string dialogueNextNodeId;
    private FMVDemoController fmvController;

    [Header("Recording")]
    public int sampleRate = 16000;
    public int maxRecordSeconds = 15;
    public int requestTimeoutSeconds = 30;

    [Header("UI")]
    public GameObject dialoguePanel;
    public Button recordButton;
    public TMP_Text statusText;
    public TMP_Text transcriptText;
    public TMP_Text replyText;

    [Header("Playback")]
    public AudioSource aiAudioSource;
    public AudioSource speakingVideoAudioSource;
    public VideoPlayer speakingVideoPlayer;
    public GameObject speakingVideoDisplay;
    public RawImage speakingVideoRawImage;

    private AudioClip recordingClip;
    private string microphoneDevice;
    private bool isRecording;
    private bool isSubmitting;
    private RenderTexture runtimeSpeakingVideoTexture;
    private bool playDialogueVideoOnPrepare = true;

    private void Awake()
    {
        ResolveDialoguePanel();
        ResolveTextBindings();
        ResolveAudioSource();

        if (recordButton != null)
        {
            recordButton.onClick.RemoveAllListeners();
            recordButton.onClick.AddListener(ToggleRecording);
        }

        if (speakingVideoDisplay != null)
        {
            speakingVideoDisplay.SetActive(false);
        }

        if (dialoguePanel != null)
        {
            dialoguePanel.SetActive(false);
        }

        SetStatus(StatusReady);
    }

    private void OnDestroy()
    {
        if (speakingVideoPlayer != null)
        {
            speakingVideoPlayer.errorReceived -= OnDialogueVideoError;
            speakingVideoPlayer.prepareCompleted -= OnDialogueVideoPrepared;
        }

        if (runtimeSpeakingVideoTexture != null)
        {
            runtimeSpeakingVideoTexture.Release();
            Destroy(runtimeSpeakingVideoTexture);
            runtimeSpeakingVideoTexture = null;
        }
    }

    private void ResolveDialoguePanel()
    {
        if (dialoguePanel != null)
        {
            return;
        }

        GameObject found = GameObject.Find("DialoguePanel");
        if (found != null)
        {
            dialoguePanel = found;
        }
    }

    private void ResolveTextBindings()
    {
        if (transcriptText == null)
        {
            GameObject foundTranscript = GameObject.Find("TranscriptText");
            if (foundTranscript != null)
            {
                transcriptText = foundTranscript.GetComponent<TMP_Text>();
            }

            if (transcriptText == null)
            {
                Debug.LogWarning("VoiceDialogueController: transcriptText is not assigned.");
            }
        }

        if (replyText == null)
        {
            GameObject foundReply = GameObject.Find("ReplyText");
            if (foundReply != null)
            {
                replyText = foundReply.GetComponent<TMP_Text>();
            }

            if (replyText == null)
            {
                Debug.LogWarning("VoiceDialogueController: replyText is not assigned.");
            }
        }
    }

    private void ResolveAudioSource()
    {
        if (aiAudioSource == null)
        {
            aiAudioSource = GetComponent<AudioSource>();
            if (aiAudioSource == null)
            {
                aiAudioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        if (speakingVideoAudioSource == null)
        {
            speakingVideoAudioSource = gameObject.AddComponent<AudioSource>();
            speakingVideoAudioSource.playOnAwake = false;
        }

        EnsureSeparateAudioSources();
    }

    public void OpenDialogue(string roleIdValue, string roleNameValue, string sceneIdValue)
    {
        OpenDialogue(roleIdValue, roleNameValue, sceneIdValue, "");
    }

    public void OpenDialogue(string roleIdValue, string roleNameValue, string sceneIdValue, string dialogueVideoFileNameValue)
    {
        OpenDialogue(roleIdValue, roleNameValue, sceneIdValue, dialogueVideoFileNameValue, "", null);
    }

    public void OpenDialogue(string roleIdValue, string roleNameValue, string sceneIdValue, string dialogueVideoFileNameValue, string nextNodeIdValue, FMVDemoController fmvControllerValue)
    {
        roleId = string.IsNullOrEmpty(roleIdValue) ? roleId : roleIdValue;
        roleName = string.IsNullOrEmpty(roleNameValue) ? roleName : roleNameValue;
        sceneId = string.IsNullOrEmpty(sceneIdValue) ? sceneId : sceneIdValue;
        dialogueVideoFileName = dialogueVideoFileNameValue;
        dialogueNextNodeId = nextNodeIdValue;
        fmvController = fmvControllerValue;
        Debug.Log($"[VoiceDialogue] OpenDialogue video={dialogueVideoFileName}, next={dialogueNextNodeId}");

        if (dialoguePanel != null)
        {
            ActivateParents(dialoguePanel.transform);
            dialoguePanel.SetActive(true);
            Debug.Log("VoiceDialogueController: dialogue panel opened.");
        }
        else
        {
            Debug.LogError("VoiceDialogueController: DialoguePanel is not assigned and object named DialoguePanel was not found.");
        }

        if (transcriptText != null)
        {
            transcriptText.text = "";
        }

        if (replyText != null)
        {
            replyText.text = "";
        }

        SetStatus(StatusReady);
        PlayDialogueVideo(dialogueVideoFileName);
    }

    private bool PlayDialogueVideo(string videoFileName)
    {
        return PlayDialogueVideo(videoFileName, true);
    }

    private bool PlayDialogueVideo(string videoFileName, bool loopVideo)
    {
        return PlayDialogueVideo(videoFileName, loopVideo, true);
    }

    private bool PlayDialogueVideo(string videoFileName, bool loopVideo, bool playOnPrepare)
    {
        if (string.IsNullOrEmpty(videoFileName))
        {
            Debug.LogWarning("[VoiceDialogue] dialogue video file name is empty.");
            return false;
        }

        Debug.Log("[VoiceDialogue] PlayDialogueVideo videoFileName=" + videoFileName);

        string normalizedFileName = videoFileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
            ? videoFileName
            : videoFileName + ".mp4";

        string localPath = Path.Combine(Application.streamingAssetsPath, "Videos", normalizedFileName);
        Debug.Log("[VoiceDialogue] dialogue localPath=" + localPath);

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        bool fileExists = File.Exists(localPath);
        Debug.Log("[VoiceDialogue] dialogue file exists=" + fileExists);
        if (!fileExists)
        {
            Debug.LogWarning("VoiceDialogueController: dialogue video missing. path=" + localPath);
            return false;
        }
#endif

        if (speakingVideoDisplay != null)
        {
            speakingVideoDisplay.SetActive(true);
            Debug.Log($"[VoiceDialogue] speakingVideoDisplay activeSelf={speakingVideoDisplay.activeSelf}, activeInHierarchy={speakingVideoDisplay.activeInHierarchy}");
        }
        else
        {
            Debug.LogWarning("[VoiceDialogue] speakingVideoDisplay is not assigned.");
        }

        if (speakingVideoPlayer == null)
        {
            Debug.LogWarning("[VoiceDialogue] speakingVideoPlayer is not assigned.");
            return false;
        }

        EnsureSpeakingVideoOutput();

        string videoUrl = BuildVideoUrl(normalizedFileName);
        speakingVideoPlayer.Stop();
        speakingVideoPlayer.source = VideoSource.Url;
        speakingVideoPlayer.url = videoUrl;
        speakingVideoPlayer.isLooping = loopVideo;
        playDialogueVideoOnPrepare = playOnPrepare;
        speakingVideoPlayer.playOnAwake = false;
        speakingVideoPlayer.waitForFirstFrame = true;
        speakingVideoPlayer.errorReceived -= OnDialogueVideoError;
        speakingVideoPlayer.errorReceived += OnDialogueVideoError;
        speakingVideoPlayer.prepareCompleted -= OnDialogueVideoPrepared;
        speakingVideoPlayer.prepareCompleted += OnDialogueVideoPrepared;

        Debug.Log("[VoiceDialogue] dialogue video url=" + videoUrl);
        speakingVideoPlayer.Prepare();
        return true;
    }

    private void ConfigureSpeakingVideoAudio(bool enableVideoAudio)
    {
        if (speakingVideoPlayer == null)
        {
            return;
        }

        EnsureSeparateAudioSources();

        speakingVideoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        speakingVideoPlayer.controlledAudioTrackCount = 1;
        speakingVideoPlayer.EnableAudioTrack(0, enableVideoAudio);

        if (speakingVideoAudioSource == null)
        {
            speakingVideoAudioSource = gameObject.AddComponent<AudioSource>();
            speakingVideoAudioSource.playOnAwake = false;
        }

        speakingVideoPlayer.SetTargetAudioSource(0, speakingVideoAudioSource);
        speakingVideoAudioSource.mute = !enableVideoAudio;
        speakingVideoAudioSource.volume = enableVideoAudio ? 1f : 0f;
    }

    private void EnsureSeparateAudioSources()
    {
        if (aiAudioSource != null && speakingVideoAudioSource != null && aiAudioSource == speakingVideoAudioSource)
        {
            Debug.LogWarning("VoiceDialogueController: aiAudioSource and speakingVideoAudioSource should not be the same AudioSource. Creating a separate speakingVideoAudioSource.");
            speakingVideoAudioSource = gameObject.AddComponent<AudioSource>();
            speakingVideoAudioSource.playOnAwake = false;
        }
    }

    private void EnsureSpeakingVideoOutput()
    {
        if (speakingVideoRawImage == null && speakingVideoDisplay != null)
        {
            speakingVideoRawImage = speakingVideoDisplay.GetComponentInChildren<RawImage>(true);
        }

        if (speakingVideoRawImage == null)
        {
            Debug.LogWarning("[VoiceDialogue] speakingVideoRawImage is not assigned.");
        }
        else
        {
            speakingVideoRawImage.gameObject.SetActive(true);
            FitVideoToContainer();
        }

        if (speakingVideoPlayer == null)
        {
            return;
        }

        speakingVideoPlayer.renderMode = VideoRenderMode.RenderTexture;

        RenderTexture targetTexture = speakingVideoPlayer.targetTexture;
        if (targetTexture == null && speakingVideoRawImage != null)
        {
            targetTexture = speakingVideoRawImage.texture as RenderTexture;
        }

        if (targetTexture == null)
        {
            if (runtimeSpeakingVideoTexture == null)
            {
                runtimeSpeakingVideoTexture = new RenderTexture(1112, 834, 0);
                runtimeSpeakingVideoTexture.name = "RuntimeDialogueVideoRT";
            }

            targetTexture = runtimeSpeakingVideoTexture;
        }

        speakingVideoPlayer.targetTexture = targetTexture;

        if (speakingVideoRawImage != null)
        {
            speakingVideoRawImage.texture = targetTexture;
        }

        Debug.Log("[VoiceDialogue] video renderMode=" + speakingVideoPlayer.renderMode + ", targetTexture=" + (targetTexture == null ? "null" : targetTexture.name));
    }

    private void FitVideoToContainer()
    {
        if (speakingVideoRawImage == null)
        {
            return;
        }

        RectTransform containerRect = speakingVideoDisplay == null ? null : speakingVideoDisplay.GetComponent<RectTransform>();
        if (containerRect != null && containerRect.rect.width < 300f && containerRect.rect.height < 200f)
        {
            containerRect.anchorMin = new Vector2(0.5f, 0.5f);
            containerRect.anchorMax = new Vector2(0.5f, 0.5f);
            containerRect.pivot = new Vector2(0.5f, 0.5f);
            containerRect.sizeDelta = new Vector2(960f, 720f);
            containerRect.localScale = Vector3.one;
        }

        RectTransform rect = speakingVideoRawImage.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;

        ApplyVideoAspectRatio(DefaultDialogueVideoAspectRatio);

        Debug.Log("[VoiceDialogue] video rect fitted. containerSize=" + (containerRect == null ? "null" : containerRect.rect.size.ToString()) + ", rawImageSize=" + rect.rect.size);
    }

    private void OnDialogueVideoPrepared(VideoPlayer player)
    {
        ApplyPreparedVideoAspectRatio(player);
        if (playDialogueVideoOnPrepare)
        {
            Debug.Log("[VoiceDialogue] dialogue video prepared, play now.");
            player.Play();
        }
        else
        {
            Debug.Log("[VoiceDialogue] dialogue video prepared, waiting for synchronized audio playback.");
        }
    }

    private void ApplyPreparedVideoAspectRatio(VideoPlayer player)
    {
        if (speakingVideoRawImage == null)
        {
            return;
        }

        Texture videoTexture = player == null ? null : player.texture;
        float aspectRatio = DefaultDialogueVideoAspectRatio;
        if (videoTexture != null && videoTexture.height > 0)
        {
            aspectRatio = (float)videoTexture.width / videoTexture.height;
        }

        ApplyVideoAspectRatio(aspectRatio);
        Debug.Log("[VoiceDialogue] prepared video aspectRatio=" + aspectRatio);
    }

    private void ApplyVideoAspectRatio(float aspectRatio)
    {
        GameObject fitterTarget = speakingVideoDisplay != null ? speakingVideoDisplay : speakingVideoRawImage.gameObject;
        AspectRatioFitter fitter = fitterTarget.GetComponent<AspectRatioFitter>();
        if (fitter == null)
        {
            fitter = fitterTarget.AddComponent<AspectRatioFitter>();
        }

        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = aspectRatio;
    }

    private void OnDialogueVideoError(VideoPlayer player, string message)
    {
        Debug.LogError("[VoiceDialogue] dialogue video error: " + message);
    }

    private void ActivateParents(Transform target)
    {
        if (target == null)
        {
            return;
        }

        if (target.parent != null)
        {
            ActivateParents(target.parent);
        }

        target.gameObject.SetActive(true);
    }

    public void CloseDialogue()
    {
        if (isRecording)
        {
            Microphone.End(microphoneDevice);
            isRecording = false;
        }

        StopSpeakingVideo();

        if (dialoguePanel != null)
        {
            dialoguePanel.SetActive(false);
        }
    }

    public void HideDialoguePanelForExternalJump()
    {
        if (isRecording)
        {
            Microphone.End(microphoneDevice);
            isRecording = false;
        }

        if (aiAudioSource != null)
        {
            aiAudioSource.Stop();
        }

        StopSpeakingVideo();

        if (dialoguePanel != null)
        {
            dialoguePanel.SetActive(false);
        }
    }

    public void ToggleRecording()
    {
        if (isSubmitting)
        {
            return;
        }

        if (isRecording)
        {
            StopRecordingAndSend();
        }
        else
        {
            StartRecording();
        }
    }

    public void StartRecording()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        SetStatus("WebGL recording is not available in this demo");
        Debug.LogWarning("VoiceDialogueController: Unity WebGL Microphone is not supported by this controller.");
        return;
#else
        Debug.Log("VoiceDialogueController: platform = " + Application.platform);
        Debug.Log("VoiceDialogueController: Microphone.devices.Length = " + Microphone.devices.Length);
        for (int i = 0; i < Microphone.devices.Length; i++)
        {
            Debug.Log("VoiceDialogueController: Microphone.devices[" + i + "] = " + Microphone.devices[i]);
        }
        Debug.Log("VoiceDialogueController: sampleRate = " + sampleRate + ", maxRecordSeconds = " + maxRecordSeconds);

        if (Microphone.devices.Length == 0)
        {
            SetStatus("No microphone found");
            Debug.LogError("VoiceDialogueController: no microphone device found.");
            return;
        }

        Debug.Log("VoiceDialogueController: microphones = " + string.Join(", ", Microphone.devices));
        microphoneDevice = Microphone.devices[0];
        Debug.Log("VoiceDialogueController: selected microphoneDevice = " + microphoneDevice);
        recordingClip = Microphone.Start(microphoneDevice, false, maxRecordSeconds, sampleRate);
        Debug.Log("VoiceDialogueController: Microphone.Start returned clip = " + (recordingClip == null ? "null" : recordingClip.name));
        Debug.Log("VoiceDialogueController: Microphone.IsRecording = " + Microphone.IsRecording(microphoneDevice));
        if (recordingClip != null)
        {
            Debug.Log("VoiceDialogueController: recordingClip samples=" + recordingClip.samples + ", channels=" + recordingClip.channels + ", frequency=" + recordingClip.frequency);
        }
        isRecording = true;
        SetStatus(StatusRecording);
        Debug.Log("VoiceDialogueController: recording started with " + microphoneDevice);
#endif
    }

    public void StopRecordingAndSend()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return;
#else
        if (!isRecording || recordingClip == null)
        {
            return;
        }

        int samplePosition = Microphone.GetPosition(microphoneDevice);
        Debug.Log("VoiceDialogueController: microphone sample position = " + samplePosition);
        Debug.Log("VoiceDialogueController: Microphone.IsRecording before End = " + Microphone.IsRecording(microphoneDevice));
        Debug.Log("VoiceDialogueController: recordedClip is null = " + (recordingClip == null));
        if (recordingClip != null)
        {
            Debug.Log("VoiceDialogueController: recordedClip samples=" + recordingClip.samples + ", channels=" + recordingClip.channels + ", frequency=" + recordingClip.frequency);
        }
        Microphone.End(microphoneDevice);
        isRecording = false;
        if (samplePosition <= 0)
        {
            SetRecordButtonVisible(false);
            SetStatus("Recording is empty, try again");
            Debug.LogWarning("VoiceDialogueController: empty recording.");
            StartCoroutine(PlayFallbackDialogueVideoAndRestoreRecordButton());
            return;
        }

        AudioClip trimmedClip = TrimClip(recordingClip, samplePosition);
        byte[] wavBytes = WavUtility.FromAudioClip(trimmedClip);
        Debug.Log("VoiceDialogueController: wav bytes = " + wavBytes.Length);
        StartCoroutine(SendVoiceDialogue(wavBytes));
#endif
    }

    private IEnumerator SendVoiceDialogue(byte[] wavBytes)
    {
        isSubmitting = true;
        SetRecordButtonVisible(false);

        try
        {
            SetStatus(StatusUploading);

            List<IMultipartFormSection> form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("audio", wavBytes, "player_question.wav", "audio/wav"),
                new MultipartFormDataSection("roleId", roleId),
                new MultipartFormDataSection("roleName", roleName),
                new MultipartFormDataSection("sceneId", sceneId)
            };

            string voiceDialogueUrl = BuildApiUrl("/api/voice-dialogue", voiceDialogueApiUrl);
            Debug.Log("VoiceDialogueController: voice-dialogue url=" + voiceDialogueUrl);

            using (UnityWebRequest request = UnityWebRequest.Post(voiceDialogueUrl, form))
            {
                request.timeout = Mathf.Max(1, requestTimeoutSeconds);
                SetStatus(StatusWaitingAi);
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    SetStatus(StatusVoiceSendFailed);
                    Debug.LogError("VoiceDialogueController: request failed: " + request.error);
                    yield return PlayFallbackDialogueVideo();
                    yield break;
                }

                string responseJson = request.downloadHandler.text;
                Debug.Log("VoiceDialogueController: response " + responseJson);

                VoiceDialogueResponse response = null;
                try
                {
                    response = JsonUtility.FromJson<VoiceDialogueResponse>(responseJson);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("VoiceDialogueController: response parse failed: " + ex.Message);
                }

                yield return HandleDialogueResponse(response);
            }
        }
        finally
        {
            SetRecordButtonVisible(true);
        }
    }

    private void ApplyDialogueResponse(VoiceDialogueResponse response)
    {
        StartCoroutine(HandleDialogueResponse(response));
    }

    private IEnumerator HandleDialogueResponse(VoiceDialogueResponse response)
    {
        if (response == null)
        {
            SetStatus("Response parse failed");
            if (transcriptText != null)
            {
                transcriptText.text = "未识别到语音文本";
            }

            if (replyText != null)
            {
                replyText.text = "未收到角色回复";
            }

            yield return PlayFallbackDialogueVideo();
            yield break;
        }

        if (!response.ok)
        {
            Debug.LogWarning("VoiceDialogueController: backend fallback. error=" + response.error);
        }
        else if (!string.IsNullOrEmpty(response.error))
        {
            Debug.LogWarning("VoiceDialogueController: backend returned warning: " + response.error);
        }

        if (transcriptText != null)
        {
            transcriptText.text = string.IsNullOrWhiteSpace(response.transcript) ? "未识别到语音文本" : response.transcript;
            Debug.Log("VoiceDialogueController: transcript length = " + (response.transcript == null ? 0 : response.transcript.Length));
        }
        else
        {
            Debug.LogWarning("VoiceDialogueController: Transcript Text is not assigned.");
        }

        if (replyText != null)
        {
            replyText.text = string.IsNullOrWhiteSpace(response.replyText) ? "未收到角色回复" : response.replyText;
            Debug.Log("VoiceDialogueController: replyText length = " + (response.replyText == null ? 0 : response.replyText.Length));
        }
        else
        {
            Debug.LogWarning("VoiceDialogueController: Reply Text is not assigned.");
        }

        if (HasValidAiVoiceResponse(response))
        {
            SetStatus(StatusPlayingAi);
            yield return PlayAiVoiceWithSuccessVideo(response);
        }
        else
        {
            SetStatus(StatusFallback);
            yield return PlayFallbackDialogueVideo();
        }
    }

    private bool HasValidAiVoiceResponse(VoiceDialogueResponse response)
    {
        return response != null
            && response.ok
            && !string.IsNullOrWhiteSpace(response.transcript)
            && !string.IsNullOrWhiteSpace(response.replyText)
            && !string.IsNullOrWhiteSpace(response.audioUrl);
    }

    private IEnumerator PlayAiVoiceWithSpeakingVideo(VoiceDialogueResponse response)
    {
        PlaySpeakingVideo(successSpeakingVideoFileName);

        if (!string.IsNullOrEmpty(response.audioUrl))
        {
            AudioType audioType = ResolveAudioType(response.audioUrl);
            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(response.audioUrl, audioType))
            {
                request.timeout = Mathf.Max(1, requestTimeoutSeconds);
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                    if (aiAudioSource != null && clip != null)
                    {
                        aiAudioSource.Stop();
                        aiAudioSource.clip = clip;
                        aiAudioSource.Play();
                    }
                    else
                    {
                        Debug.LogWarning("VoiceDialogueController: aiAudioSource or downloaded clip is not available.");
                    }
                }
                else
                {
                    SetStatus(StatusAudioPlaybackFailed);
                    Debug.LogWarning("VoiceDialogueController: audio download failed: " + request.error);
                }
            }
        }
        else
        {
            Debug.Log("VoiceDialogueController: audioUrl is empty, skip reply audio playback.");
        }

        isSubmitting = false;
        if (string.IsNullOrEmpty(response.audioUrl))
        {
            SetStatus(GetResponseStatus(response));
        }

        ContinueAfterDialogueResponse();
    }

    private IEnumerator PlayAiVoiceWithSuccessVideo(VoiceDialogueResponse response)
    {
        SetStatus(StatusPlayingAi);
        SetRecordButtonVisible(false);

        string successVideoFileName = GetSuccessVideoFileNameForCurrentRole();
        AudioClip aiClip = null;

        AudioType audioType = ResolveAudioType(response.audioUrl);
        using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(response.audioUrl, audioType))
        {
            request.timeout = Mathf.Max(1, requestTimeoutSeconds);
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                aiClip = DownloadHandlerAudioClip.GetContent(request);
                if (aiClip != null)
                {
                    Debug.Log($"AI audio clip length = {aiClip.length}, channels = {aiClip.channels}, frequency = {aiClip.frequency}");
                }
            }
            else
            {
                SetStatus(StatusAudioPlaybackFailed);
                Debug.LogWarning("VoiceDialogueController: audio download failed: " + request.error);
            }
        }

        if (aiClip == null || aiAudioSource == null)
        {
            Debug.LogWarning("VoiceDialogueController: AI audio is not available, falling back to preset response video.");
            yield return PlayFallbackDialogueVideo();
            yield break;
        }

        ConfigureSpeakingVideoAudio(false);
        bool videoStarted = PlayDialogueVideo(successVideoFileName, true, false);
        if (videoStarted && speakingVideoPlayer != null)
        {
            float prepareWaitSeconds = 0f;
            while (!speakingVideoPlayer.isPrepared && prepareWaitSeconds < 3f)
            {
                prepareWaitSeconds += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        aiAudioSource.Stop();
        aiAudioSource.clip = aiClip;

        if (videoStarted && speakingVideoPlayer != null && speakingVideoPlayer.isPrepared)
        {
            speakingVideoPlayer.isLooping = true;
            speakingVideoPlayer.Play();
        }
        else if (videoStarted)
        {
            playDialogueVideoOnPrepare = true;
        }

        aiAudioSource.Play();
        yield return WaitForAiAudioClipToFinish(aiClip);
        yield return new WaitForSecondsRealtime(1f);

        StopSpeakingVideo();
        if (speakingVideoPlayer != null)
        {
            speakingVideoPlayer.isLooping = false;
        }

        isSubmitting = false;
        SetStatus(StatusDialogueDone);

        ContinueAfterDialogueResponse();
    }

    private IEnumerator PlayFallbackDialogueVideo()
    {
        SetStatus(StatusFallback);

        string selectedFallbackVideoFileName = GetFallbackVideoFileNameForCurrentRole();

        if (string.IsNullOrWhiteSpace(selectedFallbackVideoFileName))
        {
            Debug.LogWarning("VoiceDialogueController: fallbackVideoFileName is empty.");
            isSubmitting = false;
            yield break;
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        string normalizedFallbackFileName = selectedFallbackVideoFileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
            ? selectedFallbackVideoFileName
            : selectedFallbackVideoFileName + ".mp4";
        string fallbackPath = Path.Combine(Application.streamingAssetsPath, "Videos", normalizedFallbackFileName);
        if (!File.Exists(fallbackPath))
        {
            SetStatus(StatusFallbackVideoMissing);
            Debug.LogWarning("VoiceDialogueController: fallback video missing. path=" + fallbackPath);
            isSubmitting = false;
            yield break;
        }
#endif

        ConfigureSpeakingVideoAudio(true);
        bool videoStarted = PlayDialogueVideo(selectedFallbackVideoFileName, false);
        yield return WaitForDialoguePlayback(videoStarted, false);
        isSubmitting = false;
        ContinueAfterDialogueResponse();
        yield break;
    }

    private void ContinueAfterDialogueResponse()
    {
        if (!string.IsNullOrEmpty(dialogueNextNodeId) && fmvController != null)
        {
            fmvController.PlayNodeFromOutside(dialogueNextNodeId);
            return;
        }

        SetStatus(StatusDialogueDone);
        SetRecordButtonVisible(true);
    }

    private string GetSuccessVideoFileNameForCurrentRole()
    {
        string currentRoleId = GetCurrentRoleId();
        if (!string.IsNullOrWhiteSpace(currentRoleId) && roleSuccessVideos != null)
        {
            for (int i = 0; i < roleSuccessVideos.Length; i++)
            {
                RoleDialogueVideoData data = roleSuccessVideos[i];
                if (data == null)
                {
                    continue;
                }

                if (string.Equals(data.roleId, currentRoleId, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(data.videoFileName))
                {
                    Debug.Log("VoiceDialogueController: success video for roleId=" + currentRoleId + " file=" + data.videoFileName);
                    return data.videoFileName;
                }
            }
        }

        string fallbackSuccessVideo = !string.IsNullOrWhiteSpace(defaultSuccessVideoFileName)
            ? defaultSuccessVideoFileName
            : successSpeakingVideoFileName;
        Debug.Log("VoiceDialogueController: success video default for roleId=" + currentRoleId + " file=" + fallbackSuccessVideo);
        return fallbackSuccessVideo;
    }

    private IEnumerator WaitForDialoguePlayback(bool videoStarted, bool audioStarted)
    {
        float maxDuration = 0f;

        if (audioStarted && aiAudioSource != null && aiAudioSource.clip != null)
        {
            maxDuration = Mathf.Max(maxDuration, aiAudioSource.clip.length);
        }

        if (videoStarted && speakingVideoPlayer != null)
        {
            float prepareWaitSeconds = 0f;
            while (!speakingVideoPlayer.isPrepared && prepareWaitSeconds < 3f)
            {
                prepareWaitSeconds += Time.unscaledDeltaTime;
                yield return null;
            }

            double videoLength = speakingVideoPlayer.length;
            if (!double.IsNaN(videoLength) && !double.IsInfinity(videoLength) && videoLength > 0.1d)
            {
                maxDuration = Mathf.Max(maxDuration, (float)videoLength);
            }
        }

        if (maxDuration <= 0f)
        {
            if (videoStarted && speakingVideoPlayer != null)
            {
                float videoWaitSeconds = 0f;
                while (speakingVideoPlayer.isPlaying && videoWaitSeconds < 30f)
                {
                    videoWaitSeconds += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            yield break;
        }

        float elapsed = 0f;
        while (elapsed < maxDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private IEnumerator WaitForAiAudioClipToFinish(AudioClip clip)
    {
        if (clip == null || aiAudioSource == null)
        {
            yield break;
        }

        float maxWait = Mathf.Max(0.1f, clip.length + 2f);
        float elapsed = 0f;
        while (elapsed < maxWait &&
               aiAudioSource != null &&
               aiAudioSource.clip == clip &&
               aiAudioSource.time < clip.length - 0.05f)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private string GetFallbackVideoFileNameForCurrentRole()
    {
        string currentRoleId = GetCurrentRoleId();
        if (!string.IsNullOrWhiteSpace(currentRoleId) && roleFallbackVideos != null)
        {
            for (int i = 0; i < roleFallbackVideos.Length; i++)
            {
                RoleFallbackVideoData data = roleFallbackVideos[i];
                if (data == null)
                {
                    continue;
                }

                if (string.Equals(data.roleId, currentRoleId, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(data.fallbackVideoFileName))
                {
                    Debug.Log("VoiceDialogueController: fallback video for roleId=" + currentRoleId + " file=" + data.fallbackVideoFileName);
                    return data.fallbackVideoFileName;
                }
            }
        }

        string fallback = !string.IsNullOrWhiteSpace(defaultFallbackVideoFileName)
            ? defaultFallbackVideoFileName
            : fallbackVideoFileName;
        Debug.Log("VoiceDialogueController: fallback video default for roleId=" + currentRoleId + " file=" + fallback);
        return fallback;
    }

    private string GetCurrentRoleId()
    {
        if (!string.IsNullOrWhiteSpace(roleId))
        {
            return roleId;
        }

        string[] keys =
        {
            "roleId",
            "selectedRoleId",
            "currentRoleId",
            "SelectedRoleId",
            "CurrentRoleId"
        };

        for (int i = 0; i < keys.Length; i++)
        {
            string value = PlayerPrefs.GetString(keys[i], "");
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "";
    }

    private IEnumerator PlayFallbackDialogueVideoAndRestoreRecordButton()
    {
        yield return PlayFallbackDialogueVideo();
        SetRecordButtonVisible(true);
    }

    private void SetRecordButtonVisible(bool visible)
    {
        if (recordButton == null)
        {
            return;
        }

        recordButton.gameObject.SetActive(visible);
        recordButton.interactable = visible;
    }

    private string GetResponseStatus(VoiceDialogueResponse response)
    {
        if (response == null)
        {
            return "语音发送失败，请重试";
        }

        if (string.Equals(response.source, "doubao", StringComparison.OrdinalIgnoreCase))
        {
            return "真实语音回复";
        }

        if (string.Equals(response.source, "mock", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrEmpty(response.error) ? "Demo 模式回复" : "真实语音失败，已使用 Demo 回复";
        }

        return "已收到回复";
    }

    private AudioType ResolveAudioType(string audioUrl)
    {
        if (string.IsNullOrEmpty(audioUrl))
        {
            return AudioType.UNKNOWN;
        }

        string urlWithoutQuery = audioUrl.Split('?')[0].ToLowerInvariant();
        if (urlWithoutQuery.EndsWith(".wav"))
        {
            return AudioType.WAV;
        }

        if (urlWithoutQuery.EndsWith(".mp3"))
        {
            return AudioType.MPEG;
        }

        return AudioType.MPEG;
    }

    private void PlaySpeakingVideo(string speakingVideoNodeId)
    {
        if (speakingVideoPlayer == null)
        {
            Debug.LogWarning("VoiceDialogueController: speakingVideoPlayer is not assigned.");
            return;
        }

        string fileName = string.IsNullOrEmpty(speakingVideoNodeId)
            ? speakingVideoFileName
            : speakingVideoNodeId.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? speakingVideoNodeId : speakingVideoNodeId + ".mp4";

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        string localPath = Path.Combine(Application.streamingAssetsPath, "Videos", fileName);
        if (!File.Exists(localPath))
        {
            Debug.LogWarning("VoiceDialogueController: speaking video missing, reply text will still be shown. path=" + localPath);
            return;
        }
#endif

        if (speakingVideoDisplay != null)
        {
            speakingVideoDisplay.SetActive(true);
        }

        EnsureSpeakingVideoOutput();
        ConfigureSpeakingVideoAudio(false);

        string videoUrl = BuildVideoUrl(fileName);
        speakingVideoPlayer.Stop();
        speakingVideoPlayer.source = VideoSource.Url;
        speakingVideoPlayer.url = videoUrl;
        speakingVideoPlayer.isLooping = true;
        speakingVideoPlayer.Play();

        Debug.Log("[VoiceDialogue] video url=" + videoUrl);
    }

    public void StopSpeakingVideo()
    {
        if (speakingVideoPlayer != null)
        {
            speakingVideoPlayer.Stop();
        }

        if (speakingVideoDisplay != null)
        {
            speakingVideoDisplay.SetActive(false);
        }
    }

    private AudioClip TrimClip(AudioClip source, int samplePosition)
    {
        int channels = source.channels;
        float[] samples = new float[samplePosition * channels];
        source.GetData(samples, 0);

        AudioClip trimmed = AudioClip.Create("player_question", samplePosition, channels, source.frequency, false);
        trimmed.SetData(samples, 0);
        return trimmed;
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
        string videoPath = Path.Combine(Application.streamingAssetsPath, "Videos", videoFileName);
        return "file:///" + videoPath.Replace("\\", "/");
#else
        return Path.Combine(Application.streamingAssetsPath, "Videos", videoFileName);
#endif
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

    private void SetStatus(string value)
    {
        if (statusText != null)
        {
            statusText.text = value;
        }
    }
}

[Serializable]
public class VoiceDialogueResponse
{
    public bool ok;
    public string transcript;
    public string replyText;
    public string audioUrl;
    public string speakingVideoNodeId;
    public string emotion;
    public string source;
    public string error;
}

public static class WavUtility
{
    public static byte[] FromAudioClip(AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        byte[] pcmData = ConvertSamplesToPcm16(samples);
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            int sampleRate = clip.frequency;
            short channels = (short)clip.channels;
            short bitsPerSample = 16;
            int byteRate = sampleRate * channels * bitsPerSample / 8;
            short blockAlign = (short)(channels * bitsPerSample / 8);

            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + pcmData.Length);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write(bitsPerSample);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(pcmData.Length);
            writer.Write(pcmData);

            return stream.ToArray();
        }
    }

    private static byte[] ConvertSamplesToPcm16(float[] samples)
    {
        byte[] bytes = new byte[samples.Length * 2];

        for (int i = 0; i < samples.Length; i++)
        {
            float clamped = Mathf.Clamp(samples[i], -1f, 1f);
            short value = (short)(clamped * short.MaxValue);
            bytes[i * 2] = (byte)(value & 0xff);
            bytes[i * 2 + 1] = (byte)((value >> 8) & 0xff);
        }

        return bytes;
    }
}
