using System;
using System.Collections.Generic;
using UnityEngine;

namespace Freeland.StateMachine
{
    public class StateMachine<T> where T : MonoBehaviour
    {
        public IState CurrentState { get; private set; }
        public IState PreviousState { get; private set; }

        private T owner;
        private Dictionary<Type, IState> states = new Dictionary<Type, IState>();

        public StateMachine(T owner)
        {
            this.owner = owner;
        }
        public void RegisterState<TState>(TState state) where TState : IState
        {
            var type = typeof(TState);
            if (!states.ContainsKey(type))
            {
                states[type] = state;
            }
        }

        /// <summary>
        /// Registers <paramref name="state"/> under the key of <typeparamref name="TKey"/> instead of its own type.
        /// Use when a subclass state should be found via the base state's key.
        /// Example: RegisterStateAs&lt;HumanAttackState, ArcherAttackState&gt;(archerAttack)
        /// → ChangeState&lt;HumanAttackState&gt;() will resolve to ArcherAttackState.
        /// </summary>
        public void RegisterStateAs<TKey, TState>(TState state)
            where TKey  : IState
            where TState : TKey
        {
            var type = typeof(TKey);
            if (!states.ContainsKey(type))
            {
                states[type] = state;
            }
        }
        public void ChangeState<TState>() where TState : IState
        {
            var type = typeof(TState);
            if (!states.TryGetValue(type, out var newState))
            {
                Debug.LogWarning($"State {type.Name} chưa được đăng ký!");
                return;
            }

            if (CurrentState == newState) return;

            PreviousState = CurrentState;
            CurrentState?.Exit();
            CurrentState = newState;
            CurrentState.Enter();
        }
        public void RevertToPreviousState()
        {
            if (PreviousState != null)
            {
                CurrentState?.Exit();
                var temp = CurrentState;
                CurrentState = PreviousState;
                PreviousState = temp;
                CurrentState.Enter();
            }
        }
        public void Update()
        {
            CurrentState?.Update();
        }
        public void FixedUpdate()
        {
            CurrentState?.FixedUpdate();
        }
        public bool IsInState<TState>() where TState : IState
        {
            return CurrentState is TState;
        }
        public TState GetState<TState>() where TState : IState
        {
            var type = typeof(TState);
            if (states.TryGetValue(type, out var state))
            {
                return (TState)state;
            }
            return default;
        }
    }
}
