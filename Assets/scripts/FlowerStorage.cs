using System;
using System.IO;
using System.Text;
using UnityEngine;
using SimpleJSON;

public class FlowerStorage
{
    private const int PairsPerFile = 50;
    private const int MaxLogFiles = 20;
    private const int MaxFileBytes = 256 * 1024;
    private const long MaxTotalBytes = 5L * 1024 * 1024;

    private readonly UTF8Encoding encoding = new UTF8Encoding(false);

    private JSONArray savedTurns;
    private string sessionId;
    private string startedAtUtc;
    private int partNumber;
    private bool saveEnabled;

    public string SaveFolder { get; private set; }

    // true: 개발 모드 저장 / false: 전시 모드 저장 안 함
    public FlowerStorage(bool isDeveloperMode = false)
    {
        SaveFolder = Path.Combine(
            Application.persistentDataPath,
            "ChamomileLogs"
        );

        BeginSession(isDeveloperMode);
    }

    // 새 대화를 시작할 때 호출
    public void BeginSession(bool isDeveloperMode)
    {
        saveEnabled = isDeveloperMode;

        savedTurns = new JSONArray();
        sessionId = Guid.NewGuid().ToString("N");
        startedAtUtc = DateTime.UtcNow.ToString("o");
        partNumber = 1;

        if (!saveEnabled)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(SaveFolder);
            PruneLogFiles(null);
        }
        catch (Exception ex)
        {
            Debug.LogWarning(
                "JSON 저장 준비 실패: " + ex.GetType().Name
            );
        }
    }

    // API 답변을 정상적으로 받은 뒤 호출
    public void SavePair(string userMessage, string assistantReply)
    {
        if (!saveEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(userMessage) ||
            string.IsNullOrWhiteSpace(assistantReply))
        {
            return;
        }

        string temporaryPath = null;

        try
        {
            var turn = new JSONObject();
            turn["timestampUtc"] = DateTime.UtcNow.ToString("o");
            turn["user"] = userMessage;
            turn["assistant"] = assistantReply;

            // 기존 기록에 새 대화 한 쌍을 추가한 후보
            var candidateTurns = new JSONArray();

            for (int i = 0; i < savedTurns.Count; i++)
            {
                candidateTurns.Add(savedTurns[i]);
            }

            candidateTurns.Add(turn);

            int candidatePart = partNumber;
            string json = BuildJson(candidateTurns, candidatePart);

            // 파일 제한을 넘으면 다음 파일로 분리
            if (candidateTurns.Count > PairsPerFile ||
                encoding.GetByteCount(json) > MaxFileBytes)
            {
                candidatePart++;

                candidateTurns = new JSONArray();
                candidateTurns.Add(turn);

                json = BuildJson(candidateTurns, candidatePart);
            }

            // 대화 한 쌍만으로도 제한을 넘으면 저장하지 않음
            if (encoding.GetByteCount(json) > MaxFileBytes)
            {
                Debug.LogWarning(
                    "대화 한 쌍이 너무 커서 JSON 저장을 건너뛰었습니다."
                );

                return;
            }

            Directory.CreateDirectory(SaveFolder);

            string fileName =
                "chat_" + sessionId + "_" +
                candidatePart.ToString("D4") + ".json";

            string filePath = Path.Combine(SaveFolder, fileName);
            temporaryPath = filePath + ".tmp";

            // 임시 파일을 먼저 작성해서 기존 파일을 보호
            File.WriteAllText(temporaryPath, json, encoding);

            if (File.Exists(filePath))
            {
                File.Replace(temporaryPath, filePath, null);
            }
            else
            {
                File.Move(temporaryPath, filePath);
            }

            // 저장 성공 후에만 메모리 기록 갱신
            savedTurns = candidateTurns;
            partNumber = candidatePart;

            Debug.Log("대화 JSON 저장 완료: " + filePath);

            PruneLogFiles(filePath);
        }
        catch (Exception ex)
        {
            // 저장 실패가 대화 기능까지 중단시키지 않도록 처리
            Debug.LogWarning(
                "대화 JSON 저장/정리 실패: " + ex.GetType().Name
            );
        }
        finally
        {
            // 실패한 임시 파일이 있으면 정리
            if (temporaryPath != null)
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch (Exception)
                {
                    Debug.LogWarning("JSON 임시 파일 정리에 실패했습니다.");
                }
            }
        }
    }

    private string BuildJson(JSONArray turns, int part)
    {
        var root = new JSONObject();

        root["schemaVersion"] = 1;
        root["sessionId"] = sessionId;
        root["startedAtUtc"] = startedAtUtc;
        root["updatedAtUtc"] = DateTime.UtcNow.ToString("o");
        root["part"] = part;
        root["mode"] = "Developer";
        root["turns"] = turns;

        return root.ToString();
    }

    // 파일 개수와 전체 용량 제한 적용
    private void PruneLogFiles(string protectedPath)
    {
        FileInfo[] files =
            new DirectoryInfo(SaveFolder).GetFiles("chat_*.json");

        // 오래전에 갱신된 파일부터 정리
        Array.Sort(
            files,
            (a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc)
        );

        long totalBytes = 0;

        foreach (FileInfo file in files)
        {
            totalBytes += file.Length;
        }

        int fileCount = files.Length;

        foreach (FileInfo file in files)
        {
            if (fileCount <= MaxLogFiles &&
                totalBytes <= MaxTotalBytes)
            {
                break;
            }

            // 방금 저장한 파일은 유지
            if (file.FullName == protectedPath)
            {
                continue;
            }

            long fileBytes = file.Length;

            file.Delete();

            totalBytes -= fileBytes;
            fileCount--;
        }
    }

    // 저장 위치를 Console에 출력
    public void PrintSaveFolder()
    {
        Debug.Log("JSON 저장 폴더: " + SaveFolder);
    }
}