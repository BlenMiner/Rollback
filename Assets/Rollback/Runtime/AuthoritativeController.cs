using UnityEngine;
using Riten.Rollback;
using FishNet.Object;
using FishNet.Connection;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using System.Runtime.Serialization;
using System;

/// <summary>
/// Fishnet networked authoritative controller with client prediction.
/// The client controlling it has to be the owner.
/// </summary>
/// <typeparam name="I">Input struct (Describes input, usually keys, look direction, etc, can also be empty or just time)</typeparam>
/// <typeparam name="S">State struct (This should describe the complete state at any frame for time travel)</typeparam>
public abstract class AuthoritativeController<I, S> : NetworkBehaviour 
    where I : struct
    where S : struct
{

    [System.Serializable]
    struct AuthoritativeSettings
    {
        [Tooltip("Server waits X inputs before using them to allow for late packets to arrive. Higher numbers avoids skips but adds delay.")]
        public int MinServerBufferSize;

        [Tooltip("If buffer is behind X inputs it will simulate a bit faster to catch up.")]
        public int MaxServerBufferSize;

        public int HistoryBufferSize;
    }

    const int MEMORY_CAPACITY = 1024;

    static BinaryFormatter FORMATTER = new BinaryFormatter();

    static MemoryStream STREAM = new (MEMORY_CAPACITY);

    static byte[] BUFFER = new byte[MEMORY_CAPACITY];

    [SerializeField] AuthoritativeSettings m_authoritativeSettings;

    History<I> m_inputHistory;

    History<S> m_stateHistory;

    /// <summary>
    /// Which tick is currently being processed.
    /// </summary>
    /// <value>Tick number.</value>
    public ulong ControllerTick
    {
        get
        {
            if (IsOwner)
            {
                return TimeManager.LocalTick;
            }
            else
            {
                return m_serverTick;
            }
        }
    }

    ulong m_serverTick;

    protected override void Reset()
    {
        base.Reset();

        m_authoritativeSettings = new AuthoritativeSettings{
            HistoryBufferSize = 1024,
            MaxServerBufferSize = 10,
            MinServerBufferSize = 5
        };
    }

    /// <summary>
    /// Contains information to rollback to previous ticks
    /// </summary>
    /// <value>Input history</value>
    public History<I> InputHistory 
    {
        get 
        {
            if (m_inputHistory == null)
                m_inputHistory = new (m_authoritativeSettings.HistoryBufferSize);

            return m_inputHistory;
        }
    }

    /// <summary>
    /// Contains information to rollback to previous ticks
    /// </summary>
    /// <value>State history</value>
    public History<S> StateHistory 
    {
        get 
        {
            if (m_stateHistory == null)
                m_stateHistory = new (m_authoritativeSettings.HistoryBufferSize);

            return m_stateHistory;
        }
    }

    /// <summary>
    /// Gets called on the OnTick event on the client only once.
    /// This function will be used to gather the user's input to move them around.
    /// Be careful with checking for KeyDown or KeyUp, it might not catch it, rather check in Update and lazy load here.
    /// </summary>
    /// <returns>This tick's user input</returns>
    public abstract I GatherCurrentInput();

    /// <summary>
    /// This function will be used to gather the state of the player after apllying input
    /// </summary>
    /// <returns>The current player state</returns>
    public abstract S GatherCurrentState();

    public abstract bool HasError(S stateA, S stateB);

    /// <summary>
    /// Using the given state, teleport and apply the state to the controller
    /// </summary>
    /// <param name="state">State to apply</param>
    public abstract void ApplyState(S state);

    /// <summary>
    /// Using input and delta time move the player to the next frame
    /// </summary>
    /// <param name="input">User input</param>
    /// <param name="delta">Delta time</param>
    /// <param name="replay">Use this to avoid playing sounds multiple times if replay is true</param>
    public abstract void Simulate(I input, double delta, bool replay);

    public override void OnStartServer()
    {
        base.OnStartServer();

        TimeManager.OnTick += OnServerTick;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        TimeManager.OnTick += OnClientTick;
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();

        if( !typeof(I).IsSerializable && !(typeof(ISerializable).IsAssignableFrom(typeof(I)) ) )
            throw new InvalidOperationException("Input struct must be Serializable");

                if( !typeof(S).IsSerializable && !(typeof(ISerializable).IsAssignableFrom(typeof(S)) ) )
            throw new InvalidOperationException("State struct must be serializable");
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();

        if (IsClient) TimeManager.OnTick -= OnClientTick;
        if (IsServer) TimeManager.OnTick -= OnServerTick;
    }

    private void OnClientTick()
    {
        if (!IsOwner) return;

        ulong tick = TimeManager.LocalTick;
        var input = GatherCurrentInput();

        Simulate(input, TimeManager.TickDelta, false);
        SendInput(tick, ToArray(input), ToArray(GatherCurrentState()));
    }


    private void OnServerTick()
    {
        if (IsOwner) return;

        int minBuffer = m_authoritativeSettings.MinServerBufferSize;
        int maxBuffer = m_authoritativeSettings.MaxServerBufferSize;

        if (m_inputHistory.Count >= minBuffer)
        {
            if (m_inputHistory.Find(m_serverTick, out var index) && m_inputHistory.Count - index > maxBuffer)
            {
                int targetIndex = m_inputHistory.Count - minBuffer;
                ulong newTick = m_inputHistory.GetEntryTick(targetIndex);

                Debug.LogError($"Too many inputs behind, we need to catch up. Skipped {newTick - m_serverTick} ticks.");

                m_serverTick = newTick;
            }

            bool validState = m_inputHistory.Read(m_serverTick, out var input);

            if (!validState)
            {
                if (m_inputHistory.MostRecentTick < m_serverTick)
                {
                    Debug.LogError("Waiting for missing tick.");
                    return;
                }
                else
                {
                    Debug.LogError("Packet dropped, skipped input frame.");
                }
            }

            Simulate(input, TimeManager.TickDelta, false);
            var serverState = GatherCurrentState();

            m_stateHistory.Read(m_serverTick, out var clientState);
            m_stateHistory.Write(m_serverTick, serverState);


            if (HasError(serverState, clientState))
            {
                Reconcile(Owner, m_serverTick, ToArray(serverState));
            }

            m_serverTick += 1;
        }
    }

    /// <summary>
    /// Generic aren't supported so this deserialized the data to get our struct back
    /// </summary>
    T ReadArray<T>(byte[] data)
    {
        STREAM.Write(data, 0, data.Length);
        return (T)FORMATTER.Deserialize(STREAM);
    }

    /// <summary>
    /// Generic aren't supported so this serialized the data to bytes
    /// </summary>
    byte[] ToArray<T>(T data)
    {
        FORMATTER.Serialize(STREAM, data);
        STREAM.Read(BUFFER, 0, (int)STREAM.Length);
        return BUFFER;
    }

    /// <summary>
    /// The server calls this when it notices an issue only otherwise this won't be called.
    /// Even if this is called, we check for the error ourselves because if we rewrote the history 
    /// after sending the packets the server might be 'wrong'.
    /// </summary>
    [TargetRpc(DataLength = MEMORY_CAPACITY)]
    void Reconcile(NetworkConnection conn, ulong tick, byte[] rawServerState)
    {
        S serverState = ReadArray<S>(rawServerState);

        bool validClientState = m_stateHistory.Read(tick, out var clientState);
        bool needsReconcile = HasError(clientState, serverState);
        
        if (!validClientState || !needsReconcile) return;

        ulong presentTick = m_stateHistory.MostRecentTick;

        m_stateHistory.ClearFuture(tick, true);
        m_stateHistory.Write(tick, serverState);

        ApplyState(serverState);

        for (ulong t = tick + 1; t <= presentTick; ++t)
        {
            if (!m_inputHistory.Read(t, out var input))
            {
                Debug.LogError($"Client skipping missing input at tick {t}");
                continue;
            }

            Simulate(input, TimeManager.TickDelta, true);

            var newState = GatherCurrentState();
            m_stateHistory.Write(t, newState);
        }
    }

    /// <summary>
    /// Send the input data along with the resulting movement to the server.
    /// The server will use the input to move the player and the resulting state to check for errors.
    /// </summary>
    [ServerRpc(RequireOwnership = true, RunLocally = true, DataLength = MEMORY_CAPACITY)]
    void SendInput(ulong tick, byte[] rawInput, byte[] rawState)
    {
        bool firstInput = m_inputHistory.Count == 0;

        // Safety checks
        if (firstInput || tick > m_inputHistory.MostRecentTick)
        {
            if (firstInput)
            {
                // Initialize server tick number
                m_serverTick = tick;
            }

            I input = ReadArray<I>(rawInput);
            S state = ReadArray<S>(rawState);

            m_inputHistory.Write(tick, input);
            m_stateHistory.Write(tick, state);
        }
        else
        {
            Debug.LogError($"Illegal input received for tick {tick}");
        }
    }
}