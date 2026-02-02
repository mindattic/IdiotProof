// ============================================================================
// StrategyConsoleManager - Interactive console UI for strategy management
// ============================================================================
//
// FEATURES:
// - View all strategies with status
// - Toggle strategies on/off
// - Create/Edit strategies using IdiotScript
// - Cancel/delete strategies
// - Interactive menu system
//
// ============================================================================

using IdiotProof.Console.Scripting;
using IdiotProof.Console.Services;
using IdiotProof.Console.UI;
using IdiotProof.Shared.Models;
using IdiotProof.Shared.Scripting;

namespace IdiotProof.Console.Management;

/// <summary>
/// Interactive console manager for viewing, creating, toggling, and canceling strategies.
/// </summary>
public class StrategyConsoleManager
{
    private readonly List<StrategyDefinition> _strategies = [];
    private readonly BackendClient? _client;
    private bool _isRunning = true;

    public StrategyConsoleManager(BackendClient? client = null, IEnumerable<StrategyDefinition>? initialStrategies = null)
    {
        _client = client;
        if (initialStrategies != null)
            _strategies.AddRange(initialStrategies);
    }

    /// <summary>
    /// Runs the interactive console UI.
    /// </summary>
    public async Task RunAsync()
    {
        await LoadStrategiesFromDiskAsync();
        DisplayHeader();

        while (_isRunning)
        {
            DisplayMenu();
            var choice = await ReadKeyAsync();
            await HandleMenuChoiceAsync(choice);
        }

    }

    private async Task LoadStrategiesFromDiskAsync()
    {
        try
        {
            var folder = IdiotScriptFileManager.GetDefaultFolder();
            IdiotScriptFileManager.EnsureFolderExists(folder);

            var loaded = await IdiotScriptFileManager.LoadStrategiesFromFolderAsync(folder);

            _strategies.Clear();
            _strategies.AddRange(loaded.OrderBy(s => s.Name));
        }
        catch
        {
            // Best-effort: if loading fails, allow app to continue with in-memory strategies.
        }
    }

    private void DisplayHeader()
    {
        System.Console.Clear();
        System.Console.ForegroundColor = ConsoleColor.Cyan;
        System.Console.WriteLine();
        System.Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║           IdiotProof Strategy Manager - Console Edition              ║");
        System.Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
        System.Console.ResetColor();
        System.Console.WriteLine();
    }

    private void DisplayMenu()
    {
        System.Console.WriteLine();
        System.Console.ForegroundColor = ConsoleColor.White;
        System.Console.WriteLine("┌─────────────────────────────────────────┐");
        System.Console.WriteLine("│              MAIN MENU                  │");
        System.Console.WriteLine("├─────────────────────────────────────────┤");
        System.Console.ResetColor();

        WriteMenuItem("1", "View Strategies", ConsoleColor.Green);
        WriteMenuItem("2", "Create Strategy (Script)", ConsoleColor.Yellow);
        WriteMenuItem("3", "Edit Strategy", ConsoleColor.Cyan);
        WriteMenuItem("4", "Toggle Strategy", ConsoleColor.DarkCyan);
        WriteMenuItem("5", "Delete Strategy", ConsoleColor.Red);
        WriteMenuItem("H", "Help (Script Syntax)", ConsoleColor.DarkGray);
        WriteMenuItem("ESC", "Go Back", ConsoleColor.Gray);

        System.Console.ForegroundColor = ConsoleColor.White;
        System.Console.WriteLine("└─────────────────────────────────────────┘");
        System.Console.ResetColor();

        System.Console.Write("\nSelect option: ");
    }

    private static void WriteMenuItem(string key, string description, ConsoleColor keyColor)
    {
        System.Console.Write("│  ");
        System.Console.ForegroundColor = keyColor;
        System.Console.Write($"[{key}]");
        System.Console.ResetColor();
        System.Console.Write($" {description}");
        System.Console.WriteLine(new string(' ', 35 - description.Length - key.Length) + "│");
    }

    private static async Task<ConsoleKey> ReadKeyAsync()
    {
        return await Task.Run(() => System.Console.ReadKey(intercept: true).Key);
    }

