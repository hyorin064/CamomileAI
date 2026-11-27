using UnityEngine;             // MonoBehaviour, Debug 등 Unity 기본 기능
using UnityEngine.Networking;  // HTTP 요청을 보내는 UnityWebRequest
using System.Collections;      // IEnumerator: 코루틴 작성에 사용
using TMPro;                   // TextMeshPro 텍스트와 입력창
using SimpleJSON;              // JSON 요청 생성 및 응답 분석

public class FlowerChat : MonoBehaviour
{
    // NVIDIA에 대화 요청을 보내는 주소
    // const: 실행 중 값이 바뀌지 않는 상수
    private const string ApiUrl =
        "https://integrate.api.nvidia.com/v1/chat/completions";

    // private: 다른 클래스에서 직접 접근하지 못하도록 제한
    // SerializeField: private 변수도 Unity Inspector에 표시하고 저장
    // 실제 키는 소스 코드에 적지 않기
    [SerializeField] private string apiKey = "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\r\n";

    [Header("대화 설정")]

    // AI에게 전달할 최근 대화의 최대 쌍 수
    // 한 쌍 = 사용자 메시지 1개 + AI 답변 1개
    // Min(1): Inspector에서 설정할 수 있는 최솟값을 1로 지정
    [SerializeField, Min(1)] private int maxConversationPairs = 5;

    // 사용자가 한 번에 입력할 수 있는 최대 글자 수
    [SerializeField, Min(1)] private int maxInputCharacters = 1000;

    [Header("저장 설정")]

    // 개발 중에는 대화를 파일로 저장하고,
    // 전시 중에는 저장하지 않도록 선택하는 설정
    [Tooltip("체크하면 개발용 JSON 저장, 해제하면 전시용 저장 안 함")]
    [SerializeField] private bool developerMode = false;

    [Header("UI 연결")]

    // AI 답변을 표시할 텍스트: Inspector에서 연결
    public TMP_Text flowerSpeech;

    // 사용자가 글을 입력할 입력창: Inspector에서 연결
    public TMP_InputField userInput;

    // 최근 대화를 관리하는 별도 클래스
    private FlowerMemory memory;

    // 대화를 JSON 파일로 저장하는 별도 클래스
    private FlowerStorage storage;

    // 요청 처리 중인지 확인하는 변수
    // true인 동안 추가 전송을 막음
    private bool isSending;

    // 새 대화를 시작하거나 요청을 취소할 때 증가하는 번호
    // 취소 이전에 보낸 요청의 답변이 나중에 표시되는 것을 막음
    private int sessionVersion;

    // 현재 진행 중인 요청
    // 취소할 때 이 요청에 Abort()를 호출
    private UnityWebRequest activeRequest;

    // 오브젝트가 초기화될 때 실행
    // OnEnable보다 먼저 필요한 객체를 준비
    private void Awake()
    {
        memory = new FlowerMemory(maxConversationPairs);
        storage = new FlowerStorage(developerMode);
    }

    // 컴포넌트가 활성화될 때 실행
    private void OnEnable()
    {
        if (userInput != null)
        {
            // 입력창에서 제출 이벤트가 발생하면 이 함수를 실행
            userInput.onSubmit.AddListener(OnSubmitFromInput);
        }
    }

    // 컴포넌트 또는 오브젝트가 비활성화될 때 실행
    private void OnDisable()
    {
        if (userInput != null)
        {
            // 활성화할 때 등록했던 이벤트를 제거
            // 반복 활성화 시 이벤트가 중복 등록되는 것을 방지
            userInput.onSubmit.RemoveListener(OnSubmitFromInput);
        }

        // 진행 중인 요청 취소
        CancelRequest();

        // 메모리에 있는 최근 대화 삭제
        // ?.는 memory가 null이 아닐 때만 Clear()를 실행한다는 뜻
        memory?.Clear();

        // 화면에 표시된 AI 답변 지우기
        ShowMessage("");

        if (userInput != null)
        {
            // 입력창 내용 지우기
            userInput.text = "";
        }
    }

    // AI 답변 영역에 원하는 문장을 표시하는 공통 함수
    private void ShowMessage(string text)
    {
        // UI가 연결되지 않았을 때 발생할 오류를 방지
        if (flowerSpeech != null)
        {
            flowerSpeech.text = text;
        }
    }

    // NVIDIA에 메시지를 보내고 답변을 받는 코루틴
    // 코루틴은 요청을 기다리는 동안 Unity의 화면 처리를 계속할 수 있게 함
    public IEnumerator SendMessageToNvidia(string userMessage)
    {
        // 이미 전송 중이거나 내용이 없으면 종료
        // IsNullOrWhiteSpace: null, 빈 문자열, 공백만 있는 문자열 확인
        if (isSending || string.IsNullOrWhiteSpace(userMessage))
        {
            // yield break: 코루틴 종료
            yield break;
        }

        // 실제 검사에서도 입력 제한이 최소 1 이상이 되도록 보정
        int inputLimit = Mathf.Max(1, maxInputCharacters);

        if (userMessage.Length > inputLimit)
        {
            ShowMessage(
                "한 번에 " + inputLimit + "자 이내로 입력해주세요."
            );

            yield break;
        }

        // 키가 없으면 API 요청을 보내지 않음
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ShowMessage("Unity Inspector에서 API Key를 입력해주세요.");
            yield break;
        }

