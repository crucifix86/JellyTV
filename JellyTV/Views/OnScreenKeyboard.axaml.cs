using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JellyTV.Views;

public partial class OnScreenKeyboard : UserControl
{
    public event EventHandler<string>? TextEntered;
    public event EventHandler? Dismissed;

    private string _currentText = "";
    private bool _shiftPressed = false;
    private List<List<Button>> _keyRows = new();
    private int _currentRow = 0;
    private int _currentCol = 0;
    private TextBlock? _inputDisplay;

    public string CurrentText
    {
        get => _currentText;
        set
        {
            _currentText = value;
            if (_inputDisplay != null)
            {
                _inputDisplay.Text = _currentText;
            }
        }
    }

    public OnScreenKeyboard()
    {
        InitializeComponent();
        this.Loaded += OnLoaded;
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _inputDisplay = this.FindControl<TextBlock>("InputDisplay");
        if (_inputDisplay != null)
        {
            _inputDisplay.Text = _currentText;
        }

        // Build the key grid for navigation
        _keyRows.Clear();
        for (int i = 1; i <= 5; i++)
        {
            var row = new List<Button>();
            var rowPanel = this.FindControl<WrapPanel>($"Row{i}");
            if (rowPanel != null)
            {
                foreach (var child in rowPanel.Children)
                {
                    if (child is Button btn)
                    {
                        row.Add(btn);
                        btn.Click += OnKeyClick;
                    }
                }
            }
            _keyRows.Add(row);
        }

        // Focus the first key
        if (_keyRows.Count > 0 && _keyRows[0].Count > 0)
        {
            _currentRow = 0;
            _currentCol = 0;
            _keyRows[0][0].Focus();
        }
    }

    private void OnKeyClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string key)
        {
            HandleKeyPress(key);
        }
    }

    public void HandleGamepadInput(string direction)
    {
        if (_keyRows.Count == 0) return;

        switch (direction)
        {
            case "Left":
                if (_currentCol > 0)
                {
                    _currentCol--;
                }
                else
                {
                    // Wrap to end of row
                    _currentCol = _keyRows[_currentRow].Count - 1;
                }
                break;

            case "Right":
                if (_currentCol < _keyRows[_currentRow].Count - 1)
                {
                    _currentCol++;
                }
                else
                {
                    // Wrap to beginning of row
                    _currentCol = 0;
                }
                break;

            case "Up":
                if (_currentRow > 0)
                {
                    _currentRow--;
                    // Clamp column to valid range for new row
                    if (_currentCol >= _keyRows[_currentRow].Count)
                    {
                        _currentCol = _keyRows[_currentRow].Count - 1;
                    }
                }
                break;

            case "Down":
                if (_currentRow < _keyRows.Count - 1)
                {
                    _currentRow++;
                    // Clamp column to valid range for new row
                    if (_currentCol >= _keyRows[_currentRow].Count)
                    {
                        _currentCol = _keyRows[_currentRow].Count - 1;
                    }
                }
                break;

            case "Select":
                // Press the currently focused key
                var currentButton = _keyRows[_currentRow][_currentCol];
                if (currentButton.Tag is string key)
                {
                    HandleKeyPress(key);
                }
                break;

            case "Back":
                // Dismiss keyboard
                Dismissed?.Invoke(this, EventArgs.Empty);
                return;
        }

        // Focus the current button
        _keyRows[_currentRow][_currentCol].Focus();
    }

    private void HandleKeyPress(string key)
    {
        Console.WriteLine($"Key pressed: {key}");

        switch (key)
        {
            case "SPACE":
                CurrentText += " ";
                break;

            case "BACKSPACE":
                if (CurrentText.Length > 0)
                {
                    CurrentText = CurrentText.Substring(0, CurrentText.Length - 1);
                }
                break;

            case "SHIFT":
                _shiftPressed = !_shiftPressed;
                UpdateShiftButton();
                break;

            case "DONE":
                TextEntered?.Invoke(this, CurrentText);
                break;

            default:
                // Regular character key
                var charToAdd = _shiftPressed ? key.ToUpper() : key.ToLower();
                CurrentText += charToAdd;
                // Auto-reset shift after typing one character
                if (_shiftPressed)
                {
                    _shiftPressed = false;
                    UpdateShiftButton();
                }
                break;
        }
    }

    private void UpdateShiftButton()
    {
        var shiftButton = this.FindControl<Button>("ShiftButton");
        if (shiftButton != null)
        {
            shiftButton.Background = _shiftPressed
                ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF00A4DC"))
                : null;
        }
    }
}