    private async Task HandleMenuChoiceAsync(ConsoleKey key)
    {
        System.Console.WriteLine();

        switch (key)
        {
            case ConsoleKey.D1 or ConsoleKey.NumPad1:
                DisplayStrategies();
                break;

            case ConsoleKey.D2 or ConsoleKey.NumPad2:
                await CreateStrategyAsync();
                break;

            case ConsoleKey.D3 or ConsoleKey.NumPad3:
                await EditStrategyAsync();
                break;

            case ConsoleKey.D4 or ConsoleKey.NumPad4:
                await ToggleStrategyAsync();
                break;

            case ConsoleKey.D5 or ConsoleKey.NumPad5:
                await DeleteStrategyAsync();
                break;

            case ConsoleKey.H:
                DisplayScriptHelp();
                break;

            case ConsoleKey.Escape:
                _isRunning = false;
                break;

            default:
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine("Invalid option. Press any key to continue...");
                System.Console.ResetColor();
                System.Console.ReadKey(true);
                break;
        }
    }

    #region View Strategies

    private void DisplayStrategies()
    {
        System.Console.Clear();
        DisplayHeader();

        System.Console.ForegroundColor = ConsoleColor.White;
        System.Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        System.Console.WriteLine("                           STRATEGIES                                  ");
        System.Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        System.Console.ResetColor();

        if (_strategies.Count == 0)
        {
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.WriteLine("\n  No strategies defined. Press [2] to create one or [5] for quick create.\n");
            System.Console.ResetColor();
        }
        else
        {
            System.Console.WriteLine();
            for (int i = 0; i < _strategies.Count; i++)
            {
                DisplayStrategyRow(i + 1, _strategies[i]);
            }
            System.Console.WriteLine();
        }

        System.Console.ForegroundColor = ConsoleColor.DarkGray;
        System.Console.WriteLine("Press any key to return to menu...");
        System.Console.ResetColor();
        System.Console.ReadKey(true);
    }

    private static void DisplayStrategyRow(int index, StrategyDefinition strategy)
    {
        var stats = strategy.GetStats();

        // Index
        System.Console.ForegroundColor = ConsoleColor.DarkGray;
        System.Console.Write($"  [{index}] ");

        // Status indicator
        if (strategy.Enabled)
        {
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.Write("● ");
        }
        else
        {
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.Write("○ ");
        }

        // Symbol
        System.Console.ForegroundColor = ConsoleColor.White;
        System.Console.Write($"{strategy.Symbol,-6}");

        // Name
        System.Console.ForegroundColor = ConsoleColor.Cyan;
        var displayName = strategy.Name.Length > 25 ? strategy.Name[..22] + "..." : strategy.Name;
        System.Console.Write($" {displayName,-25}");

        // Quantity
        System.Console.ForegroundColor = ConsoleColor.Gray;
        System.Console.Write($" Qty: ");
        System.Console.ForegroundColor = ConsoleColor.White;
        System.Console.Write($"{stats.Quantity,4}");

        // Entry price
        if (stats.Price > 0)
        {
            System.Console.ForegroundColor = ConsoleColor.Gray;
            System.Console.Write($" Entry: ");
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write($"${stats.Price:F2}");
        }

        // Take profit
        if (stats.TakeProfit > 0)
        {
            System.Console.ForegroundColor = ConsoleColor.Gray;
            System.Console.Write($" TP: ");
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.Write($"${stats.TakeProfit:F2}");
        }

        // Stop loss or trailing
        if (stats.TrailingStopLossPercent > 0)
        {
            System.Console.ForegroundColor = ConsoleColor.Gray;
            System.Console.Write($" TSL: ");
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.Write($"{stats.TrailingStopLossPercent:P0}");
        }
        else if (stats.StopLoss > 0)
        {
            System.Console.ForegroundColor = ConsoleColor.Gray;
            System.Console.Write($" SL: ");
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.Write($"${stats.StopLoss:F2}");
        }

        System.Console.ResetColor();
        System.Console.WriteLine();
    }

    #endregion

    #region Create Strategy

