using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

/// <summary>
/// NetworkManager — kết nối đến Go game server.
///
/// TCP: Login, JoinRoom, Attack, Damage events (reliable).
/// UDP: gửi MoveInput mỗi frame, nhận WorldState broadcast (low-latency).
///
/// SRP: chỉ xử lý network I/O, không chứa game logic.
/// Singleton pattern (chỉ 1 instance toàn game).
/// </summary>
public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance { get; private set; }

    // ─── Config ───────────────────────────────────────────────────────────────

    [Header("Server")]
    private string serverIP  = "54.169.108.73";

    private int    tcpPort   = 7777;
    private int    udpPort   = 7778;

    public string ServerIP => serverIP;
    public int    TcpPort  => tcpPort;



    // ─── Events ───────────────────────────────────────────────────────────────

    public event Action<uint>                     OnLoginSuccess;
    public event Action<string>                   OnLoginFailed;
    public event Action<bool, string>             OnRegisterResult;
    public event Action<string, List<PlayerInfo>> OnJoinRoomSuccess;
    public event Action<PlayerInfo>               OnPlayerJoined;
    public event Action<uint>                     OnPlayerLeft;
    public event Action<WorldStatePacket>         OnWorldState;
    public event Action<DamageEventPacket>        OnDamageEvent;
    public event Action<DieEventPacket>           OnDieEvent;
    public event Action<RespawnAckPacket>         OnRespawnAck;
    public event Action<uint>                     OnPong;

    // ─── State ────────────────────────────────────────────────────────────────

    public uint  LocalPlayerID { get; private set; }
    public byte  LocalJobClass  { get; private set; } = 1; // 1 = Archer (server default)
    public bool  IsConnected   => tcpClient != null && tcpClient.Connected;

    private TcpClient  tcpClient;
    private NetworkStream tcpStream;
    private UdpClient  udpClient;

    private Thread tcpReadThread;
    private Thread udpReadThread;

    // Thread-safe queue để marshal network events sang main thread
    private readonly Queue<Action> mainThreadActions = new Queue<Action>();
    private readonly object        queueLock         = new object();

    private uint  moveTimestamp;
    private float moveInputTimer;

    // ─── Stats (FPS + Ping) ───────────────────────────────────────────────────
    private float statsTimer;
    private float pingTimestamp;        // Time.time khi SendPing
    public  float LatencyMs  { get; private set; } = -1f;  // RTT ms, -1 = chưa đo
    public  float CurrentFPS { get; private set; }

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        // Xử lý các action từ network thread trên main thread
        lock (queueLock)
        {
            while (mainThreadActions.Count > 0)
                mainThreadActions.Dequeue()?.Invoke();
        }

        // ── FPS + Ping log mỗi giây ──────────────────────────────────────────
        CurrentFPS  = 1f / Mathf.Max(Time.deltaTime, 0.0001f);
        statsTimer += Time.deltaTime;
        if (statsTimer >= 1f)
        {
            statsTimer = 0f;
            string latencyStr = LatencyMs >= 0 ? $"{LatencyMs:F0}ms" : "---";
            Debug.Log($"[Network] FPS={CurrentFPS:F0}  Ping={latencyStr}");

            // Gửi Ping để đo latency vòng tiếp theo
            if (IsConnected)
            {
                pingTimestamp = Time.time;
                SendPing();
            }
        }
    }

    private void OnDestroy()
    {
        Disconnect();
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Kết nối đến server và login với username, password và slot.</summary>
    public void Connect(string username, string password, byte slot)
    {
        StartCoroutine(ConnectRoutine(username, password, slot));
    }

    /// <summary>Đăng ký tài khoản mới.</summary>
    public void RegisterAccount(string username, string password)
    {
        StartCoroutine(RegisterRoutine(username, password));
    }

    /// <summary>Vào phòng — "" = matchmake tự động.</summary>
    public void JoinRoom(string roomID = "")
    {
        var buf = PacketEncoder.EncodeJoinRoomReq(roomID);
        SendTCP(buf);
    }

    /// <summary>Gửi MoveInput qua UDP — gọi từ Player.Update().</summary>
    public void SendMoveInput(uint playerID, Vector2 dest, Vector2 dir)
    {
        moveTimestamp++;
        var buf = PacketEncoder.EncodeMoveInput(playerID, dest, dir, moveTimestamp);
        SendUDP(buf);
    }

    /// <summary>Gửi AttackReq qua TCP.</summary>
    public void SendAttack(uint playerID, uint targetID, Vector2 dir)
    {
        var buf = PacketEncoder.EncodeAttackReq(playerID, targetID, dir);
        SendTCP(buf);
    }

    /// <summary>Gửi Respawn request.</summary>
    public void SendRespawn(uint playerID)
    {
        var buf = PacketEncoder.EncodeRespawnReq(playerID);
        SendTCP(buf);
    }

    /// <summary>Ping để đo latency.</summary>
    public void SendPing()
    {
        uint ts = (uint)(Time.time * 1000);
        var buf = PacketEncoder.EncodePing(ts);
        SendTCP(buf);
    }

    /// <summary>
    /// Gửi NavMesh path waypoints lên server qua TCP.
    /// Server sẽ di chuyển player theo đúng các waypoints này thay vì đi thẳng.
    /// Gọi sau khi NavMesh tính xong path (agent.path.corners available).
    /// </summary>
    public void SendMovePath(uint playerID, Vector3[] corners)
    {
        if (corners == null || corners.Length == 0) return;
        var buf = PacketEncoder.EncodeMovePathPacket(playerID, corners);
        SendTCP(buf);
    }

    /// <summary>Ngắt kết nối.</summary>
    public void Disconnect()
    {
        tcpReadThread?.Abort();
        udpReadThread?.Abort();
        tcpStream?.Close();
        tcpClient?.Close();
        udpClient?.Close();
        tcpClient = null;
        udpClient = null;
        Debug.Log("[Network] Disconnected");
    }

    // ─── Private — Connect ────────────────────────────────────────────────────

    private IEnumerator ConnectRoutine(string username, string password, byte slot)
    {
        Disconnect();
        Debug.Log($"[Network] Connecting to {serverIP}:{tcpPort}...");

        // TCP connect
        tcpClient = new TcpClient();
        var ar = tcpClient.BeginConnect(serverIP, tcpPort, null, null);
        yield return new WaitUntil(() => ar.IsCompleted);

        try { tcpClient.EndConnect(ar); }
        catch (Exception e)
        {
            Debug.LogError($"[Network] TCP connect failed: {e.Message}");
            OnLoginFailed?.Invoke("Connect failed: " + e.Message);
            yield break;
        }

        tcpStream = tcpClient.GetStream();

        // UDP setup
        udpClient = new UdpClient();
        udpClient.Connect(serverIP, udpPort);

        // Bắt đầu read threads
        tcpReadThread = new Thread(TCPReadLoop) { IsBackground = true };
        tcpReadThread.Start();

        udpReadThread = new Thread(UDPReadLoop) { IsBackground = true };
        udpReadThread.Start();

        Debug.Log("[Network] Connected. Sending login...");

        // Gửi LoginReq
        var loginBuf = PacketEncoder.EncodeLoginReq(username, password, slot);
        SendTCP(loginBuf);
    }

    private IEnumerator RegisterRoutine(string username, string password)
    {
        Disconnect();
        Debug.Log($"[Network] Connecting to register at {serverIP}:{tcpPort}...");

        tcpClient = new TcpClient();
        var ar = tcpClient.BeginConnect(serverIP, tcpPort, null, null);
        yield return new WaitUntil(() => ar.IsCompleted);

        try { tcpClient.EndConnect(ar); }
        catch (Exception e)
        {
            Debug.LogError($"[Network] TCP register connect failed: {e.Message}");
            OnRegisterResult?.Invoke(false, "Connect failed: " + e.Message);
            yield break;
        }

        tcpStream = tcpClient.GetStream();

        tcpReadThread = new Thread(TCPReadLoop) { IsBackground = true };
        tcpReadThread.Start();

        Debug.Log("[Network] Connected. Sending register...");
        var regBuf = PacketEncoder.EncodeRegisterReq(username, password);
        SendTCP(regBuf);
    }

    // ─── Private — TCP Read Loop ──────────────────────────────────────────────

    private void TCPReadLoop()
    {
        try
        {
            while (tcpClient != null && tcpClient.Connected)
            {
                var (pType, payload) = PacketDecoder.ReadFrame(tcpStream);
                Dispatch(pType, payload);
            }
        }
        catch (System.Threading.ThreadAbortException)
        {
            // Bình thường — xảy ra khi Unity dừng Play mode và hủy thread
        }
        catch (Exception e)
        {
            if (tcpClient != null) // không phải do Disconnect()
                Enqueue(() => Debug.LogError($"[Network] TCP read error: {e.Message}"));
        }
    }

    // ─── Private — UDP Read Loop ──────────────────────────────────────────────

    private void UDPReadLoop()
    {
        try
        {
            IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
            while (udpClient != null)
            {
                byte[] data = udpClient.Receive(ref ep);
                if (data.Length < 4) continue;

                ushort pType  = BitConverter.ToUInt16(data, 0);
                ushort payLen = BitConverter.ToUInt16(data, 2);
                if (data.Length < 4 + payLen) continue;

                byte[] payload = new byte[payLen];
                Array.Copy(data, 4, payload, 0, payLen);
                Dispatch(pType, payload);
            }
        }
        catch (System.Threading.ThreadAbortException)
        {
            // Bình thường — xảy ra khi Unity dừng Play mode và hủy thread
        }
        catch (Exception e)
        {
            if (udpClient != null)
                Enqueue(() => Debug.LogError($"[Network] UDP read error: {e.Message}"));
        }
    }

    // ─── Private — Packet Dispatch ────────────────────────────────────────────

    private void Dispatch(ushort pType, byte[] payload)
    {
        switch ((PacketType)pType)
        {
            case PacketType.LoginAck:
                var loginAck = PacketDecoder.DecodeLoginAck(payload);
                Enqueue(() =>
                {
                    if (loginAck.Success)
                    {
                        LocalPlayerID  = loginAck.PlayerID;
                        LocalJobClass  = loginAck.JobClass;
                        Debug.Log($"[Network] Login OK — ID={LocalPlayerID} Job={LocalJobClass} Level={loginAck.Level} Map={loginAck.MapName} Pos=({loginAck.X},{loginAck.Y})");

                        // Lưu đầy đủ data từ server vào GameSession
                        if (GameSession.Instance != null)
                        {
                            GameSession.Instance.SetPlayerInfo(
                                playerID:     loginAck.PlayerID,
                                jobClassByte: loginAck.JobClass,
                                username:     "",
                                hp:           loginAck.HP,
                                maxHP:        loginAck.MaxHP,
                                x:            loginAck.X,
                                y:            loginAck.Y
                            );
                            GameSession.Instance.SetLevel(loginAck.Level, (int)loginAck.Exp);
                            GameSession.Instance.SetMapName(loginAck.MapName);
                        }

                        OnLoginSuccess?.Invoke(LocalPlayerID);
                    }
                    else
                    {
                        Debug.LogWarning($"[Network] Login failed: {loginAck.Message}");
                        OnLoginFailed?.Invoke(loginAck.Message);
                    }
                });
                break;

            case PacketType.RegisterAck:
                var regAck = PacketDecoder.DecodeRegisterAck(payload);
                Enqueue(() =>
                {
                    Debug.Log($"[Network] Register OK — Success={regAck.Success}");
                    OnRegisterResult?.Invoke(regAck.Success, regAck.Message);
                });
                break;

            case PacketType.JoinRoomAck:
                var joinAck = PacketDecoder.DecodeJoinRoomAck(payload);
                Enqueue(() => OnJoinRoomSuccess?.Invoke(joinAck.RoomID, joinAck.ExistingPlayers));
                break;

            case PacketType.PlayerJoined:
                var joined = PacketDecoder.DecodePlayerJoined(payload);
                Enqueue(() => OnPlayerJoined?.Invoke(joined));
                break;

            case PacketType.PlayerLeft:
                var leftID = PacketDecoder.DecodePlayerLeft(payload);
                Enqueue(() => OnPlayerLeft?.Invoke(leftID));
                break;

            case PacketType.WorldState:
                var ws = PacketDecoder.DecodeWorldState(payload);
                Enqueue(() => OnWorldState?.Invoke(ws));
                break;

            case PacketType.DamageEvent:
                var dmg = PacketDecoder.DecodeDamageEvent(payload);
                Enqueue(() => OnDamageEvent?.Invoke(dmg));
                break;

            case PacketType.DieEvent:
                var die = PacketDecoder.DecodeDieEvent(payload);
                Enqueue(() => OnDieEvent?.Invoke(die));
                break;

            case PacketType.RespawnAck:
                var resp = PacketDecoder.DecodeRespawnAck(payload);
                Enqueue(() => OnRespawnAck?.Invoke(resp));
                break;

            case PacketType.Pong:
                // RTT phải tính trên main thread (Time.time không dùng được từ background thread)
                Enqueue(() =>
                {
                    LatencyMs = (Time.time - pingTimestamp) * 1000f;
                    OnPong?.Invoke((uint)LatencyMs);
                });
                break;
        }
    }

    // ─── Private — Send ───────────────────────────────────────────────────────

    private void SendTCP(byte[] data)
    {
        try { tcpStream?.Write(data, 0, data.Length); }
        catch (Exception e) { Debug.LogError($"[Network] TCP send error: {e.Message}"); }
    }

    private void SendUDP(byte[] data)
    {
        try { udpClient?.Send(data, data.Length); }
        catch (Exception e) { Debug.LogError($"[Network] UDP send error: {e.Message}"); }
    }

    private void Enqueue(Action action)
    {
        lock (queueLock) { mainThreadActions.Enqueue(action); }
    }
}

// ── Shared Packet Structs ─────────────────────────────────────────────────────

[Serializable]
public struct PlayerInfo
{
    public uint   PlayerID;
    public string Username;
    public float  X, Y;
    public ushort HP, MaxHP;
    public byte   JobClass;
}

[Serializable]
public struct PlayerSnapshot
{
    public uint  PlayerID;
    public float X, Y, DirX, DirY;
    public ushort HP;
    public byte  State;
}

[Serializable]
public struct WorldStatePacket
{
    public uint            Tick;
    public PlayerSnapshot[] Players;
}

[Serializable]
public struct DamageEventPacket
{
    public uint   AttackerID, TargetID;
    public uint   Damage;
    public ushort RemainingHP;
    public bool   IsCrit;
}

[Serializable]
public struct DieEventPacket
{
    public uint PlayerID, KillerID;
}

[Serializable]
public struct RespawnAckPacket
{
    public uint  PlayerID;
    public float X, Y;
    public ushort HP;
}

public struct RegisterAckData
{
    public bool Success;
    public string Message;
}

public struct JoinRoomAckData
{
    public bool             Success;
    public string           RoomID;
    public List<PlayerInfo> ExistingPlayers;
}
