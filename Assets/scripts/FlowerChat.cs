using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using TMPro;
using SimpleJSON;

public class FlowerChat : MonoBehaviour
{
    private const string ApiUrl =
        "https://integrate.api.nvidia.com/v1/chat/completions";

    [SerializeField] private string apiKey = "Your_API_Key_Here";

    [Header("대화 설정")]
    [SerializeField, Min(1)] private int maxConversationPairs = 5;
    [SerializeField, Min(1)] private int maxInputCharacters = 1000;

    [Header("저장 설정")]
    [Tooltip("체크하면 개발용 JSON 저장, 해제하면 전시용 저장 안 함")]
    [SerializeField] private bool developerMode = false;

    [Header("UI 연결")]
    public TMP_Text flowerSpeech;
    public TMP_InputField userInput;

    private FlowerMemory memory;
    private FlowerStorage storage;

    private bool isSending;
    private int sessionVersion;
    private UnityWebRequest activeRequest;

    private void Awake()
    {
        memory = new FlowerMemory(maxConversationPairs);
        storage = new FlowerStorage(developerMode);
    }

    private void OnEnable()
    {
        if (userInput != null)
        {
            userInput.onSubmit.AddListener(OnSubmitFromInput);
        }
    }

    private void OnDisable()
    {
        if (userInput != null)
        {
            userInput.onSubmit.RemoveListener(OnSubmitFromInput);
        }

        CancelRequest();
        memory?.Clear();

        ShowMessage("");

        if (userInput != null)
        {
            userInput.text = "";
        }
    }

    private void ShowMessage(string text)
    {
        if (flowerSpeech != null)
        {
            flowerSpeech.text = text;
        }
    }

    public IEnumerator SendMessageToNvidia(string userMessage)
    {
        if (isSending || string.IsNullOrWhiteSpace(userMessage))
        {
            yield break;
        }

        int inputLimit = Mathf.Max(1, maxInputCharacters);

        if (userMessage.Length > inputLimit)
        {
            ShowMessage(
                "한 번에 " + inputLimit + "자 이내로 입력해주세요."
            );

            yield break;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ShowMessage("Unity Inspector에서 API Key를 입력해주세요.");
            yield break;
        }

        isSending = true;
        int requestSession = sessionVersion;

        if (userInput != null)
        {
            userInput.interactable = false;
        }

        ShowMessage("이야기를 듣고 있어요...");

        try
        {
            // FlowerMemory에서 최근 대화 + 지금 질문 가져오기
            var messages = new JSONArray();

            var instruction = new JSONObject();
            instruction["role"] = "system";
            instruction["content"] =
                "한국어로 따뜻하게 답해주세요. " +
                "이모지나 그림 문자를 사용하지 말고 일반 문장으로 답해주세요.";

            messages.Add(instruction);

            var conversation = memory.BuildMessages(userMessage);

            for (int i = 0; i < conversation.Count; i++)
            {
                messages.Add(conversation[i]);
            }

            var body = new JSONObject();
            body["model"] = "deepseek-ai/deepseek-v4.1-flash";
            body["messages"] = messages;
            body["stream"] = false;
            body["max_tokens"] = 1024;

            using (var request = new UnityWebRequest(ApiUrl, "POST"))
            {
                activeRequest = request;

                byte[] bodyRaw =
                    System.Text.Encoding.UTF8.GetBytes(body.ToString());

                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 90;

                request.SetRequestHeader(
                    "Content-Type",
                    "application/json"
                );

                request.SetRequestHeader(
                    "Authorization",
                    "Bearer " + apiKey.Trim()
                );

                yield return request.SendWebRequest();

                // 초기화 이전의 답변은 반영하지 않음
                if (requestSession != sessionVersion)
                {
                    yield break;
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError(
                      $"NVIDIA 요청 실패: HTTP {request.responseCode} / {request.error}\n" +
                      $"응답 본문: {request.downloadHandler?.text}"
                      );

                    ShowMessage(
                        "연결에 문제가 생겼어요. 입력한 내용을 다시 보내주세요."
                    );

                    yield break;
                }

                string reply = null;

                try
                {
                    var json = JSON.Parse(request.downloadHandler.text);

                    reply =
                        json["choices"][0]["message"]["content"].Value;
                }
                catch (System.Exception)
                {
                    Debug.LogError("NVIDIA 응답을 읽지 못했습니다.");
                }

                if (string.IsNullOrWhiteSpace(reply))
                {
                    ShowMessage("답변을 받지 못했어요. 다시 보내주세요.");
                    yield break;
                }

                // 최근 대화 기억
                memory.AddPair(userMessage, reply);

                // 개발 모드일 때 JSON 저장
                if (developerMode)
                {
                    storage.SavePair(userMessage, reply);
                }

                ShowMessage(reply);

                if (userInput != null)
                {
                    userInput.text = "";
                }
            }
        }
        finally
        {
            if (requestSession == sessionVersion)
            {
                activeRequest = null;
                isSending = false;

                if (userInput != null)
                {
                    userInput.interactable = true;

                    if (isActiveAndEnabled)
                    {
                        userInput.ActivateInputField();
                    }
                }
            }
        }
    }

    // Enter로 전송
    public void OnSubmitFromInput(string text)
    {
        if (!isActiveAndEnabled ||
            isSending ||
            string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        StartCoroutine(SendMessageToNvidia(text));
    }

    // 전송 버튼의 On Click에 연결
    public void OnSendButtonClicked()
    {
        if (userInput != null)
        {
            OnSubmitFromInput(userInput.text);
        }
    }

    private void CancelRequest()
    {
        sessionVersion++;

        if (activeRequest != null)
        {
            activeRequest.Abort();
            activeRequest = null;
        }

        isSending = false;

        if (userInput != null)
        {
            userInput.interactable = true;
        }
    }

    // 새 대화 버튼의 On Click에 연결
    public void ResetConversation()
    {
        CancelRequest();

        // 최근 대화 기억 초기화
        memory = new FlowerMemory(maxConversationPairs);

        // 새 저장 세션 시작
        if (storage != null)
        {
            storage.BeginSession(developerMode);
        }

        ShowMessage("");

        if (userInput != null)
        {
            userInput.text = "";

            if (isActiveAndEnabled)
            {
                userInput.ActivateInputField();
            }
        }
    }

    // 컴포넌트 메뉴에서 저장 위치 확인
    [ContextMenu("Print JSON Save Folder")]
    public void PrintJsonSaveFolder()
    {
        if (storage != null)
        {
            storage.PrintSaveFolder();
        }
        else
        {
            Debug.Log("Play 실행 후 저장 위치를 확인해주세요.");
        }
    }

    // 컴포넌트 메뉴에서 최근 대화 기억 확인
    [ContextMenu("Print Conversation Memory")]
    public void PrintConversationMemory()
    {
        if (memory == null)
        {
            Debug.Log("Play 실행 후 확인해주세요.");
            return;
        }

        memory.PrintMemory();
    }
}