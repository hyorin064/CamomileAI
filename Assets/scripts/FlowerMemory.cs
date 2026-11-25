using System.Collections.Generic; // Queue 같은 자료구조 사용
using SimpleJSON;                 // JSON 배열과 객체 생성
using UnityEngine;                // Debug.Log 사용

// 최근 대화를 임시로 기억하는 클래스
// MonoBehaviour가 아니므로 FlowerChat에서 new로 만들어 사용
public class FlowerMemory
{
    // 사용자 질문과 AI 답변을 하나의 묶음으로 보관
    // private: FlowerMemory 안에서만 사용하는 클래스
    private class ConversationPair
    {
        public string user;      // 사용자가 보낸 메시지
        public string assistant; // AI가 보낸 답변
    }

    // 대화 쌍을 저장하는 큐
    // 먼저 들어온 대화가 먼저 나가는 선입선출(FIFO) 구조
    // readonly: 생성자에서 지정한 큐를 다른 큐로 바꿀 수 없음
    // 단, 큐 안의 대화를 추가하거나 삭제하는 것은 가능
    private readonly Queue<ConversationPair> conversationQueue;

    // 기억할 수 있는 최대 대화 쌍 수
    private readonly int maxPairs;

    // 현재 큐에 들어 있는 대화 쌍 수를 외부에 알려주는 속성
    // => 오른쪽 값을 반환하며, 외부에서 Count를 직접 수정할 수 없음
    public int Count => conversationQueue.Count;

    // 생성자: new FlowerMemory(...)를 실행할 때 호출
    // 숫자를 지정하지 않으면 기본적으로 최근 5쌍을 기억
    public FlowerMemory(int maxConversationPairs = 5)
    {
        // 전달받은 숫자가 0이나 음수여도 최소 1쌍은 기억하도록 설정
        maxPairs = System.Math.Max(1, maxConversationPairs);

        // 대화를 담을 빈 큐 생성
        conversationQueue = new Queue<ConversationPair>();
    }

    // API에서 정상적인 답변을 받은 뒤
    // 사용자 메시지와 AI 답변을 함께 저장하는 함수
    public void AddPair(string userMessage, string assistantReply)
    {
        // 둘 중 하나라도 null, 빈 문자열, 공백뿐이면 저장하지 않음
        if (string.IsNullOrWhiteSpace(userMessage) ||
            string.IsNullOrWhiteSpace(assistantReply))
        {
            return;
        }

        // 질문과 답변을 하나의 ConversationPair로 만들어
        // 큐의 맨 뒤에 추가
        conversationQueue.Enqueue(
            new ConversationPair
            {
                user = userMessage,
                assistant = assistantReply
            }
        );

        // 최대 개수를 초과하면 가장 오래된 대화부터 제거
        // 예: 최대 5쌍일 때 6번째를 넣으면 첫 번째 쌍 삭제
        while (conversationQueue.Count > maxPairs)
        {
            conversationQueue.Dequeue();
        }
    }

    // 기억 중인 대화와 지금 질문을 API에 보낼 JSON 배열로 구성
    // 이 함수는 전송할 목록만 만들고 큐 자체는 변경하지 않음
    public JSONArray BuildMessages(string currentUserMessage)
    {
        // API에 전달할 빈 메시지 배열 생성
        var messages = new JSONArray();

        // 큐를 오래된 대화부터 최신 대화 순서로 읽음
        // foreach로 읽는 것은 Dequeue와 달리 대화를 삭제하지 않음
        foreach (ConversationPair pair in conversationQueue)
        {
            // 한 쌍에서 사용자 메시지를 먼저 추가
            AddMessage(messages, "user", pair.user);

            // 이어서 해당 메시지에 대한 AI 답변 추가
            AddMessage(messages, "assistant", pair.assistant);
        }

        // 마지막에 이번 사용자 메시지를 추가
        // 아직 답변을 받기 전이므로 큐에는 저장하지 않음
        AddMessage(messages, "user", currentUserMessage);

        // 완성한 JSON 배열을 FlowerChat에 반환
        return messages;
    }

    // 메시지 하나를 JSON 객체로 만들어 배열에 추가하는 공통 함수
    // role: 발화자 구분 ("user" 또는 "assistant")
    // content: 실제 대화 내용
    private void AddMessage(
        JSONArray messages,
        string role,
        string content)
    {
        // 메시지 한 개를 담을 JSON 객체 생성
        var message = new JSONObject();

        // API가 요구하는 항목 이름으로 값 입력
        message["role"] = role;
        message["content"] = content;

        // 전달받은 배열의 맨 뒤에 메시지 추가
        messages.Add(message);
    }

    // 기억 중인 대화를 모두 지우는 함수
    // 저장된 JSON 파일을 삭제하는 기능은 아님
    public void Clear()
    {
        conversationQueue.Clear();
    }

    // 개발 중 기억 내용을 확인하기 위한 함수
    // 호출하면 Unity Console에 대화를 출력
    public void PrintMemory()
    {
        Debug.Log(
            "현재 기억 중인 대화: " +
            conversationQueue.Count + "쌍"
        );

        // Console에 표시할 대화 번호
        int number = 1;

        // 오래된 대화부터 순서대로 출력
        foreach (ConversationPair pair in conversationQueue)
        {
            // \n은 줄바꿈을 의미
            Debug.Log(
                "[" + number + "] 사용자: " + pair.user +
                "\n꽃: " + pair.assistant
            );

            // 다음 대화를 출력할 때 번호를 1 증가
            number++;
        }
    }
}