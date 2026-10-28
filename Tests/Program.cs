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
// Relocated art must fit the memory sizes the game reads from VRSN 9 FOITs.
string spriteSource = Path.Combine(root, "SpriteStorageMod");
byte[] paletteName = System.Text.Encoding.ASCII.GetBytes("valentine_1p.pal");
byte[] spriteMetadata = new byte[35 + paletteName.Length + 8];
"VRSN"u8.CopyTo(spriteMetadata.AsSpan(4));
System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(spriteMetadata.AsSpan(8), 9);
for (int i = 0; i < 5; i++) System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(spriteMetadata.AsSpan(12 + 4 * i), 8);
System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(spriteMetadata.AsSpan(32), 1);
spriteMetadata[34] = (byte)paletteName.Length; paletteName.CopyTo(spriteMetadata, 35);
string spriteFoit = Payload("SpriteStorageMod/characters-foits.gfs/characters/valentine.foit", "");
File.WriteAllBytes(spriteFoit, spriteMetadata);
File.WriteAllBytes(Payload("SpriteStorageMod/characters-art.gfs/characters/valentine.art", ""), new byte[32]);
File.WriteAllBytes(Payload("SpriteStorageMod/characters-art-pt.gfs/characters/valentine.art-pt", ""), new byte[48]);
new ModInfo { Id = "tests.sprite-storage", Name = "Relocated sprites", Version = new Version(1, 0) }.Write(Path.Combine(spriteSource, "mod.json"));
string spriteZip = Path.Combine(root, "sprite-storage.zip"); ZipFile.CreateFromDirectory(spriteSource, spriteZip);
var spriteMod = new Library(Path.Combine(root, "SpriteStorageLibrary")).Import(spriteZip);
byte[] repairedMetadata = File.ReadAllBytes(spriteMod.Replacements()["characters-foits.gfs"]["characters/valentine.foit"]);
byte[] expectedMetadata = (byte[])spriteMetadata.Clone();
System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(expectedMetadata.AsSpan(20), 32);
System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(expectedMetadata.AsSpan(24), 48);
Check(repairedMetadata.SequenceEqual(expectedMetadata), "Workshop import grows both art memory sizes and preserves every other FOIT byte");
Check(File.ReadAllBytes(spriteFoit).SequenceEqual(spriteMetadata) && !spriteMod.Enabled, "sprite size repair preserves source files and imports disabled");
Check(JsonSerializer.Deserialize<List<SpriteStorageRepair>>(File.ReadAllText(Path.Combine(spriteMod.Folder, SpriteStorage.Receipt)))!.Count == 2,
    "sprite size repairs have a reviewable import receipt");
Check(SpriteStorage.Repair(spriteMod).Count == 0, "sprite size repair is idempotent and retains sufficient budgets");
File.WriteAllBytes(spriteMod.Replacements()["characters-foits.gfs"]["characters/valentine.foit"], spriteMetadata[..30]);
Check(SpriteStorage.Repair(spriteMod).Count == 0, "unrecognized or truncated metadata is left unchanged");
Throws(() => library.Import(sourceA), "duplicate mod IDs rejected without deleting installed version");
// Updating is a standalone library operation: no game or editing tools required.
var updates = new Library(Path.Combine(root, "UpdateLibrary"));
var updateFirst = updates.Import(sourceA); var updateSecond = updates.Import(zip);
updateFirst.SetEnabled(true); updates.SetOrder([updateSecond, updateFirst]);
string updateSource = Path.Combine(root, "UpdateV2");
Payload("UpdateV2/core.gfs/core/music.ini", "updated music");
Payload("UpdateV2/core.gfs/core/new.ini", "new asset");
new ModInfo { Id = "tests.a", Name = "Mod A", Version = new Version(2, 0) }.Write(Path.Combine(updateSource, "mod.json"));
string updateZip = Path.Combine(root, "update-v2.zip"); ZipFile.CreateFromDirectory(updateSource, updateZip);
string savedOrder = File.ReadAllText(Path.Combine(updates.Root, "order.json"));
using (var update = updates.PrepareUpdate(updateFirst, updateZip))
{
    Check(update.Changes.Any(c => c.Kind == "Added" && c.Path.EndsWith("new.ini")) &&
        update.Changes.Any(c => c.Kind == "Changed" && c.Path.EndsWith("music.ini")) &&
        update.Changes.Any(c => c.Kind == "Removed" && c.Path.EndsWith("menu.txt")), "update preview lists added, changed and removed assets");
    updateFirst = updates.CommitUpdate(update);
}
Check(updateFirst.Enabled && updateFirst.Info.Version == new Version(2, 0) && updates.Mods()[1].Folder == updateFirst.Folder &&
    File.ReadAllText(Path.Combine(updates.Root, "order.json")) == savedOrder, "update preserves enabled selection and exact order file");
