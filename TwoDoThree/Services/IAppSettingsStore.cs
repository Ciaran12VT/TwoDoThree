using TwoDoThree.Models;

namespace TwoDoThree.Services;

public interface IAppSettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}
