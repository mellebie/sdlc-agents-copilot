namespace TCPA.Core.Exceptions;

public class ConfigurationException : Exception
{
    public string ConfigKey { get; }

    public ConfigurationException(string key)
        : base($"Required configuration key '{key}' is missing or empty in SystemConfig table.")
    {
        ConfigKey = key;
    }
}
