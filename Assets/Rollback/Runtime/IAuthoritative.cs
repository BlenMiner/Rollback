using System;
using System.Collections.Generic;
using FishNet.Connection;
using Riten.Rollback;
using Riten.Utils;
using UnityEngine;

namespace Riten.Authorative
{
    public interface IRollback
    {
        void RollbackTo(ulong tick);

        void ResetState();
    }

    public interface IAuthoritative<S>
        where S : struct
    {
        /// <summary>
        /// Contains information to rollback to previous ticks for all players
        /// </summary>
        /// <value>State history</value>
        Dictionary<NetworkConnection, History<S>> StateHistories { get; set; }

        /// <summary>
        /// Contains information to rollback to previous ticks for local player
        /// </summary>
        /// <value>State history</value>
        History<S> StateHistory { get; set; }

        void Initialize(int maxEntries)
        {
            StateHistories = new Dictionary<NetworkConnection, History<S>>();
            StateHistory = new History<S>(maxEntries);
        }

        History<S> GetHistory(NetworkConnection conn = null)
        {
            if (conn == null)
            {
                return StateHistory;
            }
            else
            {
                if (StateHistories.TryGetValue(conn, out var history))
                {
                    return history;
                }

                history = new History<S>(StateHistory.Capacity);
                StateHistories.Add(conn, history);

                return history;
            }
        }

        void ClearConnection(NetworkConnection conn)
        {
            StateHistories.Remove(conn);
        }

        /// <summary>
        /// This function will be used to gather the state of the player after apllying input
        /// </summary>
        /// <returns>The current player state</returns>
        S GatherCurrentState();

        /// <summary>
        /// Check if two states are equal, you should add a margin for floating point error
        /// 0.001 is usually a good start
        /// </summary>
        /// <param name="stateA">Value A</param>
        /// <param name="stateB">Value B</param>
        /// <returns>True if they are different, aka contain an "error"</returns>
        bool HasError(S stateA, S stateB);

        bool HasError(byte[] stateA, byte[] stateB)
        {
            return HasError(MemoryHelper.ReadArray<S>(stateA), MemoryHelper.ReadArray<S>(stateB));
        }

        /// <summary>
        /// Using the given state, teleport and apply the state to the controller
        /// </summary>
        /// <param name="state">State to apply</param>
        void ApplyState(S state);

        void ApplyState(byte[] state)
        {
            ApplyState(MemoryHelper.ReadArray<S>(state));
        }

        /// <summary>
        /// Using input and delta time move the player to the next frame
        /// </summary>
        /// <param name="delta">Delta time</param>
        /// <param name="replay">Use this to avoid playing sounds multiple times if replay is true</param>
        void Simulate(double delta, bool replay);

        void Reconcile(double delta, ulong tick, byte[] rawServerState)
        {
            S serverState = MemoryHelper.ReadArray<S>(rawServerState);

            bool validClientState = StateHistory.Read(tick, out var clientState);
            bool needsReconcile = HasError(clientState, serverState);
            
            if (!validClientState || !needsReconcile) return;

            ulong presentTick = StateHistory.MostRecentTick;

            StateHistory.ClearFuture(tick, true);
            StateHistory.Write(tick, serverState);

            ApplyState(serverState);

            for (ulong t = tick + 1; t <= presentTick; ++t)
            {
                Simulate(delta, true);

                var newState = GatherCurrentState();
                StateHistory.Write(t, newState);
            }
        }
    
        void OnServerTick(ulong serverTick, double delta, Action<NetworkConnection, ulong, byte[]> ReconcileFunction)
        {
            Simulate(delta, false);

            var serverState = GatherCurrentState();

            foreach(var conn in StateHistories)
            {
                var hist = conn.Value;
                bool hasValue = hist.Read(serverTick, out var clientState);

                if (hasValue && HasError(serverState, clientState))
                {
                    MemoryHelper.WriteArray<S>(serverState, MemoryHelper.BUFFER_S);
                    ReconcileFunction(conn.Key, serverTick, MemoryHelper.BUFFER_S);

                    Debug.Log("Reconcile no input controller");
                }
            }

            GetHistory(null).Write(serverTick, serverState);
        }

        void SendState(NetworkConnection conn, ulong tick, byte[] rawState)
        {
            GetHistory(conn).Write(tick, MemoryHelper.ReadArray<S>(rawState));
        }
    }

