using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [SerializeField] private GameEvent<object> onReceiveEvent;
    [SerializeField] private GameEvent<string> onChatEvent;

    private void Awake()
    {
        if(Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnEnable()
    {
        onReceiveEvent.AddListener(OnReceive);
        onChatEvent.AddListener(OnChat);
    }

    private void OnDisable()
    {
        onReceiveEvent.RemoveListener(OnReceive);
        onChatEvent.RemoveListener(OnChat);
    }

    private void Start()
    {
        
    }

    private void Update()
    {
        
    }

    public void OnReceive(object message)
    {

    }

    public void OnChat(string message)
    {
        Debug.Log(message);
    }
}