Check(!Directory.Exists(Path.Combine(updateFirst.Folder, "ui.gfs")), "complete package update removes deleted archive assets");
Check(Crypto.Checksum(core) == originalCore && Crypto.Checksum(ui) == originalUi, "updating the library does not touch installed game archives");
updateFirst.SetEnabled(false);
using (var versionRollback = updates.PrepareRollback(updateFirst)) updateFirst = updates.CommitUpdate(versionRollback);
Check(updateFirst.Enabled && updateFirst.Info.Version == new Version(1, 0) && File.Exists(Path.Combine(updateFirst.Folder, "ui.gfs/ui/menu.txt")), "rollback restores previous complete package and enabled setting");
using (var cancelled = updates.PrepareUpdate(updateFirst, updateSource)) { }
Check(new Mod(updateFirst.Folder).Info.Version == new Version(1, 0) && !Directory.GetDirectories(updates.Root, ".prepared-*").Any(), "cancelled update preserves original and removes staging");
string invalidUpdate = Path.Combine(root, "InvalidUpdate"); Directory.CreateDirectory(invalidUpdate);
File.WriteAllText(Path.Combine(invalidUpdate, "mod.json"), "invalid JSON");
Throws(() => updates.PrepareUpdate(updateFirst, invalidUpdate), "invalid update fails before modifying original");
Check(new Mod(updateFirst.Folder).Enabled && new Mod(updateFirst.Folder).Info.Version == new Version(1, 0), "failed validation leaves original package and settings intact");
using (var staleUpdate = updates.PrepareUpdate(updateFirst, updateSource))
{
    updateFirst.SetEnabled(false);
    Throws(() => updates.CommitUpdate(staleUpdate), "changes to library settings after preview reject stale update");
    updateFirst.SetEnabled(true);
}
using (var altered = updates.PrepareUpdate(updateFirst, updateSource))
{
    File.AppendAllText(Path.Combine(altered.Replacement.Folder, "core.gfs/core/music.ini"), "tamper");
    Throws(() => updates.CommitUpdate(altered), "changed staged payload is rejected after preview");
}
using (var lockedUpdate = updates.PrepareUpdate(updateFirst, updateSource))
{
    using (var packageLock = new FileStream(Path.Combine(lockedUpdate.Replacement.Folder, "core.gfs/core/music.ini"), FileMode.Open, FileAccess.Read, FileShare.Read))
        Throws(() => updates.CommitUpdate(lockedUpdate), "locked replacement package fails directory swap safely");
    Check(new Mod(updateFirst.Folder).Info.Version == new Version(1, 0) && new Mod(updateFirst.Folder).Enabled &&
        !File.Exists(Path.Combine(updates.Root, ".mod-update.json")), "failed directory swap restores original library package and clears journal");
}
using (var duplicate = updates.PrepareUpdate(updateFirst, sourceB))
    Throws(() => updates.CommitUpdate(duplicate, true), "explicit ID mapping cannot collide with another library entry");
new ModInfo { Id = "tests.renamed", Name = "Renamed A", Version = new Version(2, 0) }.Write(Path.Combine(updateSource, "mod.json"));
using (var mapped = updates.PrepareUpdate(updateFirst, updateSource))
{
    Throws(() => updates.CommitUpdate(mapped), "different ID requires explicit mapping");
    updateFirst = updates.CommitUpdate(mapped, true);
}
Check(updateFirst.Info.Id == "tests.renamed" && updateFirst.Enabled && updates.Mods()[1].Folder == updateFirst.Folder, "explicit ID mapping preserves selected entry and order");
string interruptedPrevious = Path.Combine(updates.Root, ".versions", Path.GetFileName(updateFirst.Folder), "interrupted");
Directory.CreateDirectory(Path.GetDirectoryName(interruptedPrevious)!);
Storage.Save(Path.Combine(updates.Root, ".mod-update.json"), new { Destination = Path.GetFileName(updateFirst.Folder), Previous = Path.GetRelativePath(updates.Root, interruptedPrevious) });
Directory.Move(updateFirst.Folder, interruptedPrevious);
var recoveredLibrary = new Library(updates.Root);
Check(recoveredLibrary.Mods().Any(m => m.Folder == updateFirst.Folder && m.Info.Id == "tests.renamed") &&
    !File.Exists(Path.Combine(updates.Root, ".mod-update.json")), "reopening library recovers an interrupted update swap");
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
var musicOutcome = plan.Outcomes.Single(o => o.Archive == "core.gfs" && o.Asset == "core/music.ini");
Check(musicOutcome.Contributors.Select(c => c.ModFolder).SequenceEqual([modA.Folder, modB.Folder]) &&
    musicOutcome.Winner.ModFolder == modB.Folder && musicOutcome.Contributors.Select(c => c.LoadOrder).SequenceEqual([1, 2]), "asset conflict retains all contributors, library identities, order and final winner");
