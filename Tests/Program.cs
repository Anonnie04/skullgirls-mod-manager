using System.IO.Compression;
using System.Text.Json;
using SkullgirlsModManager;
using ThemModdingHerds.GFS;
using ThemModdingHerds.IO.Binary;
using ThemModdingHerds.VelvetBeautifier.Modding;
using ThemModdingHerds.VelvetBeautifier.Utilities;

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../TestResults", DateTime.Now.ToString("yyyyMMdd-HHmmss")));
Directory.CreateDirectory(root);
int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); Console.WriteLine("PASS " + name); passed++; }
void Throws(Action action, string name)
{
    bool threw = false;
    try { action(); } catch { threw = true; }
    Check(threw, name);
}
string Payload(string name, string text)
{
    string path = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); return path;
}
void Seed(string folder, string target)
{
    var archive = RevergePackage.Create(folder);
    using var writer = new Writer(target);
    writer.Write(archive);
}
string Contents(string archive, string path)
{
    var index = Archives.Read(archive); var entry = index.Entries.Single(e => e.Path == path);
    using var stream = File.OpenRead(archive); stream.Position = entry.Offset;
    var bytes = new byte[(int)entry.Size]; stream.ReadExactly(bytes); return System.Text.Encoding.UTF8.GetString(bytes);
}
string game = Path.Combine(root, "FixtureGame");
Directory.CreateDirectory(Path.Combine(game, "data01"));
File.WriteAllText(Path.Combine(game, "SkullGirls.exe"), "fixture, not executable");
Payload("seed/core/core/music.ini", "original music"); Payload("seed/core/core/empty.txt", "");
Payload("seed/ui/ui/menu.txt", "original menu");
string core = Path.Combine(game, "data01/core.gfs"), ui = Path.Combine(game, "data01/ui.gfs");
Seed(Path.Combine(root, "seed/core"), core); Seed(Path.Combine(root, "seed/ui"), ui);
string originalCore = Crypto.Checksum(core), originalUi = Crypto.Checksum(ui);
Check(Archives.Read(core).Entries.Count == 2, "upstream-created archive index reads correctly");
var replacement = Payload("replacement.ini", "replacement longer than original");
var added = Payload("added.ini", "new asset");
string built = Path.Combine(root, "roundtrip.gfs");
var changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["core/music.ini"] = replacement, ["core/new.ini"] = added };
Archives.Build(core, built, changes); Archives.ValidateBuild(core, built, changes);
Check(Contents(built, "core/music.ini") == "replacement longer than original" && Contents(built, "core/new.ini") == "new asset", "replace and add assets with different payload lengths");
Check(RevergePackage.Open(built).Verify(), "upstream reader accepts rebuilt header and entry count");
Check(Contents(built, "core/empty.txt") == "", "zero-byte unchanged entry survives repacking");
var extract = Path.Combine(root, "Extracted"); Archives.Extract(built, extract);
Check(File.ReadAllText(Path.Combine(extract, "core/new.ini")) == "new asset", "extract preserves asset paths and contents");
Throws(() => Archives.Extract(built, extract), "extraction refuses to overwrite existing files");
Throws(() => SafePaths.Under(root, "../escaped"), "parent traversal rejected");
Throws(() => SafePaths.Under(root, "C:/escaped"), "absolute paths rejected");
Throws(() => SafePaths.Under(root, "core/test:stream"), "alternate data streams rejected");
var library = new Library(Path.Combine(root, "Data/mods"));
string sourceA = Path.Combine(root, "ModA"), sourceB = Path.Combine(root, "ModB");
Payload("ModA/core.gfs/core/music.ini", "mod A"); Payload("ModA/ui.gfs/ui/menu.txt", "mod A menu");
new ModInfo { Id = "tests.a", Name = "Mod A", Version = new Version(1, 0) }.Write(Path.Combine(sourceA, "mod.json"));
File.WriteAllText(Path.Combine(sourceA, "enabled"), "");
Payload("ModB/core.gfs/core/music.ini", "mod B wins");
new ModInfo { Id = "tests.b", Name = "Mod B", Version = new Version(1, 0) }.Write(Path.Combine(sourceB, "mod.json"));
var modA = library.Import(sourceA);
Check(!modA.Enabled, "folder import starts disabled even if source was enabled");
string zip = Path.Combine(root, "ModB.zip"); ZipFile.CreateFromDirectory(sourceB, zip);
var modB = library.Import(zip);
Check(modB.Info.Id == "tests.b", "ZIP import retains Velvet mod.json metadata");
Throws(() => library.Import(sourceA), "duplicate mod IDs rejected without deleting installed version");
string maliciousZip = Path.Combine(root, "traversal.zip");
using (var archive = ZipFile.Open(maliciousZip, ZipArchiveMode.Create))
using (var writer = new StreamWriter(archive.CreateEntry("../../escaped.txt").Open())) writer.Write("bad");
Throws(() => library.Import(maliciousZip), "ZIP traversal rejected");
Check(!File.Exists(Path.Combine(root, "escaped.txt")), "malicious ZIP did not write outside staging");
modA.SetEnabled(true); modB.SetEnabled(true); library.SetOrder([modA, modB]);
Check(library.Mods().Select(m => m.Info.Id).SequenceEqual(["tests.a", "tests.b"]), "load order persists across library reload");
var manager = new Manager(game, Path.Combine(root, "Data")) { Log = Console.WriteLine };
var plan = manager.Preview([modA, modB]);
Check(plan.Changes.Count(c => c.Conflict) == 1, "preview reports overlapping replacements");
manager.Apply([modA, modB]);
Check(Contents(core, "core/music.ini") == "mod B wins" && Contents(ui, "ui/menu.txt") == "mod A menu", "later mod wins and multiple archives apply");
Check(Crypto.Checksum(Path.Combine(manager.BackupFolder, "core.gfs")) == originalCore, "verified original backup retained");
manager.Apply([modA, modB], true);
Check(Crypto.Checksum(core) == originalCore && Crypto.Checksum(ui) == originalUi, "restore is byte-for-byte identical to originals");
Check(modA.Enabled && modB.Enabled, "restore preserves enabled selections");
// Force second replacement to fail while the first archive has already committed.
using (var held = new FileStream(ui, FileMode.Open, FileAccess.Read, FileShare.Read))
    Throws(() => manager.Apply([modA, modB]), "locked second archive causes transaction failure");
