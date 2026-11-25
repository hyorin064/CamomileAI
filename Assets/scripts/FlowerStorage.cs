using System;         // 날짜, GUID, 예외 처리, 배열 정렬
using System.IO;      // 파일과 폴더 생성·읽기·삭제
using System.Text;    // UTF-8 인코딩과 바이트 수 계산
using UnityEngine;    // 저장 경로, Console 출력
using SimpleJSON;     // JSON 객체와 배열 생성

// 개발자 모드에서 대화 내용을 JSON 파일로 저장하는 클래스
// MonoBehaviour가 아니므로 FlowerChat에서 new로 만들어 사용
public class FlowerStorage
{
    // 한 파일에 저장할 수 있는 최대 질문·답변 쌍 수
    private const int PairsPerFile = 50;

    // 저장 폴더에 보관할 최대 로그 파일 수
    private const int MaxLogFiles = 20;

    // 파일 하나의 최대 크기: 256KB
    private const int MaxFileBytes = 256 * 1024;

    // 로그 파일 전체의 최대 크기: 5MB
    // 5L의 L은 long 자료형이라는 뜻
    private const long MaxTotalBytes = 5L * 1024 * 1024;

    // 한글을 포함한 내용을 UTF-8로 저장
    // false: 파일 앞에 BOM이라는 인코딩 표시를 붙이지 않음
    private readonly UTF8Encoding encoding = new UTF8Encoding(false);

    // 현재 작성 중인 파일에 들어갈 대화 목록
    // 전체 세션의 모든 파일 내용을 보관하는 배열은 아님
    private JSONArray savedTurns;

    // 현재 대화 세션을 구분하는 고유 번호
    private string sessionId;

    // 현재 대화 세션이 시작된 시각
    private string startedAtUtc;

    // 같은 세션 안에서 파일을 나눌 때 사용하는 번호
    private int partNumber;

    // 실제 파일 저장을 허용할지 결정
    private bool saveEnabled;

    // 외부에서는 저장 경로를 읽을 수 있지만,
    // 값을 변경하는 것은 이 클래스 안에서만 가능
    public string SaveFolder { get; private set; }

    // 생성자: new FlowerStorage(...)를 실행할 때 호출
    // 값을 생략하면 저장하지 않는 전시 모드로 시작
    public FlowerStorage(bool isDeveloperMode = false)
    {
        // Unity가 제공하는 앱 데이터 저장 경로 아래에
        // ChamomileLogs라는 하위 폴더 경로를 구성
        // Path.Combine은 운영체제에 맞게 경로를 연결
        SaveFolder = Path.Combine(
            Application.persistentDataPath,
            "ChamomileLogs"
        );

        // 첫 번째 대화 세션 준비
        BeginSession(isDeveloperMode);
    }

    // 새 대화를 시작할 때 저장 상태를 초기화하는 함수
    // 기존에 저장된 JSON 파일을 모두 삭제하는 기능은 아님
    public void BeginSession(bool isDeveloperMode)
    {
        // true: 개발자 모드 저장
        // false: 전시회 모드 저장 안 함
        saveEnabled = isDeveloperMode;

        // 새 세션의 대화 목록을 빈 배열로 시작
        savedTurns = new JSONArray();

        // 다른 세션과 구분할 고유 ID 생성
        // "N": 하이픈 없는 GUID 문자열
        sessionId = Guid.NewGuid().ToString("N");

        // UTC 기준 현재 시각을 표준 날짜 문자열로 저장
        // "o": 시간대와 소수 초 정보를 포함하는 형식
        startedAtUtc = DateTime.UtcNow.ToString("o");

        // 새 세션은 첫 번째 파일부터 시작
        partNumber = 1;

        // 전시회 모드에서는 폴더 생성과 파일 정리도 실행하지 않음
        if (!saveEnabled)
        {
            return;
        }

        try
        {
            // 폴더가 없으면 생성
            // 이미 존재하면 그대로 사용
            Directory.CreateDirectory(SaveFolder);

            // 기존 로그가 개수나 전체 용량 제한을 넘으면 정리
            // null: 이번에는 특별히 보호할 파일이 없음
            PruneLogFiles(null);
        }
        catch (Exception ex)
        {
            // 파일 준비에 실패해도 게임 전체가 멈추지 않도록 경고
            Debug.LogWarning(
                "JSON 저장 준비 실패: " + ex.GetType().Name
            );
        }
    }

