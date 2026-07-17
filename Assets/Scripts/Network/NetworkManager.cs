using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

/// <summary>
/// NetworkManager — connects to the Go game server.
///
/// TCP: Login, JoinRoom, Attack, Damage events (reliable).
/// UDP: sends MoveInput every frame, receives WorldState broadcasts (low-latency).
///
/// SRP: handles network I/O only, contains no game logic.
/// Singleton pattern (only 1 instance across the entire game).
/// </summary>
public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance { get; private set; }

    // ─── Config ───────────────────────────────────────────────────────────────

    [Header("Server")]
    private string serverIP = "54.169.108.73";
    private int    tcpPort  = 7777;
    private int    udpPort  = 7778;

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
    public event Action<ProjectileSpawnPacket>    OnProjectileSpawn;
    public event Action<uint>                     OnPong;
    /// <summary>Fired when the server returns the 3-slot character list in response to GetCharListReq.</summary>
    public event Action<CharacterData[]>          OnCharacterListReceived;

    // ─── State ────────────────────────────────────────────────────────────────

    public uint  LocalPlayerID { get; private set; }
    public byte  LocalJobClass  { get; private set; } = 1; // 1 = Archer (server default)
    public bool  IsConnected   => tcpClient != null && tcpClient.Connected && tcpStream != null;

    private TcpClient     tcpClient;
    private NetworkStream tcpStream;
    private UdpClient     udpClient;

    private Thread tcpReadThread;
    private Thread udpReadThread;

    // Thread-safe queue to marshal network events onto the main thread
    private readonly Queue<Action> mainThreadActions = new Queue<Action>();
    private readonly object        queueLock         = new object();

    private uint  moveTimestamp;
    private float moveInputTimer;

    // ─── Stats (FPS + Ping) ───────────────────────────────────────────────────
    private float statsTimer;
    private float pingTimestamp;        // Time.time when SendPing was called
    public  float LatencyMs  { get; private set; } = -1f;  // RTT in ms, -1 = not yet measured
    public  float CurrentFPS { get; private set; }

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Maintain connection and run game loop in the background to prevent desync
        Application.runInBackground = true;
    }

    private void Update()
    {
        // Dispatch network thread actions on the main thread
        lock (queueLock)
        {
            while (mainThreadActions.Count > 0)
                mainThreadActions.Dequeue()?.Invoke();
        }

        // FPS + Ping log every second
        CurrentFPS  = 1f / Mathf.Max(Time.deltaTime, 0.0001f);
        statsTimer += Time.deltaTime;
        if (statsTimer >= 1f)
        {
            statsTimer = 0f;
            string latencyStr = LatencyMs >= 0 ? $"{LatencyMs:F0}ms" : "---";
            Debug.Log($"[Network] FPS={CurrentFPS:F0}  Ping={latencyStr}");

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

    /// <summary>Connects to the server and logs in with username, password, and slot.</summary>
    public void Connect(string username, string password, byte slot)
    {
        StartCoroutine(ConnectRoutine(username, password, slot));
    }

    /// <summary>Registers a new account.</summary>
    public void RegisterAccount(string username, string password)
    {
        StartCoroutine(RegisterRoutine(username, password));
    }

    /// <summary>Joins a room — empty string triggers automatic matchmaking.</summary>
    public void JoinRoom(string roomID = "")
    {
        var buf = PacketEncoder.EncodeJoinRoomReq(roomID);
        SendTCP(buf);
    }

    /// <summary>Sends MoveInput over UDP — called from Player.Update().</summary>
    public void SendMoveInput(uint playerID, Vector2 dest, Vector2 dir)
    {
        moveTimestamp++;
        var buf = PacketEncoder.EncodeMoveInput(playerID, dest, dir, moveTimestamp);
        SendUDP(buf);
    }

    /// <summary>Sends an AttackReq over TCP.</summary>
    public void SendAttack(uint playerID, uint targetID, Vector2 dir)
    {
        var buf = PacketEncoder.EncodeAttackReq(playerID, targetID, dir);
        SendTCP(buf);
    }

    /// <summary>Sends a DashReq with NavMesh-computed path over TCP.</summary>
    public void SendDash(uint playerID, Vector3[] waypoints, float totalDistance)
    {
        var buf = PacketEncoder.EncodeDashReq(playerID, waypoints, totalDistance);
        SendTCP(buf);
    }

    /// <summary>Sends a Respawn request.</summary>
    public void SendRespawn(uint playerID)
    {
        var buf = PacketEncoder.EncodeRespawnReq(playerID);
        SendTCP(buf);
    }

    /// <summary>Sends a Ping to measure latency.</summary>
    public void SendPing()
    {
        uint ts = (uint)(Time.time * 1000);
        var buf = PacketEncoder.EncodePing(ts);
        SendTCP(buf);
    }

    /// <summary>
    /// Sends a LoginReq packet over the existing TCP connection.
    /// Used to select a character slot after authentication without reconnecting.
    /// </summary>
    public void SendLoginReq(string username, string password, byte slot)
    {
        var loginBuf = PacketEncoder.EncodeLoginReq(username, password, slot);
        SendTCP(loginBuf);
        Debug.Log($"[Network] Sent LoginReq for slot {slot} over existing connection");
    }

    /// <summary>
    /// Sends GetCharListReq — requests the server to return the 3 character slots.
    /// Call after a successful LoginAck, before showing the CharacterPickingPanel.
    /// </summary>
    public void RequestCharacterList()
    {
        var buf = PacketEncoder.EncodeGetCharListReq();
        SendTCP(buf);
        Debug.Log("[Network] GetCharListReq sent");
    }

    /// <summary>
    /// Sends NavMesh path waypoints to the server over TCP.
    /// The server moves the player along these waypoints instead of in a straight line.
    /// Call after NavMesh has computed a path (agent.path.corners available).
    /// </summary>
    public void SendMovePath(uint playerID, Vector3[] corners)
    {
        if (corners == null || corners.Length == 0) return;
        var buf = PacketEncoder.EncodeMovePathPacket(playerID, corners);
        SendTCP(buf);
    }

    /// <summary>Disconnects from the server.</summary>
    public void Disconnect()
    {
        tcpReadThread?.Abort();
        udpReadThread?.Abort();
        tcpStream?.Close();
        tcpStream = null;
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

        udpClient = new UdpClient();
        udpClient.Connect(serverIP, udpPort);

        tcpReadThread = new Thread(TCPReadLoop) { IsBackground = true };
        tcpReadThread.Start();

        udpReadThread = new Thread(UDPReadLoop) { IsBackground = true };
        udpReadThread.Start();

        Debug.Log("[Network] Connected. Sending login...");

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
            // Normal — occurs when Unity exits Play mode and aborts the thread
        }
        catch (Exception e)
        {
            if (tcpClient != null && !e.Message.Contains("Thread was being aborted") && !e.Message.Contains("aborted"))
                Enqueue(() => Debug.LogWarning($"[Network] TCP read warning: {e.Message}"));
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
            // Normal — occurs when Unity exits Play mode and aborts the thread
        }
        catch (Exception e)
        {
            if (udpClient != null && !e.Message.Contains("Thread was being aborted") && !e.Message.Contains("aborted"))
                Enqueue(() => Debug.LogWarning($"[Network] UDP read warning: {e.Message}"));
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
                        LocalPlayerID = loginAck.PlayerID;
                        LocalJobClass = loginAck.JobClass;
                        Debug.Log($"[Network] Login OK — ID={LocalPlayerID} Job={LocalJobClass} Level={loginAck.Level} Map={loginAck.MapName} Pos=({loginAck.X},{loginAck.Y})");

                        if (GameSession.Instance != null)
                        {
                            GameSession.Instance.SetPlayerInfo(
                                playerID:     loginAck.PlayerID,
                                jobClassByte: loginAck.JobClass,
                                username:     loginAck.CharName,
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

            case PacketType.ProjectileSpawn:
                var proj = PacketDecoder.DecodeProjectileSpawn(payload);
                Enqueue(() => OnProjectileSpawn?.Invoke(proj));
                break;

            case PacketType.GetCharListAck:
                var charList = PacketDecoder.DecodeGetCharListAck(payload);
                Enqueue(() =>
                {
                    Debug.Log($"[Network] GetCharListAck received: {charList.Length} slots");
                    OnCharacterListReceived?.Invoke(charList);
                });
                break;

            case PacketType.Pong:
                // RTT must be calculated on the main thread (Time.time is not thread-safe)
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
    public uint   PlayerID;
    public float  X, Y, DirX, DirY;
    public ushort HP;
    public byte   State;
}

[Serializable]
public struct WorldStatePacket
{
    public uint             Tick;
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
    public bool   Success;
    public string Message;
}

public struct JoinRoomAckData
{
    public bool             Success;
    public string           RoomID;
    public List<PlayerInfo> ExistingPlayers;
}
