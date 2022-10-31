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

    [SerializeField] private bool isLocal;
    [SerializeField] private int port;
    [SerializeField] private ushort maxConnection = ushort.MaxValue;
    [SerializeField] private UnityEvent<object> OnReceive;

    private string publicIP;
    private string localIP;
    private Socket listenSocket;
    private MemoryPool<SocketAsyncEventArgs> readWritePool;
    private int numConnections;

    private ConcurrentDictionary<string, Session> sessions;


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

        IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, port);

        listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listenSocket.Bind(endpoint);
        listenSocket.Listen(numConnections);


        Accept();

        Logger.Log(LogLevel.System, $"Server Started... Local: {localIP}:{port} / Public: {publicIP}");
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
            session.Id = Guid.NewGuid().ToString();
            session.IPAddress = e.AcceptSocket.LocalEndPoint.ToString();
            
            sessions.TryAdd(session.Id, session);
            readEventArgs.UserToken = session;
            readEventArgs.SetBuffer(session.RecvBuffer.Buffer, session.RecvBuffer.Rear, session.RecvBuffer.WritableLength);

            bool pending = e.AcceptSocket.ReceiveAsync(readEventArgs);
            if (!pending)
            {
                ProcessReceive(readEventArgs);
            }

            SendUnicast(session.Id, new MsgNetStat_SC() {id = session.Id });

            Accept();
        }
        else
        {
            Logger.Log(LogLevel.Error, $"Socket Error at ProcessAccept(). Code: {e.SocketError}");
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

            int classLength = 0;
            session.RecvBuffer.Read(ref classLength);

            string className = string.Empty;
            session.RecvBuffer.Read(ref className, classLength);

            string json = string.Empty;
            session.RecvBuffer.Read(ref json, header.messageLength);

            Type msgType = Type.GetType(className);
            if (msgType == null)
            {
                // 존재하지 않는 구조체 이슈 (프로토콜 버전 차이 가능성)
                Logger.Log(LogLevel.Error, $"Invalid message.");
                Disconnect(session.Id);
                break;
            }

            object msg = JsonConvert.DeserializeObject(json, msgType);
            if (msg == null)
            {
                // 존재하지 않는 구조체 이슈 (프로토콜 버전 차이 가능성)
                Logger.Log(LogLevel.Error, $"Invalid message.");
                Disconnect(session.Id);
                break;
            }

            OnReceiveCallback(msg);
            OnReceive.Invoke(msg);
        }
    }

    private void ProcessSend(SocketAsyncEventArgs e)
    {
        Session session = (Session)e.UserToken;
        if (e.SocketError != SocketError.Success)
        {
            Logger.Log(LogLevel.Warning, $"Send Failed. SocketError : {e.SocketError}");
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

        Packet packet = new Packet();

        NetworkHeader header = new NetworkHeader();
        header.magicNumber = Protocol.MagicNumber;

        string className = data.GetType().Name;
        string json = JsonConvert.SerializeObject(data);

        // 여기서 암호화
        byte[] binary = Encoding.UTF8.GetBytes(json);

        header.messageLength = binary.Length;
        packet.Write(header);
        packet.Write(className);
        packet.Write(binary);

        SocketAsyncEventArgs args = readWritePool.Allocate();
        args.UserToken = session;
        args.SetBuffer(packet.Buffer, packet.Front, packet.Length);
        bool pending = session.Socket.SendAsync(args);
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
                Interlocked.Decrement(ref numConnections);
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
        if (msg.GetType() == typeof(MsgNetStat_SC))
        {
            MsgNetStat_SC obj = (MsgNetStat_SC)msg;

            if (sessions.TryGetValue(obj.id, out Session session) == false)
            {
                Logger.Log("Invalid session ID.");
                return;
            }

            session.IPAddress = obj.ipAddress;
            Logger.Log(LogLevel.System, $"Connected. IP Address: [{session.IPAddress}] / Session ID: [{obj.id}] / Number of Clients: {numConnections}");
        }
    }
}
