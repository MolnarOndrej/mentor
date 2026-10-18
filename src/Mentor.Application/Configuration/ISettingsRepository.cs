namespace Mentor.Application.Configuration;

public interface ISettingsRepository
{
    Settings Settings();
    void SavePrompt(string prompt);
    void SaveRuntimeSettings(RuntimeSettings settings);
}
