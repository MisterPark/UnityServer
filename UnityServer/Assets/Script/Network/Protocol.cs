using System.Runtime.InteropServices;

public static class Protocol
{
    public static byte MagicNumber = 0x58;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct NetworkHeader
{
    public byte checksum;
    public byte magicNumber;
    public int messageLength;
}

public class MsgNetStat
{
    public string id;
    public string ipAddress;
    public int ping;
}

public class MsgChat
{
    public string id;
    public string message;
}