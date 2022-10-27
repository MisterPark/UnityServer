using System;
using System.Net;
using System.Net.Sockets;

public class Session
{
    public Socket Socket { get; set; }
    public string Id { get; set; }
    public string IPAddress { get; set; }
    public RingBuffer RecvBuffer { get; set; } = new RingBuffer();
    public RingBuffer SendBuffer { get; set; } = new RingBuffer();
}
