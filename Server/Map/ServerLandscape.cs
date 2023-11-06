﻿using System.Text;
using CentrED.Network;
using CentrED.Utility;

namespace CentrED.Server.Map;

public sealed partial class ServerLandscape : BaseLandscape, IDisposable {
    private CEDServer _cedServer;
    private Logger _logger;
    
    public ServerLandscape(CEDServer cedServer, string mapPath, string staticsPath, string staidxPath, string tileDataPath, string radarcolPath,
        ushort width, ushort height, out bool valid) : base(width, height) {
        _cedServer = cedServer;
        _logger = cedServer._logger;
        
        _logger.LogInfo("Loading Map");
        if (!File.Exists(mapPath)) {
            Console.WriteLine("Map file not found, do you want to create it? [y/n]");
            if (Console.ReadLine() == "y") {
                InitMap(mapPath);
            }
        }
        _map = File.Open(mapPath, FileMode.Open, FileAccess.ReadWrite);
        _mapReader = new BinaryReader(_map, Encoding.UTF8);
        _mapWriter = new BinaryWriter(_map, Encoding.UTF8);
        var fi = new FileInfo(mapPath);
        IsUop = fi.Extension == ".uop";
        if (IsUop) {
            string uopPattern = fi.Name.Replace(fi.Extension, "").ToLowerInvariant();
            ReadUopFiles(uopPattern);
        }

        _logger.LogInfo($"Loaded {fi.Name}");
        if (!File.Exists(staticsPath) || !File.Exists(staidxPath)) {
            Console.WriteLine("Statics files not found, do you want to create it? [y/n]");
            if (Console.ReadLine() == "y") {
                InitStatics(staticsPath, staidxPath);
            }
        }
        _logger.LogInfo("Loading Statics");
        _statics = File.Open(staticsPath, FileMode.Open, FileAccess.ReadWrite);
        _logger.LogInfo("Loading StaIdx");
        _staidx = File.Open(staidxPath, FileMode.Open, FileAccess.ReadWrite);
        _staticsReader = new BinaryReader(_statics, Encoding.UTF8);
        _staticsWriter = new BinaryWriter(_statics, Encoding.UTF8);
        _staidxReader = new BinaryReader(_staidx, Encoding.UTF8);
        _staidxWriter = new BinaryWriter(_staidx, Encoding.UTF8);
        
        valid = Validate();
        if (valid) {
            _logger.LogInfo("Loading Tiledata");
            TileDataProvider = new TileDataProvider(tileDataPath, true);
            _logger.LogInfo("Creating Cache");
            BlockUnloaded += OnRemovedCachedObject;

            _logger.LogInfo("Creating RadarMap");
            _radarMap = new RadarMap(this, _mapReader, _staidxReader, _staticsReader, radarcolPath);
            PacketHandlers.RegisterPacketHandler(0x06, 8, OnDrawMapPacket);
            PacketHandlers.RegisterPacketHandler(0x07, 10, OnInsertStaticPacket);
            PacketHandlers.RegisterPacketHandler(0x08, 10, OnDeleteStaticPacket);
            PacketHandlers.RegisterPacketHandler(0x09, 11, OnElevateStaticPacket);
            PacketHandlers.RegisterPacketHandler(0x0A, 14, OnMoveStaticPacket);
            PacketHandlers.RegisterPacketHandler(0x0B, 12, OnHueStaticPacket);
            PacketHandlers.RegisterPacketHandler(0x0E, 0, OnLargeScaleCommandPacket);
        }
    }

    private void InitMap(string mapPath) {
        using var mapFile = File.Open(mapPath, FileMode.CreateNew, FileAccess.Write);
        using var writer = new BinaryWriter(mapFile, Encoding.UTF8);
        var emptyBLock = LandBlock.Empty(this);
        writer.Seek(0, SeekOrigin.Begin);
        for (var x = 0; x < Width; x++) {
            for (var y = 0; y < Height; y++) {
                emptyBLock.Write(writer);
            }
        }
    }

    private void InitStatics(string staticsPath, string staidxPath) {
        using var staticsFile = File.Open(staticsPath, FileMode.CreateNew, FileAccess.Write);
        using var staidxFile = File.Open(staidxPath, FileMode.CreateNew, FileAccess.Write);
        using var writer = new BinaryWriter(staidxFile, Encoding.UTF8);
        var emptyIndex = GenericIndex.Empty;
        writer.Seek(0, SeekOrigin.Begin);
        for (var x = 0; x < Width; x++) {
            for (var y = 0; y < Height; y++) {
                emptyIndex.Write(writer);
            }
        }
    }

