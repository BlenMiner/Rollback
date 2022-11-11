using System;
using Riten.Rollback;
using Riten.Utils;
using UnityEngine;

namespace Riten.Authorative
{
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

        ulong OnServerTick(ulong serverTick, double delta, int minBuffer, int maxBuffer, Action<ulong, byte[]> ReconcileFunction)
        {
            if (InputHistory.Count >= minBuffer)
            {
                if (InputHistory.Find(serverTick, out var index) && InputHistory.Count - index > maxBuffer)
                {
                    int targetIndex = InputHistory.Count - minBuffer;
                    ulong newTick = InputHistory.GetEntryTick(targetIndex);

                    Debug.LogError($"Too many inputs behind, we need to catch up. Skipped {newTick - serverTick} ticks.");

                    serverTick = newTick;
                }

                bool validState = InputHistory.Read(serverTick, out var input);

                if (!validState)
                {
                    if (InputHistory.MostRecentTick < serverTick)
                    {
                        Debug.LogError("Waiting for missing tick.");
                        return serverTick;
                    }
                    else
                    {
                        Debug.LogError("Packet dropped, skipped input frame.");
                    }
                }

                Simulate(input, delta, false);

                var serverState = GatherCurrentState();

                StateHistory.Read(serverTick, out var clientState);
                StateHistory.Write(serverTick, serverState);

                if (HasError(serverState, clientState))
                {
                    MemoryHelper.WriteArray<S>(serverState, MemoryHelper.BUFFER_S);
                    ReconcileFunction(serverTick, MemoryHelper.BUFFER_S);
                }

                return serverTick + 1;
            }
            
            return serverTick;
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
                {
                    Debug.LogError($"Client skipping missing input at tick {t}");
                    continue;
                }

                Simulate(input, delta, true);

                var newState = GatherCurrentState();
                StateHistory.Write(t, newState);
            }
        }

        void SendInput(ulong tick, byte[] rawInput, byte[] rawState, Action<ulong> UpdateServerTick)
        {
            bool firstInput = InputHistory.Count == 0;

            // Safety checks
            if (firstInput || tick > InputHistory.MostRecentTick)
            {
                if (firstInput)
                {
                    // Initialize server tick number
                    UpdateServerTick(tick);
                }

                InputHistory.Write(tick, MemoryHelper.ReadArray<I>(rawInput));
                StateHistory.Write(tick, MemoryHelper.ReadArray<S>(rawState));
            }
            else
            {
                Debug.LogError($"Illegal input received for tick {tick}");
            }
        }
    }
}