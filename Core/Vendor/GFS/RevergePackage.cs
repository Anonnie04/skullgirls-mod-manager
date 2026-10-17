using ThemModdingHerds.IO.Binary;

namespace ThemModdingHerds.GFS;
public class RevergePackage(RevergePackageHeader header,IEnumerable<KeyValuePair<string, RevergePackageEntry>> entries) : Dictionary<string,RevergePackageEntry>(entries)
{
    public RevergePackageHeader Header { get; set; } = header;
    public static RevergePackage Merge(RevergePackage gfs,params RevergePackage[] files) => Merge(gfs.Header,files);
    public static RevergePackage Merge(RevergePackageHeader header,params RevergePackage[] files) => Merge(header.Identifier,header.Version,files);
    public static RevergePackage Merge(string id,string ver,params RevergePackage[] files)
    {
        RevergePackageHeader header = new(id,ver);
        RevergePackage gfs = new(header);
        foreach(RevergePackage pak in files)
            gfs.AddRange(pak);
        gfs.RecalculateEntries();
        return gfs;
    }
    public static RevergePackage Create(string path) => Create(path,RevergePackageHeader.IDENTIFIER,RevergePackageHeader.VERSION);
    public static RevergePackage Create(string path,RevergePackageHeader header) => Create(path,header.Identifier,header.Version);
    public static RevergePackage Create(string path,string id,string ver)
    {
        // check if path exists
        if(!Directory.Exists(path))
            throw new DirectoryNotFoundException($"{path} does not exist");
        // check if the path is a folder
        if(!(File.GetAttributes(path) == FileAttributes.Directory))
            throw new Exception($"{path} is not a directory");
        // get all files in that folder
        DirectoryInfo folder = new(path);
        List<string> files = folder.GetFiles("*.*",SearchOption.AllDirectories).Select((s) => s.FullName).ToList();
        RevergePackageHeader header = new(0,files.Count,id,ver);
        files.Sort();
        Dictionary<string,RevergePackageEntry> entries = [];
        // calculate the dataoffset for the header
        int dataoffset = header.DataSize();
        foreach(string file in files)
        {
            // create an entry from the file
            RevergePackageEntry entry = RevergePackageEntry.Create(folder.FullName,file);
            // calculate the size of the entry
            dataoffset += entry.DataSize(); // add to dataoffset and...
            entries.Add(entry.Path,entry); // add the entry to the list
        }
        header.DataOffset = dataoffset;
        // create the header from the entries and dataoffset we've calculated
        // calculate the data offset for each entry
        long runningOffset = dataoffset;
        foreach(var pair in entries)
        {
            RevergePackageEntry entry = pair.Value;
            runningOffset += (entry.Alignment - (runningOffset % entry.Alignment)) % entry.Alignment;
            entry.Offset = runningOffset;
            runningOffset += entry.Size;
        }
        RevergePackage gfs = new(header,entries);
        // create the package
        return gfs;
    }
    public static RevergePackage Open(string path)
    {
        using Reader reader = new(path);
        RevergePackage gfs = reader.ReadRevergePackage();
        return gfs;
    }
    public RevergePackage(RevergePackageHeader header): this(header,[])
    {

    }
    public RevergePackage(): this(new RevergePackageHeader())
    {

    }
    public void SetAlignment(int align)
    {
        foreach(RevergePackageEntry entry in this.Values)
            entry.Alignment = align;
    }
    public bool Verify(string id,string version) => Header.Verify(id,version) && Count == Header.EntryCount;
    public bool Verify() => Verify(RevergePackageHeader.IDENTIFIER,RevergePackageHeader.VERSION);
    public void Add(RevergePackageEntry entry)
    {
        if(ContainsKey(entry.Path))
        {
            this[entry.Path] = entry;
            return;
        }
        Add(entry.Path,entry);
    }
    public void Add(KeyValuePair<string,RevergePackageEntry> pair) => Add(pair.Value);
    public void AddRange(IEnumerable<RevergePackageEntry> entries)
    {
        foreach(RevergePackageEntry entry in entries)
            Add(entry);
    }
    public void AddRange(IEnumerable<KeyValuePair<string,RevergePackageEntry>> entries)
    {
        foreach(var pair in entries)
            Add(pair);
    }
    public RevergePackage Merge(params RevergePackage[] gfs)
    {
        return Merge(this,gfs);
    }
    public void RecalculateEntries()
    {
        Header.EntryCount = this.Length();
        Header.DataOffset = Header.DataSize();
        long runningOffset = Header.DataOffset;
        for(long i = 0;i < Header.EntryCount;i++)
        {
            var pair = this.ElementAt(i);
            RevergePackageEntry entry = pair?.Value ?? throw new Exception();
            Header.DataOffset += entry.DataSize();
            runningOffset += (entry.Alignment - (runningOffset % entry.Alignment)) % entry.Alignment;
            entry.Offset = runningOffset;
            runningOffset += entry.Size;
        }
    }
    public override string ToString()
    {
        return $"RevergePackage({Header}) = {Header.EntryCount}";
    }
}
public static class RevergePackageExt
{
    public static RevergePackage ReadRevergePackage(this Reader reader)
    {
        RevergePackageHeader header = reader.ReadRevergePackageHeader();
        Dictionary<string,RevergePackageEntry> entries = reader.ReadRevergePackageEntries(header);

        return new(header,entries);
    }
    public static void Write(this Writer writer,RevergePackage file)
    {
        writer.Endianness = IO.Endianness.Big;
        writer.Write(file.Header);
        foreach(var pair in file)
        {
            RevergePackageEntry entry = file[pair.Key];
            int alignment = pair.Value.Alignment;
            entry.Alignment = alignment;
            writer.WritePascal64String(entry.Path);
            writer.Write(entry.Size);
            writer.Write(alignment);
        }
        long runningOffset = file.Header.DataOffset;
        foreach(var pair in file)
        {
            RevergePackageEntry entry = file[pair.Key];
            int alignment = pair.Value.Alignment;
            runningOffset += (alignment - (runningOffset % alignment)) % alignment;
            writer.Offset = runningOffset;
            writer.Write(entry.Data);
            runningOffset += entry.Size;
        }
    }
}