Check(manager.Preview([modB, modA]).Outcomes.Single(o => o.Asset == "core/music.ini").Winner.ModFolder == modA.Folder,
    "reordering ordinary packages updates final asset winner");
Check(musicOutcome.Explanation.Contains("complete file") && musicOutcome.Explanation.Contains("not establish game compatibility"),
    "unsupported assets explain file precedence without claiming semantic compatibility");
var cleanStatus = manager.InspectInstallation();
Check(cleanStatus.Issues.Count == 0 && cleanStatus.TrackedArchives == 0, "fresh installation status has no tracked backups to verify");
modB.SetEnabled(false);
Check(!manager.Preview([modA, modB]).Outcomes.Single(o => o.Asset == "core/music.ini").Conflict,
    "disabled contributors do not appear as conflicts"); modB.SetEnabled(true);
// Metadata warnings depend on package bytes and supported paths, without an external editor.
string metadataGame = Path.Combine(root, "MetadataGame"); Directory.CreateDirectory(Path.Combine(metadataGame, "data01"));
File.WriteAllText(Path.Combine(metadataGame, "SkullGirls.exe"), "fixture");
File.WriteAllBytes(Payload("MetadataSeed/characters/valentine.foit", ""), spriteMetadata);
Seed(Path.Combine(root, "MetadataSeed"), Path.Combine(metadataGame, "data01/characters-foits.gfs"));
var metadataLibrary = new Library(Path.Combine(root, "MetadataLibrary"));
var metadataA = metadataLibrary.CreateDraft("Same display name"); var metadataB = metadataLibrary.CreateDraft("Same display name");
foreach (var metadataMod in new[] { metadataA, metadataB })
{
    Directory.CreateDirectory(Path.Combine(metadataMod.Folder, "characters-foits.gfs", "characters"));
    File.WriteAllBytes(Path.Combine(metadataMod.Folder, "characters-foits.gfs", "characters/valentine.foit"), spriteMetadata);
    metadataMod.SetEnabled(true);
}
var metadataManager = new Manager(metadataGame, Path.Combine(root, "MetadataData"));
var metadataOutcome = metadataManager.Preview([metadataA, metadataB]).Outcomes.Single();
Check(metadataOutcome.Explanation.Contains("Recognized VRSN 9") && metadataOutcome.Explanation.Contains("palette-name table") && metadataOutcome.Explanation.Contains("associated art"),
    "recognized character metadata warns about full-file palette and associated art effects");
Check(metadataOutcome.Contributors[0].ModFolder != metadataOutcome.Contributors[1].ModFolder && metadataOutcome.Winner.ModFolder == metadataB.Folder,
    "same-named mods retain distinct library identities for conflict navigation");
File.WriteAllText(Path.Combine(metadataB.Folder, "characters-foits.gfs", "characters/valentine.foit"), "unknown format");
Check(metadataManager.Preview([metadataA, metadataB]).Outcomes.Single().Explanation.Contains("Only file-level precedence is known"),
    "unsupported character metadata retains file-level explanation without speculative palette compatibility");
manager.Apply([modA, modB]);
Check(Contents(core, "core/music.ini") == "mod B wins" && Contents(ui, "ui/menu.txt") == "mod A menu", "later mod wins and multiple archives apply");
Check(Crypto.Checksum(Path.Combine(manager.BackupFolder, "core.gfs")) == originalCore, "verified original backup retained");
Check(manager.InspectInstallation().Issues.Count == 0 && manager.InspectInstallation().TrackedArchives == 2,
    "installation status verifies applied archive hashes and original backups");
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
var pendingStatus = manager.InspectInstallation();
Check(pendingStatus.PendingTransaction && pendingStatus.Issues.Any(i => i.Action.Contains("Recover interrupted operation")) &&
    pendingStatus.Issues.All(i => !i.Problem.Contains("outside")), "interrupted transaction status points to recovery without mislabeling in-flight writes as external edits");
