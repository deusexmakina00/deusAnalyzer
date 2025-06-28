using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MoonSharp.Interpreter;
using NLog;

namespace PacketCapture
{
    /// <summary>
    /// Manages packet filtering configuration using Lua scripts
    /// </summary>
    public class PacketConfigManager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private Script? luaScript;
        private FileSystemWatcher? configWatcher;
        private readonly string configPath;
        private readonly object scriptLock = new object();
        private DateTime lastReloadTime = DateTime.MinValue;
        private const int MinReloadIntervalSeconds = 1; // Prevent too frequent reloads

        public PacketConfigManager(string configPath = "scripts/packet_config.lua")
        {
            this.configPath = Path.GetFullPath(configPath);
            LoadConfiguration();
            SetupFileWatcher();
        }

        /// <summary>
        /// Loads or reloads the Lua configuration
        /// </summary>
        public void LoadConfiguration()
        {
            lock (scriptLock)
            {
                try
                {
                    // Prevent too frequent reloads
                    if (
                        DateTime.Now - lastReloadTime
                        < TimeSpan.FromSeconds(MinReloadIntervalSeconds)
                    )
                    {
                        return;
                    }

                    luaScript = new Script();

                    // Register core libraries
                    luaScript.Options.DebugPrint = s => Logger.Debug($"Lua: {s}");

                    if (File.Exists(configPath))
                    {
                        string configContent = File.ReadAllText(configPath);
                        luaScript.DoString(configContent);
                        lastReloadTime = DateTime.Now;
                        Logger.Info($"Packet configuration loaded from {configPath}");
                    }
                    else
                    {
                        Logger.Warn(
                            $"Configuration file not found: {configPath}. Using default settings."
                        );
                        LoadDefaultConfiguration();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Failed to load packet configuration from {configPath}");
                    LoadDefaultConfiguration();
                }
            }
        }

        /// <summary>
        /// Loads default configuration if Lua file is not available
        /// </summary>
        private void LoadDefaultConfiguration()
        {
            try
            {
                luaScript = new Script();
                string defaultConfig =
                    @"
                    excludes = {
                        10318, 100043, 100044, 100047, 100049, 100081, 100085, 100090,
                        100093, 100177, 100180, 100252, 100253, 100278, 100317, 100582,
                        100585, 100587, 100589, 100590, 100594, 100600, 100828, 100835
                    }
                    
                    function shouldExcludePacket(dataType, dataLength, encodeType)
                        for _, excludeType in ipairs(excludes) do
                            if dataType == excludeType then
                                return true
                            end
                        end
                        return false
                    end
                    
                    config = {
                        maxPacketLength = 65536,
                        minPacketLength = 1,
                        enableHexDump = true,
                        enableMetadataGeneration = true,
                        enablePacketSaving = true
                    }
                ";
                luaScript.DoString(defaultConfig);
                Logger.Info("Default packet configuration loaded");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to load default configuration");
            }
        }

        /// <summary>
        /// Sets up file watcher to automatically reload configuration when changed
        /// </summary>
        private void SetupFileWatcher()
        {
            try
            {
                string? directory = Path.GetDirectoryName(configPath);
                string fileName = Path.GetFileName(configPath);

                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    configWatcher = new FileSystemWatcher(directory, fileName)
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                        EnableRaisingEvents = true,
                    };

                    configWatcher.Changed += OnConfigFileChanged;
                    Logger.Info($"File watcher set up for {configPath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to set up file watcher for configuration");
            }
        }

