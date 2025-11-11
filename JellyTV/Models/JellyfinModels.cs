using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;

namespace JellyTV.Models;

public class AuthenticationResult
{
    [JsonPropertyName("AccessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("ServerId")]
    public string? ServerId { get; set; }

    [JsonPropertyName("User")]
    public UserDto? User { get; set; }
}

public class UserDto
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("Name")]
    public string? Name { get; set; }
}

public class BaseItemDto : INotifyPropertyChanged
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("Type")]
    public string? Type { get; set; }

    [JsonPropertyName("CollectionType")]
    public string? CollectionType { get; set; }

    [JsonPropertyName("Overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("ProductionYear")]
    public int? ProductionYear { get; set; }

    [JsonPropertyName("RunTimeTicks")]
    public long? RunTimeTicks { get; set; }

    [JsonPropertyName("OfficialRating")]
    public string? OfficialRating { get; set; }

    [JsonPropertyName("CommunityRating")]
    public float? CommunityRating { get; set; }

    [JsonPropertyName("BackdropImageTags")]
    public List<string>? BackdropImageTags { get; set; }

    [JsonPropertyName("ImageTags")]
    public Dictionary<string, string>? ImageTags { get; set; }

    [JsonPropertyName("UserData")]
    public UserItemDataDto? UserData { get; set; }

    [JsonPropertyName("People")]
    public List<PersonInfo>? People { get; set; }

    // Observable property for image bitmap (not from JSON)
    private Bitmap? _imageBitmap;
    [JsonIgnore]
    public Bitmap? ImageBitmap
    {
        get => _imageBitmap;
        set
        {
            if (_imageBitmap != value)
            {
                _imageBitmap = value;
                OnPropertyChanged();
            }
        }
    }

    [JsonIgnore]
    public List<Bitmap>? LibraryPreviewImages { get; set; }

    // Computed property for cast (actors only)
    [JsonIgnore]
    public string? CastList
    {
        get
        {
            if (People == null || People.Count == 0) return null;
            var actors = People.Where(p => p.Type == "Actor").Take(5).Select(p => p.Name);
            return string.Join(", ", actors);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class PersonInfo
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("Role")]
    public string? Role { get; set; }

    [JsonPropertyName("Type")]
    public string? Type { get; set; }

    [JsonIgnore]
    public Bitmap? ImageBitmap { get; set; }
}

public class UserItemDataDto
{
    [JsonPropertyName("PlaybackPositionTicks")]
    public long PlaybackPositionTicks { get; set; }

    [JsonPropertyName("PlayCount")]
    public int PlayCount { get; set; }

    [JsonPropertyName("IsFavorite")]
    public bool IsFavorite { get; set; }

    [JsonPropertyName("Played")]
    public bool Played { get; set; }
}

public class QueryResult<T>
{
    [JsonPropertyName("Items")]
    public List<T>? Items { get; set; }

    [JsonPropertyName("TotalRecordCount")]
    public int TotalRecordCount { get; set; }
}

public class ServerInfo
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("ServerName")]
    public string? ServerName { get; set; }

    [JsonPropertyName("Version")]
    public string? Version { get; set; }

    [JsonPropertyName("LocalAddress")]
    public string? LocalAddress { get; set; }
}
