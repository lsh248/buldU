using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class NetworkUI : MonoBehaviour
{
    // 다른 스크립트에서 쉽게 접근할 수 있도록 싱글톤 인스턴스 설정
    public static NetworkUI Instance;

    private NetworkGridManager gridManager;
    private string alertMessage = "";
    private Coroutine alertCoroutine;

    void Awake()
    {
        // 싱글톤 초기화
        Instance = this;
    }

    void Start()
    {
        // 같은 오브젝트에 부착된 GridManager 참조
        gridManager = GetComponent<NetworkGridManager>();
    }

    // ★ NetworkGridManager에서 호출하는 바로 그 함수입니다 ★
    public void DisplayAlert(string msg)
    {
        if (alertCoroutine != null) StopCoroutine(alertCoroutine);
        alertMessage = msg;
        alertCoroutine = StartCoroutine(ClearAlert());
    }

    IEnumerator ClearAlert()
    {
        yield return new WaitForSeconds(2.0f);
        alertMessage = "";
    }

    void OnGUI()
    {
        if (NetworkManager.Singleton == null || gridManager == null) return;

        // 1. 접속 UI
        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            GUILayout.BeginArea(new Rect(20, 20, 200, 250));
            if (GUILayout.Button("방 만들기 (Host)", GUILayout.Height(50))) NetworkManager.Singleton.StartHost();
            if (GUILayout.Button("참가하기 (Client)", GUILayout.Height(50))) NetworkManager.Singleton.StartClient();
            GUILayout.EndArea();
            return;
        }

        // 2. 게임 상태 정보
        GUILayout.BeginArea(new Rect(20, 20, 250, 220));
        int myColorType = gridManager.GetMyColorType();
        string myColorName = (myColorType == 1) ? "<color=black>검정색</color>" : "<color=white>흰색</color>";
        string turnName = (gridManager.turnPlayer.Value == 1) ? "검정" : "흰색";

        GUILayout.Box(NetworkManager.Singleton.IsHost ? "방장(Host)" : "참가자(Client)");
        GUILayout.Label($"<b>나의 돌: {myColorName}</b>");
        GUILayout.Label($"<b>현재 턴: {turnName}</b>");
        GUILayout.Label($"남은 함정: {gridManager.GetRemainingTraps(myColorType)}개");
        GUILayout.EndArea();

        // 3. 함정 알림 메시지 출력
        if (!string.IsNullOrEmpty(alertMessage))
        {
            GUIStyle alertStyle = new GUIStyle(GUI.skin.box);
            alertStyle.fontSize = 25;
            alertStyle.fontStyle = FontStyle.Bold;
            alertStyle.alignment = TextAnchor.MiddleCenter;
            alertStyle.normal.textColor = Color.yellow;

            float aw = 550; float ah = 60;
            Rect alertRect = new Rect((Screen.width - aw) / 2, 100, aw, ah);
            GUI.Box(alertRect, alertMessage, alertStyle);
        }

        // 4. 승리 알림 및 재시작 버튼
        if (gridManager.winner.Value != 0)
        {
            float w = 450; float h = 200;
            Rect centerRect = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            GUILayout.BeginArea(centerRect, GUI.skin.box);

            string winColor = (gridManager.winner.Value == 1) ? "검정" : "흰색";
            var style = new GUIStyle(GUI.skin.label) { fontSize = 35, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUILayout.Label($"{winColor} 승리!", style, GUILayout.Height(70));

            if (NetworkManager.Singleton.IsHost)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("그대로 재시작", GUILayout.Height(60))) RestartGame(false);
                if (GUILayout.Button("순서 바꿔서 재시작", GUILayout.Height(60))) RestartGame(true);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }
    }

    void RestartGame(bool swapColor)
    {
        gridManager.SetNextMatchColor(swapColor);

        NetworkObject[] allNetObjs = FindObjectsByType<NetworkObject>(FindObjectsSortMode.None);
        foreach (var netObj in allNetObjs)
        {
            if (netObj.gameObject.name.Contains("Clone")) netObj.Despawn();
        }

        NetworkManager.Singleton.SceneManager.LoadScene(SceneManager.GetActiveScene().name, LoadSceneMode.Single);
    }
}
