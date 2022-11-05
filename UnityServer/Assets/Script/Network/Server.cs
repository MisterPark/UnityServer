using System;
using System.Collections.Concurrent;
using System.IO;
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
    [SerializeField] private int receiveBufferSize = 1024;
    [SerializeField] private int packetPoolSize = 1000;
    [SerializeField] private GameEvent<string> OnAccept;
    [SerializeField] private GameEvent<object> OnReceive;

    private string publicIP;
    private string localIP;
    private Socket listenSocket;
    private MemoryPool<SocketAsyncEventArgs> readWritePool;
    private int numConnections;

    private ConcurrentDictionary<string, Session> sessions;
    private MemoryPool<Packet> packetPool;

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
        OnReceive.AddListener(OnReceiveCallback);
        publicIP = GetPublicIPAddress();
        localIP = GetLocalIPAddress();
        sessions = new ConcurrentDictionary<string, Session>();
        readWritePool = new MemoryPool<SocketAsyncEventArgs>(0);

        SocketAsyncEventArgs readWriteEventArg;
        for (int i = 0; i < maxConnection; i++)
        {
            readWriteEventArg = new SocketAsyncEventArgs();
            readWriteEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(IO_Completed);

            readWritePool.Free(readWriteEventArg);
        }

        packetPool = new MemoryPool<Packet>(0);
        for (int i = 0; i < packetPoolSize; i++)
        {
            Packet packet = new Packet(receiveBufferSize);
            packetPool.Free(packet);
        }

        IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, port);

        listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listenSocket.LingerState = new LingerOption(true, 0);
        listenSocket.NoDelay = true;
        listenSocket.Bind(endpoint);
        listenSocket.Listen(numConnections);


        Accept();

        Logger.Log(LogLevel.System, $"Server Started... Local: {localIP}:{port} / Public: {publicIP}");
    }

    private void OnApplicationQuit()
    {
        DisconnectAll();
        listenSocket.Shutdown(SocketShutdown.Both);
        listenSocket.Close();
        listenSocket.Dispose();
        listenSocket = null;
    }

    private void AcceptCompleted(object sender, SocketAsyncEventArgs e)
    {
        ProcessAccept(e);
    }

    private void Accept()
    {
        SocketAsyncEventArgs acceptEventArg = new SocketAsyncEventArgs();
        acceptEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(AcceptCompleted);

        bool pending = listenSocket.AcceptAsync(acceptEventArg);
        if (!pending)
        {
            ProcessAccept(acceptEventArg);
        }
    }

    private void ProcessAccept(SocketAsyncEventArgs e)
    {
        if (e.SocketError == SocketError.Success)
        {
            Interlocked.Increment(ref numConnections);
            SocketAsyncEventArgs readEventArgs = readWritePool.Allocate();
            Session session = new Session();
            session.Socket = e.AcceptSocket;
            session.Socket.LingerState = new LingerOption(true, 0);
            session.Socket.NoDelay = true;
            session.Id = Guid.NewGuid().ToString();
            session.IPAddress = e.AcceptSocket.LocalEndPoint.ToString();
            session.RecvBuffer = new NetBuffer(receiveBufferSize);
            
            sessions.TryAdd(session.Id, session);

            OnAccept.Invoke(session.Id);

            readEventArgs.UserToken = session;
            readEventArgs.SetBuffer(session.RecvBuffer.Buffer, session.RecvBuffer.Rear, session.RecvBuffer.WritableLength);

            bool pending = session.Socket.ReceiveAsync(readEventArgs);
            if (!pending)
            {
                ProcessReceive(readEventArgs);
            }

            SendUnicast(session.Id, new MsgNetStat() { id = session.Id, ping = Environment.TickCount});
            Accept();
        }
        else
        {
            Logger.Log(LogLevel.Error, $"Accept Failed. SocketError.{e.SocketError}");
        }

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
        if (e.SocketError == SocketError.Success && e.BytesTransferred > 0)
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
        else if (e.BytesTransferred == 0)
        {
            readWritePool.Free(e);
            Disconnect(session.Id);
        }
        else
        {
            Logger.Log(LogLevel.Error, $"Receicve Failed! SocketError.{e.SocketError}");
            readWritePool.Free(e);
            Disconnect(session.Id);
        }
    }

    private void ProcessPacket(Session session)
    {
        NetHeader header;
        header.Code = 0;
        header.Length = 0;
        int headerSize = Marshal.SizeOf(header);
        int size;

        while (true)
        {
            size = session.RecvBuffer.Length;
            if (size < headerSize)
            {
                //Logger.Log(LogLevel.Debug, $"헤더 크기보다 작음. 헤더 크기: {headerSize} / 현재 크기: {size}");
                break;
            }
            session.RecvBuffer.Peek<NetHeader>(ref header);
            if (header.Code != Packet.CODE)
            {
                Logger.Log(LogLevel.Warning, $"패킷의 코드가 일치하지 않습니다. Code : {header.Code}");
                break;
            }

            int packetSize = headerSize + header.Length;
            if (size < packetSize)
            {
                Logger.Log(LogLevel.Debug, $"패킷 크기보다 작음. 패킷 크기: {packetSize} / 현재 크기: {size}");
                break;
            }

            Packet packet = packetPool.Allocate();
            packet.Initialize();
            if(packet.WritableLength < header.Length)
            {
                packet.Resize(header.Length);
            }

            session.RecvBuffer.MoveFront(headerSize);
            session.RecvBuffer.Read(ref packet.Buffer, packet.Rear, header.Length);
            packet.MoveRear(header.Length);

            string typeName = string.Empty;
            string json = string.Empty;
            packet.Read(ref typeName);
            packet.Read(ref json);

            packetPool.Free(packet);

            object msg = JsonConvert.DeserializeObject(json, Type.GetType(typeName));

            OnReceive.Invoke(msg);
        }
    }

    private void ProcessSend(SocketAsyncEventArgs e)
    {
        Session session = (Session)e.UserToken;
        if (e.SocketError != SocketError.Success && e.SocketError != SocketError.IOPending)
        {
            Logger.Log(LogLevel.Error, $"Send Failed. SocketError : {e.SocketError}");
            readWritePool.Free(e);
            Disconnect(session.Id);
        }
    }

    public void SendUnicast(string sessionId, object data)
    {
        var result = sessions.TryGetValue(sessionId, out var session);
        if (result == false)
        {
            Logger.Log(LogLevel.Warning, $"Invalid session ID. [{sessionId}]");
            return;
        }

        Packet packet = packetPool.Allocate();
        packet.Initialize();

        string typeName = data.GetType().Name;
        string json = JsonConvert.SerializeObject(data);
        packet.Write(typeName);
        packet.Write(json);

        packet.SetHeader();
        SocketAsyncEventArgs args = readWritePool.Allocate();
        args.UserToken = session;
        args.SetBuffer(packet.Buffer, packet.Front, packet.Length);
        bool pending = session.Socket.SendAsync(args);
        if (!pending)
        {
            ProcessSend(args);
        }
    }

    public void SendBroadcast(object data)
    {
        foreach(var session in sessions.Values)
        {
            SendUnicast(session.Id, data);
        }
    }

    public void Disconnect(string sessionId)
    {
        sessions.TryRemove(sessionId, out var session);
        if(session != null)
        {
            if(session.Socket != null)
            {
                session.Socket.Shutdown(SocketShutdown.Both);
                session.Socket.Disconnect(false);
                session.Socket.Close(5);
                session.Socket.Dispose();
                Interlocked.Decrement(ref numConnections);
                Logger.Log($"Session Disconnected. IP Address: [{session.IPAddress}] / Session ID: [{session.Id}] / Number of Clients: {numConnections}");
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

    private string GetPublicIPAddress()
    {
        var request = (HttpWebRequest)WebRequest.Create("http://ifconfig.me");

        request.UserAgent = "curl"; // this will tell the server to return the information as if the request was made by the linux "curl" command

        string publicIPAddress;

        request.Method = "GET";
        using (WebResponse response = request.GetResponse())
        {
            using (var reader = new StreamReader(response.GetResponseStream()))
            {
                publicIPAddress = reader.ReadToEnd();
            }
        }

        return publicIPAddress.Replace("\n", "");
    }

    private string GetLocalIPAddress()
    {
        string localIP;
        using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
        {
            socket.Connect("8.8.8.8", 65530);
            IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
            localIP = endPoint.Address.ToString();
        }
        return localIP;
    }

    private void OnReceiveCallback(object msg)
    {
        if (msg.GetType() == typeof(MsgNetStat))
        {
            int tick = Environment.TickCount;
            MsgNetStat obj = (MsgNetStat)msg;

            if (sessions.TryGetValue(obj.id, out Session session) == false)
            {
                Logger.Log("Invalid session ID.");
                return;
            }

            session.IPAddress = obj.ipAddress;
            int ping = tick - obj.ping;
            ping = ping < 0 ? int.MaxValue - obj.ping + tick : ping;
            Logger.Log(LogLevel.System, $"Connected. IP Address: [{session.IPAddress}] / Session ID: [{obj.id}] / Number of Clients: {numConnections} / Ping: {ping}");
        }
    }
}
