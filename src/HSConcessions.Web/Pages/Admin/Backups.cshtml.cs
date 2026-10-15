using HSConcessions.Web.Services;

namespace HSConcessions.Web.Pages.Admin;

public sealed class BackupsModel : AdminPageModel
{
    protected override string? RequiredFunction => Functions.ManageBackups;

    public List<BackupService.BackupInfo> Backups { get; private set; } = new();

    public void OnGet() => Backups = Svc<BackupService>().List();

    public string Created(DateTime utc) => Time.FormatUtcDateTime(utc);
}
