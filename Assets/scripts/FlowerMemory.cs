using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;

public class FlowerMemory
{
    private class ConversationPair
    {
        public string user;
        public string assistant;
    }

    private readonly Queue<ConversationPair> conversationQueue;
    private readonly int maxPairs;

    // 현재 기억하고 있는 질문·답변 쌍의 개수
    public int Count => conversationQueue.Count;

    // 기본값: 최근 질문·답변 5쌍
    public FlowerMemory(int maxConversationPairs = 5)
    {
        maxPairs = System.Math.Max(1, maxConversationPairs);
        conversationQueue = new Queue<ConversationPair>();
    }

    // API 답변을 정상적으로 받은 뒤 호출
    public void AddPair(string userMessage, string assistantReply)
    {
        if (string.IsNullOrWhiteSpace(userMessage) ||
            string.IsNullOrWhiteSpace(assistantReply))
        {
            return;
        }

        conversationQueue.Enqueue(
            new ConversationPair
            {
                user = userMessage,
                assistant = assistantReply
            }
        );

        // 제한을 넘으면 가장 오래된 대화부터 삭제
        while (conversationQueue.Count > maxPairs)
        {
            conversationQueue.Dequeue();
        }
    }

    // 최근 대화 + 지금 질문을 API에 보낼 형태로 구성
    // 지금 질문은 여기서 Queue에 저장하지 않음
    public JSONArray BuildMessages(string currentUserMessage)
    {
        var messages = new JSONArray();

        foreach (ConversationPair pair in conversationQueue)
        {
            AddMessage(messages, "user", pair.user);
            AddMessage(messages, "assistant", pair.assistant);
        }

        AddMessage(messages, "user", currentUserMessage);

        return messages;
    }

    private void AddMessage(
        JSONArray messages,
        string role,
        string content)
    {
        var message = new JSONObject();

        message["role"] = role;
        message["content"] = content;

        messages.Add(message);
    }

    // 새 대화 시작 또는 대화 종료 시 호출
    public void Clear()
    {
        conversationQueue.Clear();
    }

    // 디버그용: 현재 기억 중인 대화 출력
    public void PrintMemory()
    {
        Debug.Log("현재 기억 중인 대화: " + conversationQueue.Count + "쌍");

        int number = 1;

        foreach (ConversationPair pair in conversationQueue)
        {
            Debug.Log(
                "[" + number + "] 사용자: " + pair.user +
                "\n꽃: " + pair.assistant
            );

            number++;
        }
    }
}