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
    [Header("API")]
    public string voiceDialogueApiUrl = "http://localhost:3000/api/voice-dialogue";

    [Header("Role")]
    public string roleId = "investigator";
    public string roleName = "Mozart";
    public string sceneId = "dialogue_scene";
    public string speakingVideoFileName = "role_speaking.mp4";

    [Header("Recording")]
    public int sampleRate = 16000;
    public int maxRecordSeconds = 15;

    [Header("UI")]
    public GameObject dialoguePanel;
    public Button recordButton;
    public TMP_Text statusText;
    public TMP_Text transcriptText;
    public TMP_Text replyText;

    [Header("Playback")]
    public AudioSource aiAudioSource;
    public VideoPlayer speakingVideoPlayer;
    public GameObject speakingVideoDisplay;

    private AudioClip recordingClip;
    private string microphoneDevice;
    private bool isRecording;
    private bool isSubmitting;

    private void Awake()
    {
        ResolveDialoguePanel();
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

        SetStatus("Ready to record");
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

    private void ResolveAudioSource()
    {
        if (aiAudioSource != null)
        {
            return;
        }

        aiAudioSource = GetComponent<AudioSource>();
        if (aiAudioSource == null)
        {
            aiAudioSource = gameObject.AddComponent<AudioSource>();
        }
    }

    public void OpenDialogue(string roleIdValue, string roleNameValue, string sceneIdValue)
    {
        roleId = string.IsNullOrEmpty(roleIdValue) ? roleId : roleIdValue;
        roleName = string.IsNullOrEmpty(roleNameValue) ? roleName : roleNameValue;
        sceneId = string.IsNullOrEmpty(sceneIdValue) ? sceneId : sceneIdValue;

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

        SetStatus("Ready to record");
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
        if (Microphone.devices.Length == 0)
        {
            SetStatus("No microphone found");
            Debug.LogError("VoiceDialogueController: no microphone device found.");
            return;
        }

        Debug.Log("VoiceDialogueController: microphones = " + string.Join(", ", Microphone.devices));
        microphoneDevice = Microphone.devices[0];
        recordingClip = Microphone.Start(microphoneDevice, false, maxRecordSeconds, sampleRate);
        isRecording = true;
        SetStatus("Recording, click again to send");
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
        Microphone.End(microphoneDevice);
        isRecording = false;

        if (samplePosition <= 0)
        {
            SetStatus("Recording is empty, try again");
            Debug.LogWarning("VoiceDialogueController: empty recording.");
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
        SetStatus("Sending voice...");

        List<IMultipartFormSection> form = new List<IMultipartFormSection>
        {
            new MultipartFormFileSection("audio", wavBytes, "player_question.wav", "audio/wav"),
            new MultipartFormDataSection("roleId", roleId),
            new MultipartFormDataSection("roleName", roleName),
            new MultipartFormDataSection("sceneId", sceneId)
        };

        using (UnityWebRequest request = UnityWebRequest.Post(voiceDialogueApiUrl, form))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                SetStatus("Voice request failed");
                Debug.LogError("VoiceDialogueController: request failed: " + request.error);
                isSubmitting = false;
                yield break;
            }

            string responseJson = request.downloadHandler.text;
            Debug.Log("VoiceDialogueController: response " + responseJson);

            VoiceDialogueResponse response = JsonUtility.FromJson<VoiceDialogueResponse>(responseJson);
            ApplyDialogueResponse(response);
        }
    }

    private void ApplyDialogueResponse(VoiceDialogueResponse response)
    {
        if (response == null)
        {
            SetStatus("Response parse failed");
            isSubmitting = false;
            return;
        }

        if (!response.ok)
        {
            Debug.LogWarning("VoiceDialogueController: backend fallback. error=" + response.error);
        }

        if (transcriptText != null)
        {
            transcriptText.text = string.IsNullOrEmpty(response.transcript) ? "" : response.transcript;
            Debug.Log("VoiceDialogueController: transcript displayed: " + response.transcript);
        }
        else
        {
            Debug.LogWarning("VoiceDialogueController: Transcript Text is not assigned.");
        }

        if (replyText != null)
        {
            replyText.text = string.IsNullOrEmpty(response.replyText) ? "(No reply text)" : response.replyText;
            Debug.Log("VoiceDialogueController: reply displayed: " + response.replyText);
        }
        else
        {
            Debug.LogWarning("VoiceDialogueController: Reply Text is not assigned.");
        }

        SetStatus("Character replied");
        StartCoroutine(PlayReply(response));
    }

    private IEnumerator PlayReply(VoiceDialogueResponse response)
    {
        PlaySpeakingVideo(response.speakingVideoNodeId);

        if (!string.IsNullOrEmpty(response.audioUrl))
        {
            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(response.audioUrl, AudioType.MPEG))
            {
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
                }
                else
                {
                    Debug.LogError("VoiceDialogueController: audio download failed: " + request.error);
                }
            }
        }

        isSubmitting = false;
        SetStatus("Ready to record");
    }

    private void PlaySpeakingVideo(string speakingVideoNodeId)
    {
        if (speakingVideoPlayer == null)
        {
            return;
        }

        string fileName = string.IsNullOrEmpty(speakingVideoNodeId) ? speakingVideoFileName : speakingVideoNodeId + ".mp4";

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

        speakingVideoPlayer.Stop();
        speakingVideoPlayer.source = VideoSource.Url;
        speakingVideoPlayer.url = BuildVideoUrl(fileName);
        speakingVideoPlayer.isLooping = true;
        speakingVideoPlayer.Play();

        Debug.Log("VoiceDialogueController: play speaking video " + speakingVideoPlayer.url);
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
