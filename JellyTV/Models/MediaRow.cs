using System.Collections.ObjectModel;

namespace JellyTV.Models;

public class MediaRow
{
    public string? Title { get; set; }
    public ObservableCollection<BaseItemDto> Items { get; set; } = new();
}