        // 중복 전송 방지 시작
        isSending = true;

        // 이번 요청이 시작된 시점의 세션 번호를 보관
        int requestSession = sessionVersion;

        if (userInput != null)
        {
            // 답변을 기다리는 동안 입력창 조작 차단
            userInput.interactable = false;
        }

        ShowMessage("이야기를 듣고 있어요...");

        // 아래 작업을 마치거나 중간에 종료하더라도
        // finally에서 입력창과 전송 상태를 정리
        try
        {
            // API에 보낼 메시지 목록
            var messages = new JSONArray();

            // system 메시지: AI의 역할과 대화 규칙을 지정
            var instruction = new JSONObject();
            instruction["role"] = "system";

            // + 기호는 여러 문자열을 하나로 연결
            instruction["content"] =
                "평서문 끝에는 반드시 온점(.)을, 질문 끝에는 물음표(?)를 붙여주세."+
                "각 문장이 끝날 때 실제 줄바꿈을 한 번 넣어, 한 줄에 한 문장씩 작성해주세요."+
                "문장 사이에 빈 줄은 넣지 마세요." +
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

            // 메시지 목록 맨 앞에 시스템 지침 추가
            messages.Add(instruction);

            // 최근 대화 기록과 이번 사용자 메시지를 가져옴
            var conversation = memory.BuildMessages(userMessage);

            // 가져온 메시지를 순서대로 요청 목록에 추가
            for (int i = 0; i < conversation.Count; i++)
            {
                messages.Add(conversation[i]);
            }

            // HTTP 요청의 본문(JSON) 생성
            var body = new JSONObject();

            // 사용할 모델 이름
            body["model"] = "nvidia/nemotron-3.5-bolt-e4b";

            // 시스템 지침 + 최근 대화 + 이번 메시지
            body["messages"] = messages;

            // false: 완성된 답변을 한 번에 받음
            // true로 변경하려면 조각별 응답을 처리하는 코드가 필요
            body["stream"] = false;

            // 생성할 수 있는 최대 토큰 수
            // 토큰은 모델이 글을 처리하는 단위로, 글자 수와는 다름
            body["max_tokens"] = 1024;

            // 답변 생성의 무작위성을 조절하는 값
            body["temperature"] = 1.0;

            // 다음 토큰 후보를 누적 확률 기준으로 제한하는 값
            body["top_p"] = 0.95;

            // 모델의 별도 추론 모드를 끄는 설정
            var chatSettings = new JSONObject();
            chatSettings["enable_thinking"] = false;
            body["chat_template_kwargs"] = chatSettings;

            // POST 방식의 HTTP 요청 생성
            // using: 이 블록을 벗어나면 요청과 연결된 리소스를 정리
            using (var request = new UnityWebRequest(ApiUrl, "POST"))
            {
                // 취소할 수 있도록 현재 요청을 보관
                activeRequest = request;

                // JSON을 문자열로 바꾼 뒤 UTF-8 바이트로 변환
                // UTF-8은 한글을 포함한 텍스트를 전송하는 인코딩 방식
                byte[] bodyRaw =
                    System.Text.Encoding.UTF8.GetBytes(body.ToString());

                // 서버에 보낼 데이터 지정
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);

                // 서버에서 받은 응답을 메모리에 저장
                request.downloadHandler = new DownloadHandlerBuffer();

                // 요청 제한 시간을 90초로 설정
                request.timeout = 90;

                // 전송하는 본문이 JSON 형식임을 서버에 알림
                request.SetRequestHeader(
                    "Content-Type",
                    "application/json"
                );

                // API 키로 요청 인증
                // Trim(): 키 앞뒤의 불필요한 공백 제거
                request.SetRequestHeader(
                    "Authorization",
                    "Bearer " + apiKey.Trim()
                );

                // 요청 전송 후 완료될 때까지 이 코루틴만 대기
                yield return request.SendWebRequest();

                // 기다리는 사이 새 대화나 취소가 발생했으면
                // 이전 요청의 결과는 화면에 반영하지 않음
                if (requestSession != sessionVersion)
                {
                    yield break;
                }

                // 네트워크 또는 HTTP 오류 등으로 요청이 실패한 경우
                if (request.result != UnityWebRequest.Result.Success)
                {
                    // 개발자가 원인을 확인할 수 있도록 Console에 출력
                    // $"..." 안의 {변수}에는 실제 값이 들어감
                    Debug.LogError(
                        $"NVIDIA 요청 실패: HTTP {request.responseCode} / {request.error}\n" +
                        $"응답 본문: {request.downloadHandler?.text}"
                    );

                    // 사용자 화면에는 간단한 안내 표시
                    ShowMessage(
                        "연결에 문제가 생겼어요. 입력한 내용을 다시 보내주세요."
                    );

                    yield break;
                }

                // AI 답변을 담을 변수
                string reply = null;

                try
                {
                    // 서버가 보낸 JSON 문자열을 분석
                    var json = JSON.Parse(request.downloadHandler.text);

                    // 첫 번째 응답 후보의 답변 본문을 가져옴
                    // [0]: 배열의 첫 번째 항목
                    reply =
                        json["choices"][0]["message"]["content"].Value;
                }
                catch (System.Exception)
                {
                    // JSON 분석 또는 응답 접근 중 예외가 발생한 경우
                    Debug.LogError("NVIDIA 응답을 읽지 못했습니다.");
                }

                // 요청에 성공했어도 답변 내용이 비어 있으면 종료
                if (string.IsNullOrWhiteSpace(reply))
                {
                    ShowMessage("답변을 받지 못했어요. 다시 보내주세요.");
                    yield break;
                }

                // 정상적으로 받은 대화를 최근 대화 기록에 추가
                memory.AddPair(userMessage, reply);

                // 개발자 모드일 때만 파일 저장 실행
                if (developerMode)
                {
                    storage.SavePair(userMessage, reply);
                }

                // 완성된 AI 답변을 화면에 표시
                ShowMessage(reply);

                if (userInput != null)
                {
                    // 전송에 성공한 입력 내용 지우기
                    userInput.text = "";
                }
            }
        }
        finally
        {
            // 아직 이번 요청과 같은 세션일 때만 상태 정리
            // 이전 요청이 새 세션의 상태를 바꾸지 않도록 방지
            if (requestSession == sessionVersion)
            {
                activeRequest = null;
                isSending = false;

                if (userInput != null)
                {
                    // 다시 입력할 수 있도록 활성화
                    userInput.interactable = true;

                    if (isActiveAndEnabled)
                    {
                        // 입력창에 포커스를 주어 바로 타이핑할 수 있게 함
                        userInput.ActivateInputField();
                    }
                }
            }
        }
    }

    // 입력창의 제출 이벤트에서 호출되는 함수
    public void OnSubmitFromInput(string text)
    {
        // 비활성 상태, 전송 중, 빈 입력이면 처리하지 않음
        if (!isActiveAndEnabled ||
            isSending ||
            string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // IEnumerator 함수를 코루틴으로 실행
        StartCoroutine(SendMessageToNvidia(text));
    }

    // 전송 버튼의 On Click()에 연결할 함수
    public void OnSendButtonClicked()
    {
        if (userInput != null)
        {
            // 버튼도 입력창 제출과 같은 전송 경로를 사용
            OnSubmitFromInput(userInput.text);
        }
    }

    // 진행 중인 요청을 취소하고 입력 가능한 상태로 복구
    private void CancelRequest()
    {
        // 기존 요청이 더 이상 유효하지 않음을 표시
        sessionVersion++;

        if (activeRequest != null)
        {
            // 진행 중인 네트워크 요청 중단
            activeRequest.Abort();
            activeRequest = null;
        }

        isSending = false;

        if (userInput != null)
        {
            userInput.interactable = true;
        }
    }

    // 새 대화 버튼의 On Click()에 연결할 함수
    public void ResetConversation()
    {
        // 답변 대기 중이어도 기존 요청 취소
        CancelRequest();

        // 새 메모리 객체를 만들어 최근 대화 기록 초기화
        memory = new FlowerMemory(maxConversationPairs);

        if (storage != null)
        {
            // 저장 관리 클래스에 새 세션 시작을 알림
            // 실제 저장 여부는 developerMode 설정에 따름
            storage.BeginSession(developerMode);
        }

        // 화면의 AI 답변 지우기
        ShowMessage("");

        if (userInput != null)
        {
            // 입력 내용 지우기
            userInput.text = "";

            if (isActiveAndEnabled)
            {
                userInput.ActivateInputField();
            }
        }
    }

    // Inspector의 컴포넌트 메뉴에서 실행할 수 있는 확인 기능
    [ContextMenu("Print JSON Save Folder")]
    public void PrintJsonSaveFolder()
    {
        if (storage != null)
        {
            // 저장 경로를 Console에 출력
            storage.PrintSaveFolder();
        }
        else
        {
            // Awake가 실행되기 전에는 storage가 준비되지 않을 수 있음
            Debug.Log("Play 실행 후 저장 위치를 확인해주세요.");
        }
    }

    // Inspector의 컴포넌트 메뉴에서 최근 대화 확인
    [ContextMenu("Print Conversation Memory")]
    public void PrintConversationMemory()
    {
        if (memory == null)
        {
            Debug.Log("Play 실행 후 확인해주세요.");
            return;
        }

        // 현재 메모리에 남아 있는 최근 대화를 Console에 출력
        memory.PrintMemory();
    }
}