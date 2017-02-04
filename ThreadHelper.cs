using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Core
{
    public class ThreadHelper : MonoBehaviour
    {
        private int _locked = 0;
        private Thread _mainThread;
        private readonly List<Action> _actions = new List<Action>();

        private static ThreadHelper _instance;
        public static ThreadHelper Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<ThreadHelper>();
                    if (_instance == null)
                    {
                        var obj = new GameObject();
                        obj.name = typeof(ThreadHelper).Name;
                        _instance = obj.AddComponent<ThreadHelper>();
                    }
                }
                return _instance;
            }
        }

        public bool IsMainThread
        {
            get
            {
                return _mainThread == Thread.CurrentThread;
            }
        }

        protected void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }

            _mainThread = Thread.CurrentThread;
        }

        public void RunAsync(Action action)
        {
            if (action != null)
            {
                ThreadPool.QueueUserWorkItem(obj => action());
            }
        }

        public void RunAsync(Action<object> action, object args)
        {
            if (action != null)
            {
                ThreadPool.QueueUserWorkItem(obj => action(obj), args);
            }
        }

        public void QueueOnMainThread(Action action)
        {
            if (action == null)
            {
                return;
            }

            if (IsMainThread)
            {
                action();
                return;
            }

            // wait for other threads
            while (Interlocked.Exchange(ref _locked, 1) != 0)
            {
            }
            _actions.Add(action);
            Interlocked.Exchange(ref _locked, 0);
        }

        private void Update()
        {
            while (Interlocked.Exchange(ref _locked, 1) != 0)
            {
            }
            var count = _actions.Count;
            for (var i = 0; i < count; i++)
            {
                _actions[i]();
            }
            _actions.Clear();
            Interlocked.Exchange(ref _locked, 0);
        }
    }
}