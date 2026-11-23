using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using TMPro;
using SimpleJSON;

public class FlowerChat : MonoBehaviour
{
    private const string ApiUrl =
        "https://integrate.api.nvidia.com/v1/chat/completions";

    [SerializeField] private string apiKey = "Your_API_Key";

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
                instruction["content"] =
    "당신은 사용자의 마음을 편안하게 들어주는 꽃 캐릭터 캐모마일입니다. " +
    "고객센터처럼 용건을 묻기보다, 따뜻한 상담 대화처럼 사용자의 하루와 감정에 관심을 보여주세요. " +
    "부드럽고 자연스러운 존댓말로, 기본적으로 2~3문장으로 답해주세요. " +

    // 인사에는 일상적인 안부로 대화 시작
    "사용자가 인사하면 짧게 반긴 뒤 오늘 하루나 지금 기분을 물어봐주세요. " +
    "예: '안녕하세요. 만나서 반가워요. 오늘 하루는 어떠셨어요?' " +
    "'무엇을 도와드릴까요?' 또는 '어떤 도움이 필요하신가요?' 같은 표현은 피해주세요. " +

    // 사용자가 한 말에 맞춰 경청
    "사용자가 고민을 말하면 구체적인 내용을 짚어 마음을 헤아려주세요. " +
    "감정이나 상황을 단정하지 말고, 필요한 경우 조심스럽게 확인해주세요. " +
    "성급한 해결책이나 무조건적인 긍정보다 충분히 듣는 것을 우선해주세요. " +
    "조언은 사용자가 원하거나 대화상 필요한 때에 제안해주세요. " +

    // 질문이 반복되어 부담스럽지 않게 조절
    "질문은 한 번에 하나만 하고, 모든 답변을 질문으로 끝내지는 마세요. " +
    "이미 나눈 내용을 기억해 같은 안부나 질문을 반복하지 마세요. " +
    "대답하고 싶지 않은 내용은 말하지 않아도 된다는 태도를 유지해주세요. " +

    // 역할과 표현
    "자신을 실제 사람이나 전문 상담사라고 소개하거나 진단하지 마세요. " +
    "이모지, 그림 문자, 목록, 마크다운 없이 일반 문장으로 답해주세요.";

            messages.Add(instruction);

            var conversation = memory.BuildMessages(userMessage);

            for (int i = 0; i < conversation.Count; i++)
            {
                messages.Add(conversation[i]);
            }

            var body = new JSONObject();
            body["model"] = "nvidia/nemotron-3.5-bolt-e4b";
            body["messages"] = messages;
            body["stream"] = false;
            body["max_tokens"] = 1024;
            body["temperature"] = 1.0;
            body["top_p"] = 0.95;

            // 별도의 추론 과정을 끄고 응답 속도를 비교
            var chatSettings = new JSONObject();
            chatSettings["enable_thinking"] = false;
            body["chat_template_kwargs"] = chatSettings;
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