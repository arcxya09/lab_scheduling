namespace LabSchedule.Core;

public static class ItemTypes
{
    public const string Experiment = "experiment";
    public const string Maintenance = "maintenance";
}

public sealed class ScheduleSettings
{
    public string PageSize { get; set; } = "A4";
    public string Orientation { get; set; } = "landscape";
    public string Font { get; set; } = "SimHei";
    public bool ShowLegend { get; set; } = true;
    public bool ShowWeekends { get; set; }
    public bool ShowMaintenanceInList { get; set; } = true;

    public ScheduleSettings Clone() => (ScheduleSettings)MemberwiseClone();
}

public sealed class ScheduleItem
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = ItemTypes.Experiment;
    public string Name { get; set; } = "";
    public string Leader { get; set; } = "";

    /// <summary>实验单位 (organisation running the experiment). Optional; only written to JSON when set.</summary>
    public string Unit { get; set; } = "";

    /// <summary>负责人电话. Optional; only written to JSON when set.</summary>
    public string Phone { get; set; } = "";

    /// <summary>联系邮箱. Optional; only written to JSON when set.</summary>
    public string Email { get; set; } = "";

    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public string? Color { get; set; }
    public string Description { get; set; } = "";

    public bool IsMaintenance => Type == ItemTypes.Maintenance;

    public ScheduleItem Clone() => (ScheduleItem)MemberwiseClone();
}

public sealed class Schedule
{
    public int Year { get; set; } = DateTime.Now.Year;
    public string Title { get; set; } = "";
    public ScheduleSettings Settings { get; set; } = new();
    public List<ScheduleItem> Items { get; set; } = [];

    public Schedule Clone() => new()
    {
        Year = Year,
        Title = Title,
        Settings = Settings.Clone(),
        Items = Items.Select(item => item.Clone()).ToList()
    };
}

/// <summary>Output locations shown in the editor (pdf_path / html_path / json_path).</summary>
public sealed class OutputPaths
{
    public string PdfPath { get; set; } = "";
    public string HtmlPath { get; set; } = "";
    public string JsonPath { get; set; } = "";

    public OutputPaths Clone() => (OutputPaths)MemberwiseClone();
}
