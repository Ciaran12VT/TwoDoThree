using TwoDoThree.Models;

namespace TwoDoThree.Services;

public interface IEmailCacheStore
{
    IReadOnlyList<EmailMessage> Load();

    void Save(IEnumerable<EmailMessage> messages);

    void Clear();
}
