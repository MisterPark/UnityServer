using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Events;


public class Server : MonoBehaviour
{
    public static Server Instance { get; private set; }

    [SerializeField] private int port;
    [SerializeField] private ushort maxConnection = ushort.MaxValue;

    private Socket listenSocket;
    private MemoryPool<SocketAsyncEventArgs> readWritePool;
    private int numConnections;

    private ConcurrentDictionary<string, Session> sessions;

    public UnityEvent<object> OnReceive { get; private set; } = new UnityEvent<object> { };

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(this);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    private void Start()
    {
        sessions = new ConcurrentDictionary<string, Session>();
        readWritePool = new MemoryPool<SocketAsyncEventArgs>(0);

        SocketAsyncEventArgs readWriteEventArg;
        for (int i = 0; i < maxConnection; i++)
        {
            readWriteEventArg = new SocketAsyncEventArgs();
            readWriteEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(IO_Completed);

            readWritePool.Free(readWriteEventArg);
        }

        IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, port);
        listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listenSocket.Bind(endpoint);
        listenSocket.Listen(numConnections);

        SocketAsyncEventArgs acceptEventArg = new SocketAsyncEventArgs();
        acceptEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(AcceptCompleted);

        StartAccept(acceptEventArg);

        Logger.Log(LogLevel.System, "Server Started...");
    }

    private void AcceptCompleted(object sender, SocketAsyncEventArgs e)
    {
        ProcessAccept(e);
    }

    private void StartAccept(SocketAsyncEventArgs e)
    {
        bool pending = listenSocket.AcceptAsync(e);
        if (!pending)
        {
            ProcessAccept(e);
        }
    }

    private void ProcessAccept(SocketAsyncEventArgs e)
    {
        Interlocked.Increment(ref numConnections);

        SocketAsyncEventArgs readEventArgs = readWritePool.Allocate();

        Session session = new Session();
        session.Socket = e.AcceptSocket;
        session.Id = Guid.NewGuid().ToString();
        session.IPAddress = ((IPEndPoint)e.RemoteEndPoint).Address.ToString();

        sessions.TryAdd(session.Id, session);

        readEventArgs.UserToken = session;

        readEventArgs.SetBuffer(session.RecvBuffer.Buffer, session.RecvBuffer.Rear, session.RecvBuffer.WritableLength);

        Logger.Log(LogLevel.System, $"[{session.IPAddress}] is connected. Number of currently connected clients : {numConnections}");


        bool pending = e.AcceptSocket.ReceiveAsync(readEventArgs);
        if (!pending)
        {
            ProcessReceive(readEventArgs);
        }

        StartAccept(e);
    }

    private void IO_Completed(object sender, SocketAsyncEventArgs e)
    {
        Session session = (Session)e.UserToken;
        switch (e.LastOperation)
        {
            case SocketAsyncOperation.Receive:
                ProcessReceive(e);
                break;
            case SocketAsyncOperation.Send:
                ProcessSend(e);
                break;
            default:
                Disconnect(session.Id);
                Logger.Log(LogLevel.Error, $"Invalid Packet. {session.IPAddress}");
                break;
        }
    }

    private void ProcessReceive(SocketAsyncEventArgs e)
    {
        Session session = (Session)e.UserToken;
        if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
        {
            session.RecvBuffer.MoveRear(e.BytesTransferred);

            ProcessPacket(session);

            if (session.RecvBuffer.WritableLength == 0)
            {
                session.RecvBuffer.Resize(session.RecvBuffer.BufferSize * 2);
            }

            e.SetBuffer(session.RecvBuffer.Buffer, session.RecvBuffer.Rear, session.RecvBuffer.WritableLength);

            bool pending = session.Socket.ReceiveAsync(e);
            if (!pending)
            {
                ProcessReceive(e);
            }
        }
        else
        {
            Disconnect(session.Id);
        }
    }

    private void ProcessPacket(Session session)
    {
        NetworkHeader header = new NetworkHeader();
        int headerLength = Marshal.SizeOf(header);
        int packetLength;

        while (true)
        {
            if (session.RecvBuffer.Length < headerLength)
            {
                break;
            }

            session.RecvBuffer.Peek<NetworkHeader>(ref header);
            if (header.magicNumber != Protocol.MagicNumber)
            {
                Logger.Log(LogLevel.Error, $"Magic Code does not match. Id: {session.Id}");
                Disconnect(session.Id);
                break;
            }

            packetLength = headerLength + header.messageLength;

            if (session.RecvBuffer.Length < packetLength)
            {
                break;
            }
            // 여기서 패킷처리
            session.RecvBuffer.MoveFront(headerLength);

            string json = string.Empty;
            session.RecvBuffer.Read(ref json, header.messageLength);

            // 여기서 복호화

            object msg = JsonConvert.DeserializeObject(json);
            
            OnReceive.Invoke(msg);
        }
    }

    private void ProcessSend(SocketAsyncEventArgs e)
    {
        Session session = (Session)e.UserToken;
        if(e.SocketError != SocketError.Success)
        {
            Logger.Log(LogLevel.Warning, $"Send Failed. SocketError : {e.SocketError.ToString()}");
            Disconnect(session.Id);
        }
    }

    public void SendUnicast(string sessionId, object data)
    {
        var result = sessions.TryGetValue(sessionId, out var session);
        if(result == false)
        {
            Logger.Log(LogLevel.Warning, $"Invalid session ID. [{sessionId}]");
            return;
        }

        Packet packet = new Packet();
        
        NetworkHeader header = new NetworkHeader();
        header.magicNumber = Protocol.MagicNumber;
        
        string json = JsonConvert.SerializeObject(data);

        // 여기서 암호화
        byte[] binary = Encoding.UTF8.GetBytes(json);

        header.messageLength = binary.Length;
        packet.Write(header);
        packet.Write(binary);


        SocketAsyncEventArgs args = readWritePool.Allocate();
        args.UserToken = session;
        args.SetBuffer(packet.Buffer, packet.Front, packet.Length);
        bool pending = listenSocket.SendAsync(args);
        if (!pending)
        {
            ProcessSend(args);
        }
    }

    public void Disconnect(string sessionId)
    {
        sessions.TryRemove(sessionId, out Session session);

        if (session.Socket.Connected)
        {
            try
            {
                session.Socket.Shutdown(SocketShutdown.Both);
            }
            finally
            {
                Logger.Log($"Session Disconnected. / ID: {session.Id}/ IP Address: {session.IPAddress}");
                session.Socket.Close();
                session.Socket.Dispose();
                session.Socket = null;
            }
        }
    }

    public void DisconnectAll()
    {
        foreach (Session session in sessions.Values)
        {
            Disconnect(session.Id);
        }
    }
}
