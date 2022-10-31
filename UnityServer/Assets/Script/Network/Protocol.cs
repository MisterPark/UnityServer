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

[System.Serializable]
public class MsgChat
{
    public string message;
}