Throws(() => manager.Preview([]), "pending transaction blocks new plans");
manager.Recover();
Check(Crypto.Checksum(core) == originalCore && !manager.NeedsRecovery, "persisted interrupted transaction recovers original data and state");
modA.SetEnabled(true);
using (var stream = new FileStream(ui, FileMode.Append)) stream.WriteByte(42);
string externalHash = Crypto.Checksum(ui);
Check(manager.InspectInstallation().Issues.Any(i => i.Target == "ui.gfs" && i.Problem.Contains("outside") && i.Action.Contains("Update archive")),
    "status names externally changed archive and explains explicit baseline adoption");
Check(Crypto.Checksum(ui) == externalHash && Crypto.Checksum(core) == originalCore, "status inspection preserves installed archive bytes");
Throws(() => manager.Apply([modA]), "externally modified archive blocks apply before any commit");
Check(Crypto.Checksum(core) == originalCore && Crypto.Checksum(ui) == externalHash, "external archive changes are preserved");
File.Copy(Path.Combine(manager.BackupFolder, "ui.gfs"), ui, true);
File.AppendAllText(Path.Combine(manager.BackupFolder, "core.gfs"), "tamper");
Check(manager.InspectInstallation().Issues.Any(i => i.Target == "core.gfs" && i.Problem.Contains("checksum failed")),
    "status identifies damaged original backup before Apply");
File.Move(Path.Combine(manager.BackupFolder, "ui.gfs"), Path.Combine(manager.BackupFolder, "ui.gfs.saved"));
Check(manager.InspectInstallation().Issues.Any(i => i.Target == "ui.gfs" && i.Problem.Contains("backup is missing")),
    "status reports missing backup alongside other verification problems");