    ~ServerLandscape() {
        Dispose(false);
    }

    private readonly FileStream _map;
    private readonly FileStream _statics;
    private readonly FileStream _staidx;
    
    private readonly BinaryReader _mapReader;
    private readonly BinaryReader _staticsReader;
    private readonly BinaryReader _staidxReader;
    
    private readonly BinaryWriter _mapWriter;
    private readonly BinaryWriter _staticsWriter;
    private readonly BinaryWriter _staidxWriter;

    private readonly Dictionary<long, HashSet<NetState<CEDServer>>> _blockSubscriptions = new();
    
    public bool IsUop { get; }
    public bool IsMul => !IsUop;
    
    private UopFile[] UopFiles { get; set; } = null!;
    
    public TileDataProvider TileDataProvider { get; } = null!;
    private RadarMap _radarMap = null!;

    private void OnRemovedCachedObject(Block block) {
        if (block.LandBlock.Changed)
            SaveBlock(block.LandBlock);
        if (block.StaticBlock.Changed)
            SaveBlock(block.StaticBlock);
    }
    
    internal void AssertStaticTileId(ushort tileId) {
        if(tileId >= TileDataProvider.StaticTiles.Length) 
            throw new ArgumentException($"Invalid static tile id {tileId}");
    }

    internal void AssertLandTileId(ushort tileId) {
        if(tileId >= TileDataProvider.LandTiles.Length) 
            throw new ArgumentException($"Invalid land tile id {tileId}");
    }

    internal void AssertHue(ushort hue) {
        if(hue > 3000)
            throw new ArgumentException($"Invalid hue {hue}");
    }
    
    public HashSet<NetState<CEDServer>> GetBlockSubscriptions(ushort x, ushort y) {
        AssertBlockCoords(x, y);
        var key = GetBlockNumber(x, y);

        if (_blockSubscriptions.TryGetValue(key, out var subscriptions)) {
            subscriptions.RemoveWhere(ns => !ns.Running);
            return subscriptions;
        }
        
        var result = new HashSet<NetState<CEDServer>>();
        _blockSubscriptions.Add(key, result);
        return result;
    }
    
    public long GetBlockNumber(ushort x, ushort y) {
        return x * Height + y;
    }

    public long GetMapOffset(ushort x, ushort y) {
        long offset = GetBlockNumber(x, y) * 196;
        if (IsUop)
            offset = CalculateOffsetFromUop(offset);
        return offset;
    }

    public long GetStaidxOffset(ushort x, ushort y) {
        return GetBlockNumber(x, y) * 12;
    }

    protected override Block LoadBlock(ushort x, ushort y) {
        AssertBlockCoords(x, y);
        _map.Position = GetMapOffset(x, y);
        var map = new LandBlock(this, x, y, _mapReader);
        
        _staidx.Position = GetStaidxOffset(x, y);
        var index = new GenericIndex(_staidxReader);
        var statics = new StaticBlock(this, _staticsReader, index, x, y);

        var block = new Block(map, statics);
        BlockCache.Add(Block.Id(block), block);
        return block;
    }

    public void UpdateRadar(NetState<CEDServer> ns, ushort x, ushort y) {
        if ((x & 0x7) != 0 || (y & 0x7) != 0) return;

        var landTile = GetLandTile(x, y);
        var landPriority = GetEffectiveAltitude(landTile);
        var radarId = landTile.Id;
        
        var block = GetStaticBlock((ushort)(x / 8), (ushort)(y / 8));
        block.SortTiles(ref TileDataProvider.StaticTiles);
        var topStaticTile = block.AllTiles().MaxBy(tile => tile.PriorityZ);

        if (topStaticTile?.PriorityZ > landPriority)
            radarId = (ushort)(topStaticTile.Id + 0x4000);

        _radarMap.Update(ns, (ushort)(x / 8), (ushort)(y / 8), radarId);
    }

    public sbyte GetLandAlt(ushort x, ushort y) {
        return GetLandTile(x, y).Z;
    }

