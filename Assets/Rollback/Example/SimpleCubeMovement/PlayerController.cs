using FishNet.Connection;
using FishNet.Object;
using UnityEngine;
using Riten.Rollback;

public sealed class PlayerController : NetworkBehaviour
{
    public struct PlayerInput
    {
        public Vector2Int MoveInput;
    }

    public struct PlayerState
    {
        public Vector3 Position;
    }

    [SerializeField, Tooltip("Server waits X inputs before using them to allow for late packets to arrive. Higher numbers avoids skips but adds delay.")]
    int m_minServerBufferSize = 5;

    [SerializeField, Tooltip("If buffer is behind X inputs it will simulate a bit faster to catch up.")]
    int m_maxServerBufferSize = 10;

    [SerializeField] float m_maxError = 0.001f;

    [SerializeField] int m_inputBufferSize = 1024;

    History<PlayerInput> m_inputHistory;

    History<PlayerState> m_stateHistory;

    void Awake()
    {
        if (m_inputHistory == null) m_inputHistory = new (m_inputBufferSize);
        if (m_stateHistory == null) m_stateHistory = new (m_inputBufferSize);
    }

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
        
        ulong tick = TimeManager.LocalTick;

        int horizontal = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0);
        int vertical = (Input.GetKey(KeyCode.Z) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);

        PlayerInput input = new PlayerInput {
            MoveInput = new Vector2Int(horizontal, vertical)
        };

        PlayerState clientState = SimulateMovement(input);
        SendInput(tick, input, clientState);
    }

    ulong m_serverTick;

    private void OnServerTick()
    {
        if (IsOwner) return;

        if (m_inputHistory.Count >= m_minServerBufferSize)
        {
            if (m_inputHistory.Find(m_serverTick, out var index) && m_inputHistory.Count - index > m_maxServerBufferSize)
            {
                int targetIndex = m_inputHistory.Count - m_minServerBufferSize - 1;
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

            PlayerState serverState = SimulateMovement(input);
            m_stateHistory.Read(m_serverTick, out var clientState);
            m_stateHistory.Write(m_serverTick, serverState);


            float error = Vector3.Distance(clientState.Position, serverState.Position);
            
            if (error > m_maxError)
            {
                Reconcile(Owner, m_serverTick, serverState);
            }

            m_serverTick += 1;
        }
    }

    PlayerState SimulateMovement(PlayerInput input)
    {
        const float SPEED = 10f;

        float delta = (float)TimeManager.TickDelta;
        Vector2 moveDir = input.MoveInput;

        moveDir.Normalize();

        var move = moveDir * delta * SPEED;

        // Stupid solution
        TranslateWithCollision(new Vector3(move.x, 0, 0));
        TranslateWithCollision(new Vector3(0, move.y, 0));

        return new PlayerState() {
            Position = transform.position
        };
    }

    static RaycastHit[] CACHE = new RaycastHit[512];
 
    void TranslateWithCollision(Vector3 move)
    {
        const float SKIN_SIZE = 0.1f;
        const float BOX_SIZE = 1f - SKIN_SIZE;

        int count = Physics.BoxCastNonAlloc(transform.position, new Vector3(BOX_SIZE, BOX_SIZE, BOX_SIZE) * 0.5f, move.normalized, CACHE, transform.rotation, move.magnitude);

        count = Mathf.Min(count, CACHE.Length);

        float moveDistance = move.magnitude;

        for (int i = 0; i < count; ++i)
        {
            var hit = CACHE[i];

            if (hit.distance < moveDistance && hit.collider.gameObject != gameObject)
            {
                moveDistance = hit.distance;
            }
        }

        transform.position += move.normalized * (moveDistance - SKIN_SIZE * 0.5f);
    }

    void ApplyState(PlayerState state)
    {
        transform.position = state.Position;
    }

    [TargetRpc]
    void Reconcile(NetworkConnection conn, ulong tick, PlayerState serverState)
    {
        bool validClientState = m_stateHistory.Read(tick, out var clientState);

        float error = Vector3.Distance(clientState.Position, serverState.Position);
        bool needsReconcile = error > m_maxError;
        
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

            var newState = SimulateMovement(input);
            m_stateHistory.Write(t, newState);
        }
    }

    [ServerRpc(RequireOwnership = true, RunLocally = true)]
    void SendInput(ulong tick, PlayerInput input, PlayerState clientState)
    {
        bool firstInput = m_inputHistory.Count == 0;

        // Safety check
        if (firstInput || tick > m_inputHistory.MostRecentTick)
        {
            if (firstInput)
            {
                // Initialize server tick number
                m_serverTick = tick;
            }

            m_inputHistory.Write(tick, input);
            m_stateHistory.Write(tick, clientState);
        }
        else
        {
            Debug.LogError($"Illegal input received for tick {tick}");
        }
    }
}
