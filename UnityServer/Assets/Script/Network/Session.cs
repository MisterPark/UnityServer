using System.Net.Sockets;

public class Session
{
    public Socket Socket { get; set; }
    public string Id { get; set; }
    public string IPAddress { get; set; }
    public NetBuffer RecvBuffer { get; set; }

}
