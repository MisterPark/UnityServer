using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;


public class GameEvent<T> : GameEventBase
{
    private List<UnityAction<T>> listeners = new List<UnityAction<T>>();
    public void AddListener(UnityAction<T> call)
    {
        listeners.Add(call);
    }

    public void RemoveListener(UnityAction<T> call)
    {
        listeners.Remove(call);
    }

    public void Invoke(T arg)
    {
        int count = listeners.Count;
        for(int i = 0; i < count; i++)
        {
            listeners[i].Invoke(arg);
        }
    }
}

public class GameEventBase : ScriptableObject
{
    private List<UnityAction> listeners = new List<UnityAction>();
    public void AddListener(UnityAction call)
    {
        listeners.Add(call);
    }

    public void RemoveListener(UnityAction call)
    {
        listeners.Remove(call);
    }

    public void Invoke()
    {
        int count = listeners.Count;
        for (int i = 0; i < count; i++)
        {
            listeners[i].Invoke();
        }
    }
}