        /// <summary>
        /// Handles configuration file changes
        /// </summary>
        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            // Delay reload to handle multiple events
            Thread.Sleep(100);
            Logger.Info("Configuration file changed, reloading...");
            LoadConfiguration();
        }

        /// <summary>
        /// Checks if a packet should be excluded based on Lua configuration
        /// </summary>
        public bool ShouldExcludePacket(ushort dataType, int dataLength, byte encodeType)
        {
            lock (scriptLock)
            {
                try
                {
                    if (luaScript == null)
                    {
                        LoadConfiguration();
                        if (luaScript == null)
                            return false;
                    }

                    // Try advanced filtering first
                    DynValue advancedFunction = luaScript.Globals.Get(
                        "shouldExcludePacketAdvanced"
                    );
                    if (advancedFunction.Type == DataType.Function)
                    {
                        DynValue result = luaScript.Call(
                            advancedFunction,
                            dataType,
                            dataLength,
                            encodeType,
                            DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                        );
                        return result.Boolean;
                    }

                    // Fall back to basic filtering
                    DynValue basicFunction = luaScript.Globals.Get("shouldExcludePacket");
                    if (basicFunction.Type == DataType.Function)
                    {
                        DynValue result = luaScript.Call(
                            basicFunction,
                            dataType,
                            dataLength,
                            encodeType
                        );
                        return result.Boolean;
                    }

                    Logger.Warn("No filtering function found in Lua configuration");
                    return false;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error checking packet exclusion for type {dataType}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Gets configuration value from Lua script
        /// </summary>
        public T GetConfigValue<T>(string key, T defaultValue = default!)
        {
            lock (scriptLock)
            {
                try
                {
                    if (luaScript == null)
                    {
                        return defaultValue;
                    }

                    DynValue config = luaScript.Globals.Get("config");
                    if (config.Type == DataType.Table)
                    {
                        DynValue value = config.Table.Get(key);
                        if (value.Type != DataType.Nil)
                        {
                            return (T)value.ToObject<T>();
                        }
                    }
                    return defaultValue;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error getting config value for key {key}");
                    return defaultValue;
                }
            }
        }

        /// <summary>
        /// Updates configuration value at runtime
        /// </summary>
        public bool UpdateConfigValue(string key, object value)
        {
            lock (scriptLock)
            {
                try
                {
                    if (luaScript == null)
                    {
                        return false;
                    }

                    DynValue updateFunction = luaScript.Globals.Get("updateConfig");
                    if (updateFunction.Type == DataType.Function)
                    {
                        DynValue result = luaScript.Call(updateFunction, key, value);
                        return result.Boolean;
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error updating config value for key {key}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Gets the current list of excluded packet types
        /// </summary>
        public List<int> GetExcludedPacketTypes()
        {
            lock (scriptLock)
            {
                try
                {
                    if (luaScript == null)
                    {
                        return new List<int>();
                    }

                    DynValue getExcludesFunction = luaScript.Globals.Get("getExcludes");
                    if (getExcludesFunction.Type == DataType.Function)
                    {
                        DynValue result = luaScript.Call(getExcludesFunction);
                        if (result.Type == DataType.Table)
                        {
                            var excludesList = new List<int>();
                            foreach (var pair in result.Table.Pairs)
                            {
                                if (pair.Value.Type == DataType.Number)
                                {
                                    excludesList.Add((int)pair.Value.Number);
                                }
                            }
                            return excludesList;
                        }
                    }

                    // Fallback: try to get excludes directly
                    DynValue excludes = luaScript.Globals.Get("excludes");
                    if (excludes.Type == DataType.Table)
                    {
                        var excludesList = new List<int>();
                        foreach (var pair in excludes.Table.Pairs)
                        {
                            if (pair.Value.Type == DataType.Number)
                            {
                                excludesList.Add((int)pair.Value.Number);
                            }
                        }
                        return excludesList;
                    }

                    return new List<int>();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error getting excluded packet types");
                    return new List<int>();
                }
            }
        }

        /// <summary>
        /// Adds a packet type to the exclusion list
        /// </summary>
        public void AddExcludedPacketType(int dataType)
        {
            lock (scriptLock)
            {
                try
                {
                    if (luaScript == null)
                    {
                        return;
                    }

                    DynValue addExcludeFunction = luaScript.Globals.Get("addExclude");
                    if (addExcludeFunction.Type == DataType.Function)
                    {
                        luaScript.Call(addExcludeFunction, dataType);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error adding excluded packet type {dataType}");
                }
            }
        }

        /// <summary>
        /// Removes a packet type from the exclusion list
        /// </summary>
        public void RemoveExcludedPacketType(int dataType)
        {
            lock (scriptLock)
            {
                try
                {
                    if (luaScript == null)
                    {
                        return;
                    }

                    DynValue removeExcludeFunction = luaScript.Globals.Get("removeExclude");
                    if (removeExcludeFunction.Type == DataType.Function)
                    {
                        luaScript.Call(removeExcludeFunction, dataType);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error removing excluded packet type {dataType}");
                }
            }
        }

        /// <summary>
        /// Disposes resources
        /// </summary>
        public void Dispose()
        {
            configWatcher?.Dispose();
        }
    }
}
