namespace StateAlchemist.Samples.Telnet;

/// <summary>The telnet bytes the sample machine understands (RFC 854 and RFC 1073).</summary>
public static class TelnetBytes
{
    public const byte LineFeed = 10;
    public const byte NawsOption = 31;
    public const byte GmcpOption = 201;
    public const byte Se = 240;
    public const byte Sb = 250;
    public const byte Will = 251;
    public const byte Wont = 252;
    public const byte Do = 253;
    public const byte Dont = 254;
    public const byte Iac = 255;
}
