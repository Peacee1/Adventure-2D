using UnityEngine;

namespace Freeland.StateMachine
{
    public abstract class BaseState<T> : IState where T : MonoBehaviour
    {
        protected T owner;
        protected StateMachine<T> stateMachine;

        public BaseState(T owner, StateMachine<T> stateMachine)
        {
            this.owner = owner;
            this.stateMachine = stateMachine;
        }

        public virtual void Enter() { }
        public virtual void Update() { }
        public virtual void FixedUpdate() { }
        public virtual void Exit() { }
    }
}
