using ThemModdingHerds.IO.Binary;
using ThemModdingHerds.IO;
namespace ThemModdingHerds.GFS;
public class RevergePackageHeader(int offset, long entryCount, string identifier, string version)
{
    public const string IDENTIFIER = "Reverge Package File";
    public const string VERSION = "1.1";
    public static int SIZE(string id,string ver) => 4 + 8 + id.Length + 8 + ver.Length + 8;
    public int DataOffset { get; set; } = offset;
    public string Identifier { get; set; } = identifier;
    public string Version { get; set; } = version;
    public long EntryCount { get; set; } = entryCount;
    public RevergePackageHeader(int offset,long entryCount): this(offset,entryCount,IDENTIFIER,VERSION)
    {

    }
    public RevergePackageHeader(string id,string ver): this(SIZE(id,ver),0,id,ver)
    {

    }
    public RevergePackageHeader(): this(IDENTIFIER,VERSION)
    {

    }
    public bool Verify(string id,string ver) => Identifier == id && Version == ver;
    public bool Verify() => Verify(IDENTIFIER,VERSION);
    public int DataSize() => SIZE(Identifier,Version);
    public override string ToString()
    {
        return $"{Identifier};{Version}@0x{DataOffset:X4}";
    }
}
public static class RevergePackageHeaderExt
{
    public static RevergePackageHeader ReadRevergePackageHeader(this Reader reader)
    {
        reader.Endianness = Endianness.Big;
        int offset = reader.ReadInt();
        string id = reader.ReadPascal64String();
        string version = reader.ReadPascal64String();
        long entryCount = reader.ReadLong();
        return new RevergePackageHeader(offset,entryCount,id,version);
    }
    public static void Write(this Writer writer,RevergePackageHeader header)
    {
        writer.Endianness = Endianness.Big;
        writer.Write(header.DataOffset);
        writer.WritePascal64String(header.Identifier);
        writer.WritePascal64String(header.Version);
        writer.Write(header.EntryCount);
    }
}