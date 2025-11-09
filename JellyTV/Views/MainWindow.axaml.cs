using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using JellyTV.Services;
using JellyTV.ViewModels;
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
    private AudioService _audioService = new AudioService();
    private VideoService _videoService = new VideoService();
    private BluetoothService _bluetoothService = new BluetoothService();

    public MainWindow()
    {
        InitializeComponent();

        // Wire up mouse movement to show cursor
        this.PointerMoved += (s, e) =>
        {
            ShowCursor();
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

        // Wire up MPV player control
        var mpvPlayer = this.FindControl<Controls.MpvPlayerControl>("MpvPlayer");
        if (mpvPlayer != null)
        {
            mpvPlayer.AttachedToVisualTree += (s, e) =>
            {
                // When the control is attached, pass the native window handle to the media player service
                if (mpvPlayer.NativeHandle != IntPtr.Zero && DataContext is ViewModels.MainWindowViewModel viewModel)
                {
                    var mediaPlayerField = viewModel.GetType().GetField("_mediaPlayer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (mediaPlayerField != null)
                    {
                        var mediaPlayer = mediaPlayerField.GetValue(viewModel) as MediaPlayerService;
                        mediaPlayer?.SetWindowHandle(mpvPlayer.NativeHandle);
                    }
                }
            };
        }

        // Watch for detail view changes to auto-focus Play button
        this.DataContextChanged += (s, e) =>
        {
            if (DataContext is ViewModels.MainWindowViewModel viewModel)
            {
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

        // Ensure window is activated before any navigation
        try
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "wmctrl",
                Arguments = "-a JellyTV",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            System.Diagnostics.Process.Start(processInfo);
        }
        catch { }

        // Get the currently focused element
        var focusManager = TopLevel.GetTopLevel(this)?.FocusManager;
        var focused = focusManager?.GetFocusedElement() as Control;

        Console.WriteLine($"=== HANDLE GAMEPAD NAV DEBUG ===");
        Console.WriteLine($"FocusManager: {focusManager != null}");
        Console.WriteLine($"Focused element: {focused?.GetType().Name ?? "NULL"}");
        Console.WriteLine($"Key: {key}");

        if (focused == null)
        {
            // Nothing focused, focus the first toolbar button
            Console.WriteLine("No focus - trying to focus HomeButton");
            var homeButton = this.FindControl<Button>("HomeButton");
            homeButton?.Focus();
            Console.WriteLine($"HomeButton focus attempted: {homeButton != null}");
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
        // Include both Button and TextBox controls for navigation
        if ((control is Button || control is TextBox) && control.Focusable && control.IsVisible)
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

        // If keyboard is showing, route Select to it
        var keyboardOverlay = this.FindControl<Border>("KeyboardOverlay");
        var keyboard = this.FindControl<OnScreenKeyboard>("OnScreenKeyboard");

        if (keyboardOverlay?.IsVisible == true && keyboard != null)
        {
            keyboard.HandleGamepadInput("Select");
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

        // If Bluetooth manage is showing, go back to settings
        var bluetoothManageOverlay = this.FindControl<Border>("BluetoothManageOverlay");
        if (bluetoothManageOverlay?.IsVisible == true)
        {
            HideBluetoothManage();
            ShowSettings();
            return;
        }

        // If Bluetooth pair is showing, go back to settings
        var bluetoothPairOverlay = this.FindControl<Border>("BluetoothPairOverlay");
        if (bluetoothPairOverlay?.IsVisible == true)
        {
            HideBluetoothPairing();
            ShowSettings();
            return;
        }

        // If video output selection is showing, go back to settings
        var videoOutputOverlay = this.FindControl<Border>("VideoOutputOverlay");
        if (videoOutputOverlay?.IsVisible == true)
        {
            HideVideoOutputSelection();
            ShowSettings();
            return;
        }

        // If audio output selection is showing, go back to settings
        var audioOutputOverlay = this.FindControl<Border>("AudioOutputOverlay");
        if (audioOutputOverlay?.IsVisible == true)
        {
            HideAudioOutputSelection();
            ShowSettings();
            return;
        }

        // If settings is showing, hide it
        var settingsOverlay = this.FindControl<Border>("SettingsOverlay");
        if (settingsOverlay?.IsVisible == true)
        {
            HideSettings();
            return;
        }

        // If home menu is showing, hide it
        var menuOverlay = this.FindControl<Border>("HomeMenuOverlay");
        if (menuOverlay?.IsVisible == true)
        {
            HideHomeMenu();
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
            // Access the MediaPlayerService via reflection to check if playing
            var mediaPlayerField = viewModel.GetType().GetField("_mediaPlayer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (mediaPlayerField != null)
            {
                var mediaPlayer = mediaPlayerField.GetValue(viewModel) as MediaPlayerService;
                if (mediaPlayer != null && mediaPlayer.IsPlaying)
                {
                    Console.WriteLine("MPV is playing - stopping playback and returning to app");
                    mediaPlayer.StopPlayback();
                    viewModel.IsPlaying = false;

                    return; // Don't execute back command, just stop playback
                }
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

        // Toggle the menu
        var menuOverlay = this.FindControl<Border>("HomeMenuOverlay");
        if (menuOverlay != null)
        {
            if (menuOverlay.IsVisible)
            {
                HideHomeMenu();
            }
            else
            {
                ShowHomeMenu();
            }
        }
    }

    private void ShowHomeMenu()
    {
        var menuOverlay = this.FindControl<Border>("HomeMenuOverlay");
        var logoutButton = this.FindControl<Button>("LogoutButton");
        var settingsButton = this.FindControl<Button>("SettingsButton");
        var closeMenuButton = this.FindControl<Button>("CloseMenuButton");

        if (menuOverlay == null) return;

        // Wire up button events
        if (logoutButton != null)
        {
            logoutButton.Click -= OnLogoutButtonClick;
            logoutButton.Click += OnLogoutButtonClick;
        }

        if (settingsButton != null)
        {
            settingsButton.Click -= OnSettingsButtonClick;
            settingsButton.Click += OnSettingsButtonClick;
        }

        if (closeMenuButton != null)
        {
            closeMenuButton.Click -= OnCloseMenuButtonClick;
            closeMenuButton.Click += OnCloseMenuButtonClick;
        }

        menuOverlay.IsVisible = true;
        menuOverlay.UpdateLayout();

        // Focus the first button
        logoutButton?.Focus();

        Console.WriteLine("Home menu shown");
    }

    private void HideHomeMenu()
    {
        var menuOverlay = this.FindControl<Border>("HomeMenuOverlay");
        if (menuOverlay != null)
        {
            menuOverlay.IsVisible = false;
            Console.WriteLine("Home menu hidden");
        }
    }

    private void OnLogoutButtonClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Logout button clicked");
        HideHomeMenu();

        // Call the logout command from the ViewModel
        if (DataContext is MainWindowViewModel viewModel)
        {
            // Clear saved credentials
            var credentialsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jellytv_credentials");
            if (File.Exists(credentialsPath))
            {
                File.Delete(credentialsPath);
                Console.WriteLine("Credentials deleted");
            }

            // Reset authentication state
            viewModel.IsAuthenticated = false;
            Console.WriteLine("Logged out successfully");
        }
    }

    private void OnSettingsButtonClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Settings button clicked");
        HideHomeMenu();
        ShowSettings();
    }

    private void OnCloseMenuButtonClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Close menu button clicked");
        HideHomeMenu();
    }

    private void ShowSettings()
    {
        var settingsOverlay = this.FindControl<Border>("SettingsOverlay");
        var closeSettingsButton = this.FindControl<Button>("CloseSettingsButton");
        var audioOutputButton = this.FindControl<Button>("AudioOutputButton");
        var videoOutputButton = this.FindControl<Button>("VideoOutputButton");
        var bluetoothPairButton = this.FindControl<Button>("BluetoothPairButton");
        var bluetoothManageButton = this.FindControl<Button>("BluetoothManageButton");

        if (settingsOverlay == null) return;

        // Wire up button events
        if (closeSettingsButton != null)
        {
            closeSettingsButton.Click -= OnCloseSettingsButtonClick;
            closeSettingsButton.Click += OnCloseSettingsButtonClick;
        }

        // Wire up settings option buttons
        if (audioOutputButton != null)
        {
            audioOutputButton.Click -= OnAudioOutputButtonClick;
            audioOutputButton.Click += OnAudioOutputButtonClick;
        }

        if (videoOutputButton != null)
        {
            videoOutputButton.Click -= OnVideoOutputButtonClick;
            videoOutputButton.Click += OnVideoOutputButtonClick;
        }

        if (bluetoothPairButton != null)
        {
            bluetoothPairButton.Click -= OnBluetoothPairButtonClick;
            bluetoothPairButton.Click += OnBluetoothPairButtonClick;
        }

        if (bluetoothManageButton != null)
        {
            bluetoothManageButton.Click -= OnBluetoothManageButtonClick;
            bluetoothManageButton.Click += OnBluetoothManageButtonClick;
        }

        settingsOverlay.IsVisible = true;
        settingsOverlay.UpdateLayout();

        // Focus the first button
        audioOutputButton?.Focus();

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

    private void OnAudioOutputButtonClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Audio Output button clicked");
        ShowAudioOutputSelection();
    }

    private void ShowAudioOutputSelection()
    {
        var audioOutputOverlay = this.FindControl<Border>("AudioOutputOverlay");
        var audioDeviceList = this.FindControl<StackPanel>("AudioDeviceList");
        var closeButton = this.FindControl<Button>("CloseAudioOutputButton");

        if (audioOutputOverlay == null || audioDeviceList == null) return;

        // Wire up close button
        if (closeButton != null)
        {
            closeButton.Click -= OnCloseAudioOutputButtonClick;
            closeButton.Click += OnCloseAudioOutputButtonClick;
        }

        // Clear existing buttons
        audioDeviceList.Children.Clear();

        // Get audio devices
        var devices = _audioService.GetAudioDevices();
        Console.WriteLine($"Found {devices.Count} audio devices");

        Button? firstButton = null;

        foreach (var device in devices)
        {
            var button = new Button
            {
                Content = device.Name + (device.IsDefault ? " (Current)" : ""),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                FontSize = 24,
                Padding = new Thickness(20, 15),
                Margin = new Thickness(0, 0, 0, 8),
                Tag = device.Id,
                Focusable = true,
                IsTabStop = true
            };

            // Highlight the current default device
            if (device.IsDefault)
            {
                button.Background = Avalonia.Media.Brushes.DarkGreen;
            }

            button.Click += OnAudioDeviceSelected;

            audioDeviceList.Children.Add(button);

            if (firstButton == null)
                firstButton = button;
        }

        // Show overlay
        HideSettings(); // Hide settings overlay first
        audioOutputOverlay.IsVisible = true;
        audioOutputOverlay.UpdateLayout();

        // Focus first button
        firstButton?.Focus();

        Console.WriteLine("Audio output selection shown");
    }

    private void OnCloseAudioOutputButtonClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Close audio output button clicked");
        HideAudioOutputSelection();
        ShowSettings(); // Go back to settings
    }

    private void HideAudioOutputSelection()
    {
        var audioOutputOverlay = this.FindControl<Border>("AudioOutputOverlay");
        if (audioOutputOverlay != null)
        {
            audioOutputOverlay.IsVisible = false;
            Console.WriteLine("Audio output selection hidden");
        }
    }

    private void OnAudioDeviceSelected(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string sinkName)
        {
            Console.WriteLine($"Audio device selected: {sinkName}");

            if (_audioService.SetDefaultAudioDevice(sinkName))
            {
                Console.WriteLine("Audio device changed successfully");
                // Refresh the list to update the "Current" indicator
                ShowAudioOutputSelection();
            }
            else
            {
                Console.WriteLine("Failed to change audio device");
            }
        }
    }

    private void OnVideoOutputButtonClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Video Output button clicked");
        ShowVideoOutputSelection();
    }

    private void ShowVideoOutputSelection()
    {
        var videoOutputOverlay = this.FindControl<Border>("VideoOutputOverlay");
        var videoOutputList = this.FindControl<StackPanel>("VideoOutputList");
        var closeButton = this.FindControl<Button>("CloseVideoOutputButton");

        if (videoOutputOverlay == null || videoOutputList == null) return;

        // Wire up close button
        if (closeButton != null)
        {
            closeButton.Click -= OnCloseVideoOutputButtonClick;
            closeButton.Click += OnCloseVideoOutputButtonClick;
        }

        // Clear existing buttons
        videoOutputList.Children.Clear();

        // Get video outputs
        var outputs = _videoService.GetVideoOutputs();
        Console.WriteLine($"Found {outputs.Count} video outputs");

        Button? firstButton = null;

        foreach (var output in outputs)
        {
            var displayText = $"{output.Name} - {output.Resolution}";
            if (output.IsPrimary) displayText += " (Primary)";
            if (output.IsActive) displayText += " (Active)";

            var button = new Button
            {
                Content = displayText,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                FontSize = 24,
                Padding = new Thickness(20, 15),
                Margin = new Thickness(0, 0, 0, 8),
                Tag = output,
                Focusable = true,
                IsTabStop = true
            };

            // Highlight the active/primary output
            if (output.IsActive || output.IsPrimary)
            {
                button.Background = Avalonia.Media.Brushes.DarkGreen;
            }

            button.Click += OnVideoOutputSelected;

            videoOutputList.Children.Add(button);

            if (firstButton == null)
                firstButton = button;
        }

        // Show overlay
        HideSettings(); // Hide settings overlay first
        videoOutputOverlay.IsVisible = true;
        videoOutputOverlay.UpdateLayout();

        // Focus first button
        firstButton?.Focus();

        Console.WriteLine("Video output selection shown");
    }

    private void OnCloseVideoOutputButtonClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Close video output button clicked");
        HideVideoOutputSelection();
        ShowSettings(); // Go back to settings
    }

    private void HideVideoOutputSelection()
    {
        var videoOutputOverlay = this.FindControl<Border>("VideoOutputOverlay");
        if (videoOutputOverlay != null)
        {
            videoOutputOverlay.IsVisible = false;
            Console.WriteLine("Video output selection hidden");
        }
    }

    private void OnVideoOutputSelected(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is VideoOutput output)
        {
            Console.WriteLine($"Video output selected: {output.Name}");

            // If output has multiple resolutions, let user pick one
            // For now, just enable it with auto resolution
            if (_videoService.EnableVideoOutput(output.Name, true))
            {
                Console.WriteLine("Video output changed successfully");
                // Refresh the list to update the active indicator
                ShowVideoOutputSelection();
            }
            else
            {
                Console.WriteLine("Failed to change video output");
            }
        }
    }

    // Bluetooth Methods
    private void OnBluetoothPairButtonClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Bluetooth Pair button clicked");
        ShowBluetoothPairing();
    }

    private async void ShowBluetoothPairing()
    {
        var bluetoothPairOverlay = this.FindControl<Border>("BluetoothPairOverlay");
        var bluetoothDeviceList = this.FindControl<StackPanel>("BluetoothDeviceList");
        var refreshButton = this.FindControl<Button>("RefreshBluetoothButton");
        var closeButton = this.FindControl<Button>("CloseBluetoothPairButton");
        var statusText = this.FindControl<TextBlock>("BluetoothScanStatus");

        if (bluetoothPairOverlay == null || bluetoothDeviceList == null) return;

        // Wire up buttons
        if (refreshButton != null)
        {
            refreshButton.Click -= OnRefreshBluetoothButtonClick;
            refreshButton.Click += OnRefreshBluetoothButtonClick;
        }

        if (closeButton != null)
        {
            closeButton.Click -= OnCloseBluetoothPairButtonClick;
            closeButton.Click += OnCloseBluetoothPairButtonClick;
        }

        HideSettings();
        bluetoothPairOverlay.IsVisible = true;
        bluetoothPairOverlay.UpdateLayout();

        // Start scanning
        if (statusText != null)
            statusText.Text = "Starting Bluetooth scan...";

        await _bluetoothService.StartScanAsync();
        await Task.Delay(5000); // Wait longer for devices to appear

        await RefreshBluetoothDeviceList();

        Console.WriteLine("Bluetooth pairing shown");
    }

    private async void OnRefreshBluetoothButtonClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Refresh Bluetooth scan clicked");

        // Restart the scan to discover new devices
        await _bluetoothService.StopScanAsync();
        await _bluetoothService.StartScanAsync();
        await Task.Delay(5000); // Wait for devices to appear

        await RefreshBluetoothDeviceList();
    }

    private async Task RefreshBluetoothDeviceList()
    {
        var bluetoothDeviceList = this.FindControl<StackPanel>("BluetoothDeviceList");
        var statusText = this.FindControl<TextBlock>("BluetoothScanStatus");

        if (bluetoothDeviceList == null) return;

        bluetoothDeviceList.Children.Clear();

        // Get available devices
        var devices = await _bluetoothService.GetDevicesAsync();
        Console.WriteLine($"Found {devices.Count} Bluetooth devices");

        if (statusText != null)
            statusText.Text = devices.Count > 0 ? $"Found {devices.Count} devices" : "No devices found. Make sure your device is discoverable.";

        Button? firstButton = null;

        // Show all devices, not just unpaired ones
        foreach (var device in devices)
        {
            var displayText = device.Name;
            if (!string.IsNullOrEmpty(device.DeviceType))
                displayText += $" ({device.DeviceType})";

            // Add status indicator
            if (device.IsConnected)
                displayText += " - Connected";
            else if (device.IsPaired)
                displayText += " - Paired";

            var button = new Button
            {
                Content = displayText,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                FontSize = 22,
                Padding = new Thickness(20, 12),
                Margin = new Thickness(0, 0, 0, 8),
                Tag = device,
                Focusable = true,
                IsTabStop = true
            };

            button.Click += OnBluetoothDevicePairClick;

            bluetoothDeviceList.Children.Add(button);

            if (firstButton == null)
                firstButton = button;
        }

        // If no devices found, focus on Refresh button as fallback
        if (firstButton != null)
        {
            firstButton.Focus();
        }
        else
        {
            var refreshButton = this.FindControl<Button>("RefreshBluetoothButton");
            refreshButton?.Focus();
        }
    }

    private async void OnBluetoothDevicePairClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is BluetoothDevice device)
        {
            button.IsEnabled = false;

            bool success = false;

            if (device.IsConnected)
            {
                // Already connected, do nothing
                Console.WriteLine($"Device {device.Name} is already connected");
                button.IsEnabled = true;
                return;
            }
            else if (device.IsPaired)
            {
                // Already paired, just connect
                Console.WriteLine($"Connecting to already-paired device: {device.Name} ({device.Address})");
                button.Content = $"Connecting to {device.Name}...";
                success = await _bluetoothService.ConnectDeviceAsync(device.Address);
            }
            else
            {
                // Need to pair first
                Console.WriteLine($"Pairing with device: {device.Name} ({device.Address})");
                button.Content = $"Pairing with {device.Name}...";
                success = await _bluetoothService.PairDeviceAsync(device.Address);

                if (success)
                {
                    // Try to connect after pairing
                    await _bluetoothService.ConnectDeviceAsync(device.Address);
                }
            }

            if (success)
            {
                Console.WriteLine($"Successfully connected to {device.Name}");
                // Refresh the list
                await RefreshBluetoothDeviceList();
            }
            else
            {
                button.Content = $"Failed: {device.Name}";
                button.IsEnabled = true;
            }
        }
    }

    private void OnCloseBluetoothPairButtonClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Close Bluetooth pair button clicked");
        HideBluetoothPairing();
        ShowSettings();
    }

    private async void HideBluetoothPairing()
    {
        await _bluetoothService.StopScanAsync();

        var bluetoothPairOverlay = this.FindControl<Border>("BluetoothPairOverlay");
        if (bluetoothPairOverlay != null)
        {
            bluetoothPairOverlay.IsVisible = false;
            Console.WriteLine("Bluetooth pairing hidden");
        }
    }

    private void OnBluetoothManageButtonClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Bluetooth Manage button clicked");
        ShowBluetoothManage();
    }

    private async void ShowBluetoothManage()
    {
        var bluetoothManageOverlay = this.FindControl<Border>("BluetoothManageOverlay");
        var pairedDeviceList = this.FindControl<StackPanel>("PairedDeviceList");
        var closeButton = this.FindControl<Button>("CloseBluetoothManageButton");

        if (bluetoothManageOverlay == null || pairedDeviceList == null) return;

        // Wire up button
        if (closeButton != null)
        {
            closeButton.Click -= OnCloseBluetoothManageButtonClick;
            closeButton.Click += OnCloseBluetoothManageButtonClick;
        }

        HideSettings();
        bluetoothManageOverlay.IsVisible = true;
        bluetoothManageOverlay.UpdateLayout();

        // Load paired devices
        pairedDeviceList.Children.Clear();

        var devices = await _bluetoothService.GetPairedDevicesAsync();
        Console.WriteLine($"Found {devices.Count} paired devices");

        Button? firstButton = null;

        foreach (var device in devices)
        {
            var panel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 10,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var infoText = new TextBlock
            {
                Text = $"{device.Name} {(device.IsConnected ? "(Connected)" : "")}",
                FontSize = 20,
                Foreground = Avalonia.Media.Brushes.White,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Width = 500
            };

            var connectButton = new Button
            {
                Content = device.IsConnected ? "Disconnect" : "Connect",
                FontSize = 18,
                Padding = new Thickness(20, 10),
                Tag = device,
                Focusable = true,
                IsTabStop = true
            };

            connectButton.Click += OnBluetoothDeviceConnectClick;

            var removeButton = new Button
            {
                Content = "Remove",
                FontSize = 18,
                Padding = new Thickness(20, 10),
                Tag = device,
                Focusable = true,
                IsTabStop = true,
                Background = Avalonia.Media.Brushes.DarkRed
            };

            removeButton.Click += OnBluetoothDeviceRemoveClick;

            panel.Children.Add(infoText);
            panel.Children.Add(connectButton);
            panel.Children.Add(removeButton);

            pairedDeviceList.Children.Add(panel);

            if (firstButton == null)
                firstButton = connectButton;
        }

        firstButton?.Focus();

        Console.WriteLine("Bluetooth manage shown");
    }

    private async void OnBluetoothDeviceConnectClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is BluetoothDevice device)
        {
            button.IsEnabled = false;

            if (device.IsConnected)
            {
                await _bluetoothService.DisconnectDeviceAsync(device.Address);
            }
            else
            {
                await _bluetoothService.ConnectDeviceAsync(device.Address);
            }

            // Refresh the list
            ShowBluetoothManage();
        }
    }

    private async void OnBluetoothDeviceRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is BluetoothDevice device)
        {
            Console.WriteLine($"Removing device: {device.Name}");
            await _bluetoothService.RemoveDeviceAsync(device.Address);

            // Refresh the list
            ShowBluetoothManage();
        }
    }

    private void OnCloseBluetoothManageButtonClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Close Bluetooth manage button clicked");
        HideBluetoothManage();
        ShowSettings();
    }

    private void HideBluetoothManage()
    {
        var bluetoothManageOverlay = this.FindControl<Border>("BluetoothManageOverlay");
        if (bluetoothManageOverlay != null)
        {
            bluetoothManageOverlay.IsVisible = false;
            Console.WriteLine("Bluetooth manage hidden");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _gamepadService?.Dispose();
        base.OnClosed(e);
    }
}