Check(Crypto.Checksum(core) == originalCore && Crypto.Checksum(ui) == originalUi && !manager.NeedsRecovery, "failed multi-archive transaction rolls back all earlier replacements");
manager.Apply([modA, modB]);
modA.SetEnabled(false); modB.SetEnabled(false);
Check(manager.Preview([modA, modB]).Restore.Count == 2, "disabling all mods plans restoration of previously changed archives");
manager.Apply([modA, modB]);
Check(Crypto.Checksum(core) == originalCore && Crypto.Checksum(ui) == originalUi, "apply disabled selections restores originals");
// Simulate a crash between File.Replace and final state commit.
var statePath = Path.Combine(manager.Profile, "state.json");
var state = JsonSerializer.Deserialize<InstallState>(File.ReadAllText(statePath))!;
string rollback = core + ".sgmm-test.rollback", staged = core + ".sgmm-test.tmp";
File.Copy(core, rollback); File.Copy(built, core, true);
Storage.Save(Path.Combine(manager.Profile, "transaction.json"), new Transaction(state, [new(core, staged, rollback, originalCore, Crypto.Checksum(core))]));
Throws(() => manager.Preview([]), "pending transaction blocks new plans");
manager.Recover();
Check(Crypto.Checksum(core) == originalCore && !manager.NeedsRecovery, "persisted interrupted transaction recovers original data and state");
modA.SetEnabled(true);
using (var stream = new FileStream(ui, FileMode.Append)) stream.WriteByte(42);
string externalHash = Crypto.Checksum(ui);
Throws(() => manager.Apply([modA]), "externally modified archive blocks apply before any commit");
Check(Crypto.Checksum(core) == originalCore && Crypto.Checksum(ui) == externalHash, "external archive changes are preserved");
File.Copy(Path.Combine(manager.BackupFolder, "ui.gfs"), ui, true);
File.AppendAllText(Path.Combine(manager.BackupFolder, "core.gfs"), "tamper");
Throws(() => manager.Apply([modA]), "tampered backup blocks writes");
Check(Crypto.Checksum(core) == originalCore, "tampered backup never reaches game archive");
// Read actual installed game files only; generated archive stays in TestResults.
string actual = Path.Combine(Environment.GetEnvironmentVariable("SKULLGIRLS_GAME_ROOT") ?? "", "data01");
if (Directory.Exists(actual))
{
    var indexes = Directory.GetFiles(actual, "*.gfs").Select(Archives.Read).ToList();
    Check(indexes.Count >= 17, $"actual installation: {indexes.Count} archive indexes, {indexes.Sum(i => i.Entries.Count):N0} assets parsed");
    string actualCore = Path.Combine(actual, "core.gfs"), actualBuilt = Path.Combine(root, "actual-core-roundtrip.gfs");
    Archives.Build(actualCore, actualBuilt, new Dictionary<string, string>());
    Archives.ValidateBuild(actualCore, actualBuilt, new Dictionary<string, string>());
    Check(Crypto.Checksum(actualCore) == Crypto.Checksum(actualBuilt), "actual Skullgirls core.gfs no-change round trip is byte-for-byte identical");
    var actualChanges = new Dictionary<string, string> { [indexes.Single(i => Path.GetFileName(i.FilePath) == "core.gfs").Entries[0].Path] = replacement, ["sgmm-test/new.txt"] = added };
    string modified = Path.Combine(root, "actual-core-modified.gfs");
    Archives.Build(actualCore, modified, actualChanges); Archives.ValidateBuild(actualCore, modified, actualChanges);
    Check(RevergePackage.Open(modified).Count == Archives.Read(actualCore).Entries.Count + 1, "actual archive copy accepts a replacement and new entry; all other payloads verified");
}
// Read-only preview decoder tests. Sprite Workshop and sprite packages are not involved.
Check(AssetPreviews.Kind("ui/menu.dds") == "Image" && AssetPreviews.Kind("music/theme.wav") == "Audio" && AssetPreviews.Kind("core/music.ini") == "Text", "asset categories identify images, audio and scripts");
Check(AssetPreviews.WorkshopAsset("characters-art.gfs", "characters/annie.art") && AssetPreviews.WorkshopAsset("characters-art-pt.gfs", "characters/filia.art-pt")
    && AssetPreviews.WorkshopAsset("characters-foits.gfs", "characters/filia.foit"), "Workshop redirect covers its supported character art banks and metadata");
