using System.Collections.Concurrent;

namespace AvaloniaMediaPlayer.Core.Overlay;

/// <summary>
/// Container for managing overlays with PTS synchronization (inspired by Kodi's CDVDOverlayContainer)
/// </summary>
public class DVDOverlayContainer
{
    private readonly List<DVDOverlay> _overlays = new();
    private readonly object _lock = new();

    /// <summary>
    /// Add an overlay to the container
    /// </summary>
    public void Add(DVDOverlay overlay)
    {
        lock (_lock)
        {
            _overlays.Add(overlay);
        }
    }

    /// <summary>
    /// Process and add overlay if valid
    /// </summary>
    public void ProcessAndAddOverlayIfValid(DVDOverlay overlay)
    {
        lock (_lock)
        {
            // Check if this overlay is already contained in another
            bool shouldAdd = true;

            foreach (var existing in _overlays.ToList())
            {
                // If new overlay is completely contained within existing, don't add
                if (overlay.PTSStart >= existing.PTSStart &&
                    overlay.PTSStop <= existing.PTSStop &&
                    overlay.Type == existing.Type)
                {
                    shouldAdd = false;
                    break;
                }

                // If existing overlay is completely contained in new one, remove it
                if (existing.PTSStart >= overlay.PTSStart &&
                    existing.PTSStop <= overlay.PTSStop &&
                    existing.Type == overlay.Type)
                {
                    _overlays.Remove(existing);
                }
            }

            if (shouldAdd)
            {
                _overlays.Add(overlay);
            }
        }
    }

    /// <summary>
    /// Get all overlays that should be displayed at the given PTS
    /// </summary>
    public List<DVDOverlay> GetOverlays(double pts)
    {
        lock (_lock)
        {
            return _overlays
                .Where(o => pts >= o.PTSStart && (o.PTSStop == 0 || pts <= o.PTSStop))
                .ToList();
        }
    }

    /// <summary>
    /// Get all overlays regardless of PTS
    /// </summary>
    public List<DVDOverlay> GetAllOverlays()
    {
        lock (_lock)
        {
            return _overlays.ToList();
        }
    }

    /// <summary>
    /// Check if container has overlays of a specific type
    /// </summary>
    public bool ContainsOverlayType(OverlayType type)
    {
        lock (_lock)
        {
            return _overlays.Any(o => o.Type == type);
        }
    }

    /// <summary>
    /// Clean up overlays that are no longer valid for the given PTS
    /// </summary>
    public void CleanUp(double pts)
    {
        lock (_lock)
        {
            _overlays.RemoveAll(o => o.PTSStop > 0 && o.PTSStop < pts);
        }
    }

    /// <summary>
    /// Flush all flushable overlays
    /// </summary>
    public void Flush()
    {
        lock (_lock)
        {
            _overlays.RemoveAll(o => o.IsFlushable);
        }
    }

    /// <summary>
    /// Clear all overlays
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _overlays.Clear();
        }
    }

    /// <summary>
    /// Get overlay count
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _overlays.Count;
            }
        }
    }
}
