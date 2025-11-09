# JellyTV - Jellyfin TV Client for Linux

A modern, TV-optimized Jellyfin client built with C# and AvaloniaUI, designed for 10-foot viewing experiences on Linux PCs.

## Features

### Current Implementation (v0.1)
- ✅ **TV-Optimized UI**: Large fonts (24pt+), high contrast, optimized for 1920x1080 displays
- ✅ **Dark Theme**: Light text on dark background for comfortable viewing
- ✅ **Side Navigation**: Easy-to-navigate menu (Home, Movies, TV Shows, Music, Search, Settings)
- ✅ **Jellyfin Authentication**: Connect to any Jellyfin server
- ✅ **Home Screen**: Continue Watching and Recently Added sections
- ✅ **Focus Indicators**: Clear visual feedback for keyboard navigation
- ✅ **MVVM Architecture**: Clean separation of concerns

### Planned Features
- 🔲 Media browsing with poster images from Jellyfin
- 🔲 Detail views with metadata (cast, ratings, descriptions)
- 🔲 Video player with LibVLC (hardware acceleration support)
- 🔲 Full keyboard navigation (Arrow keys, Enter, Escape)
- 🔲 Remote control support (LIRC/CEC)
- 🔲 Search functionality
- 🔲 Multiple user profiles
- 🔲 Resume playback tracking
- 🔲 Library filtering and sorting

## Technology Stack

- **Framework**: AvaloniaUI 11.3.8 (cross-platform XAML-based UI)
- **Runtime**: .NET 8.0
- **Architecture**: MVVM with CommunityToolkit.Mvvm
- **Video Player**: LibVLCSharp (planned)
- **API Client**: Custom HTTP client for Jellyfin REST API

## Design Principles

### 10-Foot UI Guidelines
- **Minimum font size**: 24pt (readable from 10 feet away)
- **Target resolution**: 1920x1080 (Full HD)
- **Color scheme**: High contrast with dark background (#0F0F0F)
- **Button size**: Minimum 60px height for easy targeting
- **Focus indicators**: 4px bright borders (#00D9FF) with background changes
- **Layout**: Horizontal rows for categories, vertical scrolling through sections

### Navigation Pattern
```
┌─────────────┬──────────────────────────────────┐
│             │                                  │
│  JellyTV    │     Continue Watching            │
│             │  ┌──────┬──────┬──────┬──────┐   │
│  • Home     │  │ Item │ Item │ Item │ Item │   │
│  • Movies   │  └──────┴──────┴──────┴──────┘   │
│  • TV Shows │                                  │
│  • Music    │     Recently Added               │
│  • Search   │  ┌─────┬─────┬─────┬─────┐      │
│  • Settings │  │Item │Item │Item │Item │      │
│             │  └─────┴─────┴─────┴─────┘      │
└─────────────┴──────────────────────────────────┘
```

## Project Structure

```
JellyTV/
├── Models/
│   └── JellyfinModels.cs          # DTOs for Jellyfin API
├── Services/
│   └── JellyfinClient.cs          # HTTP client for Jellyfin API
├── ViewModels/
│   ├── ViewModelBase.cs
│   └── MainWindowViewModel.cs     # Main screen logic
├── Views/
│   ├── MainWindow.axaml           # Main UI layout
│   └── MainWindow.axaml.cs
├── App.axaml                      # Application entry point
├── Program.cs
└── JellyTV.csproj
```

## Getting Started

### Prerequisites
- .NET 8.0 SDK
- Linux with X11 or Wayland
- A running Jellyfin server

### Build and Run

```bash
cd JellyTV
dotnet restore
dotnet build
dotnet run
```

### First Run

1. Enter your Jellyfin server URL (e.g., `http://192.168.1.100:8096`)
2. Enter your username and password
3. Click "Connect"
4. Browse your media!

## Keyboard Controls (Current)

- **Tab**: Navigate between UI elements
- **Enter**: Activate selected button
- **Arrow Keys**: Navigate lists (partial support)

## API Integration

The app uses the Jellyfin REST API with the following key endpoints:

- `/System/Info/Public` - Server information
- `/Users/AuthenticateByName` - User authentication
- `/Users/{userId}/Items/Resume` - Continue watching items
- `/Users/{userId}/Items/Latest` - Recently added media
- `/Users/{userId}/Views` - Library views (Movies, TV Shows, etc.)
- `/Items/{itemId}/Images/{imageType}` - Poster/backdrop images
- `/Videos/{itemId}/stream` - Video streaming

## Development Roadmap

### Phase 1: Foundation ✅ (COMPLETED)
- [x] Project setup with AvaloniaUI
- [x] Jellyfin API client
- [x] Authentication
- [x] Basic TV UI layout
- [x] Home screen with Continue Watching and Recently Added

### Phase 2: Media Browsing (NEXT)
- [ ] Library browser (Movies, TV Shows, Music)
- [ ] Grid view with poster images
- [ ] Detail view for selected media
- [ ] Metadata display (cast, ratings, description)
- [ ] Episode list for TV shows

### Phase 3: Playback
- [ ] LibVLCSharp integration
- [ ] Video player view
- [ ] Playback controls overlay
- [ ] Hardware acceleration
- [ ] Subtitle support
- [ ] Audio track selection

### Phase 4: Navigation & Polish
- [ ] Complete keyboard navigation
- [ ] Focus management system
- [ ] Remote control support
- [ ] Settings persistence
- [ ] Search functionality
- [ ] Performance optimizations

## Contributing

This is a personal project to create a TV-style media center experience on Linux. Feel free to fork and customize!

## License

MIT License - See LICENSE file

## Acknowledgments

- **Jellyfin Team**: For the excellent open-source media server
- **AvaloniaUI Team**: For the cross-platform UI framework
- **LibVLC**: For video playback capabilities
