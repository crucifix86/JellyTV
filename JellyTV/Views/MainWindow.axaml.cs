using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using JellyTV.Services;
using JellyTV.ViewModels;
using JellyTV.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace JellyTV.Views;

public partial class MainWindow : Window
{
    private GamepadInputService? _gamepadService;
    private TextBox? _keyboardTargetTextBox;
    private VideoPlayerControl? _videoPlayerControl;
    private readonly AppLauncherService _appLauncher = new();
    private readonly UpdaterService _updater = new();
    private readonly BluetoothService _bluetooth = new();
    private bool _bluetoothBusy;
    private readonly WiFiService _wifi = new();
    private bool _wifiBusy;
    private readonly SshService _ssh = new();
    private bool _sshBusy;
    private readonly InstallerService _installer = new();
    private InstallTarget? _selectedInstallTarget;
    private bool _installRunning;
    private TaskCompletionSource<string?>? _keyboardPromptTcs;

    /// <summary>
    /// True when JellyTV is running on the appliance image (cage kiosk).
    /// jellytv-launch sets JELLYTV_APPLIANCE=1 there. Used to hide UI
    /// elements that only make sense on a desktop dev install.
    /// </summary>
    private static bool IsApplianceMode() =>
        Environment.GetEnvironmentVariable("JELLYTV_APPLIANCE") == "1";

