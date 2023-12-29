using System.Collections.Generic;
using UnityEngine;

namespace vFrame.Bundler.Base.Coroutine
{
    internal class CoroutinePoolBehaviour : MonoBehaviour
    {
        [SerializeField]
        private int _capacity;
        [SerializeField]
        private List<CoroutineTask> _tasksWaiting;
        [SerializeField]
        private List<CoroutineRunnerBehaviour> _coroutineList;

        private CoroutinePool _pool;

        public CoroutinePool Pool {
            set {
                _capacity = value.Capacity;
                _coroutineList = value.RunnerList;
                _tasksWaiting = value.TasksWaiting;
                _pool = value;
            }
            get { return _pool; }
        }

        private void Update() {
            if (null != Pool) {
                Pool.OnUpdate();
            }
        }
    }
}