File.Move(Path.Combine(manager.BackupFolder, "ui.gfs.saved"), Path.Combine(manager.BackupFolder, "ui.gfs"));
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
Check(looseManager.Preview([looseMod,looseOther]).Outcomes.Single(o => o.Archive == "Loose files" && o.Conflict).Winner.ModFolder == looseOther.Folder,
    "loose-file conflicts identify final winner and navigable library entry");
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
// A reinstall returning an archive to its verified original is safe to reapply.
looseManager.Apply([looseMod]);
File.Copy(Path.Combine(looseManager.BackupFolder,"core.gfs"),looseCore,true);
looseManager.Apply([looseMod]);
Check(Contents(looseCore,"core/music.ini")!="original music","reapply accepts a reinstalled archive matching its original checksum");
looseManager.Apply([],true);
File.WriteAllText(Path.Combine(looseGame,"SkullGirls.exe"),"MZ fixture Package %s.gfs not verified.");
Check(ArchiveCompatibility.Required(looseGame,["core.gfs"]) && ArchiveCompatibility.Required(looseGame,["characters-foits.gfs"]) && !ArchiveCompatibility.Required(looseGame,["story_art.gfs"]),"compatibility detection distinguishes verified archives from ordinary artwork");
Throws(()=>looseManager.Apply([looseMod]),"verified archive mods are blocked before installation without a compatible loader");
Check(Contents(looseCore,"core/music.ini")=="original music"&&!looseManager.NeedsRecovery,"missing loader leaves archives and transaction state unchanged");
string invalidLoader=Payload("unsupported-CoreMessaging.dll","invalid loader"); Throws(()=>ArchiveCompatibility.Install(looseGame,looseManager.Profile,invalidLoader),"unsupported loader is rejected"); Check(!File.Exists(Path.Combine(looseGame,"CoreMessaging.dll")),"rejected loader does not change game files");
// Saved mod support must never trust missing or modified cached DLLs.
Check(!ArchiveCompatibility.RestoreSaved(looseGame,looseManager.Profile),"missing saved support returns to one-time setup");
string cachedLoader=ArchiveCompatibility.SavedLoader(looseManager.Profile);
Directory.CreateDirectory(Path.GetDirectoryName(cachedLoader)!);File.WriteAllText(cachedLoader,"invalid cached loader");
Check(!ArchiveCompatibility.Saved(looseManager.Profile)&&!ArchiveCompatibility.RestoreSaved(looseGame,looseManager.Profile)&&!File.Exists(Path.Combine(looseGame,"CoreMessaging.dll")),"tampered saved support is not installed");
string? supportedLoader=Environment.GetEnvironmentVariable("SGMM_TEST_COMPATIBILITY_LOADER");
if(supportedLoader!=null) {
 ArchiveCompatibility.Install(looseGame,looseManager.Profile,supportedLoader);
 Check(ArchiveCompatibility.Installed(looseGame)&&ArchiveCompatibility.Saved(looseManager.Profile),"one-time setup installs and caches verified support");
 Directory.CreateDirectory(Path.Combine(looseGame,"data02"));
 var redirected = new Manager(looseGame,Path.Combine(root,"LooseProfile"));
 Check(redirected.DataFolder==Path.Combine(looseGame,"data02")&&redirected.Profile!=looseManager.Profile,"compatibility redirect targets data02 with isolated backups");
 Check(ArchiveCompatibility.Saved(redirected.Profile),"redirected profile retains saved support");
 File.Copy(Path.Combine(looseManager.BackupFolder,"core.gfs"),Path.Combine(looseGame,"data02","core.gfs"));
 redirected.Apply([looseMod]);
 Check(Contents(Path.Combine(looseGame,"data02/core.gfs"),"core/music.ini")!="original music"&&Contents(looseCore,"core/music.ini")=="original music","Apply targets data02 without changing data01");
 redirected.Apply([],true);
 Check(Contents(Path.Combine(looseGame,"data02/core.gfs"),"core/music.ini")=="original music"&&Contents(looseCore,"core/music.ini")=="original music","Restore originals restores data02 and leaves data01 unchanged");
 Directory.Delete(Path.Combine(looseGame,"data02"),true);

 File.Delete(Path.Combine(looseGame,"CoreMessaging.dll"));
 Check(ArchiveCompatibility.RestoreSaved(looseGame,looseManager.Profile)&&ArchiveCompatibility.Installed(looseGame),"game reinstall restores saved support without source ZIP");
 File.WriteAllText(Path.Combine(looseGame,"CoreMessaging.dll"),"another loader");
 Throws(()=>ArchiveCompatibility.RestoreSaved(looseGame,looseManager.Profile),"saved support refuses to overwrite another loader");
 Check(File.ReadAllText(Path.Combine(looseGame,"CoreMessaging.dll"))=="another loader","conflicting loader is preserved");
 File.Delete(Path.Combine(looseGame,"CoreMessaging.dll"));
 File.WriteAllText(cachedLoader,"tampered");
 Check(!ArchiveCompatibility.RestoreSaved(looseGame,looseManager.Profile),"damaged cache after successful setup requires new source");
}
// Explicitly adopt external archive changes while retaining previous baselines.
string adoptGame = Path.Combine(root, "BaselineGame"), adoptData = Path.Combine(root, "BaselineData");
Directory.CreateDirectory(Path.Combine(adoptGame, "data01")); File.WriteAllText(Path.Combine(adoptGame, "SkullGirls.exe"), "fixture");
string adoptCore = Path.Combine(adoptGame, "data01/core.gfs"), adoptUi = Path.Combine(adoptGame, "data01/ui.gfs");
Seed(Path.Combine(root, "seed/core"), adoptCore); Seed(Path.Combine(root, "seed/ui"), adoptUi);
var adopt = new Manager(adoptGame, adoptData);
string initialCoreHash = Crypto.Checksum(adoptCore);
adopt.UpdateArchiveBaseline(adopt.ReviewArchiveUpdate("ui.gfs"));
adopt.UpdateArchiveBaseline(adopt.ReviewArchiveUpdate("core.gfs"));
Check(Crypto.Checksum(adoptCore) == initialCoreHash && Crypto.Checksum(Path.Combine(adopt.BackupFolder, "core.gfs")) == initialCoreHash, "initial archive baseline captures a verified backup without changing the game");
Check(adopt.UpdateArchiveBaseline(adopt.ReviewArchiveUpdate("core.gfs")) == null, "updating an unchanged baseline is a no-op");
string external = Payload("external-change.txt", "outside custom artwork");
string newArchive = Path.Combine(root, "external-core.gfs"); Archives.Build(adoptCore, newArchive, new Dictionary<string,string> { ["core/external.ini"] = external });
File.Copy(newArchive, adoptCore, true); string adoptedHash = Crypto.Checksum(adoptCore);
var reviewed = adopt.ReviewArchiveUpdate("core.gfs");
Throws(() => adopt.Apply([modA]), "external changes still block Apply before baseline adoption");
string beforeAdoptState = File.ReadAllText(Path.Combine(adopt.Profile, "state.json"));
string history = adopt.UpdateArchiveBaseline(reviewed)!;
Check(Crypto.Checksum(adoptCore) == adoptedHash && Crypto.Checksum(Path.Combine(adopt.BackupFolder, "core.gfs")) == adoptedHash, "adoption updates the baseline without modifying installed archive bytes");
Check(Crypto.Checksum(Path.Combine(history, "previous-backup.gfs")) == initialCoreHash && File.ReadAllText(Path.Combine(history, "previous-state.json")) == beforeAdoptState, "previous original backup and full profile state are preserved in history");
Check(Crypto.Checksum(Path.Combine(history, "current.gfs")) == adoptedHash && !adopt.NeedsRecovery, "adopted snapshot is verified and journal cleared");
modA.SetEnabled(true); adopt.Apply([modA]); adopt.Apply([modA], true);
Check(Crypto.Checksum(adoptCore) == adoptedHash && Contents(adoptCore,"core/external.ini") == "outside custom artwork", "Apply and Restore use the adopted baseline and preserve external additions");
var stale = adopt.ReviewArchiveUpdate("core.gfs"); File.Copy(built, adoptCore, true);
Throws(() => adopt.UpdateArchiveBaseline(stale), "a changed archive invalidates the confirmation snapshot");
Check(Crypto.Checksum(Path.Combine(adopt.BackupFolder, "core.gfs")) == adoptedHash, "stale confirmation leaves the baseline unchanged");
string priorBackup = Path.Combine(adopt.BackupFolder, "core.gfs"); File.WriteAllText(priorBackup, "corrupt backup");
Throws(() => adopt.UpdateArchiveBaseline(adopt.ReviewArchiveUpdate("core.gfs")), "damaged previous baseline cannot be silently replaced");
File.Copy(Path.Combine(history, "current.gfs"), priorBackup, true);
Throws(() => adopt.ReviewArchiveUpdate("../core.gfs"), "baseline archive path traversal rejected");
string adoptedState = Path.Combine(adopt.Profile, "state.json");
var interrupted = adopt.ReviewArchiveUpdate("core.gfs");
using (var held = new FileStream(adoptedState, FileMode.Open, FileAccess.Read, FileShare.Read))
    Throws(() => adopt.UpdateArchiveBaseline(interrupted), "interrupted state publication leaves a recoverable baseline update");