    public interface IAuthoritative<I, S>
        where I : struct
        where S : struct
    {
        /// <summary>
        /// Contains information to rollback to previous ticks
        /// </summary>
        /// <value>Input history</value>
        History<I> InputHistory { get; set; }

        /// <summary>
        /// Contains information to rollback to previous ticks
        /// </summary>
        /// <value>State history</value>
        History<S> StateHistory { get; set; }

        void Initialize(int maxEntries)
        {
            InputHistory = new History<I>(maxEntries);
            StateHistory = new History<S>(maxEntries);
        }

        /// <summary>
        /// Gets called on the OnTick event on the client only once.
        /// This function will be used to gather the user's input to move them around.
        /// Be careful with checking for KeyDown or KeyUp, it might not catch it, rather check in Update and lazy load here.
        /// </summary>
        /// <returns>This tick's user input</returns>
        I GatherCurrentInput();

        /// <summary>
        /// This function will be used to gather the state of the player after apllying input
        /// </summary>
        /// <returns>The current player state</returns>
        S GatherCurrentState();

        /// <summary>
        /// Check if two states are equal, you should add a margin for floating point error
        /// 0.001 is usually a good start
        /// </summary>
        /// <param name="stateA">Value A</param>
        /// <param name="stateB">Value B</param>
        /// <returns>True if they are different, aka contain an "error"</returns>
        bool HasError(S stateA, S stateB);

        bool HasError(byte[] stateA, byte[] stateB)
        {
            return HasError(MemoryHelper.ReadArray<S>(stateA), MemoryHelper.ReadArray<S>(stateB));
        }

        /// <summary>
        /// Using the given state, teleport and apply the state to the controller
        /// </summary>
        /// <param name="state">State to apply</param>
        void ApplyState(S state);

        void ApplyState(byte[] state)
        {
            ApplyState(MemoryHelper.ReadArray<S>(state));
        }

        /// <summary>
        /// Using input and delta time move the player to the next frame
        /// </summary>
        /// <param name="input">User input</param>
        /// <param name="delta">Delta time</param>
        /// <param name="replay">Use this to avoid playing sounds multiple times if replay is true</param>
        void Simulate(I input, double delta, bool replay);

        void Simulate(byte[] input, double delta, bool replay)
        {
            Simulate(MemoryHelper.ReadArray<I>(input), delta, replay);
        }

        void OnServerTick(ulong tick, double delta, int minBuffer, int maxBuffer, Action<NetworkConnection, ulong, byte[]> ReconcileFunction)
        {
            bool validState = InputHistory.Read(tick, out var input);

            if (!validState)
            {
                Debug.LogError("Packet dropped, skipped input frame.");
            }

            Simulate(input, delta, false);

            var serverState = GatherCurrentState();

            StateHistory.Read(tick, out var clientState);
            StateHistory.Write(tick, serverState);

            if (HasError(serverState, clientState))
            {
                MemoryHelper.WriteArray<S>(serverState, MemoryHelper.BUFFER_S);
                ReconcileFunction(null, tick, MemoryHelper.BUFFER_S);
            }
        }

        void Reconcile(double delta, ulong tick, byte[] rawServerState)
        {
            S serverState = MemoryHelper.ReadArray<S>(rawServerState);

            bool validClientState = StateHistory.Read(tick, out var clientState);
            bool needsReconcile = HasError(clientState, serverState);
            
            if (!validClientState || !needsReconcile) return;

            ulong presentTick = StateHistory.MostRecentTick;

            StateHistory.ClearFuture(tick, true);
            StateHistory.Write(tick, serverState);

            ApplyState(serverState);

            for (ulong t = tick + 1; t <= presentTick; ++t)
            {
                if (!InputHistory.Read(t, out var input))
                    continue;

                Simulate(input, delta, true);

                var newState = GatherCurrentState();
                StateHistory.Write(t, newState);
            }
        }

        void SendInput(ulong tick, byte[] rawInput, byte[] rawState)
        {
            InputHistory.Write(tick, MemoryHelper.ReadArray<I>(rawInput));
            StateHistory.Write(tick, MemoryHelper.ReadArray<S>(rawState));
        }
    }
}