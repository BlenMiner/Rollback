using UnityEngine;
using Riten.Rollback;
using FishNet.Object;
using FishNet.Connection;

/// <summary>
/// This interface allows to check if two structs are essentially different
/// </summary>
/// <typeparam name="T">The struct's type.</typeparam>
public interface IError<T>
{
    /// <summary>
    /// This usually should allow a small margin or error for float values, for example 0.0001f
    /// Checking for float exact equality may lead to unecessary reconciliations
    /// </summary>
    /// <param name="other">The struct to compare to</param>
    /// <returns>True if different, false if equal</returns>
    bool HasError(T other)
    {
        return !other.Equals(this);
    }
}

/// <summary>
/// Fishnet networked authoritative controller with client prediction.
/// The client controlling it has to be the owner.
/// </summary>
/// <typeparam name="I">Input struct (Describes input, usually keys, look direction, etc, can also be empty or just time)</typeparam>
/// <typeparam name="S">State struct (This should describe the complete state at any frame for time travel)</typeparam>
public abstract class AuthoritativeController<I, S> : NetworkBehaviour 
    where I : struct, IError<I>
    where S : struct, IError<S>
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
    /// This function will be used to gather the user's input to move them around
    /// </summary>
    /// <returns>This tick's user input</returns>
    public abstract I GatherCurrentInput();

    /// <summary>
    /// This function will be used to gather the state of the player after apllying input
    /// </summary>
    /// <returns>The current player state</returns>
    public abstract S GatherCurrentState();

    /// <summary>
    /// Using the given state, teleport and apply the state to the controller
    /// </summary>
    /// <param name="state">State to apply</param>
    public abstract void ApplyMovementState(S state);

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

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();

        if (IsClient) TimeManager.OnTick -= OnClientTick;
        if (IsServer) TimeManager.OnTick -= OnServerTick;
    }

    private void OnClientTick()
    {
        if (!IsOwner) return;


    }


    private void OnServerTick()
    {
        if (IsOwner) return;


    }

    /// <summary>
    /// The server calls this when it notices an issue only otherwise this won't be called.
    /// Even if this is called, we check for the error ourselves because if we rewrote the history 
    /// after sending the packets the server might be 'wrong'.
    /// </summary>
    [TargetRpc]
    void Reconcile(NetworkConnection conn, ulong tick, S serverState)
    {
        bool validClientState = m_stateHistory.Read(tick, out var clientState);
        bool needsReconcile = clientState.HasError(serverState);
        
        if (!validClientState || !needsReconcile) return;

        ulong presentTick = m_stateHistory.MostRecentTick;

        m_stateHistory.ClearFuture(tick, true);
        m_stateHistory.Write(tick, serverState);

        ApplyMovementState(serverState);

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
    [ServerRpc(RequireOwnership = true, RunLocally = true)]
    void SendInput(ulong tick, I input, S state)
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

            m_inputHistory.Write(tick, input);
            m_stateHistory.Write(tick, state);
        }
        else
        {
            Debug.LogError($"Illegal input received for tick {tick}");
        }
    }
}