Check(!AssetPreviews.WorkshopAsset("sprites.gfs", "sprites/character_select_annie.dds") && !AssetPreviews.WorkshopAsset("sprites.gfs", "sprites/annie_1p.dds"), "standalone DDS images and palettes are not redirected to the Workshop");
Check(!AssetPreviews.WorkshopAsset("story_art.gfs", "story_art/THs/filia/neutral.dds") && !AssetPreviews.WorkshopAsset("story_art.gfs", "story_art/Cinematics/Annie/01.dds"), "story portraits and cinematics stay in the image preview");
Check(AssetPreviews.WorkshopAsset("characters-art.gfs", "temp/boss_marie.art") && !AssetPreviews.WorkshopAsset("story_art.gfs", "characters/annie.art")
    && !AssetPreviews.WorkshopAsset("characters-art.gfs", "characters/unsupported_character.art"), "Workshop scope matches its legacy alias, archive and supported roster rules");
Check(AssetPreviews.Text([255, 254, 65, 0]) == "A", "UTF-16 text preview recognizes its byte order marker");
Check(AssetPreviews.Hex([0, 65, 255]).Contains("00 41 FF") && AssetPreviews.Hex([0, 65, 255]).Contains(".A."), "binary preview includes hex and printable text");
byte[] DdsFixture(string format)
{
    var bytes = new byte[format == "DXT1" ? 136 : 144];
    void I(int p, int value) => System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(p, 4), value);
    "DDS "u8.CopyTo(bytes); I(4, 124); I(12, 4); I(16, 4); I(76, 32); I(80, 4);
    System.Text.Encoding.ASCII.GetBytes(format).CopyTo(bytes, 84);
    int color = format == "DXT1" ? 128 : 136;
    bytes[color] = 0; bytes[color + 1] = 248; // RGB565 red, all color indices zero.
    if (format == "DXT5") bytes[128] = 128;
    if (format == "DXT3") for (int i = 128; i < 136; i++) bytes[i] = 255;
    return bytes;
}
Check(AssetPreviews.Dds(DdsFixture("DXT1")).Pixels.All(p => p == unchecked((int)0xFFFF0000)), "DXT1 block expands to expected solid red pixels");
Check(AssetPreviews.Dds(DdsFixture("DXT5")).Pixels.All(p => p == unchecked((int)0x80FF0000)), "DXT5 preserves decoded transparency");
Check(AssetPreviews.Dds(DdsFixture("DXT3")).Pixels.All(p => p == unchecked((int)0xFFFF0000)), "DXT3 explicit alpha decodes correctly");
var rgb565 = new byte[132]; "DDS "u8.CopyTo(rgb565);
foreach (var field in new[] { (4, 124), (12, 1), (16, 2), (76, 32), (80, 64), (88, 16), (92, 0xF800), (96, 0x07E0), (100, 0x001F) })
    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(rgb565.AsSpan(field.Item1, 4), field.Item2);
