using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class NetworkGridManager : NetworkBehaviour
{
    [Header("프리팹 설정")]
    public GameObject[] playerPrefabs; // 0: 검정, 1: 흰색
    public GameObject trapVisualPrefab;

    private int[,] board = new int[10, 10];
    private int[,] traps = new int[10, 10]; // 플레이어 설치 함정 (비트 플래그)
    private bool[,] publicTraps = new bool[10, 10]; // 공용 함정 (랜덤 10개)

    private GameObject[,] spawnedPieces = new GameObject[10, 10];
    private GameObject[,] myTrapVisuals = new GameObject[10, 10];

    public NetworkVariable<int> turnPlayer = new NetworkVariable<int>(1);
    public NetworkVariable<int> winner = new NetworkVariable<int>(0);

    private static int nextMatchHostColor = 1;
    public NetworkVariable<int> hostColorType = new NetworkVariable<int>(1);

    private int[] remainingTraps = { 3, 3 };

    public override void OnNetworkSpawn()
    {
        board = new int[10, 10];
        traps = new int[10, 10];
        publicTraps = new bool[10, 10];
        spawnedPieces = new GameObject[10, 10];
        myTrapVisuals = new GameObject[10, 10];

        if (IsServer)
        {
            hostColorType.Value = nextMatchHostColor;
            winner.Value = 0;
            turnPlayer.Value = 1;
            remainingTraps[0] = 3;
            remainingTraps[1] = 3;

            GeneratePublicTraps();
        }
    }

    void GeneratePublicTraps()
    {
        int count = 0;
        List<Vector2Int> allCells = new List<Vector2Int>();
        for (int x = 0; x < 10; x++)
            for (int z = 0; z < 10; z++)
                allCells.Add(new Vector2Int(x, z));

        while (count < 10 && allCells.Count > 0)
        {
            int randIndex = Random.Range(0, allCells.Count);
            Vector2Int pos = allCells[randIndex];
            publicTraps[pos.x, pos.y] = true;
            SyncPublicTrapClientRpc(pos.x, pos.y);
            allCells.RemoveAt(randIndex);
            count++;
        }
    }

    [Rpc(SendTo.NotServer)]
    void SyncPublicTrapClientRpc(int x, int z)
    {
        publicTraps[x, z] = true;
    }

    public int GetMyColorType()
    {
        if (IsHost) return hostColorType.Value;
        return (hostColorType.Value == 1) ? 2 : 1;
    }

    void Update()
    {
        if (!IsSpawned || winner.Value != 0) return;
        if (turnPlayer.Value != GetMyColorType()) return;

        if (Mouse.current != null)
        {
            if (Mouse.current.leftButton.wasPressedThisFrame) HandleInput(false);
            else if (Mouse.current.rightButton.wasPressedThisFrame) HandleInput(true);
        }
    }

    void HandleInput(bool isTrapRequest)
    {
        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = Camera.main.ScreenPointToRay(mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            int x = Mathf.FloorToInt(hit.point.x);
            int z = Mathf.FloorToInt(hit.point.z);
            if (x >= 0 && x < 10 && z >= 0 && z < 10)
            {
                if (isTrapRequest) RequestPlaceTrapRpc(x, z);
                else RequestPlacePieceRpc(x, z);
            }
        }
    }

    public void SetNextMatchColor(bool swap)
    {
        if (!IsServer) return;
        nextMatchHostColor = swap ? (hostColorType.Value == 1 ? 2 : 1) : hostColorType.Value;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void RequestPlaceTrapRpc(int x, int z, RpcParams rpcParams = default)
    {
        int pID = turnPlayer.Value;
        if (remainingTraps[pID - 1] <= 0 || board[x, z] != 0) return;

        int myBit = (pID == 1) ? 1 : 2;
        if ((traps[x, z] & myBit) != 0) return;

        traps[x, z] |= myBit;
        remainingTraps[pID - 1]--;
        ShowTrapVisualClientRpc(x, z, rpcParams.Receive.SenderClientId);
        turnPlayer.Value = (pID == 1) ? 2 : 1;
    }

    [Rpc(SendTo.Everyone)]
    void ShowTrapVisualClientRpc(int x, int z, ulong ownerClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == ownerClientId)
        {
            Vector3 pos = new Vector3(x + 0.5f, 0.05f, z + 0.5f);
            GameObject visual = Instantiate(trapVisualPrefab, pos, Quaternion.identity);
            myTrapVisuals[x, z] = visual;
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void RequestPlacePieceRpc(int x, int z)
    {
        if (board[x, z] != 0) return;

        int pID = turnPlayer.Value;
        int enemyID = (pID == 1) ? 2 : 1;
        int enemyBit = (enemyID == 1) ? 1 : 2;
        int myBit = (pID == 1) ? 1 : 2;

        bool hitPublic = publicTraps[x, z];
        bool hitEnemy = (traps[x, z] & enemyBit) != 0;

        if (hitPublic || hitEnemy)
        {
            string message = "";
            int penaltyAmount = 0;

            if (hitPublic && hitEnemy)
            {
                message = "⚠️ 더블 함정 발동! (돌 4개 삭제)";
                penaltyAmount = 4;
            }
            else if (hitPublic)
            {
                message = "⚙️ 공용 함정 발동! (돌 2개 삭제)";
                penaltyAmount = 2;
            }
            else
            {
                message = "💀 상대 함정 발동! (돌 2개 삭제)";
                penaltyAmount = 2;
            }

            ShowTrapMessageClientRpc(message);

            if (hitEnemy)
            {
                traps[x, z] &= ~enemyBit;
                RemoveTrapVisualForPlayerClientRpc(x, z, enemyID);
            }

            TriggerTrapRpc(pID, penaltyAmount);
            turnPlayer.Value = enemyID;
            return;
        }

        if ((traps[x, z] & myBit) != 0)
        {
            traps[x, z] &= ~myBit;
            RemoveTrapVisualForPlayerClientRpc(x, z, pID);
        }

        SpawnPieceRpc(x, z, pID);
        if (CheckWin(x, z, pID)) winner.Value = pID;
        else turnPlayer.Value = enemyID;
    }

    [Rpc(SendTo.Everyone)]
    void ShowTrapMessageClientRpc(string msg)
    {
        NetworkUI ui = Object.FindFirstObjectByType<NetworkUI>();
        if (ui != null) ui.DisplayAlert(msg);
    }

    [Rpc(SendTo.Everyone)]
    void RemoveTrapVisualForPlayerClientRpc(int x, int z, int ownerColorType)
    {
        if (GetMyColorType() == ownerColorType && myTrapVisuals[x, z] != null)
        {
            Destroy(myTrapVisuals[x, z]);
            myTrapVisuals[x, z] = null;
        }
    }

    [Rpc(SendTo.Everyone)]
    void SpawnPieceRpc(int x, int z, int pID)
    {
        board[x, z] = pID;
        if (IsServer)
        {
            Vector3 spawnPos = new Vector3(x + 0.5f, 0.1f, z + 0.5f);
            GameObject piece = Instantiate(playerPrefabs[pID - 1], spawnPos, Quaternion.identity);
            spawnedPieces[x, z] = piece;
            piece.GetComponent<NetworkObject>().Spawn();
        }
    }

    [Rpc(SendTo.Server)]
    void TriggerTrapRpc(int victimColorType, int amount)
    {
        List<Vector2Int> myPieces = new List<Vector2Int>();
        for (int i = 0; i < 10; i++)
            for (int j = 0; j < 10; j++)
                if (board[i, j] == victimColorType) myPieces.Add(new Vector2Int(i, j));

        int removeCount = Mathf.Min(myPieces.Count, amount);
        for (int i = 0; i < removeCount; i++)
        {
            int randIndex = Random.Range(0, myPieces.Count);
            Vector2Int target = myPieces[randIndex];
            if (spawnedPieces[target.x, target.y] != null)
                spawnedPieces[target.x, target.y].GetComponent<NetworkObject>().Despawn();
            board[target.x, target.y] = 0;
            myPieces.RemoveAt(randIndex);
        }
    }

    bool CheckWin(int x, int z, int pID)
    {
        int[,] dirs = { { 1, 0 }, { 0, 1 }, { 1, 1 }, { 1, -1 } };
        for (int i = 0; i < 4; i++)
        {
            int count = 1;
            count += CountInDirection(x, z, dirs[i, 0], dirs[i, 1], pID);
            count += CountInDirection(x, z, -dirs[i, 0], -dirs[i, 1], pID);
            if (count >= 5) return true;
        }
        return false;
    }

    int CountInDirection(int x, int z, int dx, int dz, int pID)
    {
        int count = 0;
        int nx = x + dx; int nz = z + dz;
        while (nx >= 0 && nx < 10 && nz >= 0 && nz < 10 && board[nx, nz] == pID)
        { count++; nx += dx; nz += dz; }
        return count;
    }

    public int GetRemainingTraps(int colorType) => remainingTraps[colorType - 1];
}