Check(adopt.NeedsRecovery, "interrupted baseline updates block later operations");
var baselineStatus = adopt.InspectInstallation();
Check(baselineStatus.PendingBaselineUpdate && baselineStatus.HistoryCount > 0 && baselineStatus.Issues.Any(i => i.Problem.Contains("baseline update")),
    "status reports interrupted baseline update and retained history without performing recovery");
Throws(() => adopt.Apply([modA]), "Apply refuses an unfinished baseline update");
string interruptedHash = Crypto.Checksum(adoptCore); File.Copy(newArchive, adoptCore, true);
Throws(() => adopt.Recover(), "baseline recovery refuses newer external archive changes");
File.Copy(built, adoptCore, true); Check(Crypto.Checksum(adoptCore) == interruptedHash, "recovery test restores the captured external file");
var pending = JsonSerializer.Deserialize<ArchiveBaselineUpdate>(File.ReadAllText(Path.Combine(adopt.Profile,"baseline-update.json")))!;
string previousSnapshot = Path.Combine(adopt.BaselineHistory,pending.History,"previous-backup.gfs"); byte[] preserved = File.ReadAllBytes(previousSnapshot);
File.WriteAllText(previousSnapshot,"tamper"); Throws(() => adopt.Recover(), "baseline recovery verifies the preserved previous backup"); File.WriteAllBytes(previousSnapshot,preserved);
adopt.Recover();
Check(!adopt.NeedsRecovery && Crypto.Checksum(adoptCore) == interruptedHash && Crypto.Checksum(priorBackup) == interruptedHash, "baseline recovery completes adoption without writing game files");
adopt.Apply([modA],true); Check(Crypto.Checksum(adoptCore)==interruptedHash,"Restore after recovered adoption uses the new baseline");
string inspectedState = File.ReadAllText(adoptedState);
var inspectedHistory = Directory.GetFiles(adopt.BaselineHistory, "*", SearchOption.AllDirectories).ToDictionary(p => p, Crypto.Checksum);
var historyStatus = adopt.InspectInstallation();
Check(!historyStatus.NeedsRecovery && historyStatus.HistoryCount > 0 && historyStatus.Issues.Count == 0 &&
    File.ReadAllText(adoptedState) == inspectedState && inspectedHistory.All(p => Crypto.Checksum(p.Key) == p.Value),
    "status enumerates available baseline history without altering saved state or history snapshots");
File.Move(adoptCore, adoptCore + ".missing-test");
Check(adopt.InspectInstallation().Issues.Any(i => i.Target == "core.gfs" && i.Problem.Contains("Installed file is missing")),
    "status identifies missing installed archive without blocking backup inspection");
