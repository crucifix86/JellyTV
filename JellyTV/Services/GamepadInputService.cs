using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input;

namespace JellyTV.Services;

public class GamepadInputService : IDisposable
{
    private readonly string _devicePath;
    private FileStream? _deviceStream;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _readTask;

    // Joystick event structure from Linux kernel
    private const int JS_EVENT_BUTTON = 0x01;
    private const int JS_EVENT_AXIS = 0x02;
    private const int JS_EVENT_INIT = 0x80;

    // Xbox controller button mappings
    private const byte BTN_A = 0;
    private const byte BTN_B = 1;
    private const byte BTN_X = 2;
    private const byte BTN_Y = 3;
    private const byte BTN_LB = 4;
    private const byte BTN_RB = 5;
    private const byte BTN_BACK = 6;
    private const byte BTN_START = 7;
    private const byte BTN_XBOX = 8;
    private const byte BTN_LEFT_STICK = 9;
    private const byte BTN_RIGHT_STICK = 10;

    // D-pad is reported as axes on Xbox controllers
    private const byte AXIS_DPAD_X = 6;  // Left/Right
    private const byte AXIS_DPAD_Y = 7;  // Up/Down
    private const byte AXIS_LEFT_X = 0;
    private const byte AXIS_LEFT_Y = 1;

    // Deadzone for analog stick
    private const short DEADZONE = 10000;

    public event Action<Key>? KeyPressed;
    public event Action? SelectPressed;  // A button
    public event Action? BackPressed;    // B button
    public event Action? HomePressed;    // Start button

    public GamepadInputService(string devicePath = "/dev/input/js0")
    {
        _devicePath = devicePath;
    }

    public async Task StartAsync()
    {
        if (_deviceStream != null)
            return;

        try
        {
            _deviceStream = new FileStream(_devicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            _cancellationTokenSource = new CancellationTokenSource();

            Console.WriteLine($"Gamepad connected: {_devicePath}");

            _readTask = Task.Run(() => ReadGamepadInputAsync(_cancellationTokenSource.Token));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to open gamepad device {_devicePath}: {ex.Message}");
        }
    }

    private async Task ReadGamepadInputAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[8];  // Joystick event is 8 bytes

        try
        {
            while (!cancellationToken.IsCancellationRequested && _deviceStream != null)
            {
                int bytesRead = await _deviceStream.ReadAsync(buffer, 0, 8, cancellationToken);

                if (bytesRead != 8)
                    continue;

                // Parse joystick event
                // struct js_event {
                //   __u32 time;     /* event timestamp in milliseconds */
                //   __s16 value;    /* value */
                //   __u8 type;      /* event type */
                //   __u8 number;    /* axis/button number */
                // };

                uint time = BitConverter.ToUInt32(buffer, 0);
                short value = BitConverter.ToInt16(buffer, 4);
                byte type = buffer[6];
                byte number = buffer[7];

                // Ignore init events
                if ((type & JS_EVENT_INIT) != 0)
                    continue;

                if ((type & JS_EVENT_BUTTON) != 0)
                {
                    HandleButtonEvent(number, value == 1);
                }
                else if ((type & JS_EVENT_AXIS) != 0)
                {
                    HandleAxisEvent(number, value);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading gamepad input: {ex.Message}");
        }
    }

    private void HandleButtonEvent(byte button, bool pressed)
    {
        if (!pressed)
            return;  // Only handle button press, not release

        Console.WriteLine($"Button pressed: {button}");

        switch (button)
        {
            case BTN_A:
                // A button = Select/OK (Android TV standard)
                SelectPressed?.Invoke();
                break;
            case BTN_B:
                // B button = Back (Android TV standard)
                BackPressed?.Invoke();
                break;
            case BTN_X:
                // X button = Context Menu (Kodi standard)
                // Could be used for additional options/context menu
                break;
            case BTN_Y:
                // Y button = Info/Details (common in media players)
                // Could be used for showing more info
                break;
            case BTN_BACK:
                // Back/Select button = Back
                BackPressed?.Invoke();
                break;
            case BTN_START:
                // Start button = Home/Menu (Android TV standard)
                HomePressed?.Invoke();
                break;
            case BTN_LB:
            case BTN_RB:
                // Shoulder buttons could be used for paging through content
                // or changing tabs
                break;
        }
    }

    private DateTime _lastAxisEvent = DateTime.MinValue;
    private const int AXIS_REPEAT_DELAY_MS = 150;  // Delay between repeated navigation

    private void HandleAxisEvent(byte axis, short value)
    {
        Console.WriteLine($"RAW AXIS EVENT: axis={axis}, value={value}");

        // Throttle axis events to prevent too rapid navigation
        var now = DateTime.Now;
        if ((now - _lastAxisEvent).TotalMilliseconds < AXIS_REPEAT_DELAY_MS)
        {
            Console.WriteLine($"Axis event throttled (too soon after last event)");
            return;
        }

        Key? key = null;

        switch (axis)
        {
            case AXIS_DPAD_X:
                if (value < -16000)
                {
                    key = Key.Left;
                    _lastAxisEvent = now;
                }
                else if (value > 16000)
                {
                    key = Key.Right;
                    _lastAxisEvent = now;
                }
                break;

            case AXIS_DPAD_Y:
                if (value < -16000)
                {
                    key = Key.Up;
                    _lastAxisEvent = now;
                }
                else if (value > 16000)
                {
                    key = Key.Down;
                    _lastAxisEvent = now;
                }
                break;

            case AXIS_LEFT_X:
                if (value < -DEADZONE)
                {
                    key = Key.Left;
                    _lastAxisEvent = now;
                }
                else if (value > DEADZONE)
                {
                    key = Key.Right;
                    _lastAxisEvent = now;
                }
                break;

            case AXIS_LEFT_Y:
                if (value < -DEADZONE)
                {
                    key = Key.Up;
                    _lastAxisEvent = now;
                }
                else if (value > DEADZONE)
                {
                    key = Key.Down;
                    _lastAxisEvent = now;
                }
                break;
        }

        if (key.HasValue)
        {
            Console.WriteLine($"Axis navigation: {key.Value}");
            KeyPressed?.Invoke(key.Value);
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _readTask?.Wait(1000);
        _deviceStream?.Dispose();
        _cancellationTokenSource?.Dispose();

        Console.WriteLine("Gamepad disconnected");
    }
}