    public sbyte GetEffectiveAltitude(LandTile tile) {
        var north = tile.Z;
        var west = GetLandAlt(tile.X, (ushort)(tile.Y + 1));
        var south = GetLandAlt((ushort)(tile.X + 1), (ushort)(tile.Y + 1));
        var east = GetLandAlt((ushort)(tile.X + 1), tile.Y);

        if (Math.Abs(north - south) > Math.Abs(west - east)) {
            return (sbyte)(north + south / 2);
        }
        else {
            return (sbyte)((west + east) / 2);
        }
    }
    
    public void Flush() {
        BlockCache.Clear();
        _map.Flush();
        _staidx.Flush();
        _statics.Flush();
    }
    
    public void Backup(string backupDir) {
        foreach (var fs in new[] { _map, _staidx, _statics }) {
            FileInfo fi = new FileInfo(fs.Name);
            Backup(fs, $"{backupDir}/{fi.Name}");
        }
    }

    private void Backup(FileStream file, String backupPath) {
        using var backupStream = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write);
        file.Position = 0;
        file.CopyTo(backupStream);
    }

    public void SaveBlock(LandBlock landBlock) {
        _logger.LogDebug($"Saving mapBlock {landBlock.X},{landBlock.Y}");
        _map.Position = GetMapOffset(landBlock.X, landBlock.Y);
        landBlock.Write(_mapWriter);
        landBlock.Changed = false;
    }

    public void SaveBlock(StaticBlock staticBlock) {
        _logger.LogDebug($"Saving staticBlock {staticBlock.X},{staticBlock.Y}");
        _staidx.Position = GetStaidxOffset(staticBlock.X, staticBlock.Y);
        var index = new GenericIndex(_staidxReader);
        var size = staticBlock.TotalSize;
        if (size > index.Length || index.Lookup <= 0) {
            _statics.Position = _statics.Length;
            index.Lookup = (int)_statics.Position;
        }

        index.Length = size;
        if (size == 0) {
            index.Lookup = -1;
        }
        else {
            _statics.Position = index.Lookup;
            staticBlock.Write(_staticsWriter);
        }

        _staidx.Seek(-12, SeekOrigin.Current);
        index.Write(_staidxWriter);
        staticBlock.Changed = false;
    }

    public long MapLength {
        get {
            if (IsUop)
                return UopFiles.Sum(f => f.Length);
            else {
                return _map.Length;
            }
        }
    }


    private bool Validate() {
        var blocks = Width * Height;
        var mapSize = blocks * LandBlock.SIZE;
        var staidxSize = blocks * GenericIndex.Size;
        var mapFileBlocks = MapLength / LandBlock.SIZE;
        var staidxFileBlocks = _staidx.Length / GenericIndex.Size;

        var valid = true;
        if ((IsMul && MapLength != mapSize) || 
            (IsUop && MapLength < mapSize)) {
            _logger.LogError($"{_map.Name} file doesn't match configured size: {MapLength} != {mapSize}");
            _logger.LogInfo($"{_map.Name} seems to be {MapSizeHint()}");
            valid = false;
        }

        if (IsUop && MapLength > mapSize) {
            var diff = MapLength - mapSize;
            var blocksDiff = diff / LandBlock.SIZE;
            _logger.LogInfo($"{_map.Name} is larger than configured size by {blocksDiff} blocks ({diff} bytes)");
            if (blocksDiff == 1) {
                _logger.LogInfo("This is normal for newer clients.");
            }
            else {
                _logger.LogInfo("Either configuration is wrong or there is something wrong with the uop");
            }
            
        }
        
        if (_staidx.Length != staidxSize) {
            _logger.LogError($"{_staidx.Name} file doesn't match configured size: {_staidx.Length} != {staidxSize}");
            _logger.LogInfo($"{_staidx.Name} seems to be {StaidxSizeHint()}");
            valid = false;
        }

        if ((IsMul && mapFileBlocks != staidxFileBlocks) || 
            (IsUop && mapFileBlocks < staidxFileBlocks)) {
            _logger.LogError(
                $"{_map.Name} file doesn't match {_staidx.Name} file in blocks: {mapFileBlocks} != {staidxFileBlocks} ");
            _logger.LogInfo($"{_map.Name} seems to be {MapSizeHint()}, and staidx seems to be {StaidxSizeHint()}");
            valid = false;
        }
        
        if (IsMul && MapLength + 1 == mapSize) {
            _logger.LogError($"{_map.Name} file is exactly one block larger than configured size");
            _logger.LogInfo("If extracted from UOP, then client version is too new for this UOP extractor");
            var mapPath = _map.Name + ".extrablock";
            _logger.LogInfo($"Backing up map file to {mapPath}");
            Backup(_map, mapPath);
            _logger.LogInfo("Removing excessive map block");
            _map.SetLength(_map.Length - 196);
            valid = Validate();
        }

        return valid;
    }

    private string MapSizeHint() {
        return MapLength switch {
            3_211_264 => "128x128 (map0 Pre-Alpha)",
            77_070_336 => "768x512 (map0,map1 Pre-ML)",
            89_915_392 => "896x512 (map0,map1 Post-ML)",
            11_289_600 => "288x200 (map2)",
            16_056_320 => "320x256 (map3) or 160x512(map5)",
            6_421_156 => "160x512 (map4)",
            _ => "Unknown size"
        };
    }

    private string StaidxSizeHint() {
        return _staidx.Length switch {
            196_608 => "128x128 (map0 Pre-Alpha)",
            4_718_592 => "768x512 (map0,map1 Pre-ML)",
            5_505_024 => "896x512 (map0,map1 Post-ML)",
            691_200 => "288x200 (map2)",
            983_040 => "320x256 (map3) or 160x512(map5)",
            393_132 => "160x512 (map4)",
            _ => "Unknown size"
        };
    }

    private void ReadUopFiles(string pattern) {

        _map.Seek(0, SeekOrigin.Begin);

        if (_mapReader.ReadInt32() != 0x50594D) {
            throw new ArgumentException("Bad UOP file.");
        }

        _mapReader.ReadInt64(); // version + signature
        long nextBlock = _mapReader.ReadInt64();
        _mapReader.ReadInt32(); // block capacity
        int count = _mapReader.ReadInt32();

        UopFiles = new UopFile[count];

        var hashes = new Dictionary<ulong, int>();

        for (int i = 0; i < count; i++) {
            string file = $"build/{pattern}/{i:D8}.dat";
            ulong hash = Uop.HashFileName(file);

            hashes.TryAdd(hash, i);
        }

        _map.Seek(nextBlock, SeekOrigin.Begin);

        do {
            int filesCount = _mapReader.ReadInt32();
            nextBlock = _mapReader.ReadInt64();

            for (int i = 0; i < filesCount; i++) {
                long offset = _mapReader.ReadInt64();
                int headerLength = _mapReader.ReadInt32();
                int compressedLength = _mapReader.ReadInt32();
                int decompressedLength = _mapReader.ReadInt32();
                ulong hash = _mapReader.ReadUInt64();
                _mapReader.ReadUInt32(); // Adler32
                short flag = _mapReader.ReadInt16();

                int length = flag == 1 ? compressedLength : decompressedLength;

                if (offset == 0) {
                    continue;
                }

                if (hashes.TryGetValue(hash, out int idx)) {
                    if (idx < 0 || idx > UopFiles.Length) {
                        throw new IndexOutOfRangeException(
                            "hashes dictionary and files collection have different count of entries!");
                    }

                    UopFiles[idx] = new UopFile(offset + headerLength, length);
                }
                else {
                    throw new ArgumentException(
                        $"File with hash 0x{hash:X8} was not found in hashes dictionary! EA Mythic changed UOP format!");
                }
            }
        } while (_map.Seek(nextBlock, SeekOrigin.Begin) != 0);
    }
    
    private long CalculateOffsetFromUop(long offset)
    {
        long pos = 0;

        foreach (UopFile t in UopFiles)
        {
            var currentPosition = pos + t.Length;

            if (offset < currentPosition)
            {
                return t.Offset + (offset - pos);
            }

            pos = currentPosition;
        }

        return _map.Length;
    }

    private void Dispose(bool disposing) {
        if (disposing) {
            _map.Dispose();
            _statics.Dispose();
            _staidx.Dispose();
            _mapReader.Dispose();
            _staticsReader.Dispose();
            _staidxReader.Dispose();
            _mapWriter.Dispose();
            _staticsWriter.Dispose();
            _staidxWriter.Dispose();
        }
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}