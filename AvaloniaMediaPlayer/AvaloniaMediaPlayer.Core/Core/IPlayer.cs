namespace AvaloniaMediaPlayer.Core;

/// <summary>
/// Main player interface (inspired by Kodi's IPlayer)
/// </summary>
public interface IPlayer : IDisposable
{
    /// <summary>
    /// Player callback for events
    /// </summary>
    IPlayerCallback Callback { get; }

    /// <summary>
    /// Open a media file for playback
    /// </summary>
    Task<bool> OpenFileAsync(string filePath, PlayerOptions? options = null);

    /// <summary>
    /// Close the currently playing file
    /// </summary>
    Task<bool> CloseFileAsync();

    /// <summary>
    /// Check if media is currently playing
    /// </summary>
    bool IsPlaying { get; }

    /// <summary>
    /// Check if player has video stream
    /// </summary>
    bool HasVideo { get; }

    /// <summary>
    /// Check if player has audio stream
    /// </summary>
    bool HasAudio { get; }

    /// <summary>
    /// Check if playback can be paused
    /// </summary>
    bool CanPause { get; }

    /// <summary>
    /// Check if playback can be seeked
    /// </summary>
    bool CanSeek { get; }

    /// <summary>
    /// Pause playback
    /// </summary>
    void Pause();

    /// <summary>
    /// Resume playback
    /// </summary>
    void Resume();

    /// <summary>
    /// Toggle pause/resume
    /// </summary>
    void TogglePause();

    /// <summary>
    /// Seek to a specific time in milliseconds
    /// </summary>
    void SeekTime(long timeMs);

    /// <summary>
    /// Seek to a percentage (0-100)
    /// </summary>
    void SeekPercentage(float percent);

    /// <summary>
    /// Set playback speed (1.0 = normal speed)
    /// </summary>
    void SetSpeed(float speed);

    /// <summary>
    /// Get current playback time in milliseconds
    /// </summary>
    long GetTime();

    /// <summary>
    /// Get total duration in milliseconds
    /// </summary>
    long GetTotalTime();

    /// <summary>
    /// Get current playback percentage (0-100)
    /// </summary>
    float GetPercentage();

    /// <summary>
    /// Set volume (0.0 - 1.0)
    /// </summary>
    void SetVolume(float volume);

    /// <summary>
    /// Set mute state
    /// </summary>
    void SetMute(bool mute);

    // Audio stream selection
    int GetAudioStreamCount();
    int GetAudioStream();
    void SetAudioStream(int index);
    void GetAudioStreamInfo(int index, out AudioStreamInfo info);

    // Video stream selection
    int GetVideoStreamCount();
    int GetVideoStream();
    void SetVideoStream(int index);
    void GetVideoStreamInfo(int index, out VideoStreamInfo info);

    // Subtitle stream selection
    int GetSubtitleCount();
    int GetSubtitle();
    void SetSubtitle(int index);
    void GetSubtitleStreamInfo(int index, out SubtitleStreamInfo info);
    bool GetSubtitleVisible();
    void SetSubtitleVisible(bool visible);
    void AddSubtitle(string subFilePath);

    // Chapter support
    int GetChapterCount();
    int GetChapter();
    void SeekChapter(int chapter);
    string GetChapterName(int chapter);

    /// <summary>
    /// Get player state for resume
    /// </summary>
    string GetPlayerState();

    /// <summary>
    /// Set player state for resume
    /// </summary>
    bool SetPlayerState(string state);
}
