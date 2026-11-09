using System.Text.Json;
using System.Text.Json.Serialization;
using GrepoBot.Discord.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GrepoBot.Discord.State;

/// <summary>
/// Thread-safe owner of <see cref="BotState"/>. Every change is written to disk at once
/// (write to a temp file, then replace), so a power cut on the Pi never leaves a half-written file.
/// </summary>
public sealed class StateStore
{
    #region Private Fields

    private const int MAX_POST_RECORDS = 200;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _lock = new();
    private readonly string _filePath;
    private readonly ILogger<StateStore> _logger;
    private BotState _state;

    #endregion

    #region Constructor

    public StateStore(IOptions<BotOptions> options, ILogger<StateStore> logger)
        : this(Path.Combine(options.Value.ResolveDataDirectory(), "state.json"), logger)
    {
    }

    internal StateStore(string filePath, ILogger<StateStore> logger)
    {
        _filePath = filePath;
        _logger = logger;
        _state = Load();
    }

    #endregion

    #region Public Methods

    /// <summary>Reads a value. Return copies of lists, never the live list.</summary>
    public T Read<T>(Func<BotState, T> reader)
    {
        lock (_lock)
        {
            return reader(_state);
        }
    }

    /// <summary>Changes the state and saves it.</summary>
    public void Update(Action<BotState> change)
    {
        Update(state =>
        {
            change(state);
            return true;
        });
    }

    /// <summary>Changes the state, saves it and returns a result of the change.</summary>
    public T Update<T>(Func<BotState, T> change)
    {
        lock (_lock)
        {
            var result = change(_state);
            TrimPosts(_state);
            Save();
            return result;
        }
    }

    #endregion

    #region Private Methods

    private BotState Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("No state file at {Path}, starting with defaults.", _filePath);
                return new BotState();
            }

            var json = File.ReadAllText(_filePath);
            var state = JsonSerializer.Deserialize<BotState>(json, _jsonOptions) ?? new BotState();
            StateMigrations.Apply(state);
            return state;
        }
        catch (Exception ex)
        {
            var backup = _filePath + ".broken-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            _logger.LogError(ex, "State file {Path} could not be read, keeping it as {Backup} and starting with defaults.", _filePath, backup);
            TryMove(_filePath, backup);
            return new BotState();
        }
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(_state, _jsonOptions));
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private static void TrimPosts(BotState state)
    {
        if (state.Posts.Count > MAX_POST_RECORDS)
        {
            state.Posts = state.Posts
                .OrderByDescending(p => p.PostedAt)
                .Take(MAX_POST_RECORDS)
                .ToList();
        }
    }

    private static void TryMove(string from, string to)
    {
        try
        {
            File.Move(from, to, overwrite: true);
        }
        catch
        {
            // Best effort only, the bot starts with defaults either way.
        }
    }

    #endregion
}