    private async Task CreateStrategyAsync()
    {
        System.Console.Clear();
        DisplayHeader();

        System.Console.ForegroundColor = ConsoleColor.Yellow;
        System.Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        System.Console.WriteLine("                    CREATE STRATEGY (IDIOTSCRIPT)                      ");
        System.Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        System.Console.ResetColor();
        System.Console.WriteLine();

        System.Console.ForegroundColor = ConsoleColor.DarkGray;
        System.Console.WriteLine("Enter your strategy using IdiotScript syntax. Example:");
        System.Console.ForegroundColor = ConsoleColor.White;
        System.Console.WriteLine("  Ticker(PLTR).Session(IS.PREMARKET).Qty(10).Entry(148.75).TakeProfit(158).TrailingStopLoss(15).IsAboveVwap().EmaBetween(9, 21)");
        System.Console.ResetColor();
        System.Console.WriteLine();
        System.Console.ForegroundColor = ConsoleColor.DarkGray;
        System.Console.WriteLine("Press [H] from main menu for full syntax help.");
        System.Console.ResetColor();
        System.Console.WriteLine();

        System.Console.Write("Script: ");
        System.Console.ForegroundColor = ConsoleColor.Yellow;
        var script = System.Console.ReadLine() ?? "";
        System.Console.ResetColor();

        if (string.IsNullOrWhiteSpace(script))
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine("Cancelled - no script entered.");
            System.Console.ResetColor();
            await Task.Delay(1500);
            return;
        }

        if (StrategyScriptParser.TryParse(script, out var strategy, out var error))
        {
            _strategies.Add(strategy!);

            string? savedPath = null;
            try
            {
                IdiotScriptFileManager.EnsureAllFoldersExist("Console");

                var folder = IdiotScriptFileManager.GetDefaultFolder();
                var fileName = IdiotScriptFileManager.GetSafeFileName(strategy!.Name, strategy.Symbol);
                savedPath = Path.Combine(folder, fileName);

                await IdiotScriptFileManager.SaveToFileAsync(strategy!, savedPath);
            }
            catch
            {
                // Best-effort persistence; still allow strategy to exist in-memory.
            }

            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine($"✓ Strategy '{strategy!.Name}' created successfully!");
            System.Console.ResetColor();

            if (!string.IsNullOrWhiteSpace(savedPath))
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.WriteLine($"Saved: {savedPath}");
                System.Console.ResetColor();
            }

            // Send updated strategies to backend
            if (_client?.IsConnected == true)
            {
                var result = await _client.SetStrategiesAsync(_strategies);
                if (result?.Success == true)
                {
                    System.Console.ForegroundColor = ConsoleColor.Green;
                    System.Console.WriteLine($"✓ {result.Message ?? "Strategies synced with backend"}");
                    System.Console.ResetColor();
                }
            }

            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.WriteLine("Strategy details:");
            System.Console.ResetColor();
            DisplayStrategyRow(_strategies.Count, strategy);