File.Move(adoptCore + ".missing-test", adoptCore);
using (var heldStatusBackup = new FileStream(priorBackup, FileMode.Open, FileAccess.Read, FileShare.None))
    Check(adopt.InspectInstallation().Issues.Any(i => i.Target == "core.gfs" && i.Problem.Contains("Cannot verify backup")),
        "status reports unreadable backups instead of abandoning inspection");
File.WriteAllText(adoptedState, "invalid JSON");
Check(adopt.InspectInstallation().Issues.Any(i => i.Target == "Backup state"), "unreadable saved state becomes an actionable status issue");
File.WriteAllText(adoptedState, inspectedState);
// Named profiles share the library and never touch installation backups or history.
var profileLibrary = new Library(Path.Combine(root, "ProfileLibrary"));
var profileA = profileLibrary.Import(sourceA); var profileB = profileLibrary.Import(zip);
profileA.SetEnabled(true); profileLibrary.SetOrder([profileB, profileA]);
var eveningProfile = profileLibrary.SaveProfile("Evening");
Check(eveningProfile.Mods.Select(m => m.Id).SequenceEqual(["tests.b", "tests.a"]) && eveningProfile.Mods[1].Enabled && !eveningProfile.Mods[0].Enabled,
    "profile saves the complete shared-library order and enabled selection");
profileB.SetEnabled(true); profileA.SetEnabled(false); profileLibrary.SetOrder([profileA, profileB]);
var daytimeProfile = profileLibrary.SaveProfile("Daytime");
var switchEvening = profileLibrary.ReviewProfile(eveningProfile.Id);
Check(!profileA.Enabled && profileB.Enabled && profileLibrary.Mods()[0].Folder == profileA.Folder,
    "profile review does not change enabled selection or order");
profileLibrary.SwitchProfile(switchEvening);
Check(profileA.Enabled && !profileB.Enabled && profileLibrary.Mods()[0].Folder == profileB.Folder,
    "switching a named profile restores saved selection and exact order");
profileLibrary.SwitchProfile(profileLibrary.ReviewProfile(daytimeProfile.Id));
Check(!profileA.Enabled && profileB.Enabled && profileLibrary.Mods()[0].Folder == profileA.Folder,
    "switching between two profiles restores independent selections");
Check(new Library(profileLibrary.Root).Profiles().Select(p => p.Name).SequenceEqual(["Daytime", "Evening"]),
    "named profiles persist across library reload");
Throws(() => profileLibrary.SaveProfile("evening"), "duplicate profile names require explicit overwrite regardless of case");
var overwrittenProfile = profileLibrary.SaveProfile("EVENING", true);
Check(overwrittenProfile.Id == eveningProfile.Id && profileLibrary.Profiles().Count == 2,
    "explicit overwrite keeps profile identity and avoids duplicates");
Throws(() => profileLibrary.SaveProfile("  "), "blank profile name rejected");
Throws(() => profileLibrary.SaveProfile(new string('x', 81)), "overlong profile name rejected");
var extraProfileMod = profileLibrary.CreateDraft("New library mod"); extraProfileMod.SetEnabled(true);
var withExtra = profileLibrary.ReviewProfile(daytimeProfile.Id);
Check(withExtra.Rows.Last().Folder == extraProfileMod.Folder && !withExtra.Rows.Last().Enabled && withExtra.Rows.Last().Order == 3,
    "mods added after saving a profile are appended and proposed disabled");
profileLibrary.SwitchProfile(withExtra); Check(!extraProfileMod.Enabled, "profile switch disables unsaved new mods");
// Reuse ordinary update packages to check version and identity notices.
new ModInfo { Id = "tests.a", Name = "Mod A", Version = new Version(2, 0) }.Write(Path.Combine(updateSource, "mod.json"));
using (var profileUpdate = profileLibrary.PrepareUpdate(profileA, updateSource)) profileA = profileLibrary.CommitUpdate(profileUpdate);
Check(profileLibrary.ReviewProfile(daytimeProfile.Id).Rows.Any(r => r.Folder == profileA.Folder && r.SavedVersion == "1.0" && r.CurrentVersion == "2.0" && r.Note.Contains("Version changed")),
    "profile review identifies saved versus current mod versions before switching");
Directory.Move(profileB.Folder, Path.Combine(root, "RemovedProfileModB"));
var missingProfile = profileLibrary.ReviewProfile(daytimeProfile.Id);
Check(missingProfile.Rows.Any(r => r.Folder == null && r.Name == "Mod B" && r.CurrentVersion == "Missing"),
    "profile review lists a missing saved mod without silently replacing it");
