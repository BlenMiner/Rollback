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
    public abstract class NetworkedController : NetworkBehaviour, IRollback
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

        protected override void Reset()
        {
            base.Reset();

            m_authoritativeSettings = new AuthoritativeSettings{
                HistoryBufferSize = 1024,
                MaxServerBufferSize = 10,
                MinServerBufferSize = 5
            };
        }

        bool m_noInputRequired = false;

        Func<byte[]> GatherCurrentInput;

        Func<byte[]> GatherCurrentState;

        Action<ulong, byte[]> RegisterInput;

        Action<ulong, byte[]> RegisterState;

        Func<ulong, byte[]> ReadInput;

        Action<byte[]> ApplyState;

        Action<ulong> Rollback;

        Action ResetStateTranslator;

        Action<byte[], double, bool> Simulate;

        Action<double, bool> InputlessSimulate;

        Action<ulong, double, int, int, Action<NetworkConnection, ulong, byte[]>> OnServerTickTranslator;

        Action<ulong, double, Action<NetworkConnection, ulong, byte[]>> OnInputlessServerTickTranslator;

        Action<double, ulong, byte[]> ReconcileTranslator;

        Action<ulong, byte[], byte[]> SendInputTranslator;

        Action<NetworkConnection, ulong, byte[]> SendStateTranslator;

        protected virtual void OnEnable()
        {
            NetworkedScene.RegisterController(this);
        }

        protected virtual void OnDisable()
        {
            NetworkedScene.UnregisterController(this);
        }
        
        /// <summary>
        /// Initializes the History params from the interface.
        /// </summary>
        /// <param name="contract">The controller instance aka 'this'</param>
        /// <typeparam name="I">Input struct type</typeparam>
        /// <typeparam name="S">State struct type</typeparam>
        /// <returns></returns>
        public void Initialize<I, S>(IAuthoritative<I, S> contract)
            where I : struct
            where S : struct
        {
            m_noInputRequired = false;

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

            Rollback = (tick) => {
                if (contract.StateHistory.Read(tick, out var state))
                {
                    contract.ApplyState(state);
                }
            };

            ResetStateTranslator = () => {
                if (contract.StateHistory.Read(contract.StateHistory.MostRecentTick, out var state))
                {
                    contract.ApplyState(state);
                }
            };
        }


        /// <summary>
        /// Initializes the History params from the interface.
        /// </summary>
        /// <param name="contract">The controller instance aka 'this'</param>
        /// <typeparam name="S">State struct type</typeparam>
        public void Initialize<S>(IAuthoritative<S> contract)
            where S : struct
        {
            m_noInputRequired = true;

    #if UNITY_EDITOR
            if( !typeof(S).IsSerializable && !(typeof(ISerializable).IsAssignableFrom(typeof(S)) ) )
                throw new InvalidOperationException($"{typeof(S).Name} struct must be serializable: <b>[System.Serializable]</b>");
    #endif
            contract.Initialize(m_authoritativeSettings.HistoryBufferSize);

            GatherCurrentInput = null;

            GatherCurrentState = () => {
                var state = contract.GatherCurrentState();
                MemoryHelper.WriteArray<S>(state, MemoryHelper.BUFFER_S);
                return MemoryHelper.BUFFER_S;
            };

            InputlessSimulate = contract.Simulate;

            ApplyState = contract.ApplyState;

            ReconcileTranslator = contract.Reconcile;

            OnInputlessServerTickTranslator = contract.OnServerTick;

            SendStateTranslator = contract.SendState;

            RegisterState = (tick, state) => {
                contract.StateHistory.Write(tick, MemoryHelper.ReadArray<S>(state));
            };

            Rollback = (tick) => {
                if (contract.StateHistory.Read(tick, out var state))
                {
                    contract.ApplyState(state);
                }
            };

            ResetStateTranslator = () => {
                if (contract.StateHistory.Read(contract.StateHistory.MostRecentTick, out var state))
                {
                    contract.ApplyState(state);
                }
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

        ulong CurrentTick => IsServer ? TimeManager.Tick : (TimeManager.Tick + TimeManager.TimeToTicks((TimeManager.RoundTripTime / 1000.0)));

        void OnGUI()
        {
            if (!m_noInputRequired)
            {
                GUILayout.Label(CurrentTick.ToString());
            }
        }

        private void OnClientTick()
        {
            if (m_noInputRequired)
            {
                InputlessSimulate(TimeManager.TickDelta, false);
                return;
            }

            if (!IsOwner) return;

            var input = GatherCurrentInput();
            Simulate(input, TimeManager.TickDelta, false);
            RegisterInput(CurrentTick, input);
        }

        private void OnClientPostTick()
        {
            var state = GatherCurrentState();
            ulong tick = CurrentTick;

            if (m_noInputRequired)
            {
                RegisterState(tick, state);
                SendState(tick, state);
            }
            else if (IsOwner)
            {
                RegisterState(tick, state);
                SendInput(tick, ReadInput(tick), state);
            }
        }

        private void OnServerTick()
        {
            if (IsOwner) return;

            if (m_noInputRequired)
            {
                OnInputlessServerTickTranslator(CurrentTick, TimeManager.TickDelta, PrepareReconcileData);
            }
            else
            {
                int minBuffer = m_authoritativeSettings.MinServerBufferSize;
                int maxBuffer = m_authoritativeSettings.MaxServerBufferSize;

                OnServerTickTranslator(CurrentTick, TimeManager.TickDelta, minBuffer, maxBuffer, PrepareReconcileData);
            }
        }

        void PrepareReconcileData(NetworkConnection conn, ulong tick, byte[] data)
        {
            if (m_noInputRequired)
            {
                Reconcile(conn, tick, data);
            }
            else
            {
                Reconcile(Owner, tick, data);
            }
        }

        /// <summary>
        /// The server calls this when it notices an issue only otherwise this won't be called.
        /// Even if this is called, we check for the error ourselves because if we rewrote the history 
        /// after sending the packets the server might be 'wrong'.
        /// </summary>
        [ObserversRpc(DataLength = MemoryHelper.MEMORY_CAPACITY), 
            TargetRpc(DataLength = MemoryHelper.MEMORY_CAPACITY)]
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
            if (m_noInputRequired) return;

            SendInputTranslator(tick, input, state);
        }

        /// <summary>
        /// Send the input data along with the resulting movement to the server.
        /// The server will use the input to move the player and the resulting state to check for errors.
        /// </summary>
        [ServerRpc(RequireOwnership = false, DataLength = MemoryHelper.MEMORY_CAPACITY)]
        void SendState(ulong tick, byte[] state, NetworkConnection conn = null)
        {
            if (!m_noInputRequired) return;

            SendStateTranslator(conn, tick, state);
        }

        public void RollbackTo(ulong tick)
        {
            Rollback(tick);
        }

        public void ResetState()
        {
            ResetStateTranslator();
        }
    }
}