namespace TwoDoThree.Models;

public sealed class TaskFilterSet
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string IdContains { get; set; } = string.Empty;

    public string TitleContains { get; set; } = string.Empty;

    public string TagsContains { get; set; } = string.Empty;

    public string PocsContains { get; set; } = string.Empty;

    public string SurfScopeContains { get; set; } = string.Empty;

    public List<TaskStatus> IncludedStatuses { get; set; } = [];

    public DateTime? DueByFrom { get; set; }

    public DateTime? DueByTo { get; set; }

    public DateTime? CreatedOnFrom { get; set; }

    public DateTime? CreatedOnTo { get; set; }

    public DateTime? UpdatedOnFrom { get; set; }

    public DateTime? UpdatedOnTo { get; set; }

    public double? MinTimeSpentHours { get; set; }

    public double? MaxTimeSpentHours { get; set; }

    public TaskFilterSet Clone()
    {
        return new TaskFilterSet
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id,
            Name = Name,
            IdContains = IdContains,
            TitleContains = TitleContains,
            TagsContains = TagsContains,
            PocsContains = PocsContains,
            SurfScopeContains = SurfScopeContains,
            IncludedStatuses = IncludedStatuses.ToList(),
            DueByFrom = DueByFrom,
            DueByTo = DueByTo,
            CreatedOnFrom = CreatedOnFrom,
            CreatedOnTo = CreatedOnTo,
            UpdatedOnFrom = UpdatedOnFrom,
            UpdatedOnTo = UpdatedOnTo,
            MinTimeSpentHours = MinTimeSpentHours,
            MaxTimeSpentHours = MaxTimeSpentHours
        };
    }

    public void CopyFrom(TaskFilterSet source)
    {
        Id = string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString("N") : source.Id;
        Name = source.Name;
        IdContains = source.IdContains;
        TitleContains = source.TitleContains;
        TagsContains = source.TagsContains;
        PocsContains = source.PocsContains;
        SurfScopeContains = source.SurfScopeContains;
        IncludedStatuses = source.IncludedStatuses.ToList();
        DueByFrom = source.DueByFrom;
        DueByTo = source.DueByTo;
        CreatedOnFrom = source.CreatedOnFrom;
        CreatedOnTo = source.CreatedOnTo;
        UpdatedOnFrom = source.UpdatedOnFrom;
        UpdatedOnTo = source.UpdatedOnTo;
        MinTimeSpentHours = source.MinTimeSpentHours;
        MaxTimeSpentHours = source.MaxTimeSpentHours;
    }
}
