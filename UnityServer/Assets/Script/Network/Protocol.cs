

public static class Protocol
{
    public static byte MagicNumber = 0x58;
}

[System.Serializable]
public class NetworkHeader
{
    public byte checksum;
    public byte magicNumber;
    public byte messageLength;
}