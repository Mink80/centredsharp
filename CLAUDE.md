# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CentrED# is a complete rewrite of the original CentrED map editor for Ultima Online, built with .NET 9 and C#. It's a client-server application that allows collaborative map editing.

## Build Commands

**Prerequisites:** .NET 9 SDK

**Clone with submodules:**
```bash
git clone --recursive https://github.com/kaczy93/centredsharp.git
```

**Build entire solution:**
```bash
dotnet build
```

**Build specific projects:**
```bash
dotnet build Server/Server.csproj
dotnet build CentrED/CentrED.csproj
```

**Publish for release:**
```bash
dotnet publish Server -c Release -o release_Server -p:DebugType=None -p:PublishSingleFile=true --self-contained false
dotnet publish CentrED -c Release -o release_CentrED -p:DebugType=None
```

**Run tests:**
```bash
dotnet test
dotnet test --filter "FullyQualifiedName~Shared.Tests.StaticBlockTest"
```

## Solution Architecture

The solution consists of 4 main projects with clear separation of concerns:

### Shared (centredlib)
Core library containing shared code used by both Server and Client:
- **Network layer:** `Packet`, `PacketHandler`, `NetState<T>`, `Pipe` - binary protocol implementation with compression support
- **Map data structures:** `Block`, `LandBlock`, `StaticBlock`, `LandTile`, `StaticTile`
- **Base landscape:** `BaseLandscape` - abstract base for map management with block caching via `BlockCache`
- **Tile data:** `TileDataProvider`, `TileDataLand`, `TileDataStatic` - Ultima Online tile definitions
- **Buffers:** `SpanReader`, `SpanWriter`, `STArrayPool` - high-performance memory management
- **Utilities:** `Zlib` compression, `Logger`, UOP file handling

### Server (Cedserver)
Standalone map server executable that manages the authoritative map state:
- **CEDServer:** Main server class handling clients, block subscriptions, and configuration
- **ServerLandscape:** Extends `BaseLandscape` to manage map blocks from disk (staidx/statics/map files)
- **Packet handlers:** `ConnectionHandling`, `AdminHandling`, `ClientHandling`, `ServerLandscapePacketHandlers`
- **Configuration:** YAML-based config system for accounts, regions, permissions, and auto-backup
- **Large-scale operations:** Server-side batch operations on map data
- Uses single-threaded architecture (except world saves/serialization)

### Client
Client library for connecting to the CentrED server:
- **CentrEDClient:** Main client class managing connection state and protocol
- **ClientLandscape:** Extends `BaseLandscape` to cache map blocks received from server
- **Packet handlers:** Mirrors server structure for client-side packet processing
- **Undo/Redo:** Stack-based undo system using `Packet[]` arrays
- **Admin interface:** User and region management capabilities

### CentrED
FNA-based GUI application for map editing:
- **CentrEDGame:** Main XNA/FNA game class
- **MapManager:** Core map rendering, camera, tool management
- **MapRenderer:** Renders land, statics, selection buffer, lighting using custom shaders
- **UIManager:** ImGui-based UI system with multiple windows (TilesWindow, OptionsWindow, etc.)
- **Tools:** Modular tool system - DrawTool, SelectTool, MoveTool, ElevateTool, HueTool, etc.
- **Camera:** Isometric camera with zoom/pan
- **Lighting:** Light shader system for dynamic lighting effects
- **Dependencies:** FNA (XNA reimplementation), ImGui.NET, FontStashSharp, ClassicUO.Assets

## Key Architecture Patterns

**Block-based map system:** Maps are divided into 8x8 tile blocks. `BlockCache` manages memory with LRU eviction. Both server and client work with `Block` objects containing `LandBlock` and `StaticBlock`.

**Client-Server protocol:** Custom binary protocol using `PacketHandler` registry pattern. Packets have fixed or dynamic length, compressed with Zlib. `NetState<T>` is generic to work with both `CEDServer` and `CentrEDClient`.

**Tool system:** Tools inherit from `Tool` base class and implement event handlers (`OnMousePressed`, `OnActivated`, etc.). Tools interact with `TileObject` (LandObject, StaticObject) through `MapManager`.

**Unsafe code:** The codebase uses `AllowUnsafeBlocks=true` for performance-critical operations with spans and pointers.

## Testing

Tests are in `Shared.Tests` project using xUnit:
- `SpanReaderTest` - Binary protocol serialization
- `StaticBlockTest` - Static tile deduplication
- `TileRangeTest` - Tile coordinate range iteration
- `ZlibTest` - Compression functionality

## Notable Dependencies

- **FNA (git submodule):** Open-source XNA reimplementation for cross-platform graphics
- **FontStashSharp (git submodule):** Font rendering
- **ImGui.NET (git submodule):** Immediate mode GUI
- **ClassicUO libraries:** Asset loading for Ultima Online data files (arts, texmaps, etc.)
- **SixLabors.ImageSharp:** Image processing
- **GitVersion:** Semantic versioning from git history

## Platform Support

CentrED builds for Windows, Linux, and macOS. Native libraries are copied from `lib/` and `external/fna-libs/` based on platform detection in CentrED.csproj.

## Output Directories

- Build output: `CentrED/output/`
- Publish output: `CentrED/publish/`