profileLibrary.SwitchProfile(missingProfile);
Check(profileLibrary.Mods().Count == 2 && profileLibrary.Mods()[0].Folder == profileA.Folder,
    "explicitly reviewed incomplete profile loads available entries in saved order");
profileB = profileLibrary.Import(zip);
Check(profileLibrary.ReviewProfile(daytimeProfile.Id).Rows.Any(r => r.Folder == profileB.Folder && r.Note.Contains("Resolved reimport")),
    "profile resolves a reimport by unambiguous mod ID");
var staleProfile = profileLibrary.ReviewProfile(daytimeProfile.Id); profileB.SetEnabled(!profileB.Enabled);
Throws(() => profileLibrary.SwitchProfile(staleProfile), "changed library selection invalidates a profile switch review");
var editedProfile = profileLibrary.ReviewProfile(daytimeProfile.Id); profileLibrary.SaveProfile("Daytime", true);
Throws(() => profileLibrary.SwitchProfile(editedProfile), "overwritten saved profile invalidates a switch review");
// Force an enabled-marker failure after another marker changes.
profileA.SetEnabled(false); profileB.SetEnabled(true);
profileLibrary.SetOrder([profileB, profileA, extraProfileMod]);
var failureTarget = profileLibrary.SaveProfile("Failure target");
profileA.SetEnabled(true); profileB.SetEnabled(false);
profileLibrary.SetOrder([profileA, profileB, extraProfileMod]);
string beforeFailedSwitch = File.ReadAllText(Path.Combine(profileLibrary.Root, "order.json"));
using (var markerLock = new FileStream(Path.Combine(profileA.Folder, "enabled"), FileMode.Open, FileAccess.Read, FileShare.Read))
    Throws(() => profileLibrary.SwitchProfile(profileLibrary.ReviewProfile(failureTarget.Id)), "locked enabled marker safely fails a partial profile switch");
Check(profileA.Enabled && !profileB.Enabled && File.ReadAllText(Path.Combine(profileLibrary.Root, "order.json")) == beforeFailedSwitch
    && !File.Exists(Path.Combine(profileLibrary.Root, ".profile-switch.json")), "failed profile switch restores prior markers and order");
using (var orderLock = new FileStream(Path.Combine(profileLibrary.Root, "order.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
    Throws(() => profileLibrary.SwitchProfile(profileLibrary.ReviewProfile(failureTarget.Id)), "locked order file rolls back changed enabled markers");
Check(profileA.Enabled && !profileB.Enabled && File.ReadAllText(Path.Combine(profileLibrary.Root, "order.json")) == beforeFailedSwitch
    && !File.Exists(Path.Combine(profileLibrary.Root, ".profile-switch.json")), "failed order publication leaves exact prior selection and order intact");
Storage.Save(Path.Combine(profileLibrary.Root, ".profile-switch.json"), new
{
    Before = new[] { new ProfileSelection(Path.GetFileName(profileA.Folder), true), new ProfileSelection(Path.GetFileName(profileB.Folder), false), new ProfileSelection(Path.GetFileName(extraProfileMod.Folder), false) },
    Order = beforeFailedSwitch
});
profileA.SetEnabled(false); profileB.SetEnabled(true); profileLibrary.SetOrder([profileA, profileB, extraProfileMod]);
var afterCrashProfile = new Library(profileLibrary.Root);
Check(profileA.Enabled && !profileB.Enabled && File.ReadAllText(Path.Combine(profileLibrary.Root, "order.json")) == beforeFailedSwitch
    && !File.Exists(Path.Combine(profileLibrary.Root, ".profile-switch.json")), "library reopen recovers interrupted profile selection and order");
Check(Crypto.Checksum(adoptCore) == interruptedHash && File.ReadAllText(adoptedState) == inspectedState &&
    inspectedHistory.All(p => Crypto.Checksum(p.Key) == p.Value), "saving and switching named profiles preserves game archives, installation baseline state and restore history");
Check(!Directory.GetDirectories(profileLibrary.Root, "backups", SearchOption.AllDirectories).Any()
    && !Directory.GetDirectories(profileLibrary.Root, "profiles", SearchOption.AllDirectories).Any(), "named profiles store shared-library references without duplicating installation backups");
File.WriteAllText(Path.Combine(root, "results.txt"), $"{passed} checks passed. Actual installed game files were opened read-only.\n");
Console.WriteLine($"\n{passed} checks passed. Results: {root}");