    // 정상적인 API 답변을 받은 뒤 대화 한 쌍을 저장하는 함수
    public void SavePair(string userMessage, string assistantReply)
    {
        // 저장이 비활성화되어 있으면 즉시 종료
        if (!saveEnabled)
        {
            return;
        }

        // 질문 또는 답변이 비어 있으면 저장하지 않음
        if (string.IsNullOrWhiteSpace(userMessage) ||
            string.IsNullOrWhiteSpace(assistantReply))
        {
            return;
        }

        // 임시 파일 경로를 보관
        // finally에서도 접근해야 하므로 try 바깥에서 선언
        string temporaryPath = null;

        try
        {
            // 이번 질문·답변 한 쌍을 JSON 객체로 구성
            var turn = new JSONObject();
            turn["timestampUtc"] = DateTime.UtcNow.ToString("o");
            turn["user"] = userMessage;
            turn["assistant"] = assistantReply;

            // 기존 기록을 바로 변경하지 않고,
            // 저장을 시도할 후보 배열을 별도로 생성
            var candidateTurns = new JSONArray();

            // 현재 파일에 기록한 대화를 후보 배열에 추가
            for (int i = 0; i < savedTurns.Count; i++)
            {
                candidateTurns.Add(savedTurns[i]);
            }

            // 마지막에 이번 대화 추가
            candidateTurns.Add(turn);

            // 우선 현재 파일 번호로 저장할 준비
            int candidatePart = partNumber;

            // 대화 목록과 세션 정보를 JSON 문자열로 변환
            string json = BuildJson(candidateTurns, candidatePart);

            // 대화가 50쌍을 초과하거나 파일 크기가 256KB를 초과하면
            // 이번 대화부터 새로운 파일에 저장
            // ||는 두 조건 중 하나라도 참이면 실행한다는 뜻
            if (candidateTurns.Count > PairsPerFile ||
                encoding.GetByteCount(json) > MaxFileBytes)
            {
                // 다음 파일 번호로 이동
                candidatePart++;

                // 새 파일에는 이번 대화 한 쌍만 넣음
                // 이전 대화는 기존 파일에 남아 있음
                candidateTurns = new JSONArray();
                candidateTurns.Add(turn);

                json = BuildJson(candidateTurns, candidatePart);
            }

            // 한 쌍만 넣어도 최대 파일 크기를 넘는 경우 저장 생략
            // GetByteCount는 글자 수가 아니라 UTF-8 바이트 수를 계산
            if (encoding.GetByteCount(json) > MaxFileBytes)
            {
                Debug.LogWarning(
                    "대화 한 쌍이 너무 커서 JSON 저장을 건너뛰었습니다."
                );

                return;
            }

            // 저장 직전에도 폴더가 있는지 확인하고 필요하면 생성
            Directory.CreateDirectory(SaveFolder);

            // 파일 이름 예: chat_고유세션ID_0001.json
            // "D4": 숫자를 최소 네 자리로 표시하고 앞을 0으로 채움
            string fileName =
                "chat_" + sessionId + "_" +
                candidatePart.ToString("D4") + ".json";

            // 최종 파일의 전체 경로 구성
            string filePath = Path.Combine(SaveFolder, fileName);

            // 임시 파일 이름 예: chat_고유세션ID_0001.json.tmp
            temporaryPath = filePath + ".tmp";

            // 최종 파일에 바로 쓰지 않고 임시 파일에 먼저 작성
            // 쓰는 도중 실패했을 때 기존 기록을 보호하기 위한 방식
            File.WriteAllText(temporaryPath, json, encoding);

            if (File.Exists(filePath))
            {
                // 현재 파일이 있으면 임시 파일 내용으로 교체
                // 마지막 null은 별도 백업 파일을 만들지 않는다는 뜻
                File.Replace(temporaryPath, filePath, null);
            }
            else
            {
                // 처음 저장하는 파일이면 임시 파일을 최종 경로로 이동
                File.Move(temporaryPath, filePath);
            }

            // 파일 저장에 성공한 뒤에만 내부 상태를 갱신
            // 저장 실패 시 기존 savedTurns와 partNumber는 유지
            savedTurns = candidateTurns;
            partNumber = candidatePart;

            Debug.Log("대화 JSON 저장 완료: " + filePath);

            // 파일 수와 전체 용량 제한을 적용
            // 방금 저장한 파일은 삭제하지 않도록 경로 전달
            PruneLogFiles(filePath);
        }
        catch (Exception ex)
        {
            // 저장 또는 오래된 로그 정리 중 발생한 오류를 경고로 출력
            // 이 예외가 FlowerChat까지 전달되어 대화를 중단시키지 않게 함
            Debug.LogWarning(
                "대화 JSON 저장/정리 실패: " + ex.GetType().Name
            );
        }
        finally
        {
            // 성공·실패·중간 return 여부와 관계없이 임시 파일 정리 시도
            // 정상 저장 시에는 이동 또는 교체되어 이미 없어져 있음
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
                    // 정리 중 발생한 오류도 별도로 처리
                    Debug.LogWarning("JSON 임시 파일 정리에 실패했습니다.");
                }
            }
        }
    }

    // 세션 정보와 대화 목록을 하나의 JSON 문자열로 구성
    // 이 함수 자체는 파일을 저장하지 않음
    private string BuildJson(JSONArray turns, int part)
    {
        // JSON의 가장 바깥 객체
        var root = new JSONObject();

        // 저장 데이터 형식의 버전 번호
        root["schemaVersion"] = 1;

        // 대화 세션 고유 ID
        root["sessionId"] = sessionId;

        // 세션 시작 시각
        root["startedAtUtc"] = startedAtUtc;

        // 이번 JSON을 구성한 시각
        root["updatedAtUtc"] = DateTime.UtcNow.ToString("o");

        // 같은 세션 안에서 몇 번째 파일인지 표시
        root["part"] = part;

        // 개발자 모드에서 저장한 기록임을 표시
        root["mode"] = "Developer";

        // 질문·답변 목록
        root["turns"] = turns;

        // JSON 객체를 문자열로 변환해서 반환
        return root.ToString();
    }

    // 로그 파일 개수와 전체 용량을 제한하는 함수
    // protectedPath: 삭제하지 않고 보호할 파일의 경로
    private void PruneLogFiles(string protectedPath)
    {
        // 저장 폴더에서 이름이 chat_로 시작하고
        // 확장자가 .json인 파일만 가져옴
        FileInfo[] files =
            new DirectoryInfo(SaveFolder).GetFiles("chat_*.json");

        // 마지막으로 수정된 시각이 오래된 파일부터 정렬
        // (a, b) => ...는 두 파일을 비교하는 람다식
        Array.Sort(
            files,
            (a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc)
        );

        // 현재 로그 파일 전체 크기 계산
        long totalBytes = 0;

        foreach (FileInfo file in files)
        {
            totalBytes += file.Length;
        }

        // 현재 로그 파일 개수
        int fileCount = files.Length;

        // 정렬된 순서대로 오래된 파일부터 확인
        foreach (FileInfo file in files)
        {
            // 개수와 용량이 모두 제한 이하면 더 지울 필요 없음
            // &&는 두 조건이 모두 참이어야 한다는 뜻
            if (fileCount <= MaxLogFiles &&
                totalBytes <= MaxTotalBytes)
            {
                break;
            }

            // 방금 저장한 파일이면 삭제하지 않고 다음 파일로 넘어감
            if (file.FullName == protectedPath)
            {
                continue;
            }

            // 삭제 전에 파일 크기를 기록
            long fileBytes = file.Length;

            // 오래된 파일 삭제
            file.Delete();

            // 삭제한 만큼 전체 크기와 파일 개수 감소
            totalBytes -= fileBytes;
            fileCount--;
        }
    }

    // 저장 폴더 경로를 Unity Console에 출력
    public void PrintSaveFolder()
    {
        Debug.Log("JSON 저장 폴더: " + SaveFolder);
    }
}