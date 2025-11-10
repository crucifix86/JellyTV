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

                // Watch for episodes collection changes
                viewModel.Episodes.CollectionChanged += async (sender, args) =>
                {
                    if (viewModel.Episodes.Count > 0 && args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                    {
                        // Episodes were loaded - switch to another window and back to reset focus
                        await System.Threading.Tasks.Task.Delay(150);

                        // Switch to the desktop (or any other window), then back to JellyTV
                        try
                        {
                            var processInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "bash",
                                Arguments = "-c \"wmctrl -a Desktop 2>/dev/null || wmctrl -s 0; sleep 0.2; wmctrl -a JellyTV\"",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            System.Diagnostics.Process.Start(processInfo);
                            Console.WriteLine("Switched to desktop and back to JellyTV via wmctrl");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"wmctrl window switch failed: {ex.Message}");
                        }

                        await System.Threading.Tasks.Task.Delay(300);

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

        // Handle directional navigation with spatial logic
        switch (key)
        {
            case Key.Left:
                MoveFocusSpatial(focused, -1, 0);
                break;
            case Key.Right:
                MoveFocusSpatial(focused, 1, 0);
                break;
            case Key.Up:
                MoveFocusSpatial(focused, 0, -1);
                break;
            case Key.Down:
                MoveFocusSpatial(focused, 0, 1);
                break;
        }
    }

    private void MoveFocusSpatial(Control current, int deltaX, int deltaY)
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
            return;
        }

        // Get current button's center position using visual tree
        var currentCenter = GetVisualCenter(current);
        if (currentCenter == null)
        {
            Console.WriteLine("CURRENT CENTER IS NULL - returning");
            return;
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
        }
        else
        {
            Console.WriteLine($"NO SUITABLE TARGET FOUND for direction ({deltaX},{deltaY})");
        }
        Console.WriteLine($"=== END SPATIAL NAV DEBUG ===");
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

        _keyboardTargetTextBox = targetTextBox;

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
            showDesktopButton.Click -= OnShowDesktopButtonClick;
            showDesktopButton.Click += OnShowDesktopButtonClick;
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
        // Apps functionality not yet implemented
        HideSidebar();
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

        if (settingsOverlay == null) return;

        // Wire up button events
        if (closeSettingsButton != null)
        {
            closeSettingsButton.Click -= OnCloseSettingsButtonClick;
            closeSettingsButton.Click += OnCloseSettingsButtonClick;
        }

        settingsOverlay.IsVisible = true;
        settingsOverlay.UpdateLayout();

        // Focus the close button
        closeSettingsButton?.Focus();

        Console.WriteLine("Settings shown");
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