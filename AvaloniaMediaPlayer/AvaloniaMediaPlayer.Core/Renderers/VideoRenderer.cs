using AvaloniaMediaPlayer.Core.Codecs;
using AvaloniaMediaPlayer.Core.Overlay;
using System.Collections.Concurrent;

namespace AvaloniaMediaPlayer.Core.Renderers;

/// <summary>
/// Video renderer for managing video frames and overlays (inspired by Kodi's CRenderManager)
/// </summary>
public class VideoRenderer
{
    private readonly ConcurrentQueue<VideoFrame> _frameQueue = new();
    private VideoFrame? _currentFrame;
    private readonly object _renderLock = new();
    private readonly DVDOverlayContainer _overlayContainer;
    private PlaybackStatistics? _statistics;

    public VideoRenderer(DVDOverlayContainer overlayContainer)
    {
        _overlayContainer = overlayContainer;
    }

    public void SetStatistics(PlaybackStatistics statistics)
    {
        _statistics = statistics;
    }

    /// <summary>
    /// Add a decoded video frame to the render queue
    /// </summary>
    public void QueueFrame(VideoFrame frame)
    {
        _frameQueue.Enqueue(frame);

        // Limit queue size to prevent memory issues
        // Return dropped frames to buffer pool
        while (_frameQueue.Count > 10)
        {
            if (_frameQueue.TryDequeue(out var droppedFrame))
            {
                _statistics?.RecordVideoFrameDropped();
                droppedFrame.ReturnBuffer();
            }
        }
    }

    /// <summary>
    /// Get the next frame to render based on current PTS (in milliseconds)
    /// Uses Kodi-style sync logic with thresholds
    /// </summary>
    public VideoFrame? GetNextFrame(long currentTimeMs)
    {
        lock (_renderLock)
        {
            // Tighter sync threshold for better A/V sync - within 20ms is considered "on time"
            // Reduced from 40ms to 20ms for more accurate sync
            const long SYNC_THRESHOLD_MS = 20;

            // Check if we should get a new frame
            while (_frameQueue.TryPeek(out var nextFrame))
            {
                long framePTS = nextFrame.PTS;  // Already in milliseconds
                long diff = framePTS - currentTimeMs;

                // If frame is too early (PTS > clock + threshold), wait
                if (diff > SYNC_THRESHOLD_MS)
                {
                    break;
                }
                // If frame is late or on time (PTS <= clock + threshold), use it
                else
                {
                    var oldFrame = _currentFrame;
                    _frameQueue.TryDequeue(out _currentFrame);

                    // If frame is significantly late (more than threshold behind),
                    // continue loop to potentially drop it and get next frame
                    if (diff < -SYNC_THRESHOLD_MS)
                    {
                        _statistics?.RecordVideoFrameDropped();
                        _currentFrame?.ReturnBuffer(); // Return late frame to pool
                        continue;  // Frame is late, drop it and check next
                    }
                    else
                    {
                        oldFrame?.ReturnBuffer(); // Return previous frame to pool
                        break;  // Frame is on time, use it
                    }
                }
            }

            return _currentFrame;
        }
    }

    /// <summary>
    /// Get overlays for current PTS
    /// </summary>
    public List<DVDOverlay> GetOverlays(double currentPTS)
    {
        return _overlayContainer.GetOverlays(currentPTS);
    }

    /// <summary>
    /// Clear all queued frames
    /// </summary>
    public void Flush()
    {
        lock (_renderLock)
        {
            // Return all queued frames to buffer pool
            while (_frameQueue.TryDequeue(out var frame))
            {
                frame.ReturnBuffer();
            }

            // Return current frame to pool
            _currentFrame?.ReturnBuffer();
            _currentFrame = null;
        }
    }

    /// <summary>
    /// Get current frame without advancing
    /// </summary>
    public VideoFrame? GetCurrentFrame()
    {
        lock (_renderLock)
        {
            return _currentFrame;
        }
    }

    public int QueuedFrameCount => _frameQueue.Count;
}
