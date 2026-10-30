using System.ComponentModel;
using System.Runtime.CompilerServices;
using LabSchedule.Core;
using Microsoft.UI.Xaml.Media;

namespace LabSchedule;

/// <summary>One row of the item list under the calendar.</summary>
public sealed class ItemRow : INotifyPropertyChanged
{
    private string _id = "";
    private string _typeText = "";
    private string _name = "";
    private string _leader = "";
    private string _dateRange = "";
    private string _duration = "";
    private string _attachmentCount = "";
    private Brush? _swatch;

    public ItemRow(ScheduleItem item)
    {
        Item = item;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ScheduleItem Item { get; private set; }

    public string Id { get => _id; private set => Set(ref _id, value); }

    public string TypeText { get => _typeText; private set => Set(ref _typeText, value); }

    public string Name { get => _name; private set => Set(ref _name, value); }

    public string Leader { get => _leader; private set => Set(ref _leader, value); }

    public string DateRange { get => _dateRange; private set => Set(ref _dateRange, value); }

    public string Duration { get => _duration; private set => Set(ref _duration, value); }

    /// <summary>Number of attachments, blank when there are none.</summary>
    public string AttachmentCount { get => _attachmentCount; private set => Set(ref _attachmentCount, value); }

    public Brush? Swatch { get => _swatch; private set => Set(ref _swatch, value); }

    public void Update(ScheduleItem item, string color, int attachmentCount)
    {
        Item = item;
        Id = item.Id;
        TypeText = item.IsMaintenance ? "维护" : "实验";
        Name = item.Name;
        Leader = string.IsNullOrEmpty(item.Leader) ? "-" : item.Leader;
        DateRange = $"{item.StartDate} 至 {item.EndDate}";
        Duration = $"{ScheduleRules.DurationDays(item)} 天";
        AttachmentCount = attachmentCount > 0 ? attachmentCount.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        if (Swatch is not SolidColorBrush brush || ColorUtil.ToHex(brush.Color) != color.ToUpperInvariant())
        {
            Swatch = new SolidColorBrush(ColorUtil.Parse(color));
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