            // Show the normalized script
            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.WriteLine("Normalized script (PascalCase):");
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine($"  {GenerateScript(strategy)}");
            System.Console.ResetColor();
        }
        else
        {
            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"✗ Parse error: {error}");
            System.Console.ResetColor();
            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.WriteLine("Press [H] from menu to see script syntax help.");
            System.Console.ResetColor();
        }

        System.Console.WriteLine();
        System.Console.WriteLine("Press any key to return to menu...");
        System.Console.ReadKey(true);
    }

    #endregion

    #region Toggle Strategy

    private async Task ToggleStrategyAsync()
    {
        if (_strategies.Count == 0)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine("No strategies to toggle.");
            System.Console.ResetColor();
            await Task.Delay(1500);
            return;
        }

        System.Console.Clear();
        DisplayHeader();

        System.Console.ForegroundColor = ConsoleColor.Cyan;
        System.Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        System.Console.WriteLine("                         TOGGLE STRATEGY                               ");
        System.Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        System.Console.ResetColor();
        System.Console.WriteLine();

        // Display strategies with numbers
        for (int i = 0; i < _strategies.Count; i++)
        {
            DisplayStrategyRow(i + 1, _strategies[i]);
        }

        System.Console.WriteLine();
        System.Console.Write("Enter strategy number to toggle (or 0 to cancel): ");

        var input = System.Console.ReadLine();
        if (!int.TryParse(input, out var index) || index < 0 || index > _strategies.Count)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine("Invalid selection.");
            System.Console.ResetColor();
            await Task.Delay(1500);
            return;
        }

        if (index == 0)
            return;

        var strategy = _strategies[index - 1];
        strategy.Enabled = !strategy.Enabled;

        // Save the updated strategy to disk
        try
        {
            var folder = IdiotScriptFileManager.GetDefaultFolder();
            var fileName = IdiotScriptFileManager.GetSafeFileName(strategy.Name, strategy.Symbol);
            var savedPath = Path.Combine(folder, fileName);
            await IdiotScriptFileManager.SaveToFileAsync(strategy, savedPath);
        }
        catch
        {
            // Best-effort persistence
        }

        System.Console.WriteLine();
        if (strategy.Enabled)
        {
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine($"✓ Strategy '{strategy.Name}' is now ENABLED");
        }
        else
        {
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine($"○ Strategy '{strategy.Name}' is now DISABLED");
        }
        System.Console.ResetColor();

        // Sync with backend
        if (_client?.IsConnected == true)
        {
            var result = await _client.SetStrategiesAsync(_strategies);
            if (result?.Success == true)
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.WriteLine($"Synced with backend");
                System.Console.ResetColor();
            }
        }

        System.Console.WriteLine();
        System.Console.WriteLine("Press any key to return to menu...");
        System.Console.ReadKey(true);
    }

    #endregion

    #region Edit Strategy

    private async Task EditStrategyAsync()
    {
        if (_strategies.Count == 0)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine("No strategies to edit.");
            System.Console.ResetColor();
            await Task.Delay(1500);
            return;
        }

        System.Console.Clear();
        DisplayHeader();

        System.Console.ForegroundColor = ConsoleColor.Cyan;
        System.Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        System.Console.WriteLine("                          EDIT STRATEGY                                ");
        System.Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        System.Console.ResetColor();
        System.Console.WriteLine();

        // Display strategies with numbers
        for (int i = 0; i < _strategies.Count; i++)
        {
            DisplayStrategyRow(i + 1, _strategies[i]);
        }

        System.Console.WriteLine();
        System.Console.Write("Enter strategy number to edit (or 0 to go back): ");

        var input = System.Console.ReadLine();
        if (!int.TryParse(input, out var index) || index < 0 || index > _strategies.Count)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine("Invalid selection.");
            System.Console.ResetColor();
            await Task.Delay(1500);
            return;
        }

        if (index == 0)
            return;

        var strategy = _strategies[index - 1];
        var originalScript = StrategyToScript(strategy);

        System.Console.WriteLine();
        System.Console.ForegroundColor = ConsoleColor.DarkGray;
        System.Console.WriteLine("Current script:");
        System.Console.ForegroundColor = ConsoleColor.Yellow;
        System.Console.WriteLine($"  {originalScript}");
        System.Console.ResetColor();
        System.Console.WriteLine();

        System.Console.ForegroundColor = ConsoleColor.White;
        System.Console.WriteLine("Edit the script below (or press Enter to keep unchanged, ESC to cancel):");
        System.Console.ResetColor();
        System.Console.Write("Script: ");
        System.Console.ForegroundColor = ConsoleColor.Yellow;

        // Pre-fill with original script for editing
        var newScript = ReadLineWithDefault(originalScript);
        System.Console.ResetColor();

        if (newScript == null)
        {
            ConsoleUI.Info("Edit cancelled.");
            await Task.Delay(1000);
            return;
        }

        if (string.IsNullOrWhiteSpace(newScript) || newScript == originalScript)
        {
            ConsoleUI.Info("No changes made.");
            await Task.Delay(1000);
            return;
        }

        // Parse the new script
        if (StrategyScriptParser.TryParse(newScript, out var newStrategy, out var error))
        {
            // Preserve the original ID and replace in list
            var originalIndex = _strategies.IndexOf(strategy);
            _strategies[originalIndex] = newStrategy!;

            // Save to disk
            try
            {
                var folder = IdiotScriptFileManager.GetDefaultFolder();
                var fileName = IdiotScriptFileManager.GetSafeFileName(newStrategy!.Name, newStrategy.Symbol);
                var savedPath = Path.Combine(folder, fileName);
                await IdiotScriptFileManager.SaveToFileAsync(newStrategy, savedPath);
            }
            catch
            {
                // Best-effort persistence
            }

            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine($"✓ Strategy updated: '{newStrategy!.Name}'");
            System.Console.ResetColor();

            // Sync with backend
            if (_client?.IsConnected == true)
            {
                var result = await _client.SetStrategiesAsync(_strategies);
                if (result?.Success == true)
                {
                    System.Console.ForegroundColor = ConsoleColor.DarkGray;
                    System.Console.WriteLine($"Synced with backend");
                    System.Console.ResetColor();
                }
            }

            // Show the updated strategy
            System.Console.WriteLine();
            DisplayStrategyRow(originalIndex + 1, newStrategy);
        }
        else
        {
            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"✗ Parse error: {error}");
            System.Console.WriteLine("Strategy was NOT updated.");
            System.Console.ResetColor();
        }

        System.Console.WriteLine();
        System.Console.WriteLine("Press any key to return to menu...");
        System.Console.ReadKey(true);
    }

    /// <summary>
    /// Reads a line with a default value that can be edited.
    /// Returns null if ESC is pressed.
    /// </summary>
    private static string? ReadLineWithDefault(string defaultValue)
    {
        var buffer = new System.Text.StringBuilder(defaultValue);
        var cursorPos = defaultValue.Length;

        // Write the default value
        System.Console.Write(defaultValue);

        while (true)
        {
            var key = System.Console.ReadKey(intercept: true);

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    System.Console.WriteLine();
                    return buffer.ToString();

                case ConsoleKey.Escape:
                    System.Console.WriteLine();
                    return null;

                case ConsoleKey.Backspace:
                    if (cursorPos > 0)
                    {
                        buffer.Remove(cursorPos - 1, 1);
                        cursorPos--;
                        // Rewrite the line
                        var left = System.Console.CursorLeft;
                        if (left > 0)
                        {
                            System.Console.Write("\b \b");
                            var remaining = buffer.ToString()[cursorPos..];
                            System.Console.Write(remaining + " ");
                            System.Console.CursorLeft = Math.Max(0, left - 1);
                        }
                    }
                    break;

                case ConsoleKey.Delete:
                    if (cursorPos < buffer.Length)
                    {
                        buffer.Remove(cursorPos, 1);
                        var remaining = buffer.ToString()[cursorPos..];
                        var left = System.Console.CursorLeft;
                        System.Console.Write(remaining + " ");
                        System.Console.CursorLeft = left;
                    }
                    break;

                case ConsoleKey.LeftArrow:
                    if (cursorPos > 0 && System.Console.CursorLeft > 0)
                    {
                        cursorPos--;
                        System.Console.CursorLeft--;
                    }
                    break;

                case ConsoleKey.RightArrow:
                    if (cursorPos < buffer.Length)
                    {
                        cursorPos++;
                        System.Console.CursorLeft++;
                    }
                    break;

                case ConsoleKey.Home:
                    var homeOffset = Math.Min(cursorPos, System.Console.CursorLeft);
                    System.Console.CursorLeft -= homeOffset;
                    cursorPos = 0;
                    break;

                case ConsoleKey.End:
                    System.Console.CursorLeft += buffer.Length - cursorPos;
                    cursorPos = buffer.Length;
                    break;

                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        buffer.Insert(cursorPos, key.KeyChar);
                        cursorPos++;
                        var remaining = buffer.ToString()[(cursorPos - 1)..];
                        System.Console.Write(remaining);
                        System.Console.CursorLeft -= remaining.Length - 1;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Converts a StrategyDefinition back to IdiotScript format.
    /// Uses the shared IdiotScriptSerializer for consistent output.
    /// </summary>
    private static string StrategyToScript(StrategyDefinition strategy)
    {
        return StrategyScriptParser.ToScript(strategy);
    }

    /// <summary>
    /// Generates a formatted IdiotScript for display.
    /// </summary>
    private static string GenerateScript(StrategyDefinition strategy)
    {
        return StrategyScriptParser.ToScript(strategy);
    }

    #endregion

    #region Delete Strategy

    private async Task DeleteStrategyAsync()
    {
        if (_strategies.Count == 0)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine("No strategies to delete.");
            System.Console.ResetColor();
            await Task.Delay(1500);
            return;
        }

        System.Console.Clear();
        DisplayHeader();

        System.Console.ForegroundColor = ConsoleColor.Red;
        System.Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        System.Console.WriteLine("                         DELETE STRATEGY                               ");
        System.Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        System.Console.ResetColor();
        System.Console.WriteLine();

        // Display strategies with numbers
        for (int i = 0; i < _strategies.Count; i++)
        {
            DisplayStrategyRow(i + 1, _strategies[i]);
        }

        System.Console.WriteLine();
        System.Console.Write("Enter strategy number to delete (or 0 to go back): ");

        var input = System.Console.ReadLine();
        if (!int.TryParse(input, out var index) || index < 0 || index > _strategies.Count)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine("Invalid selection.");
            System.Console.ResetColor();
            await Task.Delay(1500);
            return;
        }

        if (index == 0)
            return;

        var strategy = _strategies[index - 1];

        System.Console.WriteLine();
        System.Console.ForegroundColor = ConsoleColor.Yellow;
        System.Console.Write($"Are you sure you want to delete '{strategy.Name}'? [y/N]: ");
        System.Console.ResetColor();

        var confirm = System.Console.ReadKey(true).KeyChar;
        System.Console.WriteLine(confirm);

        if (char.ToLower(confirm) == 'y')
        {
            _strategies.RemoveAt(index - 1);

            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine($"✓ Strategy '{strategy.Name}' has been deleted.");
            System.Console.ResetColor();
        }
        else
        {
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.WriteLine("Cancelled.");
            System.Console.ResetColor();
        }

        System.Console.WriteLine();
        System.Console.WriteLine("Press any key to return to menu...");
        System.Console.ReadKey(true);
    }

    #endregion

    #region Help

    private void DisplayScriptHelp()
    {
        System.Console.Clear();
        DisplayHeader();

        System.Console.ForegroundColor = ConsoleColor.DarkGray;
        System.Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        System.Console.WriteLine("                    IDIOTSCRIPT SYNTAX HELP                            ");
        System.Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        System.Console.ResetColor();
        System.Console.WriteLine();

        System.Console.ForegroundColor = ConsoleColor.White;
        System.Console.WriteLine("Commands are separated by periods (.) and are case-insensitive.");
        System.Console.WriteLine("Scripts are auto-converted to PascalCase before validation.");
        System.Console.WriteLine("Constants use IS. prefix (e.g., IS.PREMARKET, IS.BELL, IS.MODERATE)");
        System.Console.ResetColor();
        System.Console.WriteLine();

        WriteHelpSection("BASIC COMMANDS", new[]
        {
            ("Ticker(AAPL)", "Set stock symbol (required)"),
            ("Sym(AAPL)", "Alias for Ticker()"),
            ("Qty(100)", "Set quantity (default: 1)"),
            ("Name(\"My Strategy\")", "Set strategy name"),
            ("Buy / Sell", "Order direction (default: Buy)")
        });

        WriteHelpSection("PRICE CONDITIONS", new[]
        {
            ("Entry(148.75)", "Entry price condition (price >= value)"),
            ("Price($150)", "Alias for Entry()"),
            ("TakeProfit($158) / TP($158)", "Take profit target"),
            ("StopLoss($145) / SL($145)", "Stop loss price"),
            ("TrailingStopLoss(15) / TSL(15)", "Trailing stop loss percentage"),
            ("TSL(IS.MODERATE)", "Trailing stop loss using constant (10%)")
        });

        WriteHelpSection("TSL CONSTANTS (IS.)", new[]
        {
            ("IS.TIGHT", "5% trailing stop"),
            ("IS.MODERATE", "10% trailing stop"),
            ("IS.STANDARD", "15% trailing stop"),
            ("IS.LOOSE", "20% trailing stop"),
            ("IS.WIDE", "25% trailing stop")
        });

        WriteHelpSection("INDICATOR CONDITIONS", new[]
        {
            ("Breakout(150)", "Wait for breakout above $150"),
            ("Pullback(145)", "Wait for pullback to $145"),
            ("AboveVwap / IsAboveVwap", "Price must be above VWAP"),
            ("BelowVwap / IsBelowVwap", "Price must be below VWAP"),
            ("EmaAbove(9) / IsEmaAbove(9)", "Price must be above 9 EMA"),
            ("EmaBelow(9) / IsEmaBelow(9)", "Price must be below 9 EMA"),
            ("EmaBetween(9, 21)", "Price between 9 and 21 EMA"),
            ("RsiOversold(30)", "RSI oversold condition"),
            ("RsiOverbought(70)", "RSI overbought condition"),
            ("AdxAbove(25)", "ADX above threshold (strong trend)")
        });

        WriteHelpSection("MOMENTUM CONDITIONS", new[]
        {
            ("MomentumAbove(0)", "Momentum > threshold (upward)"),
            ("MomentumBelow(0)", "Momentum < threshold (downward)"),
            ("RocAbove(2)", "Rate of Change > 2% (rising)"),
            ("RocBelow(-2)", "Rate of Change < -2% (falling)")
        });

        WriteHelpSection("SESSION CONSTANTS (IS.)", new[]
        {
            ("Session(IS.PREMARKET)", "Pre-market: 4:00 AM - 9:30 AM ET"),
            ("Session(IS.RTH)", "Regular hours: 9:30 AM - 4:00 PM ET"),
            ("Session(IS.AFTERHOURS)", "After-hours: 4:00 PM - 8:00 PM ET"),
            ("Session(IS.EXTENDED)", "Extended: 4:00 AM - 8:00 PM ET"),
            ("Session(IS.ACTIVE)", "Always active (no time restrictions)")
        });

        WriteHelpSection("TIME CONSTANTS (IS.) FOR CLOSEPOSITION", new[]
        {
            ("ClosePosition(IS.BELL)", "Close at 9:20 AM (before market open)"),
            ("ClosePosition(IS.OPEN)", "Close at market open (9:30 AM)"),
            ("ClosePosition(IS.CLOSE)", "Close at market close (4:00 PM)"),
            ("ClosePosition(9:20, Y)", "Close at time, only if profitable")
        });

        System.Console.WriteLine();
        System.Console.ForegroundColor = ConsoleColor.Yellow;
        System.Console.WriteLine("EXAMPLES:");
        System.Console.ResetColor();
        System.Console.ForegroundColor = ConsoleColor.Cyan;
        System.Console.WriteLine("  Simple:  Ticker(PLTR).Session(IS.PREMARKET).Qty(10).TakeProfit($28).TrailingStopLoss(IS.MODERATE).ClosePosition(IS.BELL)");
        System.Console.WriteLine("  Chained: Ticker(PLTR).Qty(10).Breakout(25.50).Pullback(25.00).IsAboveVwap().EmaBetween(9, 21)");
        System.Console.ResetColor();

        System.Console.WriteLine();
        System.Console.ForegroundColor = ConsoleColor.Magenta;
        System.Console.WriteLine("FILE FORMAT: .IDIOT files are plain text, editable in any text editor.");
        System.Console.WriteLine("FILE LOCATION: MyDocuments\\IdiotProof\\strategies\\");
        System.Console.ResetColor();

        System.Console.WriteLine();
        System.Console.WriteLine("Press any key to return to menu...");
        System.Console.ReadKey(true);
    }

    private static void WriteHelpSection(string title, (string Command, string Description)[] items)
    {
        System.Console.ForegroundColor = ConsoleColor.Yellow;
        System.Console.WriteLine($"  {title}:");
        System.Console.ResetColor();

        foreach (var (cmd, desc) in items)
        {
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write($"    {cmd,-20}");
            System.Console.ForegroundColor = ConsoleColor.Gray;
            System.Console.WriteLine($" {desc}");
        }
        System.Console.ResetColor();
        System.Console.WriteLine();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Gets all current strategies.
    /// </summary>
    public IReadOnlyList<StrategyDefinition> Strategies => _strategies.AsReadOnly();

    /// <summary>
    /// Adds a strategy to the manager.
    /// </summary>
    public void AddStrategy(StrategyDefinition strategy)
    {
        _strategies.Add(strategy);
    }

    /// <summary>
    /// Removes a strategy from the manager.
    /// </summary>
    public bool RemoveStrategy(Guid id)
    {
        var strategy = _strategies.FirstOrDefault(s => s.Id == id);
        if (strategy != null)
        {
            _strategies.Remove(strategy);
            return true;
        }
        return false;
    }

    #endregion
}