    public MainWindow()
    {
        InitializeComponent();

        // Get reference to the VideoPlayerControl
        _videoPlayerControl = this.FindControl<VideoPlayerControl>("VideoPlayer");

        // Wire up mouse movement to show cursor and detect left edge
        this.PointerMoved += (s, e) =>
        {
            ShowCursor();

            // Show sidebar when mouse is near left edge
            var position = e.GetPosition(this);
            if (position.X < 50)
            {
                ShowSidebar();
            }
        };

        // TV remote keys (Bluetooth/IR receivers surface as keyboard events).
        // Routes to the same handlers the gamepad uses so behavior stays in
        // one place. Avalonia handles arrow/Enter focus nav for free — we
        // only intercept the keys it doesn't already cover.
        AddHandler(KeyDownEvent, OnRemoteKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);

        // On Wayland the window can be "activated" (compositor focuses it)
        // without any inner element holding keyboard focus — keys then go to
        // the window with nothing to navigate. Force focus to a sensible
        // default whenever we get reactivated.
        Activated += (s, e) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (FocusManager?.GetFocusedElement() is Control existing && existing != this)
                {
                    return; // Something already has focus.
                }
                var firstButton = GetAllFocusableButtons(this)
                    .FirstOrDefault(b => b.IsVisible && b.IsEffectivelyVisible);
                firstButton?.Focus();
                if (firstButton != null)
                {
                    Console.WriteLine($"Activated → focused {firstButton.Name ?? firstButton.GetType().Name}");
                }
            });
        };

        // Setup on-screen keyboard for login textboxes
        SetupOnScreenKeyboard();

        // Auto-focus first field on login screen
        this.Opened += (s, e) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (DataContext is ViewModels.MainWindowViewModel viewModel && !viewModel.IsAuthenticated)
                {
                    var serverAddressBox = this.FindControl<TextBox>("ServerAddressTextBox");
                    serverAddressBox?.Focus();
                    Console.WriteLine("Auto-focused ServerAddressTextBox");
                    // Hide cursor after auto-focus
                    HideCursor();
                }
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        };

        // Wire up scroll buttons
        var scrollLeftButton = this.FindControl<Button>("ScrollLeftButton");
        var scrollRightButton = this.FindControl<Button>("ScrollRightButton");
        var scrollViewer = this.FindControl<ScrollViewer>("LibraryScrollViewer");

        if (scrollLeftButton != null && scrollViewer != null)
        {
            scrollLeftButton.Click += (s, e) =>
            {
                scrollViewer.Offset = scrollViewer.Offset.WithX(scrollViewer.Offset.X - 300);
            };
        }

        if (scrollRightButton != null && scrollViewer != null)
        {
            scrollRightButton.Click += (s, e) =>
            {
                scrollViewer.Offset = scrollViewer.Offset.WithX(scrollViewer.Offset.X + 300);
            };
        }

        // Initialize gamepad support
        InitializeGamepadAsync();

        // Wire up settings UI controls
        this.Loaded += OnLoaded;

        // VLC player control no longer needed for memory rendering

        // Watch for detail view changes to auto-focus Play button
        this.DataContextChanged += (s, e) =>
        {
            if (DataContext is ViewModels.MainWindowViewModel viewModel)
            {
                // Wire up the VideoPlayerControl to the ViewModel
                viewModel.PlayVideoAction = (url) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        Console.WriteLine($"Playing video via VideoPlayerControl: {url}");

                        // Make sure VideoPlayerControl is visible
                        if (_videoPlayerControl != null)
                        {
                            _videoPlayerControl.IsVisible = true;
                            Console.WriteLine("Set VideoPlayerControl visible");
                        }

                        _videoPlayerControl?.OpenFile(url);

                        // Force focus to the VideoPlayerControl so gamepad input goes there
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            _videoPlayerControl?.Focus();
                            Console.WriteLine("Forced focus to VideoPlayerControl");
                        }, Avalonia.Threading.DispatcherPriority.Loaded);
                    });
                };

                // Wire up playback control actions
                viewModel.TogglePlayPauseAction = () =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _videoPlayerControl?.TogglePause();
                    });
                };

                viewModel.StopPlaybackAction = () =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _videoPlayerControl?.Stop();
                    });
                };

                viewModel.SeekAction = (seconds) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _videoPlayerControl?.Seek(seconds);
                    });
                };

                viewModel.GetPositionFunc = () => _videoPlayerControl?.GetPosition() ?? 0;
                viewModel.GetDurationFunc = () => _videoPlayerControl?.GetDuration() ?? 0;

                viewModel.PropertyChanged += (sender, args) =>
                {
                    if (args.PropertyName == nameof(viewModel.ShowDetailView) && viewModel.ShowDetailView)
                    {
                        // Detail view opened - focus the Play button after a small delay to ensure UI is ready
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            var playButton = this.FindControl<Button>("PlayButton");
                            if (playButton != null)
                            {
                                playButton.Focus();
                                Console.WriteLine("Auto-focused Play button in detail view");
                            }
                        }, Avalonia.Threading.DispatcherPriority.Loaded);
                    }
                };

                // Watch for episodes collection changes - only focus when episodes are FIRST loaded
                bool episodesFocused = false;
                viewModel.Episodes.CollectionChanged += async (sender, args) =>
                {
                    // Only run once when first episode is added
                    if (!episodesFocused && viewModel.Episodes.Count > 0 && args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                    {
                        episodesFocused = true;

                        // Wait for UI to settle
                        await System.Threading.Tasks.Task.Delay(200);

                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            var allButtons = GetAllFocusableButtons(this);
                            foreach (var btn in allButtons)
                            {
                                if (btn.DataContext is Models.BaseItemDto item && item.Type == "Episode")
                                {
                                    // Scroll the button into view first
                                    btn.BringIntoView();
                                    // Then focus it
                                    var result = btn.Focus();
                                    Console.WriteLine($"Auto-focused first episode: {item.Name}, Focus success: {result}");
                                    return;
                                }
                            }
                        }, Avalonia.Threading.DispatcherPriority.Background);
                    }
                };

                // Reset the episodesFocused flag when Seasons changes (new show selected)
                viewModel.Seasons.CollectionChanged += (sender, args) =>
                {
                    if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                    {
                        episodesFocused = false;
                    }
                };
            }
        };
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Wire up LaunchOnStartupButton
        var launchOnStartupButton = this.FindControl<Button>("LaunchOnStartupButton");
        var launchOnStartupStatus = this.FindControl<TextBlock>("LaunchOnStartupStatus");

        if (launchOnStartupButton != null && launchOnStartupStatus != null)
        {
            // Check if autostart file exists and set status accordingly
            var autostartDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart");
            var autostartFile = Path.Combine(autostartDir, "jellytv.desktop");
            launchOnStartupStatus.Text = File.Exists(autostartFile) ? "[X]" : "[ ]";

            // Wire up button click event
            launchOnStartupButton.Click -= OnLaunchOnStartupButtonClick;
            launchOnStartupButton.Click += OnLaunchOnStartupButtonClick;
        }
    }

    private void OnShowDesktopButtonClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Show Desktop button clicked");
        try
        {
            // Try wmctrl first
            var processInfo = new ProcessStartInfo
            {
                FileName = "wmctrl",
                Arguments = "-k on",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(processInfo);
            Console.WriteLine("Minimized window using wmctrl");
        }
        catch
        {
            // Fallback to xdotool
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "xdotool",
                    Arguments = "key Super_L+d",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(processInfo);
                Console.WriteLine("Showed desktop using xdotool");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to minimize/show desktop: {ex.Message}");
            }
        }
    }

    private void OnLaunchOnStartupButtonClick(object? sender, RoutedEventArgs e)
    {
        var launchOnStartupStatus = this.FindControl<TextBlock>("LaunchOnStartupStatus");
        if (launchOnStartupStatus == null) return;

        var autostartDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart");
        var autostartFile = Path.Combine(autostartDir, "jellytv.desktop");

        // Toggle autostart state
        bool isCurrentlyEnabled = File.Exists(autostartFile);

        if (isCurrentlyEnabled)
        {
            // Remove autostart file
            try
            {
                File.Delete(autostartFile);
                launchOnStartupStatus.Text = "[ ]";
                Console.WriteLine($"Deleted autostart file: {autostartFile}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to delete autostart file: {ex.Message}");
            }
        }
        else
        {
            // Create autostart file
            try
            {
                Directory.CreateDirectory(autostartDir);

                var desktopEntry = @"[Desktop Entry]
Type=Application
Name=JellyTV
Comment=Jellyfin TV Client
Exec=/usr/local/bin/jellytv
Icon=jellytv
Terminal=false
Categories=AudioVideo;Video;Player;TV;
StartupNotify=false";

                File.WriteAllText(autostartFile, desktopEntry);
                launchOnStartupStatus.Text = "[X]";
                Console.WriteLine($"Created autostart file: {autostartFile}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create autostart file: {ex.Message}");
            }
        }
    }

    private async void InitializeGamepadAsync()
    {
        _gamepadService = new GamepadInputService();

        // Wire up gamepad events for direct UI control
        _gamepadService.KeyPressed += (key) =>
        {
            Console.WriteLine($"Gamepad navigation: {key}");
            Avalonia.Threading.Dispatcher.UIThread.Post(() => HandleGamepadNavigation(key));
        };

        _gamepadService.SelectPressed += () =>
        {
            Console.WriteLine("Gamepad: Select (A) pressed");
            Avalonia.Threading.Dispatcher.UIThread.Post(() => HandleGamepadSelect());
        };

        _gamepadService.BackPressed += () =>
        {
            Console.WriteLine("Gamepad: Back (B) pressed");
            Avalonia.Threading.Dispatcher.UIThread.Post(() => HandleGamepadBack());
        };

        _gamepadService.HomePressed += () =>
        {
            Console.WriteLine("Gamepad: Home (Start) pressed");
            Avalonia.Threading.Dispatcher.UIThread.Post(() => HandleGamepadHome());
        };

        _gamepadService.SidebarTogglePressed += () =>
        {
            Console.WriteLine("Gamepad: Sidebar Toggle (R1) pressed");
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnSidebarToggle());
        };

        await _gamepadService.StartAsync();
    }

    private void OnRemoteKeyDown(object? sender, KeyEventArgs e)
    {
        // Don't hijack keys when a text field is being typed into (login screen, etc.).
        // The on-screen keyboard surfaces its own focus, so this only matters for
        // physical/remote keyboards driving a TextBox directly.
        if (FocusManager?.GetFocusedElement() is TextBox tb)
        {
            // Escape always passes through (back-out mid-type).
            // Left arrow at the start of the textbox also passes through so the
            // "Left at edge opens sidebar" gesture works from the login screen,
            // where the only focusable elements are TextBoxes.
            var passThrough = e.Key == Key.Escape
                              || (e.Key == Key.Left && tb.CaretIndex == 0);
            if (!passThrough) return;
        }

        // If something downstream already handled it, don't re-act.
        if (e.Handled) return;

        // Route arrow keys through the same nav logic the gamepad uses.
        // Avalonia's built-in focus nav is fragile when nothing has focus yet
        // (window focused but no inner element), so use the spatial+default-focus
        // logic in HandleGamepadNavigation instead. Set Handled=True to stop
        // Avalonia's default focus nav from also firing on the same press.
        switch (e.Key)
        {
            case Key.Up:
            case Key.Down:
            case Key.Left:
            case Key.Right:
                HandleGamepadNavigation(e.Key);
                e.Handled = true;
                return;
        }

        // Enter/OK on the remote → invoke gamepad-select handler (same as A button).
        if (e.Key == Key.Enter)
        {
            HandleGamepadSelect();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            // Back: most TV remotes map "Back" to Escape; some to BackSpace
            // or BrowserBack (BLE HID remotes often use the latter).
            case Key.Escape:
            case Key.Back:
            case Key.BrowserBack:
                Console.WriteLine($"Remote: Back ({e.Key})");
                HandleGamepadBack();
                e.Handled = true;
                break;

            // Menu / Apps button: parity with gamepad R1 (toggle sidebar).
            case Key.F10:
            case Key.Apps:
                Console.WriteLine($"Remote: Menu ({e.Key}) — toggling sidebar");
                OnSidebarToggle();
                e.Handled = true;
                break;

            // Home button on the remote.
            case Key.BrowserHome:
            case Key.Home:
                Console.WriteLine($"Remote: Home ({e.Key})");
                HandleGamepadHome();
                e.Handled = true;
                break;

            // Media keys: play/pause toggles, stop hides the player.
            case Key.MediaPlayPause:
                if (DataContext is MainWindowViewModel vmPlay && vmPlay.IsPlaying)
                {
                    Console.WriteLine($"Remote: Media play/pause ({e.Key})");
                    vmPlay.TogglePlayPauseCommand.Execute(null);
                    e.Handled = true;
                }
                break;

            case Key.MediaStop:
                if (DataContext is MainWindowViewModel vmStop && vmStop.IsPlaying)
                {
                    Console.WriteLine("Remote: Media stop");
                    HandleGamepadBack();
                    e.Handled = true;
                }
                break;
        }
    }

    private void HideCursor()
    {
        // Set cursor to None (invisible)
        Cursor = new Cursor(StandardCursorType.None);
        Console.WriteLine("Cursor hidden");
    }

    private void ShowCursor()
    {
        // Set cursor back to default (visible)
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    private void HandleGamepadNavigation(Key key)
    {
        // Hide cursor when gamepad is used
        HideCursor();

        // If video is playing, show its controls
        if (DataContext is MainWindowViewModel viewModel && viewModel.IsPlaying && _videoPlayerControl != null)
        {
            // Trigger the VideoPlayerControl to show controls by simulating a key press
            var keyEventArgs = new Avalonia.Input.KeyEventArgs
            {
                Key = key,
                RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent
            };
            _videoPlayerControl.RaiseEvent(keyEventArgs);
            Console.WriteLine("Triggered VideoPlayerControl KeyDown to show controls");
        }

        // If keyboard is showing, route input to it
        var keyboardOverlay = this.FindControl<Border>("KeyboardOverlay");
        var keyboard = this.FindControl<OnScreenKeyboard>("OnScreenKeyboard");

        if (keyboardOverlay?.IsVisible == true && keyboard != null)
        {
            string direction = key switch
            {
                Key.Left => "Left",
                Key.Right => "Right",
                Key.Up => "Up",
                Key.Down => "Down",
                _ => ""
            };

            if (!string.IsNullOrEmpty(direction))
            {
                keyboard.HandleGamepadInput(direction);
                return;
            }
        }

        // Get the currently focused element
        var focusManager = TopLevel.GetTopLevel(this)?.FocusManager;
        var focused = focusManager?.GetFocusedElement() as Control;

        Console.WriteLine($"=== HANDLE GAMEPAD NAV DEBUG ===");
        Console.WriteLine($"FocusManager: {focusManager != null}");
        Console.WriteLine($"Focused element: {focused?.GetType().Name ?? "NULL"}");
        Console.WriteLine($"Key: {key}");

        if (focused == null)
        {
            // Nothing focused, find the first visible focusable button
            Console.WriteLine("No focus - finding first visible button");
            var allButtons = GetAllFocusableButtons(this);
            var firstVisibleButton = allButtons.FirstOrDefault(b => b.IsVisible && b.IsEffectivelyVisible);

            if (firstVisibleButton != null)
            {
                firstVisibleButton.Focus();
                Console.WriteLine($"Focused first visible button: {firstVisibleButton.GetType().Name}");
            }
            else
            {
                Console.WriteLine("No visible buttons found");
            }
            return;
        }

        Console.WriteLine($"Focus navigation: {key} from {focused.GetType().Name}");

        // Handle directional navigation with spatial logic.
        // Android-TV pattern: pressing Left at the leftmost item reveals the
        // sidebar; pressing Right while in the sidebar closes it. Lets you
        // drive the whole UI from a remote that has no dedicated Menu key.
        var sidebarOverlay = this.FindControl<Border>("SidebarOverlay");
        bool sidebarOpen = sidebarOverlay?.IsVisible == true;

        switch (key)
        {
            case Key.Left:
                if (!sidebarOpen && !MoveFocusSpatial(focused, -1, 0))
                {
                    Console.WriteLine("Left at leftmost edge → opening sidebar");
                    ShowSidebar();
                }
                break;
            case Key.Right:
                if (sidebarOpen)
                {
                    Console.WriteLine("Right while sidebar open → closing sidebar");
                    HideSidebar();
                }
                else
                {
                    MoveFocusSpatial(focused, 1, 0);
                }
                break;
            case Key.Up:
                MoveFocusSpatial(focused, 0, -1);
                break;
            case Key.Down:
                MoveFocusSpatial(focused, 0, 1);
                break;
        }
    }

    private bool MoveFocusSpatial(Control current, int deltaX, int deltaY)
    {
        // Get all focusable buttons in the window
        var allButtons = GetAllFocusableButtons(this);

        Console.WriteLine($"=== SPATIAL NAV DEBUG ===");
        Console.WriteLine($"Total focusable buttons found: {allButtons.Count}");
        Console.WriteLine($"Current focused: {current.GetType().Name} (Name: {current.Name})");
        Console.WriteLine($"Direction: deltaX={deltaX}, deltaY={deltaY}");

        foreach (var btn in allButtons)
        {
            Console.WriteLine($"  - Button: {btn.GetType().Name}, Name={btn.Name}, Visible={btn.IsVisible}, EffectivelyVisible={btn.IsEffectivelyVisible}");
        }

        if (allButtons.Count == 0)
        {
            Console.WriteLine("NO BUTTONS FOUND - returning");
            return false;
        }

        // Get current button's center position using visual tree
        var currentCenter = GetVisualCenter(current);
        if (currentCenter == null)
        {
            Console.WriteLine("CURRENT CENTER IS NULL - returning");
            return false;
        }

        Console.WriteLine($"Current center position: {currentCenter.Value}");

        Control? bestMatch = null;
        double bestScore = double.MaxValue;

        foreach (var button in allButtons)
        {
            if (button == current || !button.IsVisible || !button.IsEffectivelyVisible)
                continue;

            var buttonCenter = GetVisualCenter(button);
            if (buttonCenter == null)
                continue;

            var dx = buttonCenter.Value.X - currentCenter.Value.X;
            var dy = buttonCenter.Value.Y - currentCenter.Value.Y;

            // Check if this button is in the correct direction
            bool isInDirection = false;
            if (deltaX > 0 && dx > 20) isInDirection = true;  // Right
            else if (deltaX < 0 && dx < -20) isInDirection = true;  // Left
            else if (deltaY > 0 && dy > 50) isInDirection = true;  // Down
            else if (deltaY < 0 && dy < -50) isInDirection = true;  // Up

            if (!isInDirection)
                continue;

            // Calculate distance score (prefer closer items in the direction of movement)
            double distance = Math.Sqrt(dx * dx + dy * dy);

            // Weight the score based on alignment
            double alignmentPenalty = 0;
            if (deltaX != 0)  // Horizontal movement
            {
                alignmentPenalty = Math.Abs(dy) * 2;  // Prefer items on same row
            }
            else  // Vertical movement
            {
                alignmentPenalty = Math.Abs(dx) * 0.5;  // Prefer items in same column (less strict)
            }

            double score = distance + alignmentPenalty;

            if (score < bestScore)
            {
                bestScore = score;
                bestMatch = button;
            }
        }

        if (bestMatch != null)
        {
            Console.WriteLine($"FOUND BEST MATCH: {bestMatch.GetType().Name} (Name: {bestMatch.Name}), Score: {bestScore}");
            bestMatch.Focus();
            EnsureVisible(bestMatch);
            Console.WriteLine($"Focus moved successfully");
            Console.WriteLine($"=== END SPATIAL NAV DEBUG ===");
            return true;
        }

        Console.WriteLine($"NO SUITABLE TARGET FOUND for direction ({deltaX},{deltaY})");
        Console.WriteLine($"=== END SPATIAL NAV DEBUG ===");
        return false;
    }

    private Avalonia.Point? GetVisualCenter(Control control)
    {
        try
        {
            var bounds = control.Bounds;
            // Get bounds relative to the window using visual tree helper
            var relativePoint = control.TranslatePoint(new Avalonia.Point(bounds.Width / 2, bounds.Height / 2), this);
            return relativePoint;
        }
        catch
        {
            return null;
        }
    }

    private List<Control> GetAllFocusableButtons(Control root)
    {
        var buttons = new List<Control>();
        CollectFocusableButtons(root, buttons);
        return buttons;
    }

    private void CollectFocusableButtons(Control control, List<Control> buttons)
    {
        // Include Button, TextBox, and ComboBox controls for navigation
        if ((control is Button || control is TextBox || control is ComboBox) && control.Focusable && control.IsVisible)
        {
            buttons.Add(control);
        }

        foreach (var child in control.GetVisualChildren())
        {
            if (child is Control childControl)
            {
                CollectFocusableButtons(childControl, buttons);
            }
        }
    }

    private void EnsureVisible(Control control)
    {
        // Find the parent ScrollViewer and scroll the control into view
        var parent = control.Parent;
        while (parent != null)
        {
            if (parent is ScrollViewer scrollViewer)
            {
                // Try to bring the control into view
                try
                {
                    control.BringIntoView();
                }
                catch
                {
                    // If BringIntoView fails, just continue
                }
                break;
            }
            parent = parent.Parent;
        }
    }

    private void HandleGamepadSelect()
    {
        // Hide cursor when gamepad is used
        HideCursor();

        // Check if sidebar is showing first - it has priority
        var sidebarOverlay = this.FindControl<Border>("SidebarOverlay");
        if (sidebarOverlay?.IsVisible == true)
        {
            // Let the normal button click handling work for sidebar
            var fm = TopLevel.GetTopLevel(this)?.FocusManager;
            var focusedElement = fm?.GetFocusedElement();
            if (focusedElement is Button btn)
            {
                btn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Console.WriteLine($"Sidebar button clicked: {btn.Name}");
                return;
            }
        }

        // If keyboard is showing, route Select to it
        var keyboardOverlay = this.FindControl<Border>("KeyboardOverlay");
        var keyboard = this.FindControl<OnScreenKeyboard>("OnScreenKeyboard");

        if (keyboardOverlay?.IsVisible == true && keyboard != null)
        {
            keyboard.HandleGamepadInput("Select");
            return;
        }

        // Check if video is playing and handle play/pause (after sidebar/keyboard checks)
        if (DataContext is MainWindowViewModel viewModel && viewModel.IsPlaying)
        {
            // Toggle play/pause on VideoPlayerControl
            viewModel.TogglePlayPauseCommand.Execute(null);
            Console.WriteLine("A button: Toggle play/pause");
            return;
        }

        // Get the currently focused element and invoke its command or click event
        var focusManager = TopLevel.GetTopLevel(this)?.FocusManager;
        var focused = focusManager?.GetFocusedElement();

        Console.WriteLine($"Select pressed, focused element: {focused?.GetType().Name}");

        // If a TextBox is focused, show the on-screen keyboard
        if (focused is TextBox textBox)
        {
            Console.WriteLine($"TextBox focused: {textBox.Name}, showing keyboard");
            ShowOnScreenKeyboard(textBox);
            return;
        }

        if (focused is Button button)
        {
            Console.WriteLine($"Button found: Command={button.Command != null}");

            // Try executing the command if it exists
            if (button.Command != null && button.Command.CanExecute(button.CommandParameter))
            {
                Console.WriteLine("Executing button command");
                button.Command.Execute(button.CommandParameter);
            }
            else
            {
                // Fall back to raising click event
                Console.WriteLine("Raising button click event");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
            }
        }
    }

    private void HandleGamepadBack()
    {
        // Hide cursor when gamepad is used
        HideCursor();

        Console.WriteLine("Back button pressed");

        // If settings is showing, hide it
        var settingsOverlay = this.FindControl<Border>("SettingsOverlay");
        if (settingsOverlay?.IsVisible == true)
        {
            HideSettings();
            return;
        }

        // If sidebar is showing, hide it
        var sidebarOverlay = this.FindControl<Border>("SidebarOverlay");
        if (sidebarOverlay?.IsVisible == true)
        {
            HideSidebar();
            return;
        }

        // If keyboard is showing, hide it
        var keyboardOverlay = this.FindControl<Border>("KeyboardOverlay");
        var keyboard = this.FindControl<OnScreenKeyboard>("OnScreenKeyboard");

        if (keyboardOverlay?.IsVisible == true && keyboard != null)
        {
            HideOnScreenKeyboard();
            return;
        }

        // First check if media is playing and stop it
        if (DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            if (viewModel.IsPlaying)
            {
                Console.WriteLine("Video is playing - stopping playback and returning to app");
                _videoPlayerControl?.Stop(); // Directly stop the video player
                if (_videoPlayerControl != null)
                {
                    _videoPlayerControl.IsVisible = false; // Manually hide the player
                    Console.WriteLine("Manually hid VideoPlayerControl");
                }
                viewModel.StopPlaybackCommand.Execute(null);
                return; // Don't execute back command, just stop playback
            }

            // If not playing, execute normal back navigation
            Console.WriteLine($"ViewModel found, GoBackToHomeCommand exists: {viewModel.GoBackToHomeCommand != null}");

            if (viewModel.GoBackToHomeCommand?.CanExecute(null) == true)
            {
                Console.WriteLine("Executing GoBackToHomeCommand");
                viewModel.GoBackToHomeCommand.Execute(null);
            }
            else
            {
                Console.WriteLine("GoBackToHomeCommand cannot execute or is null");
            }
        }
        else
        {
            Console.WriteLine("ViewModel not found in DataContext");
        }
    }

    private T? FindFirstFocusableChild<T>(Control parent) where T : Control
    {
        if (parent is T control && control.Focusable)
            return control;

        foreach (var child in parent.GetVisualChildren())
        {
            if (child is Control childControl)
            {
                var found = FindFirstFocusableChild<T>(childControl);
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    private void SetupOnScreenKeyboard()
    {
        // Keyboard will be shown when pressing A (Select) on a TextBox via gamepad
        // No automatic showing on focus to avoid popping up when using mouse
    }

    private void ShowOnScreenKeyboard(TextBox targetTextBox)
    {
        var keyboardOverlay = this.FindControl<Border>("KeyboardOverlay");
        var keyboard = this.FindControl<OnScreenKeyboard>("OnScreenKeyboard");

        if (keyboardOverlay == null || keyboard == null)
        {
            Console.WriteLine("Keyboard controls not found");
            return;
        }

        // If we're rebinding to a different TextBox, drop the LostFocus
        // handler from the previous one so we don't leak handlers.
        if (_keyboardTargetTextBox != null && _keyboardTargetTextBox != targetTextBox)
        {
            _keyboardTargetTextBox.LostFocus -= OnKeyboardTargetLostFocus;
        }
        _keyboardTargetTextBox = targetTextBox;

        // Auto-hide the OSK when focus leaves the target — happens when the
        // user types via a real keyboard and Tabs to the next field, or
        // clicks Connect/Submit. Without this the OSK stays visible
        // covering content even after the user is clearly done with it.
        targetTextBox.LostFocus -= OnKeyboardTargetLostFocus;
        targetTextBox.LostFocus += OnKeyboardTargetLostFocus;

        // Set the current text in the keyboard
        keyboard.CurrentText = targetTextBox.Text ?? "";

        // Hook up keyboard events
        keyboard.TextEntered -= OnKeyboardTextEntered;
        keyboard.Dismissed -= OnKeyboardDismissed;
        keyboard.TextEntered += OnKeyboardTextEntered;
        keyboard.Dismissed += OnKeyboardDismissed;

        // Show keyboard overlay
        keyboardOverlay.IsVisible = true;

        // Force a layout update so IsEffectivelyVisible updates for child controls
        keyboardOverlay.UpdateLayout();

        Console.WriteLine("On-screen keyboard shown");
    }

    private void OnKeyboardTargetLostFocus(object? sender, RoutedEventArgs e)
    {
        // Don't hide if focus moved INTO the on-screen keyboard itself
        // (user is tapping its keys via mouse/gamepad).
        var focused = FocusManager?.GetFocusedElement() as Visual;
        var keyboardOverlay = this.FindControl<Border>("KeyboardOverlay");
        if (focused != null && keyboardOverlay != null &&
            (focused == keyboardOverlay || focused.GetVisualAncestors().Contains(keyboardOverlay)))
        {
            return;
        }
        HideOnScreenKeyboard();
    }

    private void OnKeyboardTextEntered(object? sender, string text)
    {
        if (_keyboardTargetTextBox != null)
        {
            _keyboardTargetTextBox.Text = text;
            Console.WriteLine($"Text entered: {text}");
        }
        HideOnScreenKeyboard();
    }

    private void OnKeyboardDismissed(object? sender, EventArgs e)
    {
        HideOnScreenKeyboard();
    }

    private void HideOnScreenKeyboard()
    {
        var keyboardOverlay = this.FindControl<Border>("KeyboardOverlay");
        if (keyboardOverlay != null)
        {
            keyboardOverlay.IsVisible = false;

            // Restore focus to the TextBox that was being edited
            // Capture the textbox reference before clearing it
            var textBoxToFocus = _keyboardTargetTextBox;
            if (textBoxToFocus != null)
            {
                // Drop the LostFocus hook so it doesn't fire when we
                // re-focus or when the user navigates away later.
                textBoxToFocus.LostFocus -= OnKeyboardTargetLostFocus;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    textBoxToFocus?.Focus();
                    Console.WriteLine($"Focus restored to {textBoxToFocus?.Name}");
                }, Avalonia.Threading.DispatcherPriority.Loaded);
            }

            _keyboardTargetTextBox = null;
            Console.WriteLine("On-screen keyboard hidden");
        }
    }

    private void HandleGamepadHome()
    {
        HideCursor();

        // Navigate to home (execute GoBackToHomeCommand)
        if (DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            if (viewModel.GoBackToHomeCommand?.CanExecute(null) == true)
            {
                Console.WriteLine("Executing GoBackToHomeCommand from Home button");
                viewModel.GoBackToHomeCommand.Execute(null);
            }
        }
    }

    private void OnSidebarToggle()
    {
        HideCursor();

        // Toggle the sidebar
        var sidebarOverlay = this.FindControl<Border>("SidebarOverlay");
        if (sidebarOverlay != null)
        {
            if (sidebarOverlay.IsVisible)
            {
                HideSidebar();
            }
            else
            {
                ShowSidebar();
            }
        }
    }

    private void ShowSidebar()
    {
        var sidebarOverlay = this.FindControl<Border>("SidebarOverlay");
        var homeButton = this.FindControl<Button>("HomeButton");
        var appsButton = this.FindControl<Button>("AppsButton");
        var settingsButton = this.FindControl<Button>("SettingsButton");
        var showDesktopButton = this.FindControl<Button>("ShowDesktopButton");
        var logoutButton = this.FindControl<Button>("LogoutButton");

        if (sidebarOverlay == null) return;

        // Wire up button events
        if (homeButton != null)
        {
            homeButton.Click -= OnHomeButtonClick;
            homeButton.Click += OnHomeButtonClick;
        }

        if (appsButton != null)
        {
            appsButton.Click -= OnAppsButtonClick;
            appsButton.Click += OnAppsButtonClick;
        }

        if (settingsButton != null)
        {
            settingsButton.Click -= OnSettingsButtonClick;
            settingsButton.Click += OnSettingsButtonClick;
        }

        if (showDesktopButton != null)
        {
            // No desktop to show on the appliance image (cage IS the entire
            // session). jellytv-launch sets JELLYTV_APPLIANCE=1 there.
            if (IsApplianceMode())
            {
                showDesktopButton.IsVisible = false;
            }
            else
            {
                showDesktopButton.Click -= OnShowDesktopButtonClick;
                showDesktopButton.Click += OnShowDesktopButtonClick;
            }
        }

        if (logoutButton != null)
        {
            logoutButton.Click -= OnLogoutButtonClick;
            logoutButton.Click += OnLogoutButtonClick;
        }

        // Wire up click on overlay background to close sidebar
        sidebarOverlay.PointerPressed -= OnSidebarOverlayClick;
        sidebarOverlay.PointerPressed += OnSidebarOverlayClick;

        // Wire up library buttons
        var libraryContainer = this.FindControl<ItemsControl>("LibraryButtonsContainer");
        if (libraryContainer != null)
        {
            // Wait for ItemsControl to render its items
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                foreach (var button in libraryContainer.GetVisualDescendants().OfType<Button>())
                {
                    button.Click -= OnLibraryButtonClick;
                    button.Click += OnLibraryButtonClick;
                }
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }

        sidebarOverlay.IsVisible = true;
        sidebarOverlay.UpdateLayout();

        // Focus the first button
        homeButton?.Focus();

        Console.WriteLine("Sidebar shown");
    }

    private void HideSidebar()
    {
        var sidebarOverlay = this.FindControl<Border>("SidebarOverlay");
        if (sidebarOverlay != null)
        {
            sidebarOverlay.IsVisible = false;
            Console.WriteLine("Sidebar hidden");
        }
    }

    private void OnSidebarOverlayClick(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        // Check if click was on the overlay background (not the sidebar itself)
        var sidebar = this.FindControl<Border>("Sidebar");
        if (sidebar != null && e.Source is Visual visual)
        {
            // If the click was not within the sidebar panel, hide it
            if (!IsVisualDescendantOf(visual, sidebar))
            {
                HideSidebar();
            }
        }
    }

    private bool IsVisualDescendantOf(Visual child, Visual parent)
    {
        var current = child;
        while (current != null)
        {
            if (current == parent)
                return true;
            current = current.GetVisualParent();
        }
        return false;
    }

    private void OnHomeButtonClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Home button clicked");
        HideSidebar();

        if (DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            // If video is playing, stop it first
            if (viewModel.IsPlaying)
            {
                Console.WriteLine("Stopping video playback from Home button");
                _videoPlayerControl?.Stop();
                if (_videoPlayerControl != null)
                {
                    _videoPlayerControl.IsVisible = false;
                }
                viewModel.StopPlaybackCommand.Execute(null);
            }

            // Navigate to home
            if (viewModel.GoBackToHomeCommand?.CanExecute(null) == true)
            {
                viewModel.GoBackToHomeCommand.Execute(null);
            }
        }
    }

    private void OnLibraryButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is JellyTV.Models.BaseItemDto library)
        {
            Console.WriteLine($"Library button clicked: {library.Name}");
            HideSidebar();

            if (DataContext is ViewModels.MainWindowViewModel viewModel)
            {
                viewModel.LoadLibraryCommand.Execute(library);
            }
        }
    }

    private void OnAppsButtonClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Apps button clicked");
        HideSidebar();
        ShowApps();
    }

    private void ShowApps()
    {
        var appsOverlay = this.FindControl<Border>("AppsOverlay");
        var youtubeButton = this.FindControl<Button>("YouTubeAppButton");
        var closeAppsButton = this.FindControl<Button>("CloseAppsButton");

        if (appsOverlay == null) return;

        if (youtubeButton != null)
        {
            youtubeButton.Click -= OnYouTubeAppClick;
            youtubeButton.Click += OnYouTubeAppClick;
        }

        if (closeAppsButton != null)
        {
            closeAppsButton.Click -= OnCloseAppsClick;
            closeAppsButton.Click += OnCloseAppsClick;
        }

        appsOverlay.IsVisible = true;
        appsOverlay.UpdateLayout();
        youtubeButton?.Focus();

        Console.WriteLine("Apps overlay shown");
    }

    private void HideApps()
    {
        var appsOverlay = this.FindControl<Border>("AppsOverlay");
        if (appsOverlay != null)
        {
            appsOverlay.IsVisible = false;
            Console.WriteLine("Apps overlay hidden");
        }
    }

    private void OnCloseAppsClick(object? sender, RoutedEventArgs e)
    {
        HideApps();
    }

    private async void OnYouTubeAppClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("YouTube app launch requested");
        HideApps();

        // Refocus JellyTV when the sub-app exits — Wayland will already
        // raise us when Electron quits, but Activate() ensures keyboard focus
        // returns to whatever was focused before launch.
        void OnExit()
        {
            _appLauncher.AppExited -= OnExit;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Activate();
                Console.WriteLine("YouTube exited — JellyTV refocused");
            });
        }
        _appLauncher.AppExited += OnExit;

        var started = await _appLauncher.LaunchYouTubeAsync();
        if (!started)
        {
            _appLauncher.AppExited -= OnExit;
            Console.WriteLine("YouTube launch failed — check apps/youtube install");
        }
    }


    private void OnSettingsButtonClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Settings button clicked");
        HideSidebar();
        ShowSettings();
    }

    private void ShowSettings()
    {
        var settingsOverlay = this.FindControl<Border>("SettingsOverlay");
        var closeSettingsButton = this.FindControl<Button>("CloseSettingsButton");
        var versionLabel = this.FindControl<TextBlock>("VersionLabel");
        var checkForUpdatesButton = this.FindControl<Button>("CheckForUpdatesButton");
        var updateStatusLabel = this.FindControl<TextBlock>("UpdateStatusLabel");

        if (settingsOverlay == null) return;

        // Wire up button events
        if (closeSettingsButton != null)
        {
            closeSettingsButton.Click -= OnCloseSettingsButtonClick;
            closeSettingsButton.Click += OnCloseSettingsButtonClick;
        }

        if (versionLabel != null)
        {
            versionLabel.Text = _updater.CurrentVersion.ToString();
        }

        if (checkForUpdatesButton != null)
        {
            checkForUpdatesButton.Click -= OnCheckForUpdatesClick;
            checkForUpdatesButton.Click += OnCheckForUpdatesClick;
        }

        var bluetoothScanButton = this.FindControl<Button>("BluetoothScanButton");
        if (bluetoothScanButton != null)
        {
            bluetoothScanButton.Click -= OnBluetoothScanClick;
            bluetoothScanButton.Click += OnBluetoothScanClick;
        }

        var wifiScanButton = this.FindControl<Button>("WifiScanButton");
        if (wifiScanButton != null)
        {
            wifiScanButton.Click -= OnWifiScanClick;
            wifiScanButton.Click += OnWifiScanClick;
        }

        var sshToggleButton = this.FindControl<Button>("SshToggleButton");
        if (sshToggleButton != null)
        {
            sshToggleButton.Click -= OnSshToggleClick;
            sshToggleButton.Click += OnSshToggleClick;
        }

        // "Install to Disk" only makes sense when running from a live ISO.
        // The live-boot package mounts the source at /run/live/medium; if
        // that mountpoint exists we're live, otherwise we're already on disk.
        var installSection = this.FindControl<Border>("InstallToDiskSection");
        var installToDiskButton = this.FindControl<Button>("InstallToDiskButton");
        var isLive = Directory.Exists("/run/live/medium");
        if (installSection != null) installSection.IsVisible = isLive;
        if (installToDiskButton != null)
        {
            installToDiskButton.Click -= OnInstallToDiskClick;
            installToDiskButton.Click += OnInstallToDiskClick;
        }

        // Show currently paired devices, active WiFi connection, and SSH
        // status immediately on open — saves the user a click.
        _ = RefreshBluetoothListAsync(scanFirst: false);
        _ = RefreshWifiCurrentStatusAsync();
        _ = RefreshSshStatusAsync();

        if (updateStatusLabel != null)
        {
            updateStatusLabel.IsVisible = false;
            updateStatusLabel.Text = "";
        }

        settingsOverlay.IsVisible = true;
        settingsOverlay.UpdateLayout();

        // Focus the close button
        closeSettingsButton?.Focus();

        Console.WriteLine("Settings shown");
    }

    private async void OnCheckForUpdatesClick(object? sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var status = this.FindControl<TextBlock>("UpdateStatusLabel");

        if (status != null)
        {
            status.Foreground = Avalonia.Media.Brushes.LightGray;
            status.Text = "Checking…";
            status.IsVisible = true;
        }
        if (button != null) button.IsEnabled = false;

        var result = await _updater.CheckForUpdateAsync();

        if (status == null)
        {
            if (button != null) button.IsEnabled = true;
            return;
        }

        if (result.IsError)
        {
            status.Foreground = Avalonia.Media.Brushes.OrangeRed;
            status.Text = $"Update check failed: {result.ErrorMessage}";
        }
        else if (result.HasUpdate)
        {
            status.Foreground = Avalonia.Media.Brushes.LightGreen;
            status.Text = $"Update available: {result.LatestVersion} (you have {result.CurrentVersion}). " +
                          "Apply support coming in the next build.";
        }
        else if (result.LatestVersion == null)
        {
            status.Foreground = Avalonia.Media.Brushes.LightGray;
            status.Text = "No releases published yet — you're on the development build.";
        }
        else
        {
            status.Foreground = Avalonia.Media.Brushes.LightGray;
            status.Text = $"You're on the latest version ({result.CurrentVersion}).";
        }

        if (button != null) button.IsEnabled = true;
    }

    private async void OnBluetoothScanClick(object? sender, RoutedEventArgs e)
    {
        await RefreshBluetoothListAsync(scanFirst: true);
    }

    private async Task RefreshBluetoothListAsync(bool scanFirst)
    {
        if (_bluetoothBusy) return;

        var statusLabel = this.FindControl<TextBlock>("BluetoothStatusLabel");
        var scanButton = this.FindControl<Button>("BluetoothScanButton");
        var listPanel = this.FindControl<StackPanel>("BluetoothDeviceList");
        if (listPanel == null) return;

        _bluetoothBusy = true;
        if (scanButton != null) scanButton.IsEnabled = false;

        try
        {
            if (scanFirst)
            {
                if (statusLabel != null) statusLabel.Text = "Scanning…";
                await _bluetooth.PowerOnBluetoothAsync();
                await _bluetooth.StartScanAsync();
                // bluetoothctl scan results trickle in over a few seconds;
                // give it a window before snapshotting the device list.
                await Task.Delay(6000);
                await _bluetooth.StopScanAsync();
            }
            else
            {
                if (statusLabel != null) statusLabel.Text = "";
            }

            var devices = await _bluetooth.GetDevicesAsync();
            RenderBluetoothDevices(listPanel, devices);

            if (statusLabel != null)
            {
                statusLabel.Text = devices.Count == 0
                    ? "No devices found"
                    : $"{devices.Count(d => d.IsPaired)} paired, {devices.Count} total";
            }
        }
        catch (Exception ex)
        {
            if (statusLabel != null)
            {
                statusLabel.Foreground = Avalonia.Media.Brushes.OrangeRed;
                statusLabel.Text = $"Error: {ex.Message}";
            }
        }
        finally
        {
            _bluetoothBusy = false;
            if (scanButton != null) scanButton.IsEnabled = true;
        }
    }

    private void RenderBluetoothDevices(StackPanel container, IEnumerable<BluetoothDevice> devices)
    {
        container.Children.Clear();

        // Paired first so users see their remote at the top of the list.
        var ordered = devices
            .OrderByDescending(d => d.IsConnected)
            .ThenByDescending(d => d.IsPaired)
            .ThenBy(d => d.Name);

        foreach (var device in ordered)
        {
            container.Children.Add(BuildBluetoothRow(device));
        }
    }

    private Border BuildBluetoothRow(BluetoothDevice device)
    {
        var nameText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(device.Name) ? device.Address : device.Name,
            FontSize = 22,
            Foreground = Avalonia.Media.Brushes.White,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        var statusBits = new List<string>();
        if (device.IsConnected) statusBits.Add("connected");
        else if (device.IsPaired) statusBits.Add("paired");
        if (!string.IsNullOrEmpty(device.DeviceType) && device.DeviceType != "Unknown")
            statusBits.Add(device.DeviceType.ToLowerInvariant());
        var statusText = new TextBlock
        {
            Text = string.Join(" · ", statusBits),
            FontSize = 16,
            Foreground = Avalonia.Media.Brushes.LightGray,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        var actionButton = new Button
        {
            Content = device.IsPaired ? "Forget" : "Pair",
            FontSize = 20,
            Padding = new Avalonia.Thickness(20, 8),
            Focusable = true,
            IsTabStop = true,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        var address = device.Address; // capture for closure
        var isPaired = device.IsPaired;
        actionButton.Click += async (_, _) =>
        {
            actionButton.IsEnabled = false;
            actionButton.Content = isPaired ? "Forgetting…" : "Pairing…";
            try
            {
                if (isPaired)
                {
                    await _bluetooth.UnpairDeviceAsync(address);
                }
                else
                {
                    await _bluetooth.PairDeviceAsync(address);
                }
            }
            finally
            {
                await RefreshBluetoothListAsync(scanFirst: false);
            }
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        var labelStack = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            Spacing = 2,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        labelStack.Children.Add(nameText);
        if (statusBits.Count > 0) labelStack.Children.Add(statusText);
        Grid.SetColumn(labelStack, 0);
        grid.Children.Add(labelStack);

        var addressText = new TextBlock
        {
            Text = device.Address,
            FontSize = 14,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF888888")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 0, 20, 0),
        };
        Grid.SetColumn(addressText, 1);
        grid.Children.Add(addressText);

        Grid.SetColumn(actionButton, 2);
        grid.Children.Add(actionButton);

        return new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF1F1F1F")),
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(16, 10),
            Child = grid,
        };
    }

    private async void OnWifiScanClick(object? sender, RoutedEventArgs e)
    {
        await RefreshWifiListAsync();
    }

    private async Task RefreshWifiCurrentStatusAsync()
    {
        var statusLabel = this.FindControl<TextBlock>("WifiStatusLabel");
        if (statusLabel == null) return;
        try
        {
            var current = await _wifi.GetCurrentSsidAsync();
            statusLabel.Text = string.IsNullOrEmpty(current) ? "Not connected" : $"On {current}";
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"({ex.Message})";
        }
    }

    private async Task RefreshWifiListAsync()
    {
        if (_wifiBusy) return;

        var statusLabel = this.FindControl<TextBlock>("WifiStatusLabel");
        var scanButton = this.FindControl<Button>("WifiScanButton");
        var listPanel = this.FindControl<StackPanel>("WifiNetworkList");
        if (listPanel == null) return;

        _wifiBusy = true;
        if (scanButton != null) scanButton.IsEnabled = false;
        if (statusLabel != null) statusLabel.Text = "Scanning…";

        try
        {
            var networks = await _wifi.ScanAsync();
            RenderWifiNetworks(listPanel, networks);
            await RefreshWifiCurrentStatusAsync();
        }
        catch (Exception ex)
        {
            if (statusLabel != null)
            {
                statusLabel.Foreground = Avalonia.Media.Brushes.OrangeRed;
                statusLabel.Text = $"Error: {ex.Message}";
            }
        }
        finally
        {
            _wifiBusy = false;
            if (scanButton != null) scanButton.IsEnabled = true;
        }
    }

    private void RenderWifiNetworks(StackPanel container, IEnumerable<WiFiNetwork> networks)
    {
        container.Children.Clear();
        foreach (var net in networks)
        {
            container.Children.Add(BuildWifiRow(net));
        }
    }

    private Border BuildWifiRow(WiFiNetwork net)
    {
        var lockGlyph = net.IsSecured ? "🔒 " : "";
        var nameText = new TextBlock
        {
            Text = $"{lockGlyph}{net.Ssid}",
            FontSize = 22,
            Foreground = Avalonia.Media.Brushes.White,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        var detail = new List<string>();
        if (net.InUse) detail.Add("connected");
        detail.Add($"{net.SignalStrength}%");
        if (!string.IsNullOrEmpty(net.Security) && net.Security != "--") detail.Add(net.Security);
        var detailText = new TextBlock
        {
            Text = string.Join(" · ", detail),
            FontSize = 16,
            Foreground = Avalonia.Media.Brushes.LightGray,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        var actionButton = new Button
        {
            Content = net.InUse ? "Disconnect" : "Connect",
            FontSize = 20,
            Padding = new Avalonia.Thickness(20, 8),
            Focusable = true,
            IsTabStop = true,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        var ssid = net.Ssid;        // capture
        var isSecured = net.IsSecured;
        var inUse = net.InUse;

        actionButton.Click += async (_, _) =>
        {
            actionButton.IsEnabled = false;
            try
            {
                if (inUse)
                {
                    actionButton.Content = "Disconnecting…";
                    await _wifi.DisconnectAsync();
                }
                else
                {
                    string? password = null;
                    if (isSecured)
                    {
                        password = await PromptForTextAsync();
                        if (password == null) return;   // user cancelled
                    }
                    actionButton.Content = "Connecting…";
                    var (ok, msg) = await _wifi.ConnectAsync(ssid, password);
                    if (!ok)
                    {
                        var statusLabel = this.FindControl<TextBlock>("WifiStatusLabel");
                        if (statusLabel != null)
                        {
                            statusLabel.Foreground = Avalonia.Media.Brushes.OrangeRed;
                            statusLabel.Text = msg.Length > 80 ? msg.Substring(0, 80) + "…" : msg;
                        }
                    }
                }
            }
            finally
            {
                await RefreshWifiListAsync();
            }
        };

        var labelStack = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            Spacing = 2,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        labelStack.Children.Add(nameText);
        labelStack.Children.Add(detailText);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(labelStack, 0);
        grid.Children.Add(labelStack);
        Grid.SetColumn(actionButton, 1);
        grid.Children.Add(actionButton);

        return new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF1F1F1F")),
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(16, 10),
            Child = grid,
        };
    }

    private async void OnSshToggleClick(object? sender, RoutedEventArgs e)
    {
        if (_sshBusy) return;
        _sshBusy = true;
        var button = sender as Button;
        if (button != null) button.IsEnabled = false;
        try
        {
            var status = await _ssh.GetStatusAsync();
            if (status.IsRunning || status.IsEnabledOnBoot)
            {
                await _ssh.DisableAsync();
            }
            else
            {
                await _ssh.EnableAsync();
            }
            await RefreshSshStatusAsync();
        }
        finally
        {
            _sshBusy = false;
            if (button != null) button.IsEnabled = true;
        }
    }

    private async Task RefreshSshStatusAsync()
    {
        var badge = this.FindControl<TextBlock>("SshStatusBadge");
        var hint = this.FindControl<TextBlock>("SshHintLabel");
        if (badge == null) return;

        var status = await _ssh.GetStatusAsync();
        badge.Text = status.IsRunning ? "[X]" : "[ ]";

        if (hint != null)
        {
            if (status.IsRunning)
            {
                var ip = SshService.GetLocalIp();
                hint.Text = ip != null
                    ? $"ssh jellytv@{ip}   (password: jellytv)"
                    : "Running — no IPv4 address detected";
            }
            else
            {
                hint.Text = "Disabled — turn on for remote shell access during troubleshooting.";
            }
        }
    }

    private async void OnInstallToDiskClick(object? sender, RoutedEventArgs e)
    {
        HideSettings();
        await ShowInstallTargetPickerAsync();
    }

    private async Task ShowInstallTargetPickerAsync()
    {
        var overlay = this.FindControl<Border>("InstallOverlay");
        var content = this.FindControl<StackPanel>("InstallContent");
        var title = this.FindControl<TextBlock>("InstallOverlayTitle");
        var primary = this.FindControl<Button>("InstallPrimaryButton");
        var status = this.FindControl<TextBlock>("InstallStatusLabel");
        var close = this.FindControl<Button>("InstallCloseButton");
        if (overlay == null || content == null) return;

        title!.Text = "Choose Install Target";
        primary!.IsVisible = false;
        status!.Text = "";
        close!.Click -= OnInstallCloseClick;
        close.Click += OnInstallCloseClick;

        content.Children.Clear();
        content.Children.Add(new TextBlock
        {
            Text = "Scanning for disks…",
            FontSize = 20,
            Foreground = Avalonia.Media.Brushes.LightGray,
        });

        overlay.IsVisible = true;
        overlay.UpdateLayout();

        var targets = await _installer.ListTargetsAsync();

        content.Children.Clear();
        if (targets.Count == 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = "No disks found.",
                FontSize = 20,
                Foreground = Avalonia.Media.Brushes.LightGray,
            });
            close.Focus();
            return;
        }

        foreach (var t in targets)
        {
            content.Children.Add(BuildInstallTargetRow(t));
        }
    }

    private Border BuildInstallTargetRow(InstallTarget target)
    {
        var liveBadge = target.IsLiveSource ? "  (live USB — cannot install here)" : "";
        var modelLine = string.IsNullOrWhiteSpace(target.Model) ? target.Transport : $"{target.Model.Trim()} · {target.Transport}";

        var labels = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Spacing = 4,
        };
        labels.Children.Add(new TextBlock
        {
            Text = $"{target.Device}  {target.SizeHuman}{liveBadge}",
            FontSize = 24,
            Foreground = Avalonia.Media.Brushes.White,
        });
        labels.Children.Add(new TextBlock
        {
            Text = modelLine,
            FontSize = 16,
            Foreground = Avalonia.Media.Brushes.LightGray,
        });

        var button = new Button
        {
            Content = target.IsLiveSource ? "Source — skip" : "Select",
            FontSize = 20,
            Padding = new Avalonia.Thickness(20, 10),
            Focusable = true,
            IsTabStop = !target.IsLiveSource,
            IsEnabled = !target.IsLiveSource,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        var captured = target;
        button.Click += async (_, _) => await ShowInstallConfirmAsync(captured);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(labels, 0);
        Grid.SetColumn(button, 1);
        grid.Children.Add(labels);
        grid.Children.Add(button);

        return new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF1F1F1F")),
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(20, 14),
            Child = grid,
        };
    }

    private async Task ShowInstallConfirmAsync(InstallTarget target)
    {
        _selectedInstallTarget = target;
        var content = this.FindControl<StackPanel>("InstallContent");
        var title = this.FindControl<TextBlock>("InstallOverlayTitle");
        var primary = this.FindControl<Button>("InstallPrimaryButton");
        if (content == null || title == null || primary == null) return;

        title.Text = "Confirm";
        content.Children.Clear();
        content.Children.Add(new TextBlock
        {
            Text = $"Install JellyTV to {target.Device} ({target.SizeHuman}, {target.Model.Trim()})?",
            FontSize = 24,
            Foreground = Avalonia.Media.Brushes.White,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        content.Children.Add(new TextBlock
        {
            Text = "Everything on this disk will be erased.",
            FontSize = 20,
            Foreground = Avalonia.Media.Brushes.OrangeRed,
            Margin = new Avalonia.Thickness(0, 10, 0, 0),
        });

        primary.Content = "Erase and install";
        primary.IsVisible = true;
        primary.Click -= OnInstallConfirmClick;
        primary.Click += OnInstallConfirmClick;
        primary.Focus();
        await Task.CompletedTask;
    }

    private async void OnInstallConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedInstallTarget == null || _installRunning) return;
        _installRunning = true;

        var content = this.FindControl<StackPanel>("InstallContent");
        var title = this.FindControl<TextBlock>("InstallOverlayTitle");
        var primary = this.FindControl<Button>("InstallPrimaryButton");
        var status = this.FindControl<TextBlock>("InstallStatusLabel");
        var close = this.FindControl<Button>("InstallCloseButton");
        if (content == null || title == null || primary == null) return;

        title.Text = $"Installing to {_selectedInstallTarget.Device}";
        primary.IsVisible = false;
        if (close != null) close.IsEnabled = false;
        if (status != null) status.Text = "Working…";

        content.Children.Clear();
        var log = new TextBlock
        {
            FontFamily = new Avalonia.Media.FontFamily("Monospace"),
            FontSize = 14,
            Foreground = Avalonia.Media.Brushes.LightGray,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        content.Children.Add(log);

        var lines = new List<string>();
        void OnLine(string line) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                lines.Add(line);
                // Keep the visible log bounded so a huge rsync transfer doesn't
                // turn into a giant TextBlock that blows up the renderer.
                if (lines.Count > 200) lines.RemoveRange(0, lines.Count - 200);
                log.Text = string.Join("\n", lines);
            });

        _installer.ProgressLine -= OnLine;
        _installer.ProgressLine += OnLine;

        var ok = await _installer.InstallAsync(_selectedInstallTarget.Device);

        _installer.ProgressLine -= OnLine;
        _installRunning = false;
        if (close != null) close.IsEnabled = true;

        if (ok)
        {
            if (status != null)
            {
                status.Foreground = Avalonia.Media.Brushes.LightGreen;
                status.Text = "Install complete. Reboot to use the new install.";
            }
            primary.Content = "Reboot now";
            primary.IsVisible = true;
            primary.Click -= OnInstallConfirmClick;
            primary.Click -= OnInstallRebootClick;
            primary.Click += OnInstallRebootClick;
            primary.Focus();
        }
        else
        {
            if (status != null)
            {
                status.Foreground = Avalonia.Media.Brushes.OrangeRed;
                status.Text = "Install failed. See log above.";
            }
        }
    }

    private async void OnInstallRebootClick(object? sender, RoutedEventArgs e)
    {
        await InstallerService.RebootAsync();
    }

    private void OnInstallCloseClick(object? sender, RoutedEventArgs e)
    {
        if (_installRunning) return;
        var overlay = this.FindControl<Border>("InstallOverlay");
        if (overlay != null) overlay.IsVisible = false;
        _selectedInstallTarget = null;
    }

    /// <summary>
    /// Shows the on-screen keyboard and resolves with whatever the user
    /// types when they hit Enter on it (or null if they dismiss). Used by
    /// WiFi password entry — anywhere we need a remote-friendly text prompt
    /// without binding to a real TextBox.
    /// </summary>
    private Task<string?> PromptForTextAsync(string initial = "")
    {
        var keyboardOverlay = this.FindControl<Border>("KeyboardOverlay");
        var keyboard = this.FindControl<OnScreenKeyboard>("OnScreenKeyboard");
        if (keyboardOverlay == null || keyboard == null)
        {
            return Task.FromResult<string?>(null);
        }

        // If a prompt is already pending, cancel it.
        _keyboardPromptTcs?.TrySetResult(null);

        _keyboardPromptTcs = new TaskCompletionSource<string?>();
        // Important: clear the textbox-target path so OnKeyboardTextEntered
        // doesn't try to write into the login screen's TextBox.
        _keyboardTargetTextBox = null;

        keyboard.CurrentText = initial;
        keyboard.TextEntered -= OnKeyboardTextEntered;
        keyboard.Dismissed -= OnKeyboardDismissed;
        keyboard.TextEntered -= OnPromptTextEntered;
        keyboard.Dismissed -= OnPromptDismissed;
        keyboard.TextEntered += OnPromptTextEntered;
        keyboard.Dismissed += OnPromptDismissed;

        keyboardOverlay.IsVisible = true;
        keyboardOverlay.UpdateLayout();
        return _keyboardPromptTcs.Task;
    }

    private void OnPromptTextEntered(object? sender, string text)
    {
        var tcs = _keyboardPromptTcs;
        _keyboardPromptTcs = null;
        if (sender is OnScreenKeyboard k)
        {
            k.TextEntered -= OnPromptTextEntered;
            k.Dismissed -= OnPromptDismissed;
        }
        HideOnScreenKeyboard();
        tcs?.TrySetResult(text);
    }

    private void OnPromptDismissed(object? sender, EventArgs e)
    {
        var tcs = _keyboardPromptTcs;
        _keyboardPromptTcs = null;
        if (sender is OnScreenKeyboard k)
        {
            k.TextEntered -= OnPromptTextEntered;
            k.Dismissed -= OnPromptDismissed;
        }
        HideOnScreenKeyboard();
        tcs?.TrySetResult(null);
    }

    private void HideSettings()
    {
        var settingsOverlay = this.FindControl<Border>("SettingsOverlay");
        if (settingsOverlay != null)
        {
            settingsOverlay.IsVisible = false;
            Console.WriteLine("Settings hidden");
        }
    }

    private void OnCloseSettingsButtonClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Close settings button clicked");
        HideSettings();
    }

    private void OnLogoutButtonClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Logout button clicked");
        HideSidebar();

        if (DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            // Clear credentials file
            var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jellytv_config.json");
            if (File.Exists(configPath))
            {
                File.Delete(configPath);
                Console.WriteLine("Credentials cleared");
            }

            // Set IsAuthenticated to false to return to login screen
            viewModel.IsAuthenticated = false;
            Console.WriteLine("User logged out");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _gamepadService?.Dispose();
        base.OnClosed(e);
    }
}