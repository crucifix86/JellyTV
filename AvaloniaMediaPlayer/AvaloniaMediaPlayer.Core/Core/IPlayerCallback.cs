namespace AvaloniaMediaPlayer.Core;

/// <summary>
/// Player callback interface for events (inspired by Kodi's IPlayerCallback)
/// </summary>
public interface IPlayerCallback
{
    /// <summary>
    /// Called when playback is started
    /// </summary>
    void OnPlayBackStarted();

    /// <summary>
    /// Called when playback ends
    /// </summary>
    void OnPlayBackEnded();

    /// <summary>
    /// Called when playback is stopped by user
    /// </summary>
    void OnPlayBackStopped();

    /// <summary>
    /// Called when playback is paused
    /// </summary>
    void OnPlayBackPaused();

    /// <summary>
    /// Called when playback is resumed
    /// </summary>
    void OnPlayBackResumed();

    /// <summary>
    /// Called when player is seeking
    /// </summary>
    void OnPlayBackSeek(long timeMs, long offsetMs);

    /// <summary>
    /// Called when seek is completed
    /// </summary>
    void OnPlayBackSeekChapter(int chapter);

    /// <summary>
    /// Called when playback speed changes
    /// </summary>
    void OnPlayBackSpeedChanged(float speed);

    /// <summary>
    /// Called when an error occurs
    /// </summary>
    void OnPlayBackError(string error);

    /// <summary>
    /// Called when audio stream changes
    /// </summary>
    void OnAVChange();

    /// <summary>
    /// Called when streams are ready
    /// </summary>
    void OnAVStarted();
}