rgb565[129] = 248; rgb565[130] = 224; rgb565[131] = 7;
Check(AssetPreviews.Dds(rgb565).Pixels.SequenceEqual(new[] { unchecked((int)0xFFFF0000), unchecked((int)0xFF00FF00) }), "RGB565 DDS images decode with correct channel masks and opaque alpha");
var encoded565 = TextureEncoding.EncodeLike(new(2, 1, [unchecked((int)0xFFFF0000), unchecked((int)0xFF00FF00)], "fixture"), rgb565[..128]);
Check(AssetPreviews.Dds(encoded565).Pixels.SequenceEqual(AssetPreviews.Dds(rgb565).Pixels) && encoded565.Length == 132, "RGB565 conversion preserves pixel masks and native storage layout");
var rgbaHeader = rgb565[..128].ToArray();
foreach (var field in new[] { (80, 65), (88, 32), (92, 0xFF0000), (96, 0xFF00), (100, 0xFF), (104, unchecked((int)0xFF000000)) })
    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(rgbaHeader.AsSpan(field.Item1, 4), field.Item2);
int[] rgbaPixels = [unchecked((int)0x80FF0000), unchecked((int)0x0000FF00)];
Check(AssetPreviews.Dds(TextureEncoding.EncodeLike(new(2, 1, rgbaPixels, "fixture"), rgbaHeader)).Pixels.SequenceEqual(rgbaPixels), "uncompressed RGBA DDS conversion preserves exact pixels and transparency");
var transparentDds = DdsFixture("DXT1"); Array.Clear(transparentDds, 128, 4); for (int i = 132; i < 136; i++) transparentDds[i] = 255;
Check(AssetPreviews.Dds(transparentDds).Pixels.All(p => p == 0), "DXT1 transparent color mode is preserved");
Throws(() => AssetPreviews.Dds(DdsFixture("DXT5")[..130]), "truncated DDS blocks are rejected");
var badDds = DdsFixture("DXT1"); System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(badDds.AsSpan(16, 4), 1000000);
Throws(() => AssetPreviews.Dds(badDds), "DDS dimension limits prevent huge allocations");
Throws(() => AssetPreviews.Pcx([10, 5, 1, 8]), "truncated PCX header is rejected");
var pcm = new DecodedAudio(22050, 1, [short.MinValue, 0, short.MaxValue], "fixture");
var pcmDecoded = AssetPreviews.Wav(pcm.Wave());
Check(pcmDecoded.SampleRate == 22050 && pcmDecoded.Samples.SequenceEqual(pcm.Samples), "PCM preview stream round trips exact signed samples");
Throws(() => AssetPreviews.Wav(pcm.Wave()[..30]), "truncated WAV chunks are rejected");
if (Directory.Exists(actual))
{
    foreach (var sample in new[] {
        (Archive: "story_art.gfs", Asset: "story_art/Cinematics/Annie/01.dds", Output: "story"),
        (Archive: "ui.gfs", Asset: "ui/palettized_hud_art/character_annie_colors.pcx", Output: "ui-pcx") })
    {
        var archive = Archives.Read(Path.Combine(actual, sample.Archive)); var entry = archive.Entries.Single(e => e.Path == sample.Asset);
        byte[] bytes = AssetPreviews.Read(archive, entry, 64 * 1024 * 1024);
        var image = sample.Asset.EndsWith(".dds") ? AssetPreviews.Dds(bytes) : AssetPreviews.Pcx(bytes);
        Check(image.Pixels.Length == image.Width * image.Height && image.Width > 0, $"actual {sample.Output} image decodes within bounds");
        File.WriteAllBytes(Path.Combine(root, sample.Output + Path.GetExtension(sample.Asset)), bytes);
        using var stream = File.Create(Path.Combine(root, sample.Output + ".bgra")); using var writer = new BinaryWriter(stream);
        foreach (int pixel in image.Pixels) writer.Write(pixel);
        File.WriteAllText(Path.Combine(root, sample.Output + ".json"), JsonSerializer.Serialize(new { image.Width, image.Height }));
    }
    foreach (var sample in new[] {
        (Archive: "music-win.gfs", Asset: "music/BunnyBurrow_Intro.wav", Output: "music-stereo"),
        (Archive: "story_vo-win.gfs", Asset: "story_vo/lang_en/BeowulfEnd1000_EN.wav", Output: "voice-mono") })
    {
        var archive = Archives.Read(Path.Combine(actual, sample.Archive)); var entry = archive.Entries.Single(e => e.Path == sample.Asset);
        byte[] bytes = AssetPreviews.Read(archive, entry, 128 * 1024 * 1024); var audio = AssetPreviews.Wav(bytes);
        Check(audio.Duration > 0 && audio.Samples.Any(s => s != 0), $"actual {sample.Output} ADPCM audio decodes to PCM");
        string source = Path.Combine(root, sample.Output + ".wav"), raw = Path.Combine(root, sample.Output + ".raw");
        File.WriteAllBytes(source, bytes);
        using (var stream = File.Create(raw)) using (var writer = new BinaryWriter(stream)) foreach (short value in audio.Samples) writer.Write(value);
        string ffmpeg = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(p => Path.Combine(p.Trim('"'), "ffmpeg.exe")).FirstOrDefault(File.Exists) ?? "";
        if (File.Exists(ffmpeg))
        {
            var process = new System.Diagnostics.ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            string reference = Path.Combine(root, sample.Output + "-ffmpeg.raw");
            foreach (string argument in new[] { "-y", "-hide_banner", "-loglevel", "error", "-i", source, "-f", "s16le", "-acodec", "pcm_s16le", reference }) process.ArgumentList.Add(argument);
            using var job = System.Diagnostics.Process.Start(process)!; string error = job.StandardError.ReadToEnd(); job.WaitForExit();
            Check(job.ExitCode == 0 && File.ReadAllBytes(raw).SequenceEqual(File.ReadAllBytes(reference)), $"{sample.Output} samples exactly match independent FFmpeg decode ({error})");
        }
    }
    var coreIndex = Archives.Read(Path.Combine(actual, "core.gfs")); var first = coreIndex.Entries[0];
    Check(AssetPreviews.Read(coreIndex, first, 12).Length == Math.Min(12L, first.Size), "bounded archive read returns only requested preview bytes");
}
// Asset composer: path routing, safe drafts, updates and texture conversion.
var composerLibrary = new Library(Path.Combine(root, "ComposerData/mods"));
var composerIndex = Archives.Read(core); var composerAsset = composerIndex.Entries.Single(e => e.Path == "core/music.ini");
string dropFile = Payload("drops/user-chosen-name.ini", "first replacement");
string gameBeforeDrop = Crypto.Checksum(core);
var draftResult = composerLibrary.StageAsset(composerIndex, composerAsset, dropFile, null, "My first drop mod");
Check(draftResult.CreatedMod && !draftResult.Mod.Enabled && draftResult.Mod.Info.Name == "My first drop mod", "drop creates named mod metadata and starts disabled");
Check(File.ReadAllText(Path.Combine(draftResult.Mod.Folder, "core.gfs/core/music.ini")) == "first replacement", "drop uses selected archive and internal path rather than source filename");
Check(composerLibrary.Mods().Count == 1 && composerLibrary.Mods()[0].Info.Id == draftResult.Mod.Info.Id, "auto-created mod appears in the regular library");
File.WriteAllText(dropFile, "second replacement");
var updatedDrop = composerLibrary.StageAsset(composerIndex, composerAsset, dropFile, draftResult.Mod, "unused");
Check(!updatedDrop.CreatedMod && File.ReadAllText(updatedDrop.Destination) == "second replacement", "dropping again updates the selected mod in place");
Check(updatedDrop.PreviousCopy != null && File.ReadAllText(updatedDrop.PreviousCopy) == "first replacement", "replaced mod asset is preserved in revision history");
var uiIndexForDrop = Archives.Read(ui); string uiDrop = Payload("drops/menu.txt", "new menu");
composerLibrary.StageAsset(uiIndexForDrop, uiIndexForDrop.Entries[0], uiDrop, updatedDrop.Mod, "unused");
Check(updatedDrop.Mod.Replacements().Count == 2 && composerLibrary.Mods().Count == 1, "one draft can contain replacements across multiple archives");
Check(Crypto.Checksum(core) == gameBeforeDrop, "staging replacements never writes the game archive");
string wrongDrop = Payload("drops/not-an-image.exe", "bad");
Throws(() => composerLibrary.StageAsset(composerIndex, composerAsset, wrongDrop, null, "Bad draft"), "incompatible file type is rejected before a draft is created");
Check(composerLibrary.Mods().Count == 1, "rejected drop leaves no empty mod in the library");
Throws(() => composerLibrary.StageAsset(composerIndex, new("../../escaped.ini", 0, 1, 0), dropFile, updatedDrop.Mod, ""), "forged asset paths cannot be staged");
var spriteAsset = new ArchiveEntry("characters/annie.art", 4, 1, 0);
var spriteIndex = new ArchiveIndex("characters-art.gfs", new RevergePackageHeader(), [spriteAsset]);
string spriteInput = Payload("drops/annie.art", "sprite");
Throws(() => composerLibrary.StageAsset(spriteIndex, spriteAsset, spriteInput, null, "No sprites"), "sprite drops remain delegated to the Sprite Workshop");
foreach (string format in new[] { "DXT1", "DXT3", "DXT5" })
{
    var textureTarget = new TextureTarget(4, 4, format, 1);
    var encoded = TextureEncoding.Encode(new(4, 4, Enumerable.Repeat(unchecked((int)0xFFFF0000), 16).ToArray(), "test"), textureTarget);
    var decoded = AssetPreviews.Dds(encoded);
    Check(TextureTarget.Read(encoded) == textureTarget && decoded.Pixels.All(p => ((p >> 16) & 255) >= 248), $"automatic conversion produces matching {format} DDS with expected image content");
}
var mipTarget = new TextureTarget(8, 8, "DXT5", 3);
var mipEncoded = TextureEncoding.Encode(new(8, 8, Enumerable.Repeat(unchecked((int)0x80FF0000), 64).ToArray(), "test"), mipTarget);
Check(TextureTarget.Read(mipEncoded) == mipTarget && mipEncoded.Length == 224, "DDS conversion preserves exact mip count and alpha-capable compression");
string ddsSeed = Path.Combine(root, "DdsSeed"); Directory.CreateDirectory(Path.Combine(ddsSeed, "ui"));
File.WriteAllBytes(Path.Combine(ddsSeed, "ui/picture.dds"), DdsFixture("DXT5"));
string textureArchive = Path.Combine(game, "data01/ui-texture.gfs"); Seed(ddsSeed, textureArchive);
var textureIndex = Archives.Read(textureArchive); var textureAsset = textureIndex.Entries[0];
string ddsDrop = Path.Combine(root, "drops/image.dds");
File.WriteAllBytes(ddsDrop, TextureEncoding.Encode(new(4, 4, Enumerable.Repeat(unchecked((int)0xFFFF0000), 16).ToArray(), "test"), new(4, 4, "DXT5", 1)));
var imageStaged = composerLibrary.StageAsset(textureIndex, textureAsset, ddsDrop, updatedDrop.Mod, "");
Check(AssetPreviews.Dds(File.ReadAllBytes(imageStaged.Destination)).Width == 4, "converted DDS stages into the exact selected texture path");
File.WriteAllBytes(ddsDrop, mipEncoded);
Throws(() => composerLibrary.StageAsset(textureIndex, textureAsset, ddsDrop, updatedDrop.Mod, ""), "texture dimension or mip mismatch is rejected");
Check(TextureTarget.Read(File.ReadAllBytes(imageStaged.Destination)).Width == 4, "invalid second drop preserves the previous staged asset");
updatedDrop.Mod.SetEnabled(true);
var composedPlan = new Manager(game, Path.Combine(root, "ComposerData")).Preview([updatedDrop.Mod]);
Check(composedPlan.Replacements["core.gfs"].ContainsKey("core/music.ini") && composedPlan.Replacements["ui-texture.gfs"].ContainsKey("ui/picture.dds"), "composed mod feeds the existing preview/apply pipeline without import");
Check(AssetCharacters.Names("story_art/Cinematics/Annie/04_marie.dds").SequenceEqual(["Annie", "Marie"]), "character filtering includes story chapter and named guest character");
Check(AssetCharacters.Names("sprites/robofortune_10p.dds").SequenceEqual(["Robo-Fortune"]), "Robo-Fortune stays distinct from Ms. Fortune");
Check(AssetCharacters.Names("characters/boss_marie.art").SequenceEqual(["Boss Marie"]), "boss Marie stays distinct from playable Marie");
Check(AssetCharacters.Names("sprites/dahlia_ammo.dds").SequenceEqual(["Black Dahlia"]) && AssetCharacters.Names("ui/doublebuffer.dds").Count == 0, "character aliases match without partial-word false positives");
Check(new[] { "annie_10p.dds", "annie_2p.dds", "annie_1p.dds" }.OrderBy(AssetCharacters.NaturalKey).SequenceEqual(["annie_1p.dds", "annie_2p.dds", "annie_10p.dds"]), "character assets use numeric palette order");
string looseGame = Path.Combine(root,"LooseGame");
Directory.CreateDirectory(Path.Combine(looseGame,"data01")); File.WriteAllText(Path.Combine(looseGame,"SkullGirls.exe"),"fixture");
string looseCore = Path.Combine(looseGame,"data01/core.gfs"); Seed(Path.Combine(root,"seed/core"),looseCore);
byte[] pngOriginal = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aL1sAAAAASUVORK5CYII=");
byte[] pngA = pngOriginal.Concat(new byte[]{0}).ToArray(), pngB = pngOriginal.Concat(new byte[]{1}).ToArray();
string artRoot = Path.Combine(looseGame,"Digital Art Compendium/Artwork"); Directory.CreateDirectory(artRoot);
string artA = Path.Combine(artRoot,"a.png"), artB = Path.Combine(artRoot,"b.png"), artZ = Path.Combine(artRoot,"z.png"), untouched = Path.Combine(artRoot,"Marie.png");
foreach(string path in new[]{artA,artZ,untouched}) File.WriteAllBytes(path,pngOriginal);
string looseSource = Path.Combine(root,"LooseSource"); Directory.CreateDirectory(looseSource);
new ModInfo{Id="tests.loose.a",Name="Mixed artwork mod",Version=new(1,0)}.Write(Path.Combine(looseSource,"mod.json"));
foreach(string file in new[]{"a.png","b.png","z.png"}) { string path=Path.Combine(looseSource,"loose/Digital Art Compendium/Artwork",file); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path,pngA); }
string looseReplacement=Path.Combine(looseSource,"core.gfs/core/music.ini"); Directory.CreateDirectory(Path.GetDirectoryName(looseReplacement)!); File.WriteAllText(looseReplacement,"mixed mod");
var looseLibrary=new Library(Path.Combine(root,"LooseLibrary")); var looseMod=looseLibrary.Import(looseSource); looseMod.SetEnabled(true);
var looseManager=new Manager(looseGame,Path.Combine(root,"LooseProfile"));
Check(looseManager.Preview([looseMod]).LooseFiles.Count==3,"mixed mod previews all loose files alongside archive replacements");
using(var held=new FileStream(artZ,FileMode.Open,FileAccess.Read,FileShare.Read)) Throws(()=>looseManager.Apply([looseMod]),"locked loose file fails the combined transaction");
Check(File.ReadAllBytes(artA).SequenceEqual(pngOriginal)&&!File.Exists(artB)&&Contents(looseCore,"core/music.ini")=="original music"&&!looseManager.NeedsRecovery,"loose failure rolls back replaced files, added files and archive changes together");
looseManager.Apply([looseMod]);
Check(File.ReadAllBytes(artA).SequenceEqual(pngA)&&File.Exists(artB)&&File.ReadAllBytes(untouched).SequenceEqual(pngOriginal),"loose apply replaces and adds artwork while preserving unrelated files");
string looseSourceB=Path.Combine(root,"LooseOnly"); Directory.CreateDirectory(Path.Combine(looseSourceB,"loose/Digital Art Compendium/Artwork"));
new ModInfo{Id="tests.loose.b",Name="Loose-only mod",Version=new(1,0)}.Write(Path.Combine(looseSourceB,"mod.json")); File.WriteAllBytes(Path.Combine(looseSourceB,"loose/Digital Art Compendium/Artwork/a.png"),pngB);
string looseZip=Path.Combine(root,"loose-only.zip"); ZipFile.CreateFromDirectory(looseSourceB,looseZip); var looseOther=looseLibrary.Import(looseZip); looseOther.SetEnabled(true);
Check(!new Mod(looseSourceB).Enabled&&looseManager.Preview([looseMod,looseOther]).Changes.Count(c=>c.Conflict)==1,"loose-only ZIP imports and overlapping artwork is reported");
looseManager.Apply([looseMod,looseOther]); Check(File.ReadAllBytes(artA).SequenceEqual(pngB),"later mod wins loose-file conflicts");
looseOther.SetEnabled(false); looseManager.Apply([looseMod,looseOther]); Check(File.ReadAllBytes(artA).SequenceEqual(pngA),"disabling later loose mod reveals earlier replacement");
File.WriteAllBytes(artA,pngB); Throws(()=>looseManager.Apply([looseMod]),"external loose-file edits block apply"); Check(File.ReadAllBytes(artA).SequenceEqual(pngB),"external artwork edits are preserved"); File.WriteAllBytes(artA,pngA);
string looseBackup=Path.Combine(looseManager.BackupFolder,"loose/Digital Art Compendium/Artwork/a.png"); File.WriteAllBytes(looseBackup,pngB); Throws(()=>looseManager.Apply([looseMod]),"tampered loose backup blocks writes"); File.WriteAllBytes(looseBackup,pngOriginal);
looseMod.SetEnabled(false); Check(looseManager.Preview([looseMod]).LooseRestore.Count==3,"disabled loose mod plans restoration and added-file removal"); looseManager.Apply([looseMod]);
Check(File.ReadAllBytes(artA).SequenceEqual(pngOriginal)&&!File.Exists(artB)&&Contents(looseCore,"core/music.ini")=="original music","disable restores original PNG and archive and removes mod-added PNG");
looseMod.SetEnabled(true); looseManager.Apply([looseMod]); looseManager.Apply([looseMod],true); Check(!File.Exists(artB)&&File.ReadAllBytes(artZ).SequenceEqual(pngOriginal)&&looseMod.Enabled,"Restore originals handles loose files and retains enabled selection");
var looseState=JsonSerializer.Deserialize<InstallState>(File.ReadAllText(Path.Combine(looseManager.Profile,"state.json")))!;
File.WriteAllBytes(artB,pngA); Storage.Save(Path.Combine(looseManager.Profile,"transaction.json"),new Transaction(looseState,[new(artB,artB+".sgmm-crash.tmp",artB+".sgmm-crash.rollback","",Crypto.Checksum(artB))])); looseManager.Recover(); Check(!File.Exists(artB),"crash recovery removes a newly added loose file with no original");
string looseRollback=artA+".sgmm-delete.rollback"; File.Move(artA,looseRollback); Storage.Save(Path.Combine(looseManager.Profile,"transaction.json"),new Transaction(looseState,[new(artA,artA+".sgmm-delete.tmp",looseRollback,Crypto.Checksum(looseRollback),"")])); looseManager.Recover(); Check(File.ReadAllBytes(artA).SequenceEqual(pngOriginal),"crash recovery reverses an interrupted loose-file removal");
Throws(()=>Manager.ValidateLooseName("Digital Art Compendium/../SkullGirls.exe"),"loose traversal outside compendium rejected"); Throws(()=>Manager.ValidateLooseName("Digital Art Compendium/payload.dll"),"loose executable payloads rejected");
File.WriteAllText(Path.Combine(looseSourceB,"loose/Digital Art Compendium/Artwork/a.png"),"invalid image"); var invalidLoose=new Mod(looseSourceB); invalidLoose.SetEnabled(true); Throws(()=>looseManager.Apply([invalidLoose]),"invalid PNG signature rejected before installation");
File.WriteAllText(Path.Combine(looseGame,"SkullGirls.exe"),"MZ fixture Package %s.gfs not verified.");
Check(ArchiveCompatibility.Required(looseGame,["core.gfs"]) && ArchiveCompatibility.Required(looseGame,["characters-foits.gfs"]) && !ArchiveCompatibility.Required(looseGame,["story_art.gfs"]),"compatibility detection distinguishes verified archives from ordinary artwork");
Throws(()=>looseManager.Apply([looseMod]),"verified archive mods are blocked before installation without a compatible loader");
Check(Contents(looseCore,"core/music.ini")=="original music"&&!looseManager.NeedsRecovery,"missing loader leaves archives and transaction state unchanged");
string invalidLoader=Payload("unsupported-CoreMessaging.dll","invalid loader"); Throws(()=>ArchiveCompatibility.Install(looseGame,looseManager.Profile,invalidLoader),"unsupported loader is rejected"); Check(!File.Exists(Path.Combine(looseGame,"CoreMessaging.dll")),"rejected loader does not change game files");
File.WriteAllText(Path.Combine(root, "results.txt"), $"{passed} checks passed. Actual installed game files were opened read-only.\n");
Console.WriteLine($"\n{passed} checks passed. Results: {root}");
