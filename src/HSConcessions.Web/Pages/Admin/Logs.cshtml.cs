using HSConcessions.Web.Api;
using HSConcessions.Web.Models;
using HSConcessions.Web.Services;

namespace HSConcessions.Web.Pages.Admin;

public sealed class LogsModel : AdminPageModel
{
    protected override string? RequiredFunction => Functions.DownloadLogs;

    public List<FileInfo> Files { get; private set; } = new();

    public void OnGet() => Files = AdminApi.LogFiles(Svc<StorageOptions>());

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.0} MB",
    };
}
