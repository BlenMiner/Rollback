using UnityEngine;
using FishNet.Object;
using FishNet.Connection;
using System.Runtime.Serialization;
using System;
using Riten.Utils;

namespace Riten.Authorative
{
    /// <summary>
    /// Fishnet networked authoritative controller with client prediction.
    /// The client controlling it has to be the owner.
    /// </summary>
    public abstract class NetworkedController : NetworkBehaviour
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

        Func<byte[]> GatherCurrentInput;

        Func<byte[]> GatherCurrentState;

        Action<ulong, byte[]> RegisterInput;

        Action<ulong, byte[]> RegisterState;

        Func<ulong, byte[]> ReadInput;

        Action<byte[]> ApplyState;

        Action<byte[], double, bool> Simulate;

        Func<ulong, double, int, int, Action<ulong, byte[]>, ulong> OnServerTickTranslator;

        Action<double, ulong, byte[]> ReconcileTranslator;

        Action<ulong, byte[], byte[], Action<ulong>> SendInputTranslator;

        protected virtual void OnEnable()
        {
            NetworkedScene.RegisterController(this);
        }

        protected virtual void OnDisable()
        {
            NetworkedScene.UnregisterController(this);
        }

        /// <summary>
        /// Gets called on the OnTick event on the client only once.
        /// This function will be used to gather the user's input to move them around.
        /// Be careful with checking for KeyDown or KeyUp, it might not catch it, rather check in Update and lazy load here.
        /// </summary>
        /// <returns>This tick's user input</returns>
        public void Initialize<I, S>(IAuthoritative<I, S> contract)
            where I : struct
            where S : struct
        {

    #if UNITY_EDITOR
            if( !typeof(I).IsSerializable && !(typeof(ISerializable).IsAssignableFrom(typeof(I)) ) )
                throw new InvalidOperationException($"{typeof(I).Name} struct must be serializable: <b>[System.Serializable]</b>");

            if( !typeof(S).IsSerializable && !(typeof(ISerializable).IsAssignableFrom(typeof(S)) ) )
                throw new InvalidOperationException($"{typeof(S).Name} struct must be serializable: <b>[System.Serializable]</b>");
    #endif

            contract.Initialize(m_authoritativeSettings.HistoryBufferSize);

            GatherCurrentInput = () => {
                var input = contract.GatherCurrentInput();
                MemoryHelper.WriteArray<I>(input, MemoryHelper.BUFFER_I);
                return MemoryHelper.BUFFER_I;
            };

            GatherCurrentState = () => {
                var state = contract.GatherCurrentState();
                MemoryHelper.WriteArray<S>(state, MemoryHelper.BUFFER_S);
                return MemoryHelper.BUFFER_S;
            };

            Simulate = contract.Simulate;

            ApplyState = contract.ApplyState;

            ReconcileTranslator = contract.Reconcile;

            OnServerTickTranslator = contract.OnServerTick;

            SendInputTranslator = contract.SendInput;

            RegisterInput = (tick, input) => {
                contract.InputHistory.Write(tick, MemoryHelper.ReadArray<I>(input));
            };

            RegisterState = (tick, state) => {
                contract.StateHistory.Write(tick, MemoryHelper.ReadArray<S>(state));
            };

            ReadInput = (tick) => {
                contract.InputHistory.Read(tick, out var input);
                MemoryHelper.WriteArray<I>(input, MemoryHelper.BUFFER_I);
                return MemoryHelper.BUFFER_I;
            };
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
            TimeManager.OnPostTick += OnClientPostTick;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();

            if (IsClient) 
            {
                TimeManager.OnTick -= OnClientTick;
                TimeManager.OnPostTick -= OnClientPostTick;
            }
            
            if (IsServer) TimeManager.OnTick -= OnServerTick;
        }

        private void OnClientTick()
        {
            if (!IsOwner) return;

            ulong tick = TimeManager.LocalTick;
            var input = GatherCurrentInput();

            Simulate(input, TimeManager.TickDelta, false);
            RegisterInput(tick, input);
        }

        private void OnClientPostTick()
        {
            ulong tick = TimeManager.LocalTick;

            var state = GatherCurrentState();

            RegisterState(tick, state);
            SendInput(tick, ReadInput(tick), state);
        }

        private void OnServerTick()
        {
            if (IsOwner) return;

            int minBuffer = m_authoritativeSettings.MinServerBufferSize;
            int maxBuffer = m_authoritativeSettings.MaxServerBufferSize;

            m_serverTick = OnServerTickTranslator(m_serverTick, TimeManager.TickDelta, minBuffer, maxBuffer, PrepareReconcileData);
        }

        void PrepareReconcileData(ulong tick, byte[] data)
        {
            Reconcile(Owner, tick, data);
        }

        /// <summary>
        /// The server calls this when it notices an issue only otherwise this won't be called.
        /// Even if this is called, we check for the error ourselves because if we rewrote the history 
        /// after sending the packets the server might be 'wrong'.
        /// </summary>
        [TargetRpc(DataLength = MemoryHelper.MEMORY_CAPACITY)]
        void Reconcile(NetworkConnection conn, ulong tick, byte[] serverState)
        {
            ReconcileTranslator(TimeManager.TickDelta, tick, serverState);
        }

        /// <summary>
        /// Send the input data along with the resulting movement to the server.
        /// The server will use the input to move the player and the resulting state to check for errors.
        /// </summary>
        [ServerRpc(RequireOwnership = true, DataLength = MemoryHelper.MEMORY_CAPACITY)]
        void SendInput(ulong tick, byte[] input, byte[] state)
        {
            SendInputTranslator(tick, input, state, UpdateServerTick);
        }

        void UpdateServerTick(ulong tick)
        {
            m_serverTick = tick;
        }
    }
}