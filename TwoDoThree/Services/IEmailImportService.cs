using TwoDoThree.Models;

namespace TwoDoThree.Services;

public interface IEmailImportService
{
    Task<IReadOnlyList<EmailMessage>> ImportAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken);
}
